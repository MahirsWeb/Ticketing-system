using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// NOVO-SEC-05: before uploads became private they were saved in wwwroot/uploads and served to anybody. An installation that was
/// upgraded still has those files there. The application no longer serves that folder, whoever asks and whatever is in it.
/// </summary>
[Collection(ApiCollection.Name)]
public class LegacyUploadsTests
{
    private readonly ApiWorld _world;

    public LegacyUploadsTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task A_file_left_in_the_old_public_uploads_folder_is_not_served()
    {
        var webRoot = _world.Factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var folder = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(folder);
        var name = $"legacy-{Guid.NewGuid():N}.txt";
        var path = Path.Combine(folder, name);
        await File.WriteAllTextAsync(path, "an old attachment");
        try
        {
            Assert.Equal(404, (await _world.Get(null, $"/uploads/{name}")).Code);
            Assert.Equal(404, (await _world.Get(_world.Admin, $"/uploads/{name}")).Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public async Task The_rest_of_the_web_root_is_still_served()
    {
        var webRoot = _world.Factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var folder = Path.Combine(webRoot, "avatars");
        Directory.CreateDirectory(folder);
        var name = $"probe-{Guid.NewGuid():N}.txt";
        var path = Path.Combine(folder, name);
        await File.WriteAllTextAsync(path, "x");
        try
        {
            Assert.Equal(200, (await _world.Get(null, $"/avatars/{name}")).Code); // the positive control: static files are not switched off
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public async Task Slash_tricks_in_the_path_do_not_reach_the_old_uploads_folder_either()
    {
        var webRoot = _world.Factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var folder = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(folder);
        var name = $"legacy-{Guid.NewGuid():N}.txt";
        var path = Path.Combine(folder, name);
        await File.WriteAllTextAsync(path, "an old attachment");
        try
        {
            // A request for "//uploads/<file>" (an empty first segment) once slipped past a check on the path's prefix and was served.
            // Absolute addresses are used because a relative "//uploads/..." would be read as a host name.
            foreach (var tricky in new[] { "//uploads/", "///uploads/", "/uploads//", "/./uploads/", "/avatars/../uploads/" })
            {
                var response = await _world.Get(null, $"http://localhost{tricky}{name}");
                Assert.NotEqual(200, response.Code);
                Assert.DoesNotContain("an old attachment", response.Text);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
