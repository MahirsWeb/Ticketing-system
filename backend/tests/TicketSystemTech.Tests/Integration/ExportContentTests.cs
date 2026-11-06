using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using TicketSystemTech.Api.Queries;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// What the Excel export puts in its cells (T-37): the "Tickets" sheet, one row per ticket with the people and places by name, and the
/// "Timeline" sheet, one row per block of work for every employee in scope. The tickets live in June 2036, a month no other test uses,
/// so every row below is exactly what is put in here:
/// <code>
///   X4 branch A  created 06-02 07:00  opened 08:00  closed 12:00 by empA2 (4 h)   empA1 works it; nobody logged time on it
///   XC branch B  created 06-07 07:00  opened 08:00  closed 12:00 by empB1        empA1 works it (a ticket of another branch); nobody logged time on it
///   P  branch A  created 06-08 07:00  opened 08:00  closed 12:00 by empA1        empA2 works it, empA1 helps (put on it first); nobody logged time on it
///   Q  branch A  created 06-09 07:00  opened 08:00  closed 12:00 by empA2        empA1 works it, empA2 helps (put on it first); nobody logged time on it
///   X1 branch A  created 06-03 07:00  opened 08:00  closed 12:00 by empA2 (4 h)   empA1 works it, empA2 helps; empA1 logged 09:00-10:30
///   X2 branch A  created 06-04 07:00  opened 08:00  closed 16:00 by empA1 (8 h)   empA2 works it; nobody logged time on it
///   X3 branch A  created 06-05 07:00  new, nobody on it
///   XB branch B  created 06-06 07:00  opened 08:00  closed 12:00 by empB1        empB1 works it and logged 09:00-11:00
///   a task of empA2's (branch A) with a session 06-07 09:00-11:00
///   and, outside the range asked for: sessions of empA1's on X1 (05-31 and 07-03) and of empA2's on the task (05-30 and 07-09);
///   and a session of empA1's on XB (a ticket of branch B), which is theirs, but not in their branch
/// </code>
/// </summary>
[Collection(ApiCollection.Name)]
public class ExportContentTests : IAsyncLifetime
{
    private const string Range = "from=2036-06-01&to=2036-06-30";

    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _tasks = new();
    private readonly Dictionary<string, Ticket> _byName = new();
    private string _task = "";

    public ExportContentTests(ApiWorld world) => _world = world;

