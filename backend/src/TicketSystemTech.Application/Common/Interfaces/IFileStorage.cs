namespace TicketSystemTech.Application.Common.Interfaces;

public interface IFileStorage
{
    /// <summary>Saves a file and returns its publicly reachable URL.</summary>
    Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);
}
