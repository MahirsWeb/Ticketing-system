using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Startup;

/// <summary>
/// <c>dotnet TicketSystemTech.Api.dll --import-package &lt;folder&gt; [--dry-run]</c>: fills a database that was made for it (the migrations applied, no tickets, no
/// companies) from an import package (see <see cref="LegacyPackage"/>), so that the history of another ticket system can be carried over. It writes through the
/// application's own parts: people are made by the user manager (the address is encrypted and hashed with this installation's keys), text is cleaned by the same
/// sanitizer as a ticket written in the application, and no e-mail and no notification is made (nobody is told about 20 000 old tickets).
/// With <c>--dry-run</c> the package is checked, and what would be written is counted, and nothing is written.
/// It is a step of its own, run once, with the application stopped. It is not repeatable: if it stops half way, make the database again.
/// </summary>
public static class LegacyImport
{
    /// <summary>New tickets are numbered from here, above every number of the old system (they are six digits), so that the old numbers stay and nothing clashes.</summary>
    public const long FirstNumberOfNewTickets = 1_000_001;

    private const int BatchSize = 250;

    public sealed record Result(
        bool DryRun, IReadOnlyList<string> Problems, int Companies, int Departments, int HelpTopics, int SlaPlans, int Categories, int UsersCreated, int UsersReused,
        int Tickets, int Messages, int Assignees, long? NumbersContinueAt, int TicketsOfAnotherCompanyThanTheirClient);

    public static async Task<int> RunAsync(IServiceProvider services, string? folder, bool dryRun, TextWriter output, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Give the folder of the package: --import-package <folder> [--dry-run]");
            return 1;
        }

