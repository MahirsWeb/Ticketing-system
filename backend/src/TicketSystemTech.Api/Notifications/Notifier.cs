using TicketSystemTech.Api.Emails;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Api.Notifications;

/// <summary>Something that happened and that somebody is to be told about. What they are told, in which words and by which ways is decided in <see cref="Notifier"/> and nowhere else.</summary>
public abstract record NotificationEvent;

/// <summary>A client submitted a ticket: the staff of the branch it was routed to hear about it.</summary>
public sealed record NewTicketEvent(Ticket Ticket, IReadOnlyList<ApplicationUser> Staff, ApplicationUser Client, string CompanyName) : NotificationEvent;

/// <summary>A ticket was opened and given to somebody: the person it is assigned to and the client hear about it.</summary>
public sealed record TicketOpenedEvent(Ticket Ticket, ApplicationUser Client, ApplicationUser Assignee) : NotificationEvent;

/// <summary>Somebody was added to a ticket.</summary>
public sealed record TicketAssignedEvent(Ticket Ticket, ApplicationUser Assignee) : NotificationEvent;

/// <summary>A ticket was moved to another branch: everybody in the branch that received it hears about it.</summary>
public sealed record TicketTransferredEvent(Ticket Ticket, string FromBranch, string ToBranch, ApplicationUser By, IReadOnlyList<ApplicationUser> Staff) : NotificationEvent;

/// <summary>A ticket was closed: its client hears about it.</summary>
public sealed record TicketClosedEvent(Ticket Ticket, ApplicationUser Client, ApplicationUser By) : NotificationEvent;

/// <summary>A reply was written on a ticket: the other side hears about it.</summary>
public sealed record NewResponseEvent(Ticket Ticket, IReadOnlyList<ApplicationUser> Recipients) : NotificationEvent;

/// <summary>A task was handed to somebody.</summary>
public sealed record TaskAssignedEvent(WorkTask Task, ApplicationUser Assignee, ApplicationUser By) : NotificationEvent;

/// <summary>A live ticket is past its due date, or has been open longer than the administrators allow: everybody working on it hears about it.
/// It carries the few columns the warning needs and not the ticket, because the job that raises it never loads the ticket itself.</summary>
public sealed record TicketOverdueEvent(Guid TicketId, string TicketNumber, string Title, string Reason, IReadOnlyList<ApplicationUser> Assignees) : NotificationEvent;

/// <summary>
/// The one place that knows how a happening becomes the three things a person can be given: a line in the notification list and an e-mail
/// (both kept in the same transaction as the change itself) and a real-time push (after it). A service says <i>what happened</i>; it does
/// not choose subjects, event names or the wording of the list.
/// </summary>
public interface INotifier
{
    /// <summary>The lines for the notification list. The caller adds them to its context, so they are saved together with the change they report.</summary>
    IReadOnlyList<Notification> InApp(NotificationEvent @event);

    /// <summary>The e-mails, as outbox rows. Like the lines, the caller adds them to its context so that they are saved together with the
    /// change they report: an e-mail that was promised exists exactly when the change does, and the outbox worker delivers it from there.</summary>
    IReadOnlyList<EmailOutboxItem> Emails(NotificationEvent @event);

    /// <summary>The real-time pushes. Called after the change was saved: a failure to reach somebody must not undo it, and a push that is
    /// missed is harmless (the list has the line). A background job passes its own token, so that stopping the application stops the delivery as well.</summary>
    Task DeliverAsync(NotificationEvent @event, CancellationToken ct = default);
}

public sealed class Notifier : INotifier
{
    private readonly INotificationPublisher _publisher;

    public Notifier(INotificationPublisher publisher) => _publisher = publisher;

    /// <summary>One person's share of an event: the line, the mail and the push they get.</summary>
    private sealed record Delivery(
        ApplicationUser To, NotificationType Type, Guid? TicketId, string InAppText,
        string Subject, string Html, string PushEvent, object PushPayload);

    private static string FullName(ApplicationUser user) => $"{user.FirstName} {user.LastName}";

    /// <summary>The subject in the language the recipient chose (the e-mail itself is written in it by the template).</summary>
    private static string Pick(ApplicationUser to, string english, string bosnian) => EmailLanguage.Pick(to.PreferredLanguage, english, bosnian);

