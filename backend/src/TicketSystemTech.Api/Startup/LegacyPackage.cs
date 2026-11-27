using System.Text;
using System.Text.Json;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Startup;

// The shape of an import package: a folder of JSON-lines files (one JSON object per line) in the terms of Tix itself, made from another system's data
// by a converter that is not part of the application. The references between the parts are KEYS (any text, unique within its file); the importer
// gives the rows their own ids. The package holds personal data: it is read, never kept.

public sealed record PackageCompany(string Key, string Name, string? Address, string? ContactInfo, bool AiIndexingEnabled);

public sealed record PackageDepartment(string Key, string Name, string Email);

public sealed record PackageLookup(string Key, string Name);

public sealed record PackageSlaPlan(string Key, string Name, int Hours);

/// <summary>One person. <c>Role</c> is Admin, Employee or Client; a client has a company, an employee a branch.</summary>
public sealed record PackageUser(string Key, string Role, string Email, string FirstName, string LastName, string? Phone, string? CompanyKey,
    string? DepartmentKey, bool IsActive, DateTime? CreatedAtUtc);

public sealed record PackageAssignee(string UserKey, DateTime? AssignedAtUtc, bool IsPrimary);

/// <summary><c>Type</c> is Response or InternalNote (the system events of Tix are made by Tix, not imported).</summary>
public sealed record PackageMessage(string AuthorKey, string Type, string BodyHtml, DateTime CreatedAtUtc);

public sealed record PackageTicket(
    string Number, string Title, string DescriptionHtml, string Status, string? Source, string? Priority, string ClientKey, string CompanyKey, string DepartmentKey,
    string? HelpTopicKey, string? SlaKey, string? CategoryKey, DateTime CreatedAtUtc, DateTime? OpenedAtUtc, DateTime? DueDateUtc, DateTime? ClosedAtUtc,
    string? ClosedByKey, string? ResolutionSummary, List<PackageAssignee> Assignees, List<PackageMessage> Messages);

/// <summary>Everything of a package except the tickets (those are read one at a time: they are most of it).</summary>
public sealed class LegacyPackage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public List<PackageCompany> Companies { get; init; } = [];
    public List<PackageDepartment> Departments { get; init; } = [];
    public List<PackageLookup> HelpTopics { get; init; } = [];
    public List<PackageSlaPlan> SlaPlans { get; init; } = [];
    public List<PackageLookup> Categories { get; init; } = [];
    public List<PackageUser> Users { get; init; } = [];

    public static LegacyPackage Load(string folder)
    {
        if (!Directory.Exists(folder)) throw new InvalidOperationException($"The package folder '{folder}' does not exist.");
        return new LegacyPackage
        {
            Companies = ReadLines<PackageCompany>(Path.Combine(folder, "companies.jsonl")).ToList(),
            Departments = ReadLines<PackageDepartment>(Path.Combine(folder, "departments.jsonl")).ToList(),
            HelpTopics = ReadLines<PackageLookup>(Path.Combine(folder, "helptopics.jsonl")).ToList(),
            SlaPlans = ReadLines<PackageSlaPlan>(Path.Combine(folder, "slaplans.jsonl")).ToList(),
            Categories = ReadLines<PackageLookup>(Path.Combine(folder, "categories.jsonl")).ToList(),
            Users = ReadLines<PackageUser>(Path.Combine(folder, "users.jsonl")).ToList(),
        };
    }

    public static IEnumerable<PackageTicket> ReadTickets(string folder) => ReadLines<PackageTicket>(Path.Combine(folder, "tickets.jsonl"));

    private static IEnumerable<T> ReadLines<T>(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"The package has no {Path.GetFileName(path)}.");
        var number = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            T? item;
            try { item = JsonSerializer.Deserialize<T>(line, Json); }
            catch (JsonException ex) { throw new InvalidOperationException($"{Path.GetFileName(path)}, line {number}, is not valid: {ex.Message}"); }
            yield return item ?? throw new InvalidOperationException($"{Path.GetFileName(path)}, line {number}, is empty.");
        }
    }
}

/// <summary>
/// What a package must satisfy BEFORE anything is written. The one that matters most for the owner: every ticket has a company, and it is the company of the
/// person who wrote it, so that nothing ends up under a company its client does not belong to.
/// </summary>
public static class LegacyPackageValidator
{
    public sealed record Outcome(IReadOnlyList<string> Problems, int Tickets, int Messages, int Assignees);

