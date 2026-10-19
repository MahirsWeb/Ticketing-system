namespace TicketSystemTech.Application.Common.Interfaces;

public interface IFileStorage
{
    /// <summary>
    /// Saves a file privately (outside anything the web server serves directly) and returns its stored
    /// reference (<c>/uploads/{key}</c>). Turn that into something a browser can open with
    /// <see cref="IFileLinkService.ToDownloadUrl"/>.
    /// </summary>
    Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);

    /// <summary>Saves a raster image that is meant to be shown to every signed-in user (profile pictures)
    /// and returns its public URL.</summary>
    Task<string> SavePublicImageAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);

    /// <summary>Opens a privately stored file by its key, or returns null when it does not exist.</summary>
    Stream? OpenRead(string key);
}
