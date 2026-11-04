namespace TicketSystemTech.Application.Common;

/// <summary>
/// The branch has no connected mailbox, so there is nothing to read from or import from. It used to be an
/// <see cref="InvalidOperationException"/> that two actions turned into a 400 and two others let become a 500; now every
/// action gets the same answer (409, code <c>MAILBOX_NOT_CONNECTED</c>) from the one place that translates exceptions.
/// </summary>
public sealed class MailboxNotConnectedException : Exception
{
    public MailboxNotConnectedException() : base("No mailbox is connected for your branch.") { }
}
