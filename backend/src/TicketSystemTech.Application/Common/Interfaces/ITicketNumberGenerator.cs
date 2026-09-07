namespace TicketSystemTech.Application.Common.Interfaces;

public interface ITicketNumberGenerator
{
    /// <summary>Generates the next unique, human-friendly ticket number (e.g. "100042").</summary>
    Task<string> NextAsync(CancellationToken ct = default);
}