    public static Outcome Validate(LegacyPackage package, IEnumerable<PackageTicket> tickets, int listAtMost = 40)
    {
        var problems = new List<string>();
        var total = 0;
        void Bad(string text)
        {
            total++;
            if (problems.Count < listAtMost) problems.Add(text);
        }

        HashSet<string> Keys(IEnumerable<string> keys, string what)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) Bad($"{what}: the key '{key}' is empty or used twice.");
            return seen;
        }

        var companies = Keys(package.Companies.Select(c => c.Key), "company");
        var departments = Keys(package.Departments.Select(d => d.Key), "branch");
        var topics = Keys(package.HelpTopics.Select(t => t.Key), "help topic");
        var slas = Keys(package.SlaPlans.Select(s => s.Key), "SLA plan");
        var categories = Keys(package.Categories.Select(c => c.Key), "category");
        Keys(package.Users.Select(u => u.Key), "person");

        foreach (var c in package.Companies)
            if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 200) Bad($"company {c.Key}: the name is empty or longer than 200 characters.");
        foreach (var d in package.Departments)
            if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 150) Bad($"branch {d.Key}: the name is empty or longer than 150 characters.");
        foreach (var l in package.HelpTopics.Concat(package.Categories))
            if (string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 150) Bad($"{l.Key}: the name is empty or longer than 150 characters.");
        foreach (var s in package.SlaPlans)
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 150 || s.Hours < 1) Bad($"SLA plan {s.Key}: the name is empty or too long, or the hours are less than 1.");

        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in package.Departments)
            if (!LooksLikeAnAddress(d.Email) || !emails.Add(d.Email)) Bad($"branch {d.Key}: the e-mail address is not an address or another branch has it (each branch needs its own).");

        var people = new Dictionary<string, (UserRole Role, string? CompanyKey)>(StringComparer.Ordinal);
        var personEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in package.Users)
        {
            if (!Enum.TryParse<UserRole>(u.Role, out var role) || !Enum.IsDefined(role)) { Bad($"person {u.Key}: the role '{u.Role}' is not Admin, Employee or Client."); continue; }
            people[u.Key] = (role, u.CompanyKey);
            if (!LooksLikeAnAddress(u.Email) || !personEmails.Add(u.Email)) Bad($"person {u.Key}: the e-mail address is not an address or someone else has it.");
            if (string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName) || u.FirstName.Length > 100 || u.LastName.Length > 100)
                Bad($"person {u.Key}: the first or last name is empty or longer than 100 characters.");
            if (role == UserRole.Client && (u.CompanyKey is null || !companies.Contains(u.CompanyKey))) Bad($"person {u.Key}: a client needs a company that exists.");
            if (role == UserRole.Employee && (u.DepartmentKey is null || !departments.Contains(u.DepartmentKey))) Bad($"person {u.Key}: an employee needs a branch that exists.");
        }

        var numbers = new HashSet<string>(StringComparer.Ordinal);
        int ticketCount = 0, messageCount = 0, assigneeCount = 0;
        foreach (var t in tickets)
        {
            ticketCount++;
            var at = $"ticket {t.Number}";
            if (string.IsNullOrWhiteSpace(t.Number) || t.Number.Length > 20 || !numbers.Add(t.Number)) Bad($"{at}: the number is empty, longer than 20 characters or used twice.");
            if (string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 300) Bad($"{at}: the title is empty or longer than 300 characters.");
            if (!Enum.TryParse<TicketStatus>(t.Status, out var status) || !Enum.IsDefined(status)) { Bad($"{at}: the status '{t.Status}' is not known."); continue; }
            if (t.Source is not null && (!Enum.TryParse<TicketSource>(t.Source, out var source) || !Enum.IsDefined(source))) Bad($"{at}: the source '{t.Source}' is not known.");
            if (t.Priority is not null && (!Enum.TryParse<TicketPriority>(t.Priority, out var priority) || !Enum.IsDefined(priority))) Bad($"{at}: the priority '{t.Priority}' is not known.");
            if (!departments.Contains(t.DepartmentKey)) Bad($"{at}: the branch '{t.DepartmentKey}' does not exist.");
            if (t.HelpTopicKey is not null && !topics.Contains(t.HelpTopicKey)) Bad($"{at}: the help topic '{t.HelpTopicKey}' does not exist.");
            if (t.SlaKey is not null && !slas.Contains(t.SlaKey)) Bad($"{at}: the SLA plan '{t.SlaKey}' does not exist.");
            if (t.CategoryKey is not null && !categories.Contains(t.CategoryKey)) Bad($"{at}: the category '{t.CategoryKey}' does not exist.");

            // The rule the owner asked for: a ticket belongs to a company, and to the company of the person who wrote it.
            if (!companies.Contains(t.CompanyKey)) Bad($"{at}: the company '{t.CompanyKey}' does not exist.");
            if (!people.TryGetValue(t.ClientKey, out var client) || client.Role != UserRole.Client) Bad($"{at}: the client '{t.ClientKey}' is not a client of the package.");
            else if (client.CompanyKey != t.CompanyKey) Bad($"{at}: the company of the ticket is not the company of its client.");

            if (status == TicketStatus.Closed)
            {
                if (t.ClosedAtUtc is null) Bad($"{at}: closed, but without the time it was closed.");
                if (string.IsNullOrWhiteSpace(t.ResolutionSummary)) Bad($"{at}: closed, but without a description of the resolution (Tix asks for one).");
            }
            else if (t.ClosedAtUtc is not null) Bad($"{at}: not closed, but has a time of closing.");
            if (t.ClosedByKey is not null && (!people.TryGetValue(t.ClosedByKey, out var closer) || closer.Role == UserRole.Client)) Bad($"{at}: closed by '{t.ClosedByKey}', who is not staff of the package.");

            var primaries = 0;
            foreach (var a in t.Assignees)
            {
                assigneeCount++;
                if (a.IsPrimary) primaries++;
                if (!people.TryGetValue(a.UserKey, out var person) || person.Role == UserRole.Client) Bad($"{at}: assigned to '{a.UserKey}', who is not staff of the package.");
            }
            if (primaries > 1) Bad($"{at}: more than one main assignee.");

            foreach (var m in t.Messages)
            {
                messageCount++;
                var knownAuthor = people.TryGetValue(m.AuthorKey, out var author);
                if (!knownAuthor) Bad($"{at}: a message by '{m.AuthorKey}', who is not in the package.");
                if (m.Type is not ("Response" or "InternalNote")) Bad($"{at}: a message of the type '{m.Type}' (only Response and InternalNote are imported).");
                else if (m.Type == "InternalNote" && knownAuthor && author.Role == UserRole.Client) Bad($"{at}: an internal note by a client.");
            }
        }

        if (total > problems.Count) problems.Add($"... and {total - problems.Count} more problem(s).");
        return new Outcome(problems, ticketCount, messageCount, assigneeCount);
    }

    private static bool LooksLikeAnAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && value.Count(c => c == '@') == 1 && !value.Any(char.IsWhiteSpace) && !value.StartsWith('@') && !value.EndsWith('@');
}