    private static IEnumerable<Delivery> Deliveries(NotificationEvent @event)
    {
        switch (@event)
        {
            case NewTicketEvent e:
                foreach (var member in e.Staff)
                    yield return new Delivery(member, NotificationType.NewTicket, e.Ticket.Id,
                        $"New ticket #{e.Ticket.TicketNumber}: {e.Ticket.Title}",
                        Pick(member, $"New ticket #{e.Ticket.TicketNumber} submitted — {e.CompanyName}", $"Novi tiket #{e.Ticket.TicketNumber} — {e.CompanyName}"),
                        EmailTemplates.NewTicketSubmitted(member.FirstName, e.Ticket.TicketNumber, e.Ticket.Title, FullName(e.Client), e.CompanyName, member.PreferredLanguage),
                        PushEvents.NewTicket, new { e.Ticket.Id, e.Ticket.TicketNumber, e.Ticket.Title });
                break;

            case TicketOpenedEvent e:
                yield return AssignedTo(e.Ticket, e.Assignee);
                yield return new Delivery(e.Client, NotificationType.TicketOpened, e.Ticket.Id,
                    $"Your ticket #{e.Ticket.TicketNumber} has been opened",
                    Pick(e.Client, $"Your ticket #{e.Ticket.TicketNumber} has been opened", $"Vaš tiket #{e.Ticket.TicketNumber} je otvoren"),
                    EmailTemplates.TicketOpened(e.Client.FirstName, e.Ticket.TicketNumber, e.Client.PreferredLanguage),
                    PushEvents.TicketOpened, new { e.Ticket.Id, e.Ticket.TicketNumber, e.Ticket.Title });
                break;

            case TicketAssignedEvent e:
                yield return AssignedTo(e.Ticket, e.Assignee);
                break;

            case TicketTransferredEvent e:
                foreach (var member in e.Staff)
                    yield return new Delivery(member, NotificationType.TicketTransferred, e.Ticket.Id,
                        $"Ticket #{e.Ticket.TicketNumber} transferred to {e.ToBranch}",
                        Pick(member, $"Ticket #{e.Ticket.TicketNumber} transferred to your branch", $"Tiket #{e.Ticket.TicketNumber} je premješten u vašu poslovnicu"),
                        EmailTemplates.TicketTransferred(member.FirstName, e.Ticket.TicketNumber, e.Ticket.Title, e.FromBranch, e.ToBranch, FullName(e.By), member.PreferredLanguage),
                        PushEvents.TicketTransferred, new { e.Ticket.Id, e.Ticket.TicketNumber, e.Ticket.Title, Branch = e.ToBranch });
                break;

            case TicketClosedEvent e:
                yield return new Delivery(e.Client, NotificationType.TicketClosed, e.Ticket.Id,
                    $"Ticket #{e.Ticket.TicketNumber} has been resolved",
                    Pick(e.Client, $"Your ticket #{e.Ticket.TicketNumber} has been resolved", $"Vaš tiket #{e.Ticket.TicketNumber} je riješen"),
                    EmailTemplates.TicketClosed(e.Client.FirstName, e.Ticket.TicketNumber, FullName(e.By), e.Client.PreferredLanguage),
                    PushEvents.TicketClosed, new { e.Ticket.Id, e.Ticket.TicketNumber });
                break;

            case NewResponseEvent e:
                foreach (var recipient in e.Recipients)
                    yield return new Delivery(recipient, NotificationType.NewResponse, e.Ticket.Id,
                        $"New reply on ticket #{e.Ticket.TicketNumber}",
                        Pick(recipient, $"New reply on ticket #{e.Ticket.TicketNumber}", $"Novi odgovor na tiketu #{e.Ticket.TicketNumber}"),
                        EmailTemplates.NewResponse(recipient.FirstName, e.Ticket.TicketNumber, recipient.PreferredLanguage),
                        PushEvents.NewResponse, new { e.Ticket.Id, e.Ticket.TicketNumber });
                break;

            case TaskAssignedEvent e:
                yield return new Delivery(e.Assignee, NotificationType.TaskAssigned, null,
                    $"{FullName(e.By)} assigned you a task: \"{e.Task.Title}\"",
                    Pick(e.Assignee, "A task has been assigned to you", "Dodijeljen vam je zadatak"),
                    EmailTemplates.WorkTaskAssigned(e.Assignee.FirstName, e.Task.Title, FullName(e.By), e.Assignee.PreferredLanguage),
                    PushEvents.TaskAssigned, new { e.Task.Id, e.Task.Title });
                break;

            case TicketOverdueEvent e:
                foreach (var assignee in e.Assignees)
                    yield return new Delivery(assignee, NotificationType.TicketOverdue, e.TicketId,
                        $"Ticket #{e.TicketNumber} is overdue",
                        Pick(assignee, $"Ticket #{e.TicketNumber} is overdue", $"Tiket #{e.TicketNumber} kasni"),
                        EmailTemplates.TicketOverdue(assignee.FirstName, e.TicketNumber, e.Title, e.Reason, assignee.PreferredLanguage),
                        PushEvents.TicketOverdue, new { Id = e.TicketId, e.TicketNumber, e.Title });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(@event), @event.GetType().Name, "No words have been written for this event.");
        }
    }

    // The same words whether a ticket was opened for somebody or somebody was added to it later.
    private static Delivery AssignedTo(Ticket ticket, ApplicationUser assignee) =>
        new(assignee, NotificationType.TicketAssigned, ticket.Id,
            $"Ticket #{ticket.TicketNumber} assigned to you",
            Pick(assignee, $"Ticket #{ticket.TicketNumber} assigned to you", $"Tiket #{ticket.TicketNumber} vam je dodijeljen"),
            EmailTemplates.TicketAssigned(assignee.FirstName, ticket.TicketNumber, ticket.Title, assignee.PreferredLanguage),
            PushEvents.TicketAssigned, new { ticket.Id, ticket.TicketNumber, ticket.Title });

    public IReadOnlyList<Notification> InApp(NotificationEvent @event) =>
        Deliveries(@event)
            .Select(d => new Notification { UserId = d.To.Id, Type = d.Type, Message = d.InAppText, TicketId = d.TicketId })
            .ToList();

    public IReadOnlyList<EmailOutboxItem> Emails(NotificationEvent @event) =>
        Deliveries(@event)
            .Where(d => !string.IsNullOrWhiteSpace(d.To.Email))
            .Select(d => new EmailOutboxItem { ToEmail = d.To.Email!, Subject = Shorten(d.Subject), HtmlBody = d.Html })
            .ToList();

    // The column holds 500 characters, and a subject made from a long ticket title must not make the whole save fail.
    private static string Shorten(string subject) => subject.Length <= 500 ? subject : subject[..500];

    public async Task DeliverAsync(NotificationEvent @event, CancellationToken ct = default)
    {
        foreach (var d in Deliveries(@event))
            await _publisher.PushToUserAsync(d.To.Id, d.PushEvent, d.PushPayload, ct);
    }
}
