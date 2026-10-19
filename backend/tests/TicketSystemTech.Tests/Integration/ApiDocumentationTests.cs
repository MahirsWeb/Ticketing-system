using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The Swagger page (Development only) is built from every action of the API. A single action that its generator cannot
/// describe, e.g. a file upload declared in a way a newer version of the library rejects, makes the whole document fail with a
/// 500, and nobody notices until they open the page. This builds the document the way the page does.
/// </summary>
[Collection(ApiCollection.Name)]
public class ApiDocumentationTests
{
    private readonly ApiWorld _world;

    public ApiDocumentationTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task The_swagger_document_can_be_generated_for_every_action()
    {
        using var scope = _world.Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        var document = await Task.Run(() => provider.GetSwagger("v1"));

        Assert.True(document.Paths.Count > 80, $"only {document.Paths.Count} paths were documented");
        Assert.Contains("/api/users/{id}/profile-picture", document.Paths.Keys);
        Assert.Contains("/api/tickets/{ticketId}/attachments", document.Paths.Keys);
        Assert.Contains("/api/knowledge-base/documents", document.Paths.Keys);
        Assert.Contains("Bearer", document.Components!.SecuritySchemes!.Keys);

        // The three upload actions must be described as file forms, otherwise Swagger UI cannot be used to try them.
        foreach (var path in new[] { "/api/users/{id}/profile-picture", "/api/tickets/{ticketId}/attachments", "/api/knowledge-base/documents" })
        {
            var post = document.Paths[path].Operations![System.Net.Http.HttpMethod.Post];
            Assert.Contains("multipart/form-data", post.RequestBody!.Content!.Keys);
        }
    }
}
