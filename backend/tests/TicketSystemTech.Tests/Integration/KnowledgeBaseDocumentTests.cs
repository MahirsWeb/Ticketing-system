using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Tests.Integration;

/// <summary>Rules about the uploaded documentation that nothing checked until the knowledge-base code was moved out of its controller (T-15).</summary>
[Collection(ApiCollection.Name)]
public class KnowledgeBaseDocumentTests
{
    private readonly ApiWorld _world;

    public KnowledgeBaseDocumentTests(ApiWorld world) => _world = world;

    private static MultipartFormDataContent Workbook(string text)
    {
        var book = new NPOI.XSSF.UserModel.XSSFWorkbook();
        book.CreateSheet("Lista").CreateRow(0).CreateCell(0).SetCellValue(text);
        using var bytes = new MemoryStream();
        book.Write(bytes, leaveOpen: true);
        var file = new ByteArrayContent(bytes.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var form = new MultipartFormDataContent();
        form.Add(file, "files", "kb-" + Guid.NewGuid().ToString("N")[..6] + ".xlsx");
        return form;
    }

    [SqlFact]
    public async Task A_document_over_20_MB_is_not_taken_and_the_answer_says_which()
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[21 * 1024 * 1024]), "files", "preveliki.pdf");

        var response = await _world.Post(_world.Admin, "/api/knowledge-base/documents", form);

        Assert.Equal(400, response.Code);
        Assert.Contains("preveliki.pdf (exceeds 20 MB)", response.Json.GetProperty("skipped")[0].GetString());
    }

    [SqlFact]
    public async Task A_deleted_document_takes_its_chunks_and_its_stored_original_with_it()
    {
        var uploaded = await _world.Post(_world.Admin, "/api/knowledge-base/documents", Workbook("Uputstvo za dokument koji se brise " + Guid.NewGuid().ToString("N")));
        Assert.Equal(200, uploaded.Code);
        var id = uploaded.Json[0].GetProperty("id").GetGuid();
        var reference = await _world.Db(db => db.KnowledgeBaseDocuments.Where(d => d.Id == id).Select(d => d.FileUrl).SingleAsync());
        var key = Path.GetFileName(reference!.Replace('/', Path.DirectorySeparatorChar)); // what is stored is a reference; the file's key is its last part
        using var scope = _world.Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        Assert.NotEmpty(await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.DocumentId == id).ToListAsync()));
        await using (var before = await storage.OpenReadAsync(key, TestContext.Current.CancellationToken))
            Assert.NotNull(before); // it is there before (and closed again: a file that is open cannot be deleted on Windows)

        var deleted = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/knowledge-base/documents/{id}");

        Assert.Equal(204, deleted.Code);
        Assert.Empty(await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.DocumentId == id).ToListAsync()));
        Assert.Null(await storage.OpenReadAsync(key, TestContext.Current.CancellationToken));
        Assert.Equal(404, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/knowledge-base/documents/{id}")).Code); // and a second delete finds nothing
    }
}
