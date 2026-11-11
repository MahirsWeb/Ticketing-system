using TicketSystemTech.Domain.Entities;

namespace TicketSystemTech.Infrastructure.Persistence;

public static class EmailOutboxExtensions
{
    private const int MaxSubjectLength = 500;

    /// <summary>
    /// Files an e-mail in the outbox as part of the caller's own unit of work: the row is added to <paramref name="db"/> and is saved by the
    /// same <c>SaveChanges</c> as the change the e-mail is about, so either both exist or neither does. (The sender that opens a context of its
    /// own cannot promise that: a failure after the change was saved loses the mail without anybody seeing it.) A subject built from a long
    /// ticket title is cut, so that it cannot make the whole save fail.
    /// </summary>
    public static EmailOutboxItem StageEmail(this AppDbContext db, string toEmail, string subject, string htmlBody)
    {
        var shortSubject = subject.Length <= MaxSubjectLength ? subject : subject[..MaxSubjectLength];
        var item = new EmailOutboxItem { ToEmail = toEmail, Subject = shortSubject, HtmlBody = htmlBody };
        db.EmailOutbox.Add(item);
        return item;
    }
}
