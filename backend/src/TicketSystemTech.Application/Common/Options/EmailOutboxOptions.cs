namespace TicketSystemTech.Application.Common.Options;

/// <summary>Settings of the worker that delivers queued e-mail (section <c>Email:Outbox</c>).</summary>
public class EmailOutboxOptions
{
    /// <summary>Turns the worker off (the messages still pile up in the outbox). Used by the integration tests, which drive
    /// the worker by hand so that nothing else touches a message while a test is looking at it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker looks for something to send.</summary>
    public int PollSeconds { get; set; } = 5;

    /// <summary>How many messages one round takes.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>How long a message counts as "being sent" by a worker. Longer than the provider call can take; if the worker
    /// dies mid-send, the message becomes available again after this.</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Sent and skipped messages are deleted after this many days (their body is the text of a ticket update).</summary>
    public int KeepSentDays { get; set; } = 7;

    /// <summary>Messages that could not be delivered are kept longer, for someone to find out why.</summary>
    public int KeepFailedDays { get; set; } = 30;
}