        try
        {
            var result = await RunCoreAsync(services, folder, dryRun, requireEmptyDatabase: true, restartNumbers: true, output, ct);
            if (result.Problems.Count > 0)
            {
                await output.WriteLineAsync("The package is not fit to be imported; nothing was written:");
                foreach (var problem in result.Problems) await output.WriteLineAsync("  - " + problem);
                return 1;
            }

            await output.WriteLineAsync(dryRun ? "DRY RUN: the package is fit to be imported. Nothing was written. It would write:" : "Imported:");
            await output.WriteLineAsync($"  companies {result.Companies}, branches {result.Departments}, help topics {result.HelpTopics}, SLA plans {result.SlaPlans}, categories {result.Categories}");
            await output.WriteLineAsync($"  people {result.UsersCreated} made, {result.UsersReused} already there");
            await output.WriteLineAsync($"  tickets {result.Tickets}, messages {result.Messages} (and the first message of each ticket as its description), assignees {result.Assignees}");
            if (!dryRun)
            {
                await output.WriteLineAsync($"  tickets of a company other than their client's: {result.TicketsOfAnotherCompanyThanTheirClient} (must be 0)");
                await output.WriteLineAsync($"  numbers of new tickets continue at {result.NumbersContinueAt}");
                await output.WriteLineAsync("Nobody has a password yet: an administrator gives each person a temporary one (Users, or --reset-admin-password <email> for an Admin).");
            }

            return result.TicketsOfAnotherCompanyThanTheirClient == 0 ? 0 : 1;
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync("ERROR: " + ex.Message);
            return 1;
        }
    }

    /// <summary>The import itself. <paramref name="requireEmptyDatabase"/> and <paramref name="restartNumbers"/> are off only in tests, which share a database with other tests.</summary>
    public static async Task<Result> RunCoreAsync(IServiceProvider services, string folder, bool dryRun, bool requireEmptyDatabase, bool restartNumbers, TextWriter log,
        CancellationToken ct = default)
    {
        var package = LegacyPackage.Load(folder);
        var checkedPackage = LegacyPackageValidator.Validate(package, LegacyPackage.ReadTickets(folder));
        if (checkedPackage.Problems.Count > 0) return Empty(dryRun, checkedPackage.Problems);

        var db = services.GetRequiredService<AppDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var html = services.GetRequiredService<IHtmlSanitizerService>();
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;

        if (requireEmptyDatabase)
        {
            var notEmpty = new List<string>();
            if (await db.Tickets.AnyAsync(ct)) notEmpty.Add("tickets");
            if (await db.Companies.AnyAsync(ct)) notEmpty.Add("companies");
            if (await db.Departments.AnyAsync(ct)) notEmpty.Add("branches");
            if (await db.HelpTopics.AnyAsync(ct)) notEmpty.Add("help topics");
            if (await db.SlaPlans.AnyAsync(ct)) notEmpty.Add("SLA plans");
            if (await db.TicketCategories.AnyAsync(ct)) notEmpty.Add("categories");
            if (await db.Users.AnyAsync(u => u.Role != UserRole.Admin, ct)) notEmpty.Add("people other than administrators");
            if (notEmpty.Count > 0)
                throw new InvalidOperationException("The database is not empty (" + string.Join(", ", notEmpty) + "). The import fills a database that was made for it: " +
                    "apply the migrations to a new one (--migrate-only) and run the import on that.");
        }

        if (dryRun)
            return new Result(true, [], package.Companies.Count, package.Departments.Count, package.HelpTopics.Count, package.SlaPlans.Count, package.Categories.Count,
                package.Users.Count, 0, checkedPackage.Tickets, checkedPackage.Messages, checkedPackage.Assignees, null, 0);

        var now = time.GetUtcNow().UtcDateTime;

        // ---- what tickets point at: companies, branches, help topics, SLA plans, categories
        var companyIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var c in package.Companies)
        {
            var row = new Company { Name = c.Name, Address = c.Address, ContactInfo = c.ContactInfo, IsActive = true, AiIndexingEnabled = c.AiIndexingEnabled };
            companyIds[c.Key] = row.Id;
            db.Companies.Add(row);
        }
        var departmentIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var d in package.Departments)
        {
            var row = new Department { Name = d.Name, Email = d.Email, IsActive = true };
            departmentIds[d.Key] = row.Id;
            db.Departments.Add(row);
        }
        var topicIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var t in package.HelpTopics)
        {
            var row = new HelpTopic { Name = t.Name, IsActive = true };
            topicIds[t.Key] = row.Id;
            db.HelpTopics.Add(row);
        }
        var slaIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var s in package.SlaPlans)
        {
            var row = new SlaPlan { Name = s.Name, ResolutionTimeHours = s.Hours, IsActive = true };
            slaIds[s.Key] = row.Id;
            db.SlaPlans.Add(row);
        }
        var categoryIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var c in package.Categories)
        {
            var row = new TicketCategory { Name = c.Name, IsActive = true };
            categoryIds[c.Key] = row.Id;
            db.TicketCategories.Add(row);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // ---- people, through the user manager: the address is encrypted and hashed with this installation's keys. Nobody gets a password.
        var personIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        int made = 0, reused = 0;
        foreach (var u in package.Users)
        {
            var existing = await users.FindByEmailAsync(u.Email);
            if (existing is not null)
            {
                personIds[u.Key] = existing.Id;
                reused++;
                continue;
            }

            var role = Enum.Parse<UserRole>(u.Role);
            var person = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = u.Email,
                Email = u.Email,
                // As in the application: employees are made by a trusted administrator, only clients are asked to verify their address.
                EmailConfirmed = role != UserRole.Client,
                FirstName = u.FirstName,
                LastName = u.LastName,
                PhoneNumber = string.IsNullOrWhiteSpace(u.Phone) ? null : u.Phone,
                PhoneNumberPrompted = !string.IsNullOrWhiteSpace(u.Phone),
                Role = role,
                CompanyId = u.CompanyKey is null ? null : companyIds[u.CompanyKey],
                DepartmentId = u.DepartmentKey is null ? null : departmentIds[u.DepartmentKey],
                IsActive = u.IsActive,
                CreatedAtUtc = u.CreatedAtUtc ?? now,
                MustChangePassword = true,
                PreferredLanguage = "bs",
            };
            var created = await users.CreateAsync(person);
            if (!created.Succeeded)
                throw new InvalidOperationException($"The person {u.Key} could not be made: " + string.Join(" ", created.Errors.Select(e => e.Description)));
            personIds[u.Key] = person.Id;
            made++;
        }
        db.ChangeTracker.Clear();
        await log.WriteLineAsync($"people: {made} made, {reused} already there");

        // ---- tickets, in batches
        var written = 0;
        long highestNumber = 0;
        var batch = new List<PackageTicket>(BatchSize);

        async Task WriteBatchAsync()
        {
            foreach (var t in batch)
            {
                var description = html.Sanitize(t.DescriptionHtml);
                // Who took the ticket on: its main assignee, or whoever closed it.
                Guid? openedBy = null;
                if (t.OpenedAtUtc is not null)
                {
                    if (t.Assignees.Count > 0) openedBy = personIds[t.Assignees[0].UserKey];
                    else if (t.ClosedByKey is not null) openedBy = personIds[t.ClosedByKey];
                }

                var ticket = new Ticket
                {
                    TicketNumber = t.Number,
                    Title = t.Title,
                    Description = string.IsNullOrWhiteSpace(description) ? "<p>(bez teksta)</p>" : description,
                    ClientId = personIds[t.ClientKey],
                    CompanyId = companyIds[t.CompanyKey],
                    Status = Enum.Parse<TicketStatus>(t.Status),
                    Source = t.Source is null ? null : Enum.Parse<TicketSource>(t.Source),
                    Priority = t.Priority is null ? null : Enum.Parse<TicketPriority>(t.Priority),
                    DepartmentId = departmentIds[t.DepartmentKey],
                    HelpTopicId = t.HelpTopicKey is null ? null : topicIds[t.HelpTopicKey],
                    SlaPlanId = t.SlaKey is null ? null : slaIds[t.SlaKey],
                    CategoryId = t.CategoryKey is null ? null : categoryIds[t.CategoryKey],
                    CreatedAt = t.CreatedAtUtc,
                    OpenedAtUtc = t.OpenedAtUtc,
                    OpenedByUserId = openedBy,
                    DueDateUtc = t.DueDateUtc,
                    ClosedAtUtc = t.ClosedAtUtc,
                    ClosedByUserId = t.ClosedByKey is null ? null : personIds[t.ClosedByKey],
                    ResolutionSummary = t.ResolutionSummary,
                    // A deadline that passed years ago must not make the overdue job write to everyone about it on the first day.
                    OverdueNotifiedAtUtc = t.DueDateUtc is { } due && due < now ? now : null,
                };
                foreach (var a in t.Assignees)
                    ticket.Assignees.Add(new TicketAssignee { UserId = personIds[a.UserKey], IsPrimary = a.IsPrimary, AssignedAtUtc = a.AssignedAtUtc ?? t.CreatedAtUtc });
                foreach (var m in t.Messages)
                {
                    var body = html.Sanitize(m.BodyHtml);
                    ticket.Messages.Add(new TicketMessage
                    {
                        AuthorId = personIds[m.AuthorKey],
                        Type = Enum.Parse<MessageType>(m.Type),
                        BodyHtml = string.IsNullOrWhiteSpace(body) ? "<p>(prazna poruka)</p>" : body,
                        CreatedAt = m.CreatedAtUtc,
                    });
                }

                db.Tickets.Add(ticket);
                if (long.TryParse(t.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > highestNumber) highestNumber = number;
            }

            await db.SaveChangesAsync(ct);
            written += batch.Count;
            batch.Clear();
            db.ChangeTracker.Clear();
        }

        foreach (var packageTicket in LegacyPackage.ReadTickets(folder))
        {
            batch.Add(packageTicket);
            if (batch.Count < BatchSize) continue;
            await WriteBatchAsync();
            if (written % 2000 == 0) await log.WriteLineAsync($"tickets: {written} of {checkedPackage.Tickets}");
        }
        if (batch.Count > 0) await WriteBatchAsync();
        await log.WriteLineAsync($"tickets: {written} of {checkedPackage.Tickets}");

        // ---- one line in the trail, and the numbers of new tickets continue above the old ones
        db.Audit(null, "LegacyImportCompleted", "Import", null,
            $"companies={package.Companies.Count}; branches={package.Departments.Count}; people={made}; tickets={written}; messages={checkedPackage.Messages}");
        await db.SaveChangesAsync(ct);

        long? continueAt = null;
        if (restartNumbers)
        {
            continueAt = Math.Max(FirstNumberOfNewTickets, highestNumber + 1);
            // RESTART WITH takes a constant, not a parameter; the text is built from a number made here (a long), never from the package.
#pragma warning disable EF1003
            await db.Database.ExecuteSqlRawAsync("ALTER SEQUENCE [TicketNumberSequence] RESTART WITH " + continueAt.Value.ToString(CultureInfo.InvariantCulture), ct);
#pragma warning restore EF1003
        }

        // ---- what was written is read again: no ticket may belong to a company other than its client's
        var strays = await db.Tickets.AsNoTracking().CountAsync(t => db.Users.Any(u => u.Id == t.ClientId && u.CompanyId != t.CompanyId), ct);
        return new Result(false, [], package.Companies.Count, package.Departments.Count, package.HelpTopics.Count, package.SlaPlans.Count, package.Categories.Count, made, reused,
            written, checkedPackage.Messages, checkedPackage.Assignees, continueAt, strays);
    }

    private static Result Empty(bool dryRun, IReadOnlyList<string> problems) => new(dryRun, problems, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, 0);
}
