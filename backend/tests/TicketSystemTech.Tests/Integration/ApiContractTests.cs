using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// T-41: the frontend's types are written by hand, so nothing noticed when a response changed shape on the server (a field renamed,
/// one that can now be null). The API's description is kept in <c>docs/openapi.json</c>; the frontend generates its types from that
/// file and its build checks the hand-written ones against them. This test is the other half: the file must say what the API
/// really does, so a change to a request or response cannot be committed without the file (and then the frontend check) seeing it.
///
/// After an intended change: <c>TST_UPDATE_OPENAPI=1 dotnet test --filter ApiContractTests</c> rewrites the file, then
/// <c>npm run gen:api</c> in <c>frontend</c> regenerates the types.
/// </summary>
[Collection(ApiCollection.Name)]
public class ApiContractTests
{
    private readonly ApiWorld _world;

    public ApiContractTests(ApiWorld world) => _world = world;

    /// <summary>The same text for the same API on every machine: keys sorted, and the lists that carry no order sorted too.</summary>
    private static JsonNode Canonical(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var value = property.Value is null ? null : Canonical(property.Value);
                    // `required` and `tags` are sets of names: their order carries nothing and must not decide whether the file matches
                    if (property.Key is "required" or "tags" && value is JsonArray names && names.All(n => n is JsonValue))
                        value = new JsonArray(names.Select(n => n!.GetValue<string>()).Order(StringComparer.Ordinal).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
                    sorted[property.Key] = value;
                }
                return sorted;
            case JsonArray array:
                return new JsonArray(array.Select(i => i is null ? null : Canonical(i)).ToArray());
            default:
                return node.DeepClone();
        }
    }

    private static string RepositoryFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "backend")) && Directory.Exists(Path.Combine(dir.FullName, "docs"))))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }

    private async Task<string> CurrentContractAsync()
    {
        using var scope = _world.Factory.Services.CreateScope();
        var document = await Task.Run(() => scope.ServiceProvider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1"));
        var node = JsonNode.Parse(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0))!;
        return Canonical(node).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n") + "\n"; // the same line ends on Windows and on Linux
    }

    [SqlFact]
    public async Task The_description_of_the_API_in_the_repository_is_what_the_API_really_does()
    {
        var path = RepositoryFile(Path.Combine("docs", "openapi.json"));
        var current = await CurrentContractAsync();

        if (Environment.GetEnvironmentVariable("TST_UPDATE_OPENAPI") == "1")
            await File.WriteAllTextAsync(path, current, new System.Text.UTF8Encoding(false), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path), "docs/openapi.json is missing; create it with TST_UPDATE_OPENAPI=1");
        var committed = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Replace("\r\n", "\n");

        Assert.True(committed == current,
            "The API no longer matches docs/openapi.json. If the change is intended, run the tests once with TST_UPDATE_OPENAPI=1 " +
            "and then `npm run gen:api` in frontend, and commit both.");
    }
}
