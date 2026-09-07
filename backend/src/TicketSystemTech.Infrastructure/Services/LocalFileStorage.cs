using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Stores files on local disk under wwwroot/uploads. Suitable for the MVP/demo.
/// Swap for a Supabase Storage (or S3-compatible) implementation of IFileStorage for production —
/// Render's free-tier disk is ephemeral and is wiped on every redeploy/restart.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LocalFileStorage(IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
    {
        _env = env;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadsDir = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var safeExtension = Path.GetExtension(fileName);
        var storedName = $"{Guid.NewGuid()}{safeExtension}";
        var fullPath = Path.Combine(uploadsDir, storedName);

        await using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream, ct);
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null ? $"{request.Scheme}://{request.Host}" : "";
        return $"{baseUrl}/uploads/{storedName}";
    }
}
