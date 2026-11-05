namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Where uploaded files live. Every operation is asynchronous so that an implementation that keeps files somewhere else (a blob store, a
/// network share) fits without blocking a request thread; the one that exists, <c>LocalFileStorage</c>, keeps them in a folder
/// (<c>Storage:UploadsPath</c>, which must be outside the site and part of the backup). The database holds only the stored reference.
/// Links a browser can open are made from a reference by <see cref="IFileLinkService"/>.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Saves a file privately (outside anything the web server serves directly) and returns its stored
    /// reference (<c>/uploads/{key}</c>). Turn that into something a browser can open with
    /// <see cref="IFileLinkService.ToDownloadUrl"/>.
    /// </summary>
    Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);

    /// <summary>Saves a raster image that is meant to be shown to every signed-in user (profile pictures)
    /// and returns its stored reference (<c>/avatars/{file}</c>), to be turned into a URL with
    /// <see cref="IFileLinkService.ToPublicUrl"/>.</summary>
    Task<string> SavePublicImageAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);

    /// <summary>Opens a privately stored file by its key, or returns null when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes a privately stored file by its stored reference. Returns false when there was nothing to delete.
    /// Files used to outlive the rows that pointed at them: deleting a document, or a failed text extraction, left the
    /// upload behind with no owner.</summary>
    Task<bool> DeleteAsync(string? reference, CancellationToken ct = default);

    /// <summary>Deletes a file saved with <see cref="SavePublicImageAsync"/> by its public URL (e.g. a replaced profile picture).</summary>
    Task<bool> DeletePublicImageAsync(string? url, CancellationToken ct = default);
}
