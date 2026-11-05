using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TicketSystemTech.Api.BackgroundJobs;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The job that warns the assignees of a ticket that has run past its due date. NOVO-DOM-04: a ticket waiting to be confirmed
/// ("Resolved") is still live and was never warned about; NOVO-ARC-10: the database now picks the tickets instead of the job loading all of them.
/// </summary>
[Collection(ApiCollection.Name)]
public class OverdueNotifierTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private (Guid Id, bool NotifyOnSlaBreach, int? ManualOverdueDays)? _existingSettings;
    private Guid? _createdSettings;

    public OverdueNotifierTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Notifications.Where(n => n.TicketId != null && _tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.EmailOutbox.Where(m => m.Subject.Contains("OV-")).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            if (_createdSettings is { } created) await db.OverdueNotificationSettings.Where(s => s.Id == created).ExecuteDeleteAsync();
            if (_existingSettings is { } old)
                await db.OverdueNotificationSettings.Where(s => s.Id == old.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NotifyOnSlaBreach, old.NotifyOnSlaBreach).SetProperty(x => x.ManualOverdueDays, old.ManualOverdueDays));
            return 0;
        });
    }

    private async Task UseSettingsAsync(bool notifyOnSlaBreach, int? manualOverdueDays)
    {
        await _world.Db(async db =>
        {
            var row = await db.OverdueNotificationSettings.FirstOrDefaultAsync();
            if (row is null)
            {
                row = new OverdueNotificationSettings();
                db.OverdueNotificationSettings.Add(row);
                _createdSettings = row.Id;
            }
            else
            {
                _existingSettings ??= (row.Id, row.NotifyOnSlaBreach, row.ManualOverdueDays);
            }
            row.NotifyOnSlaBreach = notifyOnSlaBreach;
            row.ManualOverdueDays = manualOverdueDays;
            return await db.SaveChangesAsync();
        });
        if (_createdSettings == Guid.Empty) _createdSettings = await _world.Db(db => db.OverdueNotificationSettings.Select(s => (Guid?)s.Id).FirstOrDefaultAsync());
    }

    private async Task<Guid> TicketAsync(TicketStatus status, DateTime? due, DateTime opened)
    {
        var ticket = new Ticket
        {
            TicketNumber = "OV-" + Guid.NewGuid().ToString("N")[..8],
            Title = "overdue",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            DueDateUtc = due,
            OpenedAtUtc = opened
        };
        ticket.Assignees.Add(new TicketAssignee { UserId = _world.EmpA1.Id, IsPrimary = true });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private Task<int> RunJobAsync()
    {
        var job = new OverdueTicketNotifier(_world.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OverdueTicketNotifier>.Instance, new FakeTimeProvider(DateTimeOffset.UtcNow));
        return job.CheckOnceAsync(CancellationToken.None);
    }

    private Task<int> WarningsAboutAsync(Guid ticket) =>
        _world.Db(db => db.Notifications.CountAsync(n => n.TicketId == ticket && n.Type == NotificationType.TicketOverdue && n.UserId == _world.EmpA1.Id));

    [SqlFact]
    public async Task A_resolved_ticket_past_its_due_date_is_warned_about_once_and_a_closed_one_never()
    {
        await UseSettingsAsync(true, null);
        var resolved = await TicketAsync(TicketStatus.Resolved, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddDays(-2));
        var closed = await TicketAsync(TicketStatus.Closed, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddDays(-2));
        var notDue = await TicketAsync(TicketStatus.InProgress, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(-2));
        var noDueDate = await TicketAsync(TicketStatus.Open, null, DateTime.UtcNow.AddDays(-2));

        var first = await RunJobAsync();
        var second = await RunJobAsync();

        Assert.True(first >= 1);
        Assert.Equal(1, await WarningsAboutAsync(resolved));
        Assert.Equal(0, await WarningsAboutAsync(closed));
        Assert.Equal(0, await WarningsAboutAsync(notDue));
        Assert.Equal(0, await WarningsAboutAsync(noDueDate));
        Assert.Equal(1, await WarningsAboutAsync(resolved)); // the second pass found nothing new
        Assert.Equal(0, second);
    }

    [SqlFact]
    public async Task The_days_open_rule_picks_a_ticket_without_a_due_date_and_leaves_a_recent_one_alone()
    {
        await UseSettingsAsync(false, 2);
        var old = await TicketAsync(TicketStatus.Open, null, DateTime.UtcNow.AddDays(-3));
        var recent = await TicketAsync(TicketStatus.Open, null, DateTime.UtcNow.AddHours(-5));

        await RunJobAsync();

        Assert.Equal(1, await WarningsAboutAsync(old));
        Assert.Equal(0, await WarningsAboutAsync(recent));
    }

    [SqlFact]
    public async Task The_warning_is_also_mailed_to_the_assignee_and_says_which_rule_it_broke()
    {
        await UseSettingsAsync(true, 2);
        var pastDue = await TicketAsync(TicketStatus.InProgress, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddDays(-1));
        var open = await TicketAsync(TicketStatus.Open, null, DateTime.UtcNow.AddDays(-3));
        var numbers = await _world.Db(db => db.Tickets.Where(t => t.Id == pastDue || t.Id == open).ToDictionaryAsync(t => t.Id, t => t.TicketNumber));

        await RunJobAsync();

        var mails = await _world.Db(db => db.EmailOutbox.AsNoTracking().ToListAsync());
        var duePastMail = Assert.Single(mails, m => m.Subject == $"Ticket #{numbers[pastDue]} is overdue");
        Assert.Equal(_world.EmpA1.Email, duePastMail.ToEmail);
        Assert.Contains("it is past its SLA due date (", duePastMail.HtmlBody);
        var openMail = Assert.Single(mails, m => m.Subject == $"Ticket #{numbers[open]} is overdue");
        Assert.Equal(_world.EmpA1.Email, openMail.ToEmail);
        Assert.Contains("it has been open for more than 2 day(s)", openMail.HtmlBody);
    }

    [SqlFact]
    public async Task Nothing_is_sent_when_the_warnings_are_switched_off()
    {
        await UseSettingsAsync(false, null);
        var overdue = await TicketAsync(TicketStatus.Open, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddDays(-2));

        await RunJobAsync();

        Assert.Equal(0, await WarningsAboutAsync(overdue));
    }
}
