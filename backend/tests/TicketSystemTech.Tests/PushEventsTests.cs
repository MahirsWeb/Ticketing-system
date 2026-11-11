using System.Text.RegularExpressions;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests;

/// <summary>
/// T-47: the real-time events have one list of names, and the browser listens for them by those names. A name that differs by a letter on either
/// side is not an error anywhere: the screen just stops being told. So the list here is held to what was in use, to what the notifier pushes, and
/// to the list in <c>NotificationBell.tsx</c>.
/// </summary>
public partial class PushEventsTests
{
    private sealed class Pushes : INotificationPublisher
    {
        public List<string> Names { get; } = new();

        public Task PushToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        {
            Names.Add(eventName);
            return Task.CompletedTask;
        }

        public Task PushToRoleAsync(UserRole role, string eventName, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void The_names_are_the_ones_that_were_in_use_before_they_were_written_once()
    {
        Assert.Equal(
            new[] { "newTicket", "ticketOpened", "ticketAssigned", "ticketClosed", "newResponse", "userActivatedViaInvite", "ticketTransferred", "ticketOverdue", "taskAssigned", "ticketUnassigned" },
            PushEvents.All);
    }

    [Fact]
    public void No_name_is_there_twice()
    {
        Assert.Equal(PushEvents.All.Count, PushEvents.All.Distinct().Count());
    }

    [Fact]
    public async Task Everything_the_notifier_pushes_is_a_name_on_the_list_and_each_of_its_events_has_one()
    {
        var pushes = new Pushes();
        var notifier = new Notifier(pushes);
        var person = new ApplicationUser { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Anić", Email = "ana@x.test" };
        var ticket = new Ticket { Id = Guid.NewGuid(), TicketNumber = "100001", Title = "t" };
        var task = new WorkTask { Id = Guid.NewGuid(), Title = "w" };
        var staff = new[] { person };

        foreach (var @event in new NotificationEvent[]
                 {
                     new NewTicketEvent(ticket, staff, person, "Acme"),
                     new TicketOpenedEvent(ticket, person, person),
                     new TicketAssignedEvent(ticket, person),
                     new TicketTransferredEvent(ticket, "A", "B", person, staff),
                     new TicketClosedEvent(ticket, person, person),
                     new NewResponseEvent(ticket, staff),
                     new TaskAssignedEvent(task, person, person),
                     new TicketOverdueEvent(ticket.Id, "100001", "t", "late", staff),
                 })
            await notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.All(pushes.Names, n => Assert.Contains(n, PushEvents.All));
        // the two that no ticket event makes are pushed by the services that know of them
        var others = new[] { PushEvents.UserActivatedViaInvite, PushEvents.TicketUnassigned };
        Assert.Equal(PushEvents.All.Except(others).OrderBy(n => n), pushes.Names.Distinct().OrderBy(n => n));
    }

    [GeneratedRegex(@"const EVENTS = \[(?<names>[^\]]*)\]")]
    private static partial Regex BrowserList();

    private static string? FindBell()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "frontend", "src", "components", "NotificationBell.tsx");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    [Fact]
    public void The_browser_listens_for_exactly_these_names()
    {
        var bell = FindBell();
        Assert.SkipWhen(bell is null, "The source of the browser is not next to the tests (they were not run from a checkout of the repository).");

        var match = BrowserList().Match(File.ReadAllText(bell!));
        Assert.True(match.Success, "The list of events could not be found in NotificationBell.tsx (is it still called EVENTS?)");
        var listened = Regex.Matches(match.Groups["names"].Value, "'([A-Za-z]+)'").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(PushEvents.All.OrderBy(n => n), listened.OrderBy(n => n));
    }
}
