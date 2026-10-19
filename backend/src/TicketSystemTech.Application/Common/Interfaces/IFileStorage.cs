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

    /// <summary>Deletes a privately stored file by its stored reference. Returns false when there was nothing to delete.
    /// Files used to outlive the rows that pointed at them: deleting a document, or a failed text extraction, left the
    /// upload behind with no owner.</summary>
    bool Delete(string? reference);

    /// <summary>Deletes a file saved with <see cref="SavePublicImageAsync"/> by its public URL (e.g. a replaced profile picture).</summary>
    bool DeletePublicImage(string? url);
}
