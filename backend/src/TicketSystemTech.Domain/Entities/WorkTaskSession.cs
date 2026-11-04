using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// One stretch of work on a task. Starting a task again adds a session; the earlier ones stay, as they do for tickets. A task
/// used to hold a single start and a single end, so the second time somebody worked on it the first was overwritten.
/// </summary>
public class WorkTaskSession : BaseEntity
{
    public Guid WorkTaskId { get; set; }
    public WorkTask? WorkTask { get; set; }

    /// <summary>Who did the work. Stays with the session if the task is later handed to somebody else.</summary>
    public Guid UserId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Null while the session is running (the "stop" button, or a manual correction, ends it).</summary>
    public DateTime? EndedAtUtc { get; set; }
}