    private static DateTime June(int day, int hour, int minute = 0) => new(2036, 6, day, hour, minute, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        await AddAsync("X4", _world.BranchA, TicketStatus.Closed, June(2, 7), June(2, 8), June(2, 12), closedBy: _world.EmpA2, worker: _world.EmpA1);
        await AddAsync("X1", _world.BranchA, TicketStatus.Closed, June(3, 7), June(3, 8), June(3, 12), closedBy: _world.EmpA2, worker: _world.EmpA1, helper: _world.EmpA2);
        await AddAsync("X2", _world.BranchA, TicketStatus.Closed, June(4, 7), June(4, 8), June(4, 16), closedBy: _world.EmpA1, worker: _world.EmpA2);
        await AddAsync("X3", _world.BranchA, TicketStatus.New, June(5, 7), null, null);
        await AddAsync("XB", _world.BranchB, TicketStatus.Closed, June(6, 7), June(6, 8), June(6, 12), closedBy: _world.EmpB1, worker: _world.EmpB1);
        await AddAsync("XC", _world.BranchB, TicketStatus.Closed, June(7, 7), June(7, 8), June(7, 12), closedBy: _world.EmpB1, worker: _world.EmpA1);
        await AddAsync("P", _world.BranchA, TicketStatus.Closed, June(8, 7), June(8, 8), June(8, 12), closedBy: _world.EmpA1, worker: _world.EmpA2, helper: _world.EmpA1, helperFirst: true);
        await AddAsync("Q", _world.BranchA, TicketStatus.Closed, June(9, 7), June(9, 8), June(9, 12), closedBy: _world.EmpA2, worker: _world.EmpA1, helper: _world.EmpA2, helperFirst: true);
        await LogAsync("X1", _world.EmpA1, June(3, 9), June(3, 10, 30));
        await LogAsync("XB", _world.EmpB1, June(6, 9), June(6, 11));
        await LogAsync("X1", _world.EmpA1, new DateTime(2036, 7, 3, 9, 0, 0, DateTimeKind.Utc), new DateTime(2036, 7, 3, 10, 0, 0, DateTimeKind.Utc)); // after the range
        await LogAsync("X1", _world.EmpA1, new DateTime(2036, 5, 31, 9, 0, 0, DateTimeKind.Utc), new DateTime(2036, 5, 31, 10, 0, 0, DateTimeKind.Utc)); // before the range
        await LogAsync("XB", _world.EmpA1, June(6, 14), June(6, 15));

        var task = new WorkTask
        {
            Title = "export task " + Guid.NewGuid().ToString("N")[..6],
            Description = "d",
            DepartmentId = _world.BranchA,
            CreatedByUserId = _world.EmpA2.Id,
            AssignedByUserId = _world.EmpA2.Id,
            AssignedToUserId = _world.EmpA2.Id,
        };
        task.Sessions.Add(new WorkTaskSession { UserId = _world.EmpA2.Id, StartedAtUtc = June(7, 9), EndedAtUtc = June(7, 11) });
        task.Sessions.Add(new WorkTaskSession { UserId = _world.EmpA2.Id, StartedAtUtc = new DateTime(2036, 7, 9, 9, 0, 0, DateTimeKind.Utc), EndedAtUtc = new DateTime(2036, 7, 9, 11, 0, 0, DateTimeKind.Utc) }); // after the range
        task.Sessions.Add(new WorkTaskSession { UserId = _world.EmpA2.Id, StartedAtUtc = new DateTime(2036, 5, 30, 9, 0, 0, DateTimeKind.Utc), EndedAtUtc = new DateTime(2036, 5, 30, 11, 0, 0, DateTimeKind.Utc) }); // before the range
        await _world.Db(async db => { db.WorkTasks.Add(task); return await db.SaveChangesAsync(); });
        _tasks.Add(task.Id);
        _task = task.Title;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.WorkTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.TicketWorkSessions.Where(s => _tickets.Contains(s.TicketId)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task AddAsync(string name, Guid branch, TicketStatus status, DateTime created, DateTime? opened, DateTime? closed,
        Actor? closedBy = null, Actor? worker = null, Actor? helper = null, bool helperFirst = false)
    {
        var ticket = new Ticket
        {
            TicketNumber = "EX-" + name + "-" + Guid.NewGuid().ToString("N")[..6],
            Title = "export " + name,
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = branch,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            CreatedAt = created,
            OpenedAtUtc = opened,
            ClosedAtUtc = closed,
            ClosedByUserId = closedBy?.Id,
            ResolutionSummary = closed.HasValue ? "done" : null,
        };
        // The helper can be put on the ticket before the main worker: the main worker is the one marked, not the first one there.
        if (helper is not null && helperFirst) ticket.Assignees.Add(new TicketAssignee { UserId = helper.Id });
        if (worker is not null) ticket.Assignees.Add(new TicketAssignee { UserId = worker.Id, IsPrimary = true });
        if (helper is not null && !helperFirst) ticket.Assignees.Add(new TicketAssignee { UserId = helper.Id });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        _byName[name] = ticket;
    }

    private Task LogAsync(string name, Actor who, DateTime start, DateTime? end) =>
        _world.Db(async db =>
        {
            db.TicketWorkSessions.Add(new TicketWorkSession { TicketId = _byName[name].Id, UserId = who.Id, StartedAtUtc = start, EndedAtUtc = end });
            return await db.SaveChangesAsync();
        });

    private async Task<XSSFWorkbook> ExportAsync(Actor who, string extra = "")
    {
        var file = await _world.GetBytes(who, $"/api/reports/export?{Range}{extra}");
        Assert.Equal(200, file.Code);
        return new XSSFWorkbook(new MemoryStream(file.Bytes));
    }

    private static string Text(IRow row, int cell) => row.GetCell(cell)?.StringCellValue ?? "";
    private static double? Number(IRow row, int cell) => row.GetCell(cell) is { CellType: CellType.Numeric } c ? c.NumericCellValue : null;
    private static string? Moment(IRow row, int cell) => Number(row, cell) is { } n ? DateTime.FromOADate(n).ToString("MM-dd HH:mm") : null;

    private static List<IRow> Rows(ISheet sheet) => Enumerable.Range(1, sheet.LastRowNum).Select(sheet.GetRow).Where(r => r is not null).ToList();

    private string TicketNumber(string name) => _byName[name].TicketNumber;

    // ---------------- the tickets sheet ----------------

    [SqlFact]
    public async Task The_tickets_sheet_has_a_row_for_each_ticket_of_the_branch_with_the_people_and_places_by_name()
    {
        string branch = "", company = "";
        await _world.Db(async db =>
        {
            branch = await db.Departments.Where(d => d.Id == _world.BranchA).Select(d => d.Name).SingleAsync();
            company = await db.Companies.Where(c => c.Id == _world.CompanyId).Select(c => c.Name).SingleAsync();
            return 0;
        });

        using var book = await ExportAsync(_world.EmpA1);
        var sheet = book.GetSheet("Tickets");
        var rows = Rows(sheet);

        Assert.Equal(6, rows.Count); // X4, X1, X2, X3, P, Q (oldest first): the ones of branch B are not here
        Assert.Equal(new[] { "export X4", "export X1", "export X2", "export X3", "export P", "export Q" }, rows.Select(r => Text(r, 1)));

        var x1 = rows[1];
        Assert.Equal(TicketNumber("X1"), Text(x1, 0));
        Assert.Equal("Closed", Text(x1, 2));
        Assert.Equal(branch, Text(x1, 3));
        Assert.Equal(company, Text(x1, 4));
        Assert.Equal("cliX Tester", Text(x1, 5));
        Assert.Equal("empA1 Tester", Text(x1, 6));
        Assert.Equal("06-03 07:00", Moment(x1, 7));
        Assert.Equal("06-03 08:00", Moment(x1, 8));
        Assert.Equal("06-03 12:00", Moment(x1, 9));
        Assert.Equal(4.0, Number(x1, 10));
        Assert.Equal("empA2 Tester", Text(x1, 11));

        var x2 = rows[2];
        Assert.Equal("empA2 Tester", Text(x2, 6)); // its own worker, and not the one who closed it
        Assert.Equal(8.0, Number(x2, 10));
        Assert.Equal("empA1 Tester", Text(x2, 11));

        var x3 = rows[3];
        Assert.Equal("New", Text(x3, 2));
        Assert.Equal("", Text(x3, 6));
        Assert.Equal("06-05 07:00", Moment(x3, 7));
        Assert.Null(Moment(x3, 8));
        Assert.Null(Moment(x3, 9));
        Assert.Null(Number(x3, 10));
        Assert.Equal("", Text(x3, 11));
    }

    [SqlFact]
    public async Task An_admin_gets_the_tickets_of_every_branch_and_can_narrow_them_to_one()
    {
        using var all = await ExportAsync(_world.Admin);
        Assert.Contains("export XB", Rows(all.GetSheet("Tickets")).Select(r => Text(r, 1)));
        Assert.Contains("export X1", Rows(all.GetSheet("Tickets")).Select(r => Text(r, 1)));

        using var onlyB = await ExportAsync(_world.Admin, $"&departmentId={_world.BranchB}");
        var titles = Rows(onlyB.GetSheet("Tickets")).Select(r => Text(r, 1)).Where(t => t.StartsWith("export ")).ToList();
        Assert.Equal(new[] { "export XB", "export XC" }, titles);
    }

    // ---------------- the timeline sheet ----------------

    private static List<(string Employee, string Type, string Title, string Status, string? Start, string? End, double? Hours)> Timeline(XSSFWorkbook book, string titleMark) =>
        Rows(book.GetSheet("Timeline"))
            .Where(r => Text(r, 2).Contains(titleMark))
            .Select(r => (Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Moment(r, 4), Moment(r, 5), Number(r, 6)))
            .ToList();

    [SqlFact]
    public async Task The_timeline_has_a_row_for_each_block_of_work_of_each_employee_of_the_branch_and_nobody_elses()
    {
        using var book = await ExportAsync(_world.EmpA1);

        var export = Timeline(book, "export");

        Assert.Equal(
            new (string, string, string, string, string?, string?, double?)[]
            {
                ("empA1 Tester", "Ticket", $"#{TicketNumber("X4")} — export X4", "Closed", "06-02 08:00", "06-02 12:00", null),  // nobody logged: opened to closed, and no hours
                ("empA1 Tester", "Ticket", $"#{TicketNumber("X1")} — export X1", "Closed", "06-03 09:00", "06-03 10:30", 1.5), // the time that was logged
                ("empA1 Tester", "Ticket", $"#{TicketNumber("Q")} — export Q", "Closed", "06-09 08:00", "06-09 12:00", null),  // the helper first on the ticket does not take it
                ("empA2 Tester", "Ticket", $"#{TicketNumber("X2")} — export X2", "Closed", "06-04 08:00", "06-04 16:00", null),  // nobody logged: opened to closed, and no hours
                ("empA2 Tester", "Task", _task, "Done", "06-07 09:00", "06-07 11:00", 2.0),
                ("empA2 Tester", "Ticket", $"#{TicketNumber("P")} — export P", "Closed", "06-08 08:00", "06-08 12:00", null),
            },
            export);
        Assert.DoesNotContain(export, r => r.Title.Contains("export X3") || r.Title.Contains("export XB") || r.Title.Contains("export XC")); // no worker; another branch
    }

    [SqlFact]
    public async Task A_helper_has_no_row_for_the_ticket_they_only_help_with()
    {
        using var book = await ExportAsync(_world.EmpA1);

        Assert.DoesNotContain(Timeline(book, "export X1"), r => r.Employee == "empA2 Tester");
    }

    [SqlFact]
    public async Task An_admin_gets_the_timeline_of_every_branch_or_of_the_one_asked_for()
    {
        using var all = await ExportAsync(_world.Admin);
        Assert.Contains(Timeline(all, "export XB"), r => r.Employee == "empB1 Tester" && r.Hours == 2.0);
        Assert.Contains(Timeline(all, "export XB"), r => r.Employee == "empA1 Tester" && r.Hours == 1.0); // their work on a ticket of another branch is theirs
        Assert.Contains(Timeline(all, "export XC"), r => r.Employee == "empA1 Tester" && r.Hours == null);
        Assert.Contains(Timeline(all, "export X1"), r => r.Employee == "empA1 Tester");

        using var onlyB = await ExportAsync(_world.Admin, $"&departmentId={_world.BranchB}");
        Assert.Single(Timeline(onlyB, "export"));
        Assert.Contains(Timeline(onlyB, "export XB"), r => r.Employee == "empB1 Tester");
    }

    [SqlFact]
    public async Task The_timeline_asks_for_the_work_of_all_the_employees_at_once_and_not_for_each_of_them()
    {
        _world.Factory.Commands.Clear();

        using var book = await ExportAsync(_world.Admin);

        // logged work and tickets nobody logged work on: one query each, whatever the number of employees; tasks: one
        var commands = _world.Factory.Commands;
        Assert.True(commands.CountNaming("TicketWorkSessions") <= 2, $"asked {commands.CountNaming("TicketWorkSessions")} times for the work on tickets");
        Assert.True(commands.CountNaming("WorkTaskSessions") <= 1, $"asked {commands.CountNaming("WorkTaskSessions")} times for the work on tasks");
        Assert.NotEmpty(Timeline(book, "export")); // and the rows are there
    }

    [SqlFact]
    public async Task The_blocks_of_several_people_come_in_two_queries_oldest_first_with_an_entry_for_everyone_asked_for()
    {
        var nobody = Guid.NewGuid();
        _world.Factory.Commands.Clear();

        var blocks = await _world.Db(db => TicketWorkBlocks.ForUsersAsync(db, new[] { _world.EmpA1.Id, _world.EmpA2.Id, nobody, _world.EmpA1.Id }, June(1, 0), June(30, 23), null, DateTime.UtcNow));

        Assert.Equal(2, _world.Factory.Commands.CountNaming("TicketWorkSessions"));
        Assert.Empty(blocks[nobody]);
        Assert.Equal(new[] { "export X4", "export X1", "export XB", "export XC", "export Q" }, blocks[_world.EmpA1.Id].Select(b => b.Title)); // 06-02, 03, 06, 07, 09
        Assert.Equal(new[] { false, true, true, false, false }, blocks[_world.EmpA1.Id].Select(b => b.IsLogged));
        Assert.Equal(new[] { "export X2", "export P" }, blocks[_world.EmpA2.Id].Select(b => b.Title)); // the help on X1, P and Q has no block
    }

    [SqlFact]
    public async Task The_blocks_of_one_person_are_what_they_were_when_they_were_asked_for_alone()
    {
        var alone = await _world.Db(db => TicketWorkBlocks.ForUserAsync(db, _world.EmpA1.Id, June(1, 0), June(30, 23), null, DateTime.UtcNow));
        var together = await _world.Db(db => TicketWorkBlocks.ForUsersAsync(db, new[] { _world.EmpA1.Id, _world.EmpB1.Id }, June(1, 0), June(30, 23), null, DateTime.UtcNow));

        Assert.Equal(alone, together[_world.EmpA1.Id]);
        Assert.Equal(new[] { "export XB" }, together[_world.EmpB1.Id].Select(b => b.Title));
    }

    [SqlFact]
    public async Task Only_the_blocks_of_one_branch_are_given_when_one_branch_is_asked_for()
    {
        var inA = await _world.Db(db => TicketWorkBlocks.ForUsersAsync(db, new[] { _world.EmpA1.Id }, June(1, 0), June(30, 23), _world.BranchA, DateTime.UtcNow));

        Assert.Equal(new[] { "export X4", "export X1", "export Q" }, inA[_world.EmpA1.Id].Select(b => b.Title)); // not the ones on XB and XC, of branch B
    }

    [SqlFact]
    public async Task One_agent_asked_for_gets_only_their_own_rows()
    {
        using var book = await ExportAsync(_world.Admin, $"&agentId={_world.EmpA2.Id}");

        var rows = Timeline(book, "export");

        Assert.All(rows, r => Assert.Equal("empA2 Tester", r.Employee));
        Assert.Equal(3, rows.Count); // X2, P and the task
    }
}
