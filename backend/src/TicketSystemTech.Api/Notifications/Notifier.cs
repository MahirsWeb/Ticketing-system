using TicketSystemTech.Api.Emails;
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
/// The one place that knows how a happening becomes the three things a person can be given: a line in the notification list (kept in the same
/// transaction as the change itself), an e-mail (through the outbox) and a real-time push. A service says <i>what happened</i>; it does not
/// choose subjects, event names or the wording of the list.
/// </summary>
public interface INotifier
{
    /// <summary>The lines for the notification list. The caller adds them to its context, so they are saved together with the change they report.</summary>
    IReadOnlyList<Notification> InApp(NotificationEvent @event);

    /// <summary>The e-mails and the pushes. Called after the change was saved: a failure to reach somebody must not undo it.
    /// A background job passes its own token, so that stopping the application stops the delivery as well.</summary>
    Task DeliverAsync(NotificationEvent @event, CancellationToken ct = default);
}

public sealed class Notifier : INotifier
{
    private readonly IEmailSender _emailSender;
    private readonly INotificationPublisher _publisher;

    public Notifier(IEmailSender emailSender, INotificationPublisher publisher)
    {
        _emailSender = emailSender;
        _publisher = publisher;
    }

    /// <summary>One person's share of an event: the line, the mail and the push they get.</summary>
    private sealed record Delivery(
        ApplicationUser To, NotificationType Type, Guid? TicketId, string InAppText,
        string Subject, string Html, string PushEvent, object PushPayload);

    private static string FullName(ApplicationUser user) => $"{user.FirstName} {user.LastName}";

    private static IEnumerable<Delivery> Deliveries(NotificationEvent @event)
    {
        switch (@event)
        {
            case NewTicketEvent e:
                foreach (var member in e.Staff)
                    yield return new Delivery(member, NotificationType.NewTicket, e.Ticket.Id,
                        $"New ticket #{e.Ticket.TicketNumber}: {e.Ticket.Title}",
                        $"New ticket #{e.Ticket.TicketNumber} submitted — {e.CompanyName}",
                        EmailTemplates.NewTicketSubmitted(member.FirstName, e.Ticket.TicketNumber, e.Ticket.Title, FullName(e.Client), e.CompanyName),
                        "newTicket", new { e.Ticket.Id, e.Ticket.TicketNumber, e.Ticket.Title });
                break;

            case TicketOpenedEvent e:
                yield return AssignedTo(e.Ticket, e.Assignee);
                yield return new Delivery(e.Client, NotificationType.TicketOpened, e.Ticket.Id,
                    $"Your ticket #{e.Ticket.TicketNumber} has been opened",
                    $"Your ticket #{e.Ticket.TicketNumber} has been opened",
                    EmailTemplates.TicketOpened(e.Client.FirstName, e.Ticket.TicketNumber),
                    "ticketOpened", new { e.Ticket.Id, e.Ticket.TicketNumber, e.Ticket.Title });
                break;

            case TicketAssignedEvent e:
                yield return AssignedTo(e.Ticket, e.Assignee);
                break;

            case TicketTransferredEvent e:
                foreach (var member in e.Staff)
                    yield return new Delivery(member, NotificationType.TicketTransferred, e.Ticket.Id,
                        $"Ticket #{e.Ticket.TicketNumber} transferred to {e.ToBranch}",
                        $"Ticket #{e.Ticket.TicketNumber} transferred to your branch",
                        EmailTemplates.TicketTransferred(member.FirstName, e.Ticket.TicketNumber, e.Ticket.Title, e.FromBranch, e.ToBranch, FullName(e.By)),
                        "ticketTransferred", new { e.Ticket.Id, e.Ticket.TicketNumber, e.Ticket.Title, Branch = e.ToBranch });
                break;

            case TicketClosedEvent e:
                yield return new Delivery(e.Client, NotificationType.TicketClosed, e.Ticket.Id,
                    $"Ticket #{e.Ticket.TicketNumber} has been resolved",
                    $"Your ticket #{e.Ticket.TicketNumber} has been resolved",
                    EmailTemplates.TicketClosed(e.Client.FirstName, e.Ticket.TicketNumber, FullName(e.By)),
                    "ticketClosed", new { e.Ticket.Id, e.Ticket.TicketNumber });
                break;

            case NewResponseEvent e:
                foreach (var recipient in e.Recipients)
                    yield return new Delivery(recipient, NotificationType.NewResponse, e.Ticket.Id,
                        $"New reply on ticket #{e.Ticket.TicketNumber}",
                        $"New reply on ticket #{e.Ticket.TicketNumber}",
                        EmailTemplates.NewResponse(recipient.FirstName, e.Ticket.TicketNumber),
                        "newResponse", new { e.Ticket.Id, e.Ticket.TicketNumber });
                break;

            case TaskAssignedEvent e:
                yield return new Delivery(e.Assignee, NotificationType.TaskAssigned, null,
                    $"{FullName(e.By)} assigned you a task: \"{e.Task.Title}\"",
                    "A task has been assigned to you",
                    EmailTemplates.WorkTaskAssigned(e.Assignee.FirstName, e.Task.Title, FullName(e.By)),
                    "taskAssigned", new { e.Task.Id, e.Task.Title });
                break;

            case TicketOverdueEvent e:
                foreach (var assignee in e.Assignees)
                    yield return new Delivery(assignee, NotificationType.TicketOverdue, e.TicketId,
                        $"Ticket #{e.TicketNumber} is overdue",
                        $"Ticket #{e.TicketNumber} is overdue",
                        EmailTemplates.TicketOverdue(assignee.FirstName, e.TicketNumber, e.Title, e.Reason),
                        "ticketOverdue", new { Id = e.TicketId, e.TicketNumber, e.Title });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(@event), @event.GetType().Name, "No words have been written for this event.");
        }
    }

    // The same words whether a ticket was opened for somebody or somebody was added to it later.
    private static Delivery AssignedTo(Ticket ticket, ApplicationUser assignee) =>
        new(assignee, NotificationType.TicketAssigned, ticket.Id,
            $"Ticket #{ticket.TicketNumber} assigned to you",
            $"Ticket #{ticket.TicketNumber} assigned to you",
            EmailTemplates.TicketAssigned(assignee.FirstName, ticket.TicketNumber, ticket.Title),
            "ticketAssigned", new { ticket.Id, ticket.TicketNumber, ticket.Title });

    public IReadOnlyList<Notification> InApp(NotificationEvent @event) =>
        Deliveries(@event)
            .Select(d => new Notification { UserId = d.To.Id, Type = d.Type, Message = d.InAppText, TicketId = d.TicketId })
            .ToList();

    public async Task DeliverAsync(NotificationEvent @event, CancellationToken ct = default)
    {
        foreach (var d in Deliveries(@event))
        {
            await _emailSender.SendAsync(d.To.Email!, d.Subject, d.Html, ct);
            await _publisher.PushToUserAsync(d.To.Id, d.PushEvent, d.PushPayload, ct);
        }
    }
}
