using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests;

/// <summary>
/// T-47: what a person is told about each kind of happening (the line in their list, the e-mail, the push) is written in one place.
/// These tests pin the words the application has always used, so a change to them is a decision and not an accident of moving code.
/// </summary>
public class NotifierTests
{
    private sealed class Mails : IEmailSender
    {
        public List<(string To, string Subject, string Html)> Sent { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((toEmail, subject, htmlBody));
            Tokens.Add(ct);
            return Task.CompletedTask;
        }
    }

    private sealed class Pushes : INotificationPublisher
    {
        public List<(Guid User, string Event, object Payload)> Sent { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();

        public Task PushToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        {
            Sent.Add((userId, eventName, payload));
            Tokens.Add(ct);
            return Task.CompletedTask;
        }

        public Task PushToRoleAsync(UserRole role, string eventName, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly Mails _mails = new();
    private readonly Pushes _pushes = new();
    private Notifier Notifier => new(_mails, _pushes);

    private static ApplicationUser Person(string first, string last) =>
        new() { Id = Guid.NewGuid(), FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}@example.test" };

    private static readonly Ticket Ticket = new() { Id = Guid.NewGuid(), TicketNumber = "100123", Title = "Printer ne radi" };

    private static string PushedTicketNumber(object payload) => (string)payload.GetType().GetProperty("TicketNumber")!.GetValue(payload)!;

    [Fact]
    public async Task A_new_ticket_is_announced_to_each_member_of_the_branch()
    {
        var ana = Person("Ana", "Anić");
        var bojan = Person("Bojan", "Bošnjak");
        var client = Person("Klijent", "Klijentić");
        var @event = new NewTicketEvent(Ticket, new[] { ana, bojan }, client, "Acme");

        var lines = Notifier.InApp(@event);
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { ana.Id, bojan.Id }, lines.Select(l => l.UserId));
        Assert.All(lines, l =>
        {
            Assert.Equal(NotificationType.NewTicket, l.Type);
            Assert.Equal("New ticket #100123: Printer ne radi", l.Message);
            Assert.Equal(Ticket.Id, l.TicketId);
        });
        Assert.Equal(new[] { "ana@example.test", "bojan@example.test" }, _mails.Sent.Select(m => m.To));
        Assert.All(_mails.Sent, m => Assert.Equal("New ticket #100123 submitted — Acme", m.Subject));
        Assert.Equal(new[] { "newTicket", "newTicket" }, _pushes.Sent.Select(p => p.Event));
    }

    [Fact]
    public async Task Opening_a_ticket_tells_the_assignee_it_is_theirs_and_the_client_it_is_open()
    {
        var assignee = Person("Marko", "Marković");
        var client = Person("Klijent", "Klijentić");
        var @event = new TicketOpenedEvent(Ticket, client, assignee);

        var lines = Notifier.InApp(@event);
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { (assignee.Id, NotificationType.TicketAssigned, "Ticket #100123 assigned to you"), (client.Id, NotificationType.TicketOpened, "Your ticket #100123 has been opened") },
            lines.Select(l => (l.UserId, l.Type, l.Message)));
        Assert.Contains(_mails.Sent, m => m.To == "marko@example.test" && m.Subject == "Ticket #100123 assigned to you");
        Assert.Contains(_mails.Sent, m => m.To == "klijent@example.test" && m.Subject == "Your ticket #100123 has been opened");
        Assert.Contains(_pushes.Sent, p => p.User == assignee.Id && p.Event == "ticketAssigned");
        Assert.Contains(_pushes.Sent, p => p.User == client.Id && p.Event == "ticketOpened");
    }

    [Fact]
    public async Task Adding_somebody_uses_the_same_words_as_opening_a_ticket_for_them()
    {
        var added = Person("Selma", "Selimović");

        var lines = Notifier.InApp(new TicketAssignedEvent(Ticket, added));
        await Notifier.DeliverAsync(new TicketAssignedEvent(Ticket, added), TestContext.Current.CancellationToken);

        var line = Assert.Single(lines);
        Assert.Equal((added.Id, NotificationType.TicketAssigned, "Ticket #100123 assigned to you"), (line.UserId, line.Type, line.Message));
        Assert.Equal("Ticket #100123 assigned to you", Assert.Single(_mails.Sent).Subject);
        Assert.Equal("ticketAssigned", Assert.Single(_pushes.Sent).Event);
    }

    [Fact]
    public async Task A_transfer_tells_the_branch_that_received_the_ticket_where_it_came_from()
    {
        var staff = Person("Dino", "Dinić");
        var by = Person("Ana", "Anić");
        var @event = new TicketTransferredEvent(Ticket, "(unrouted)", "Mostar", by, new[] { staff });

        var line = Assert.Single(Notifier.InApp(@event));
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal((NotificationType.TicketTransferred, "Ticket #100123 transferred to Mostar"), (line.Type, line.Message));
        var mail = Assert.Single(_mails.Sent);
        Assert.Equal("Ticket #100123 transferred to your branch", mail.Subject);
        Assert.Contains("from <b>(unrouted)</b> to <b>Mostar</b> by Ana Anić", mail.Html);
        var push = Assert.Single(_pushes.Sent);
        Assert.Equal("ticketTransferred", push.Event);
        Assert.Equal("Mostar", (string)push.Payload.GetType().GetProperty("Branch")!.GetValue(push.Payload)!);
    }

