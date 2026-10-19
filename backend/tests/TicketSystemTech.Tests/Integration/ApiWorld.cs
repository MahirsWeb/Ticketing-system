using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>One person in the seeded world, with a token the application accepts.</summary>
public sealed record Actor(string Key, Guid Id, string Email, UserRole Role, Guid? DepartmentId, string Token);

public sealed record ApiResponse(HttpStatusCode Status, string Text, IReadOnlyDictionary<string, string>? Headers = null)
{
    public int Code => (int)Status;

    /// <summary>The value of a response header, or null if it was not sent.</summary>
    public string? Header(string name) => Headers is not null && Headers.TryGetValue(name, out var value) ? value : null;

    public JsonElement Json => JsonDocument.Parse(Text).RootElement;

    /// <summary>Values of one property across the objects of a JSON array, or of the array found under <paramref name="arrayProperty"/>.</summary>
    public List<string> Values(string property, string? arrayProperty = null)
    {
        var array = arrayProperty is null ? Json : Json.GetProperty(arrayProperty);
        return array.EnumerateArray().Select(e => e.GetProperty(property).ToString()).ToList();
    }
}

/// <summary>
/// The world the integration tests run in: one throwaway database, migrated by the application itself, with two branches
/// (A and B), staff in each, clients who have tickets in one branch, in both, or in none, an unrouted ticket, knowledge-base
/// chunks, work tasks and notifications. Everything is created once and only read afterwards, so tests cannot disturb one another
/// (the few tests that write do so to data of their own).
/// </summary>
public sealed class ApiWorld : IAsyncLifetime
{
    public const string Password = "Authz-Test-Pass-2026!";

    private readonly Dictionary<string, string?> _savedEnvironment = new();
    private string _database = "";
    private HttpClient _client = null!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public bool Available => TestSql.ServerConnectionString is not null;
    public ApiFactory Factory { get; private set; } = null!;

    public Guid BranchA { get; private set; }
    public Guid BranchB { get; private set; }
    public Guid CompanyId { get; private set; }

    public Dictionary<string, Actor> Actors { get; } = new();
    public Dictionary<string, Guid> Tickets { get; } = new();
    public Dictionary<string, Guid> WorkTasks { get; } = new();
    public Dictionary<string, Guid> Notifications { get; } = new();

    public Actor Admin => Actors["admin"];
    public Actor EmpA1 => Actors["empA1"];
    public Actor EmpA2 => Actors["empA2"];
    public Actor EmpB1 => Actors["empB1"];
    /// <summary>Has a ticket in branch A (and the unrouted one).</summary>
    public Actor CliX => Actors["cliX"];
    /// <summary>Has a ticket in branch B.</summary>
    public Actor CliY => Actors["cliY"];
    /// <summary>Has one ticket in each branch.</summary>
    public Actor CliBoth => Actors["cliBoth"];
    /// <summary>Has no ticket at all.</summary>
    public Actor CliZ => Actors["cliZ"];

    public const string PhoneX = "+38761000001";
    public const string PhoneY = "+38761000002";

    public async Task InitializeAsync()
    {
        if (!Available) return;

        _database = TestSql.DatabasePrefix + Guid.NewGuid().ToString("N")[..10];
        SetEnvironment("ConnectionStrings__DefaultConnection", TestSql.ConnectionStringFor(_database));
        SetEnvironment("Jwt__Secret", new string('s', 48));
        SetEnvironment("Pii__HashKey", new string('h', 48));
        SetEnvironment("Storage__UploadsPath", Path.Combine(Path.GetTempPath(), _database));
        SetEnvironment("SeedAdmin__Email", "");

        Factory = new ApiFactory();
        _client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!Available) return;

        _client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();

        foreach (var (name, value) in _savedEnvironment) Environment.SetEnvironmentVariable(name, value);

