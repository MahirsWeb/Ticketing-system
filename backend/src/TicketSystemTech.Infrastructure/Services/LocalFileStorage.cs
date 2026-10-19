using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Stores files on local disk. Ticket attachments and knowledge-base documents go to a private directory that the
/// web server does NOT serve (default <c>App_Data/uploads</c>, override with <c>Storage:UploadsPath</c> — point it
/// at a path outside the deploy folder so a redeploy cannot wipe it). They are only reachable through the
/// authorised, signed download endpoint. Profile pictures are the one exception: they live under
/// <c>wwwroot/avatars</c> so an &lt;img&gt; tag can load them, and only raster images are ever written there.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _privateDir;

    public LocalFileStorage(IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _env = env;
        _httpContextAccessor = httpContextAccessor;

        var configured = configuration["Storage:UploadsPath"];
        _privateDir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : Path.GetFullPath(configured, env.ContentRootPath);
    }

    public async Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        var key = await WriteAsync(_privateDir, fileName, content, ct);
        return $"/uploads/{key}";
    }

    public async Task<string> SavePublicImageAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var key = await WriteAsync(Path.Combine(webRoot, "avatars"), fileName, content, ct);

        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null ? $"{request.Scheme}://{request.Host}" : "";
        return $"{baseUrl}/avatars/{key}";
    }

    public Stream? OpenRead(string key)
    {
        if (!StoredFileKey.IsSafe(key)) return null;

        var path = Path.Combine(_privateDir, key);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    private static async Task<string> WriteAsync(string directory, string fileName, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);

        var key = StoredFileKey.NewFor(fileName);
        await using var fileStream = File.Create(Path.Combine(directory, key));
        await content.CopyToAsync(fileStream, ct);
        return key;
    }
}