    [Fact]
    public async Task Closing_a_ticket_tells_its_client_it_has_been_resolved()
    {
        var client = Person("Klijent", "Klijentić");
        var by = Person("Marko", "Marković");
        var @event = new TicketClosedEvent(Ticket, client, by);

        var line = Assert.Single(Notifier.InApp(@event));
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal((client.Id, NotificationType.TicketClosed, "Ticket #100123 has been resolved"), (line.UserId, line.Type, line.Message));
        var mail = Assert.Single(_mails.Sent);
        Assert.Equal("Your ticket #100123 has been resolved", mail.Subject);
        Assert.Contains("Marko Marković", mail.Html);
        Assert.Equal(("ticketClosed", "100123"), (_pushes.Sent[0].Event, PushedTicketNumber(_pushes.Sent[0].Payload)));
    }

    [Fact]
    public async Task A_reply_is_announced_to_everybody_it_is_for()
    {
        var one = Person("Ana", "Anić");
        var two = Person("Bojan", "Bošnjak");
        var @event = new NewResponseEvent(Ticket, new[] { one, two });

        var lines = Notifier.InApp(@event);
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("New reply on ticket #100123", l.Message));
        Assert.All(_mails.Sent, m => Assert.Equal("New reply on ticket #100123", m.Subject));
        Assert.Equal(new[] { one.Id, two.Id }, _pushes.Sent.Select(p => p.User));
        Assert.All(_pushes.Sent, p => Assert.Equal("newResponse", p.Event));
    }

    [Fact]
    public async Task A_task_is_announced_to_the_person_it_was_handed_to_without_pointing_at_a_ticket()
    {
        var assignee = Person("Selma", "Selimović");
        var by = Person("Ana", "Anić");
        var task = new WorkTask { Id = Guid.NewGuid(), Title = "Provjeriti ugovor" };
        var @event = new TaskAssignedEvent(task, assignee, by);

        var line = Assert.Single(Notifier.InApp(@event));
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal((NotificationType.TaskAssigned, "Ana Anić assigned you a task: \"Provjeriti ugovor\""), (line.Type, line.Message));
        Assert.Null(line.TicketId);
        Assert.Equal("A task has been assigned to you", Assert.Single(_mails.Sent).Subject);
        Assert.Equal("taskAssigned", Assert.Single(_pushes.Sent).Event);
    }

    [Fact]
    public async Task A_late_ticket_warns_everybody_working_on_it_and_says_why()
    {
        var one = Person("Ana", "Anić");
        var two = Person("Bojan", "Bošnjak");
        var id = Guid.NewGuid();
        var @event = new TicketOverdueEvent(id, "100123", "Printer ne radi", "it has been open for more than 3 day(s)", new[] { one, two });

        var lines = Notifier.InApp(@event);
        await Notifier.DeliverAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { one.Id, two.Id }, lines.Select(l => l.UserId));
        Assert.All(lines, l =>
        {
            Assert.Equal(NotificationType.TicketOverdue, l.Type);
            Assert.Equal("Ticket #100123 is overdue", l.Message);
            Assert.Equal(id, l.TicketId);
        });
        Assert.Equal(new[] { "ana@example.test", "bojan@example.test" }, _mails.Sent.Select(m => m.To));
        Assert.All(_mails.Sent, m =>
        {
            Assert.Equal("Ticket #100123 is overdue", m.Subject);
            Assert.Contains("it has been open for more than 3 day(s)", m.Html);
        });
        Assert.Equal(new[] { "ticketOverdue", "ticketOverdue" }, _pushes.Sent.Select(p => p.Event));
        Assert.Equal(id, (Guid)_pushes.Sent[0].Payload.GetType().GetProperty("Id")!.GetValue(_pushes.Sent[0].Payload)!);
        Assert.Equal("100123", PushedTicketNumber(_pushes.Sent[0].Payload));
    }

    [Fact]
    public async Task The_token_of_the_caller_reaches_the_mail_and_the_push_so_that_stopping_the_application_stops_the_delivery()
    {
        using var source = new CancellationTokenSource();
        var @event = new TicketAssignedEvent(Ticket, Person("Selma", "Selimović"));

        await Notifier.DeliverAsync(@event, source.Token);

        Assert.Equal(new[] { source.Token }, _mails.Tokens);
        Assert.Equal(new[] { source.Token }, _pushes.Tokens);
    }

    private sealed record SomethingNobodyWroteWordsFor : NotificationEvent;

    [Fact]
    public async Task An_event_without_words_is_an_error_and_not_a_silent_nothing()
    {
        var unknown = new SomethingNobodyWroteWordsFor();

        Assert.Throws<ArgumentOutOfRangeException>(() => Notifier.InApp(unknown));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Notifier.DeliverAsync(unknown, TestContext.Current.CancellationToken));
        Assert.Empty(_mails.Sent);
        Assert.Empty(_pushes.Sent);
    }

    [Fact]
    public async Task Nobody_is_told_before_deliver_is_called_and_nobody_is_told_twice_by_asking_for_the_lines()
    {
        var @event = new TicketAssignedEvent(Ticket, Person("Selma", "Selimović"));

        Notifier.InApp(@event);
        Notifier.InApp(@event);

        Assert.Empty(_mails.Sent);
        Assert.Empty(_pushes.Sent);
        await Task.CompletedTask;
    }
}