        try
        {
            // Only ever a database this class created itself (the prefix and the random suffix are ours).
            if (_database.StartsWith(TestSql.DatabasePrefix, StringComparison.Ordinal))
            {
                using var master = TestSql.OpenMaster();
                using var cmd = master.CreateCommand();
                cmd.CommandText =
                    $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
                cmd.ExecuteNonQuery();
            }
        }
        catch (SqlException)
        {
            // A leftover test database is harmless; failing the run over it would hide real results.
        }

        var uploads = Path.Combine(Path.GetTempPath(), _database);
        if (Directory.Exists(uploads)) Directory.Delete(uploads, recursive: true);
    }

    private void SetEnvironment(string name, string value)
    {
        _savedEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    // ---------------- requests ----------------

    public async Task<ApiResponse> Send(Actor? actor, HttpMethod method, string url, object? body = null, IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actor.Token);
        if (headers is not null)
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        if (body is HttpContent content)
            request.Content = content;
        else if (body is not null)
            request.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request);
        var responseHeaders = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        return new ApiResponse(response.StatusCode, await response.Content.ReadAsStringAsync(), responseHeaders);
    }

    public Task<ApiResponse> Get(Actor? actor, string url, IDictionary<string, string>? headers = null) =>
        Send(actor, HttpMethod.Get, url, null, headers);

    public Task<ApiResponse> Post(Actor? actor, string url, object? body = null) => Send(actor, HttpMethod.Post, url, body ?? "{}");

    /// <summary>Runs a piece of database work in the application's own container (for reading back what a request wrote).</summary>
    public async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ---------------- people ----------------

    /// <summary>Creates an account the way the application does (Identity, encrypted email) and returns it with a valid token.
    /// Tests that change an account (deactivate it, move it) make one of their own so the shared people stay as they were.</summary>
    public async Task<Actor> CreateActorAsync(string key, UserRole role, Guid? branch, Guid? companyId, string? phone = null)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var email = $"{key.ToLowerInvariant()}@authz.test";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FirstName = key, LastName = "Tester", Role = role,
            DepartmentId = branch, CompanyId = companyId, PhoneNumber = phone, IsActive = true, PhoneNumberPrompted = true
        };
        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded) throw new InvalidOperationException($"Creating {key}: " + string.Join("; ", created.Errors.Select(e => e.Description)));

        var stamp = await users.GetSecurityStampAsync(user);
        var (token, _) = tokens.GenerateAccessToken(user.Id, email, user.FirstName, user.LastName, role, companyId, branch, stamp);
        return new Actor(key, user.Id, email, role, branch, token);
    }

    /// <summary>Forgets what the status middleware cached about this person (it re-checks an account at most every 20 seconds).</summary>
    public void ForgetCachedStatus(Actor actor) =>
        Factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            .Remove(TicketSystemTech.Api.Middleware.UserStatusCache.Key(actor.Id));

    // ---------------- seed ----------------

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var a = new Department { Name = "Branch A", Email = "a@authz.test" };
        var b = new Department { Name = "Branch B", Email = "b@authz.test" };
        var company = new Company { Name = "Acme" };
        db.AddRange(a, b, company);
        await db.SaveChangesAsync();
        BranchA = a.Id;
        BranchB = b.Id;
        CompanyId = company.Id;

        async Task Add(string key, UserRole role, Guid? branch, Guid? companyId, string? phone = null) =>
            Actors[key] = await CreateActorAsync(key, role, branch, companyId, phone);

        await Add("admin", UserRole.Admin, null, null);
        await Add("empA1", UserRole.Employee, BranchA, null);
        await Add("empA2", UserRole.Employee, BranchA, null);
        await Add("empB1", UserRole.Employee, BranchB, null);
        await Add("cliX", UserRole.Client, null, CompanyId, PhoneX);
        await Add("cliY", UserRole.Client, null, CompanyId, PhoneY);
        await Add("cliBoth", UserRole.Client, null, CompanyId);
        await Add("cliZ", UserRole.Client, null, CompanyId);

        Ticket NewTicket(string key, Guid? branch, Actor client, TicketStatus status)
        {
            var ticket = new Ticket
            {
                TicketNumber = "AZ-" + key, Title = "Ticket " + key, Description = "<p>test</p>", ClientId = client.Id, CompanyId = CompanyId,
                Status = status, DepartmentId = branch, Priority = TicketPriority.Medium, Source = TicketSource.TicketSystem,
                ResolutionSummary = status == TicketStatus.Closed ? "fixed" : null,
                ClosedAtUtc = status == TicketStatus.Closed ? DateTime.UtcNow : null
            };
            Tickets[key] = ticket.Id;
            return ticket;
        }

        db.Tickets.AddRange(
            NewTicket("A1", BranchA, CliX, TicketStatus.Closed),
            NewTicket("A2", BranchA, CliBoth, TicketStatus.InProgress),
            NewTicket("B1", BranchB, CliY, TicketStatus.Closed),
            NewTicket("B2", BranchB, CliBoth, TicketStatus.InProgress),
            NewTicket("U", null, CliX, TicketStatus.New)); // not routed to any branch yet
        await db.SaveChangesAsync();

        // The knowledge base only uses a ticket once its internal notes are long enough, so the two closed tickets get one each.
        db.TicketMessages.AddRange(
            new TicketMessage { TicketId = Tickets["A1"], AuthorId = EmpA1.Id, Type = MessageType.InternalNote, BodyHtml = new string('a', 150) },
            new TicketMessage { TicketId = Tickets["B1"], AuthorId = EmpB1.Id, Type = MessageType.InternalNote, BodyHtml = new string('b', 150) });

        var document = new KnowledgeBaseDocument { Title = "Manual", SourceFileName = "manual.docx", UploadedByUserId = Admin.Id };
        db.KnowledgeBaseDocuments.Add(document);
        await db.SaveChangesAsync();

        // The fake query embedding is (1,0,0). Branch B's ticket and the documentation match it perfectly; branch A's ticket
        // is a worse match (0.8). So a search that is limited to two results is full of B's ticket and the documentation
        // unless B's ticket has been filtered out BEFORE the ranking: the filter must not merely hide B's ticket afterwards.
        KnowledgeBaseChunk Chunk(Guid? ticketId, Guid? documentId, string content, params float[] embedding) => new()
        {
            TicketId = ticketId, DocumentId = documentId, Content = content,
            Embedding = embedding, EmbeddingModel = FakeEmbeddingService.Model
        };
        db.KnowledgeBaseChunks.AddRange(
            Chunk(Tickets["A1"], null, "zebra marker-ticket-A solution", 0.8f, 0.6f, 0f),
            Chunk(Tickets["B1"], null, "zebra marker-ticket-B solution", 1f, 0f, 0f),
            Chunk(null, document.Id, "zebra marker-documentation guidance", 1f, 0f, 0f));

        var noteA = new Notification { UserId = EmpA1.Id, Type = NotificationType.NewTicket, Message = "for A1" };
        var noteB = new Notification { UserId = EmpB1.Id, Type = NotificationType.NewTicket, Message = "for B1" };
        db.Notifications.AddRange(noteA, noteB);
        Notifications["A1"] = noteA.Id;
        Notifications["B1"] = noteB.Id;

        var taskA = new WorkTask { Title = "Task A", Description = "d", DepartmentId = BranchA, CreatedByUserId = EmpA1.Id, AssignedByUserId = EmpA1.Id, AssignedToUserId = EmpA1.Id };
        var taskB = new WorkTask { Title = "Task B", Description = "d", DepartmentId = BranchB, CreatedByUserId = EmpB1.Id, AssignedByUserId = EmpB1.Id, AssignedToUserId = EmpB1.Id };
        db.WorkTasks.AddRange(taskA, taskB);
        WorkTasks["A"] = taskA.Id;
        WorkTasks["B"] = taskB.Id;

        await db.SaveChangesAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiWorld>
{
    public const string Name = "api";
}
