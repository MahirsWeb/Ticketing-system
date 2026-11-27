using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The documents: the card of a ticket and the monthly report of a company, on the screen and as xlsx. Above all: one source for each kind of event (no doubles
/// from the system messages), what the trail does not say is "unknown", the client version holds nothing internal, and a person sees only what they may.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentsTests : IAsyncLifetime
{
    private const string Mark = "DC ";

    private readonly ApiWorld _world;
    private readonly List<Guid> _lookups = new();

    public DocumentsTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            var ids = await db.Tickets.Where(t => t.TicketNumber.StartsWith("DC-")).Select(t => t.Id).ToListAsync();
            await db.AuditLogs.Where(a => a.EntityId != null && ids.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => t.TicketNumber.StartsWith("DC-")).ExecuteDeleteAsync(); // messages, assignees and sessions go with their tickets
            await db.Companies.Where(c => c.Name.StartsWith(Mark)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
            return await db.TicketCategories.Where(c => _lookups.Contains(c.Id)).ExecuteDeleteAsync();
        });
    }

    private static DateTime At(int y, int m, int d, int h = 9, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private async Task<Company> CompanyAsync(string name)
    {
        var company = new Company { Name = Mark + name };
        await _world.Db(async db => { db.Companies.Add(company); return await db.SaveChangesAsync(); });
        return company;
    }

    private Ticket Made(Company? company, DateTime created, TicketStatus status = TicketStatus.Open, Guid? department = null, DateTime? opened = null, DateTime? closed = null,
        DateTime? due = null, Guid? topic = null, Guid? category = null, Actor? closedBy = null)
    {
        var t = new Ticket
        {
            TicketNumber = "DC-" + Guid.NewGuid().ToString("N")[..10],
            Title = "dokument",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = company?.Id ?? _world.CompanyId,
            Status = status,
            DepartmentId = department ?? _world.BranchA,
            CreatedAt = created,
            OpenedAtUtc = opened,
            ClosedAtUtc = closed,
            ClosedByUserId = closedBy?.Id,
            DueDateUtc = due,
            HelpTopicId = topic,
            CategoryId = category,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        return t;
    }

    private async Task SaveAsync(params object[] entities) => await _world.Db(async db => { db.AddRange(entities); return await db.SaveChangesAsync(); });

    private async Task<JsonElement> GetAsync(Actor? who, string path, int expected = 200)
    {
        var response = await _world.Send(who, HttpMethod.Get, path);
        Assert.True(response.Code == expected, $"{path} -> {response.Code}: {response.Text}");
        return string.IsNullOrEmpty(response.Text) ? default : JsonDocument.Parse(response.Text).RootElement.Clone();
    }

    private async Task<string> RawAsync(Actor who, string path)
    {
        var response = await _world.Send(who, HttpMethod.Get, path);
        Assert.Equal(200, response.Code);
        return response.Text;
    }

    private async Task<XSSFWorkbook> WorkbookAsync(Actor who, string path)
    {
        var file = await _world.GetBytes(who, path);
        Assert.True(file.Code == 200, $"{path} -> {file.Code}");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        return new XSSFWorkbook(new MemoryStream(file.Bytes));
    }

    private static AuditLog Row(Ticket t, string action, string? details, DateTime at, Actor? by = null) =>
        new() { Action = action, EntityType = nameof(Ticket), EntityId = t.Id, Details = details, UserId = by?.Id, CreatedAt = at };

    private TicketMessage Msg(Ticket t, Guid author, MessageType type, DateTime at, string body = "<p>x</p>") =>
        new() { TicketId = t.Id, AuthorId = author, Type = type, BodyHtml = body, CreatedAt = at };

    // ============================ the card of a ticket ============================

    private async Task<Ticket> LifeAsync(int year)
    {
        var t0 = At(year, 5, 4, 8);
        var t = Made(null, t0, TicketStatus.Closed, opened: t0.AddHours(1), closed: t0.AddHours(6), due: t0.AddHours(8), closedBy: _world.Admin);
        await SaveAsync(t);
        await SaveAsync(
            Row(t, "TicketOpened", TicketAuditDetails.Opened(_world.EmpA1.Id, _world.BranchA), t0.AddHours(1), _world.Admin),
            Row(t, "TicketStatusChanged", TicketAuditDetails.StatusChanged(TicketStatus.Open, TicketStatus.InProgress), t0.AddHours(2), _world.EmpA1),
            Row(t, "TicketAssigneeAdded", TicketAuditDetails.Added(_world.EmpA2.Id), t0.AddHours(3), _world.EmpA1),
            Row(t, "TicketTransferred", TicketAuditDetails.Transferred(_world.BranchA, _world.BranchB), t0.AddHours(4), _world.Admin),
            Row(t, "TicketClosed", "Ticket #1", t0.AddHours(6), _world.Admin),
            // the same facts as the system messages say them in words: they must not be on the card twice
            Msg(t, _world.Admin.Id, MessageType.SystemEvent, t0.AddHours(1), "<p>Ticket opened by Admin</p>"),
            Msg(t, _world.Admin.Id, MessageType.SystemEvent, t0.AddHours(4), "<p>Ticket transferred from A to B</p>"),
            Msg(t, _world.Admin.Id, MessageType.SystemEvent, t0.AddHours(6), "<p>Ticket closed by Admin</p>"),
            Msg(t, _world.EmpA1.Id, MessageType.Response, t0.AddHours(2.5), "<p>TAJNA-ODGOVOR</p>"),
            Msg(t, _world.EmpA1.Id, MessageType.InternalNote, t0.AddHours(2.6), "<p>TAJNA-BILJESKA</p>"),
            Msg(t, _world.CliX.Id, MessageType.Response, t0.AddHours(3.5), "<p>TAJNA-KLIJENT</p>"),
            new TicketWorkSession { TicketId = t.Id, UserId = _world.EmpA1.Id, StartedAtUtc = t0.AddHours(2), EndedAtUtc = t0.AddHours(3) });
        return t;
    }

    [SqlFact]
    public async Task The_card_has_every_event_once_in_order_and_the_time_in_each_status()
    {
        var t = await LifeAsync(2023);

        var card = await GetAsync(_world.Admin, $"/api/tickets/{t.Id}/card");

        var kinds = card.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Equal(new[] { "Created", "Opened", "StatusChanged", "WorkSession", "StaffReply", "InternalNote", "Assigned", "ClientReply", "Transferred", "Closed" }, kinds);
        var opened = card.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "Opened");
        Assert.Equal("empA1 Tester", opened.GetProperty("person").GetString()); // the person it was assigned to
        var change = card.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "StatusChanged");
        Assert.Equal(("Open", "InProgress"), (change.GetProperty("from").GetString(), change.GetProperty("to").GetString()));
        var moved = card.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "Transferred");
        Assert.Equal(("Branch A", "Branch B"), (moved.GetProperty("from").GetString(), moved.GetProperty("to").GetString()));
        var session = card.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "WorkSession");
        Assert.Equal(3600, session.GetProperty("durationSeconds").GetInt64());

        var totals = card.GetProperty("statusTotals").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => s.GetProperty("seconds").GetInt64());
        Assert.Equal(3600, totals["New"]); // created 08:00, opened 09:00
        Assert.Equal(3600, totals["Open"]); // opened 09:00, in progress 10:00
        Assert.Equal(4 * 3600, totals["InProgress"]); // 10:00 to closing at 14:00
        Assert.True(totals["Closed"] > 0);
        Assert.Equal(0, card.GetProperty("unknownSeconds").GetInt64());
        Assert.True(card.GetProperty("trailComplete").GetBoolean());

        var h = card.GetProperty("header");
        Assert.Equal((1, 1, 1), (h.GetProperty("staffReplies").GetInt32(), h.GetProperty("clientReplies").GetInt32(), h.GetProperty("internalNotes").GetInt32()));
        Assert.Equal(1.0, h.GetProperty("workHours").GetDouble());
        Assert.Equal(5.0, h.GetProperty("resolutionHours").GetDouble()); // opened 09:00, closed 14:00
        Assert.True(h.GetProperty("slaMet").GetBoolean());
    }

    [SqlFact]
    public async Task The_text_of_the_messages_is_not_on_the_card()
    {
        var t = await LifeAsync(2023);

        var raw = await RawAsync(_world.Admin, $"/api/tickets/{t.Id}/card");

        Assert.DoesNotContain("TAJNA", raw);
    }

    [SqlFact]
    public async Task What_the_trail_lacks_is_taken_from_the_columns_of_the_ticket_and_what_cannot_be_told_is_unknown()
    {
        var t0 = At(2023, 6, 1, 8);
        var closedWithoutTrail = Made(null, t0, TicketStatus.Closed, opened: t0, closed: t0.AddHours(3), closedBy: _world.EmpA1);
        var stuck = Made(null, t0, TicketStatus.InProgress, opened: t0.AddHours(1)); // in progress now, and no row says when it got there
        await SaveAsync(closedWithoutTrail, stuck);

        var a = await GetAsync(_world.Admin, $"/api/tickets/{closedWithoutTrail.Id}/card");
        var b = await GetAsync(_world.Admin, $"/api/tickets/{stuck.Id}/card");

        var closed = a.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "Closed");
        Assert.Equal("empA1 Tester", closed.GetProperty("person").GetString()); // from ClosedByUserId
        Assert.Equal(3 * 3600, a.GetProperty("statusTotals").EnumerateArray().Single(s => s.GetProperty("status").GetString() == "Open").GetProperty("seconds").GetInt64());
        Assert.True(a.GetProperty("trailComplete").GetBoolean());

        Assert.False(b.GetProperty("trailComplete").GetBoolean());
        Assert.True(b.GetProperty("unknownSeconds").GetInt64() > 0);
        Assert.Contains(b.GetProperty("spans").EnumerateArray(), s => s.GetProperty("status").ValueKind == JsonValueKind.Null); // shown as unknown and not guessed
        Assert.DoesNotContain(b.GetProperty("statusTotals").EnumerateArray(), s => s.GetProperty("status").GetString() == "InProgress");
    }

    [SqlFact]
    public async Task Staff_who_may_open_the_ticket_may_have_its_card_and_nobody_else_not_even_the_client_whose_ticket_it_is()
    {
        var t = await LifeAsync(2023); // in Branch A, of client X

        await GetAsync(_world.Admin, $"/api/tickets/{t.Id}/card");
        await GetAsync(_world.EmpA1, $"/api/tickets/{t.Id}/card");
        await GetAsync(_world.EmpB1, $"/api/tickets/{t.Id}/card", 403); // another branch
        await GetAsync(_world.CliX, $"/api/tickets/{t.Id}/card", 403); // the client of this very ticket: the card is not theirs (it names staff, branches, transfers and times)
        await GetAsync(_world.CliY, $"/api/tickets/{t.Id}/card", 403);
        await GetAsync(_world.Admin, $"/api/tickets/{Guid.NewGuid()}/card", 404);
        await GetAsync(null, $"/api/tickets/{t.Id}/card", 401);
        Assert.Equal(403, (await _world.GetBytes(_world.EmpB1, $"/api/tickets/{t.Id}/card/export")).Code);
        Assert.Equal(403, (await _world.GetBytes(_world.CliX, $"/api/tickets/{t.Id}/card/export")).Code);
        Assert.Equal(403, (await _world.GetBytes(_world.CliY, $"/api/tickets/{t.Id}/card/export")).Code);
        Assert.Equal(200, (await _world.GetBytes(_world.EmpA1, $"/api/tickets/{t.Id}/card/export")).Code);
    }

    [SqlFact]
    public async Task The_card_as_a_workbook_has_the_same_events_in_the_language_asked_for_and_a_header_that_says_who_and_when()
    {
        var t = await LifeAsync(2023);

        using var en = await WorkbookAsync(_world.Admin, $"/api/tickets/{t.Id}/card/export?language=en");
        using var bs = await WorkbookAsync(_world.Admin, $"/api/tickets/{t.Id}/card/export?language=bs");
        var card = await GetAsync(_world.Admin, $"/api/tickets/{t.Id}/card");

        Assert.Equal(new[] { "Ticket card", "Timeline", "Time in status" }, Enumerable.Range(0, 3).Select(i => en.GetSheetName(i)));
        Assert.Equal(new[] { "Karton tiketa", "Vremenska linija", "Vrijeme u statusu" }, Enumerable.Range(0, 3).Select(i => bs.GetSheetName(i)));
        var timeline = en.GetSheet("Timeline");
        Assert.Equal("Ticket card", timeline.GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal(t.TicketNumber, timeline.GetRow(0).GetCell(1).StringCellValue);
        Assert.Equal("Made by", timeline.GetRow(1).GetCell(0).StringCellValue);
        Assert.Contains("The state at the moment it was made", timeline.GetRow(3).GetCell(1).StringCellValue);
        var rows = Enumerable.Range(6, timeline.LastRowNum - 5).Select(i => timeline.GetRow(i)).ToList(); // under the header block and the headings
        Assert.Equal(card.GetProperty("events").GetArrayLength(), rows.Count);
        Assert.Contains(rows, r => r.GetCell(1).StringCellValue == "Transferred to another branch" && r.GetCell(3).StringCellValue == "Branch A → Branch B");
        Assert.Contains(rows, r => r.GetCell(1).StringCellValue == "Status changed" && r.GetCell(3).StringCellValue == "Open → InProgress");
        Assert.DoesNotContain(rows, r => r.GetCell(3)?.StringCellValue.Contains("TAJNA") == true);
        var bsTimeline = bs.GetSheet("Vremenska linija");
        Assert.Contains(Enumerable.Range(6, bsTimeline.LastRowNum - 5), i => bsTimeline.GetRow(i).GetCell(3).StringCellValue == "Otvoren → U toku");
    }

    [SqlFact]
    public async Task The_card_is_offered_under_the_number_of_the_ticket()
    {
        var t = await LifeAsync(2023);

        var file = await _world.GetBytes(_world.Admin, $"/api/tickets/{t.Id}/card/export");

        Assert.Equal($"ticket-card-{t.TicketNumber.ToLowerInvariant()}.xlsx", file.FileName);
    }

    // ============================ the monthly report of a company ============================

    private sealed record Scene(Company Company, Ticket InBranchB);

    /// <summary>March of the year: 7 tickets created in it (two just after midnight local time on the 1st, which is still February in UTC), 3 closed in it, one of branch B, one created on the evening of 31 March UTC (April locally) and one created in February.</summary>
    private async Task<Scene> MonthSceneAsync(int year)
    {
        var company = await CompanyAsync("Mjesec " + year);
        var topic1 = new HelpTopic { Name = Mark + "mreža" };
        var topic2 = new HelpTopic { Name = Mark + "štampač" };
        var category = new TicketCategory { Name = Mark + "hardver" };
        await SaveAsync(topic1, topic2, category);
        _lookups.AddRange(new[] { topic1.Id, topic2.Id, category.Id });

        var mar = At(year, 3, 10, 8);
        var inB = Made(company, mar, TicketStatus.Open, department: _world.BranchB, opened: mar, topic: topic1.Id);
        var tickets = new List<Ticket>
        {
            Made(company, mar, TicketStatus.New, topic: topic1.Id),
            Made(company, mar, TicketStatus.Open, opened: mar, topic: topic1.Id, category: category.Id),
            Made(company, mar, TicketStatus.Closed, opened: mar, closed: mar.AddHours(1), due: mar.AddHours(2), topic: topic2.Id), // in time: 1 h
            Made(company, mar, TicketStatus.Closed, opened: mar, closed: mar.AddHours(10), due: mar.AddHours(2)), // late: 10 h, no topic
            Made(company, At(year, 2, 20), TicketStatus.Closed, opened: At(year, 3, 5, 8), closed: At(year, 3, 5, 10)), // created in February, closed in March: 2 h
            inB,
            Made(company, At(year, 2, 28, 23, 30), TicketStatus.Open, opened: At(year, 2, 28, 23, 30)), // 00:30 on 1 March in Sarajevo
            Made(company, At(year, 2, 28, 23, 45), TicketStatus.Open, opened: At(year, 2, 28, 23, 45)), // and 00:45
            Made(company, At(year, 3, 31, 22, 30), TicketStatus.Open, opened: At(year, 3, 31, 22, 30)) // 00:30 on 1 April there: not March
        };
        await SaveAsync(tickets.Cast<object>().ToArray());
        await SaveAsync(
            new TicketWorkSession { TicketId = tickets[1].Id, UserId = _world.EmpA1.Id, StartedAtUtc = At(year, 3, 11), EndedAtUtc = At(year, 3, 11).AddMinutes(90) },
            new TicketWorkSession { TicketId = inB.Id, UserId = _world.EmpB1.Id, StartedAtUtc = At(year, 3, 12), EndedAtUtc = At(year, 3, 12).AddMinutes(30) },
            new TicketWorkSession { TicketId = inB.Id, UserId = _world.EmpB1.Id, StartedAtUtc = At(year, 4, 2), EndedAtUtc = At(year, 4, 2).AddHours(5) }); // April: not in March
        return new Scene(company, inB);
    }

    private static string Q(Company c, int year, string extra = "") => $"companyId={c.Id}&year={year}&month=3{extra}";

    private const string Monthly = "/api/reports/company-monthly";

    [SqlFact]
    public async Task The_month_is_counted_in_the_business_zone_and_the_rows_add_up_to_the_figures()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        var r = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year)}");

        Assert.Equal(7, r.GetProperty("created").GetInt32());
        Assert.Equal(3, r.GetProperty("closed").GetInt32());
        var status = r.GetProperty("byStatus").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => s.GetProperty("count").GetInt32());
        Assert.Equal((1, 4, 0, 0, 2), (status["New"], status["Open"], status["InProgress"], status["Resolved"], status["Closed"]));
        Assert.Equal(7, status.Values.Sum());
        var topics = r.GetProperty("byTopic").EnumerateArray().Select(x => (x.GetProperty("name").GetString(), x.GetProperty("count").GetInt32())).ToList();
        Assert.Equal(7, topics.Sum(x => x.Item2));
        Assert.Equal((Mark + "mreža", 3), topics[0]);
        Assert.Contains(r.GetProperty("byTopic").EnumerateArray(), x => x.GetProperty("id").ValueKind == JsonValueKind.Null && x.GetProperty("count").GetInt32() == 3); // no topic yet
        Assert.Equal(3, r.GetProperty("resolutionCount").GetInt32());
        Assert.Equal(4.3, r.GetProperty("averageResolutionHours").GetDouble()); // (1 + 10 + 2) / 3
        Assert.Equal(2.0, r.GetProperty("medianResolutionHours").GetDouble());
        Assert.Equal(50.0, r.GetProperty("slaComplianceRate").GetDouble());
        Assert.Equal(2, r.GetProperty("slaCounted").GetInt32());
        Assert.Equal(2.0, r.GetProperty("workHours").GetDouble()); // 90 + 30 minutes in March, from two branches
        Assert.Equal(8, r.GetProperty("ticketsTotal").GetInt32()); // created or closed in March
    }

    [SqlFact]
    public async Task The_time_to_solve_and_the_SLA_are_those_of_the_overview_for_the_same_tickets()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);
        // 1 March 00:00 to 1 April 00:00 in Sarajevo is 28 February 23:00 to 31 March 22:00 UTC
        var overview = await GetAsync(_world.Admin, $"/api/reports/summary?companyId={scene.Company.Id}&from={year}-02-28T23:00:00Z&to={year}-03-31T21:59:59Z");

        var r = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year)}");

        Assert.Equal(overview.GetProperty("avgResolutionHours").GetDouble(), r.GetProperty("averageResolutionHours").GetDouble());
        Assert.Equal(overview.GetProperty("slaComplianceRate").GetDouble(), r.GetProperty("slaComplianceRate").GetDouble());
        Assert.Equal(overview.GetProperty("totalClosed").GetInt32(), r.GetProperty("closed").GetInt32());
    }

    [SqlFact]
    public async Task An_employee_gets_the_same_figures_but_only_the_tickets_of_their_own_branch_in_the_list_and_the_document_says_so()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        var admin = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year)}");
        var a = await GetAsync(_world.EmpA1, $"{Monthly}?{Q(scene.Company, year)}");
        var b = await GetAsync(_world.EmpB1, $"{Monthly}?{Q(scene.Company, year)}");

        Assert.Equal("all", admin.GetProperty("header").GetProperty("listScope").GetString());
        Assert.Equal(8, admin.GetProperty("tickets").GetArrayLength());
        Assert.Equal("Branch A", a.GetProperty("header").GetProperty("listScope").GetString());
        Assert.Equal(7, a.GetProperty("ticketsTotal").GetInt32());
        Assert.DoesNotContain(a.GetProperty("tickets").EnumerateArray(), x => x.GetProperty("id").GetGuid() == scene.InBranchB.Id);
        Assert.Equal(new[] { scene.InBranchB.Id }, b.GetProperty("tickets").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        foreach (var name in new[] { "created", "closed", "averageResolutionHours", "workHours" }) // the figures are for all branches whoever asks
        {
            Assert.Equal(admin.GetProperty(name).GetRawText(), a.GetProperty(name).GetRawText());
            Assert.Equal(admin.GetProperty(name).GetRawText(), b.GetProperty(name).GetRawText());
        }
    }

    [SqlFact]
    public async Task The_list_on_the_screen_may_be_cut_but_the_figures_and_the_workbook_have_every_ticket()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        var cut = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year, "&limit=3")}");
        var whole = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year)}");
        using var book = await WorkbookAsync(_world.Admin, $"{Monthly}/export?{Q(scene.Company, year, "&limit=3&language=en")}"); // the workbook does not take a limit

        Assert.Equal(3, cut.GetProperty("tickets").GetArrayLength());
        Assert.Equal(8, cut.GetProperty("ticketsTotal").GetInt32());
        Assert.True(cut.GetProperty("ticketsTruncated").GetBoolean());
        Assert.False(whole.GetProperty("ticketsTruncated").GetBoolean());
        Assert.Equal(whole.GetProperty("created").GetRawText(), cut.GetProperty("created").GetRawText()); // the figures are never cut
        Assert.Equal(whole.GetProperty("averageResolutionHours").GetRawText(), cut.GetProperty("averageResolutionHours").GetRawText());
        var sheet = book.GetSheet("Tickets");
        Assert.Equal(8, Enumerable.Range(0, sheet.LastRowNum + 1).Count(i => (sheet.GetRow(i)?.GetCell(0)?.ToString() ?? "").StartsWith("DC-")));
        Assert.DoesNotContain("The list is cut", Everything(book)); // an archive has no warning: it is whole
    }

    [SqlFact]
    public async Task The_hours_are_always_in_the_internal_version_and_in_the_client_version_only_when_asked_for()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        var internalOff = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year, "&audience=internal&includeHours=false")}");
        var clientDefault = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year, "&audience=client")}");
        var clientOn = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year, "&audience=client&includeHours=true")}");

        Assert.Equal(2.0, internalOff.GetProperty("workHours").GetDouble());
        Assert.True(internalOff.GetProperty("header").GetProperty("includesHours").GetBoolean());
        Assert.Equal(JsonValueKind.Null, clientDefault.GetProperty("workHours").ValueKind);
        Assert.False(clientDefault.GetProperty("header").GetProperty("includesHours").GetBoolean());
        Assert.Equal(2.0, clientOn.GetProperty("workHours").GetDouble());
    }

    [SqlFact]
    public async Task The_client_version_has_no_internal_category_and_no_names_of_staff_and_the_internal_one_has_the_category()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        var clientRaw = await RawAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year, "&audience=client&includeHours=true")}");
        var internalDoc = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year)}");

        var client = JsonDocument.Parse(clientRaw).RootElement;
        Assert.Equal(JsonValueKind.Null, client.GetProperty("byCategory").ValueKind);
        Assert.DoesNotContain("hardver", clientRaw); // the internal category is nowhere in it
        foreach (var staff in new[] { "empA1 Tester", "empA2 Tester", "admin Tester" }) Assert.DoesNotContain(staff, clientRaw); // the names of staff either, not even of whoever made it
        Assert.Equal("Tix", client.GetProperty("header").GetProperty("madeBy").GetString());
        Assert.Equal("client", client.GetProperty("header").GetProperty("audience").GetString());
        var categories = internalDoc.GetProperty("byCategory").EnumerateArray().ToList();
        Assert.Equal(7, categories.Sum(c => c.GetProperty("count").GetInt32()));
        Assert.Contains(categories, c => c.GetProperty("name").GetString() == Mark + "hardver");
    }

    [SqlFact]
    public async Task A_month_without_tickets_has_zeros_and_no_SLA_rate()
    {
        var company = await CompanyAsync("Prazna");

        var r = await GetAsync(_world.Admin, $"{Monthly}?{Q(company, 2051)}");

        Assert.Equal((0, 0, 0), (r.GetProperty("created").GetInt32(), r.GetProperty("closed").GetInt32(), r.GetProperty("resolutionCount").GetInt32()));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("slaComplianceRate").ValueKind); // no data is not 100 %
        Assert.Equal(0, r.GetProperty("tickets").GetArrayLength());
        Assert.Equal(0.0, r.GetProperty("workHours").GetDouble());
    }

    [SqlFact]
    public async Task A_report_that_cannot_be_made_is_refused_with_a_reason()
    {
        var company = await CompanyAsync("Greske");

        var badAudience = await _world.Send(_world.Admin, HttpMethod.Get, $"{Monthly}?{Q(company, 2051, "&audience=public")}");
        var badMonth = await _world.Send(_world.Admin, HttpMethod.Get, $"{Monthly}?companyId={company.Id}&year=2051&month=13");

        Assert.Equal(400, badAudience.Code);
        Assert.Contains("internal or client", badAudience.Text);
        Assert.Equal(400, badMonth.Code);
        Assert.Contains("The month is not valid.", badMonth.Text);
        await GetAsync(_world.Admin, $"{Monthly}?companyId={Guid.NewGuid()}&year=2051&month=3", 404);
        await GetAsync(_world.CliX, $"{Monthly}?{Q(company, 2051)}", 403);
        Assert.Equal(403, (await _world.GetBytes(_world.CliX, $"{Monthly}/export?{Q(company, 2051)}")).Code);
    }

    private static string Everything(XSSFWorkbook wb) => string.Join("\n", Enumerable.Range(0, wb.NumberOfSheets).SelectMany(i =>
    {
        var sheet = wb.GetSheetAt(i);
        return Enumerable.Range(0, sheet.LastRowNum + 1).Select(r => sheet.GetRow(r)).Where(r => r is not null)
            .SelectMany(r => Enumerable.Range(0, r.LastCellNum < 0 ? 0 : r.LastCellNum).Select(c => r.GetCell(c)?.ToString() ?? ""));
    }));

    [SqlFact]
    public async Task The_workbook_has_the_figures_of_the_screen_in_the_language_asked_for()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);
        var screen = await GetAsync(_world.Admin, $"{Monthly}?{Q(scene.Company, year)}");

        using var en = await WorkbookAsync(_world.Admin, $"{Monthly}/export?{Q(scene.Company, year, "&language=en")}");
        using var bs = await WorkbookAsync(_world.Admin, $"{Monthly}/export?{Q(scene.Company, year, "&language=bs")}");

        Assert.Equal(new[] { "Summary", "Distribution", "Tickets" }, Enumerable.Range(0, 3).Select(i => en.GetSheetName(i)));
        Assert.Equal(new[] { "Sažetak", "Raspodjela", "Tiketi" }, Enumerable.Range(0, 3).Select(i => bs.GetSheetName(i)));
        var summary = en.GetSheet("Summary");
        Assert.Equal(scene.Company.Name, summary.GetRow(0).GetCell(1)?.StringCellValue ?? summary.GetRow(0).GetCell(0).StringCellValue);
        var figures = Enumerable.Range(0, summary.LastRowNum + 1).Select(i => summary.GetRow(i))
            .Where(r => r?.GetCell(0)?.CellType == NPOI.SS.UserModel.CellType.String && r.GetCell(1)?.CellType == NPOI.SS.UserModel.CellType.Numeric)
            .ToDictionary(r => r.GetCell(0).StringCellValue, r => r.GetCell(1).NumericCellValue);
        Assert.Equal(screen.GetProperty("created").GetInt32(), figures["Tickets created in the month"]);
        Assert.Equal(screen.GetProperty("closed").GetInt32(), figures["Tickets closed in the month"]);
        Assert.Equal(screen.GetProperty("averageResolutionHours").GetDouble(), figures["Average time to solve (h)"]);
        Assert.Equal(screen.GetProperty("medianResolutionHours").GetDouble(), figures["Median time to solve (h)"]);
        Assert.Equal(screen.GetProperty("workHours").GetDouble(), figures["Recorded work on tickets (h)"]);
        Assert.Contains("03." + year, Everything(en));
        var tickets = en.GetSheet("Tickets");
        var numbers = Enumerable.Range(0, tickets.LastRowNum + 1).Select(i => tickets.GetRow(i)?.GetCell(0)?.ToString() ?? "").Count(v => v.StartsWith("DC-"));
        Assert.Equal(screen.GetProperty("tickets").GetArrayLength(), numbers);
    }

    [SqlFact]
    public async Task The_workbook_of_the_client_version_says_what_it_leaves_out_and_has_no_category_table()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        using var client = await WorkbookAsync(_world.Admin, $"{Monthly}/export?{Q(scene.Company, year, "&audience=client&language=en")}");
        using var internalBook = await WorkbookAsync(_world.Admin, $"{Monthly}/export?{Q(scene.Company, year, "&language=en")}");

        var clientText = Everything(client);
        var internalText = Everything(internalBook);
        Assert.Contains("For the client", clientText);
        Assert.Contains("This version has no internal categories, internal notes or names of staff.", clientText);
        Assert.DoesNotContain("hardver", clientText);
        Assert.DoesNotContain("Recorded work on tickets (h)", clientText); // the hours are not asked for
        Assert.Contains("hardver", internalText);
        Assert.Contains("Recorded work on tickets (h)", internalText);
        Assert.DoesNotContain("This version has no internal categories", internalText);
    }

    [SqlFact]
    public async Task The_workbook_of_an_employee_says_that_its_list_is_of_their_branch_only()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        using var wb = await WorkbookAsync(_world.EmpA1, $"{Monthly}/export?{Q(scene.Company, year, "&language=en")}");

        Assert.Contains("Only the tickets of the branch Branch A. The figures are for all branches.", Everything(wb));
        var tickets = wb.GetSheet("Tickets");
        Assert.Equal(7, Enumerable.Range(0, tickets.LastRowNum + 1).Count(i => (tickets.GetRow(i)?.GetCell(0)?.ToString() ?? "").StartsWith("DC-")));
    }

    [SqlFact]
    public async Task The_report_is_offered_under_a_name_that_says_which_company_month_and_version()
    {
        const int year = 2050;
        var scene = await MonthSceneAsync(year);

        var file = await _world.GetBytes(_world.Admin, $"{Monthly}/export?{Q(scene.Company, year, "&audience=client")}");

        Assert.Matches(@"^company-report-[a-z0-9]+-2050-03-client\.xlsx$", file.FileName!);
    }
}
