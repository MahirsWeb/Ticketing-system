using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-11.2: the knowledge base kept exactly the text that is embedded and sent to the AI provider, with every e-mail address,
/// telephone number and "password: ..." that somebody had typed into a note. It is now made safe before it is stored, and what
/// was stored earlier is made safe the same way when the application starts.
/// </summary>
[Collection(ApiCollection.Name)]
public class KnowledgeBaseRedactionTests : IAsyncLifetime
{
    private const string Typed = "Kontakt: Test Osoba, test.osoba@primjer.tst, +387 61 000 000, lozinka: Proba123 — rješenje je bilo pomjeriti backup job van radnog vremena.";

    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();

    public KnowledgeBaseRedactionTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.KnowledgeBaseChunks.Where(c => c.TicketId != null && _tickets.Contains(c.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> ClosedTicketWithNoteAsync(string note)
    {
        var ticket = new Ticket
        {
            TicketNumber = "KB-" + Guid.NewGuid().ToString("N")[..8],
            Title = "Backup se poklapa s opterećenjem",
            Description = "<p>Opis</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.Closed,
            ClosedAtUtc = DateTime.UtcNow,
            ResolutionSummary = "Backup pomjeren.",
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        await _world.Db(async db =>
        {
            db.Tickets.Add(ticket);
            db.TicketMessages.Add(new TicketMessage { TicketId = ticket.Id, AuthorId = _world.EmpA1.Id, Type = MessageType.InternalNote, BodyHtml = $"<p>{note}</p>" });
            return await db.SaveChangesAsync();
        });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    [SqlFact]
    public async Task A_new_chunk_is_stored_without_the_personal_data_that_was_typed_into_the_note()
    {
        var id = await ClosedTicketWithNoteAsync(Typed);

        using var scope = _world.Factory.Services.CreateScope();
        var indexed = await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(id);
        var content = await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.TicketId == id).Select(c => c.Content).SingleAsync());

        Assert.True(indexed);
        Assert.DoesNotContain("test.osoba@primjer.tst", content);
        Assert.DoesNotContain("+387 61 000 000", content);
        Assert.DoesNotContain("Proba123", content);
        Assert.Contains("[email]", content);
        Assert.Contains("[phone]", content);
        Assert.Contains("lozinka: [secret]", content);
        Assert.Contains("pomjeriti backup job", content); // the useful part is still there
    }

    [SqlFact]
    public async Task The_names_of_the_people_on_the_ticket_are_not_kept_in_the_chunk()
    {
        var (client, employee) = await _world.Db(async db => (
            await db.Users.Where(u => u.Id == _world.CliX.Id).Select(u => u.FirstName + " " + u.LastName).SingleAsync(),
            await db.Users.Where(u => u.Id == _world.EmpA1.Id).Select(u => u.FirstName + " " + u.LastName).SingleAsync()));
        var clientFirst = client.Split(' ')[0];
        var employeeFirst = employee.Split(' ')[0];
        var id = await ClosedTicketWithNoteAsync($"Klijent {clientFirst} je javio problem, a kolega {employeeFirst} je pomjerio backup job van radnog vremena, pa je sve radilo.");

        using var scope = _world.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(id);
        var content = await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.TicketId == id).Select(c => c.Content).SingleAsync());

        Assert.DoesNotContain(clientFirst, content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(employeeFirst, content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[name]", content);
        Assert.Contains("pomjerio backup job", content);
    }

    [SqlFact]
    public async Task The_name_of_the_company_is_not_kept_in_the_chunk()
    {
        var company = await _world.Db(db => db.Companies.Where(c => c.Id == _world.CompanyId).Select(c => c.Name).SingleAsync());
        var id = await ClosedTicketWithNoteAsync($"Firma {company} je javila da backup job kasni. Uzrok je bio što se poklapa s opterećenjem baze preko dana, pa je backup job pomjeren van radnog vremena, na dva sata poslije ponoći, i od tada sve radi bez zastoja.");

        using var scope = _world.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(id);
        var content = await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.TicketId == id).Select(c => c.Content).SingleAsync());

        Assert.DoesNotContain(company, content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[company]", content);
        Assert.Contains("backup job kasni", content);
    }

    [SqlFact]
    public async Task A_company_that_does_not_want_its_tickets_sent_to_an_AI_provider_gets_no_chunk_and_loses_the_ones_it_had()
    {
        var kept = await ClosedTicketWithNoteAsync(Typed);
        using (var scope = _world.Factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(kept));
        Assert.Equal(1, await _world.Db(db => db.KnowledgeBaseChunks.CountAsync(c => c.TicketId == kept)));

        try
        {
            var name = await _world.Db(db => db.Companies.Where(c => c.Id == _world.CompanyId).Select(c => c.Name).SingleAsync());
            var off = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/companies/{_world.CompanyId}", new { name, isActive = true, aiIndexingEnabled = false });
            Assert.Equal(System.Net.HttpStatusCode.OK, off.Status);
            Assert.Equal(0, await _world.Db(db => db.KnowledgeBaseChunks.CountAsync(c => c.TicketId == kept))); // taken back with the permission

            var later = await ClosedTicketWithNoteAsync(Typed);
            using var scope = _world.Factory.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(later));
            Assert.Equal(0, await _world.Db(db => db.KnowledgeBaseChunks.CountAsync(c => c.TicketId == later)));
        }
        finally
        {
            await _world.Db(db => db.Companies.Where(c => c.Id == _world.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.AiIndexingEnabled, true)));
        }
    }

    [SqlFact]
    public async Task Chunks_that_were_stored_before_are_made_safe_once_and_then_left_alone()
    {
        const string plain = "Dovoljno dugačka interna bilješka da bi tiket bio dio baze znanja, bez ičega osjetljivog u njoj samoj, samo tekst.";
        var ids = new[] { await ClosedTicketWithNoteAsync(plain), await ClosedTicketWithNoteAsync(plain), await ClosedTicketWithNoteAsync(plain) };
        await _world.Db(async db =>
        {
            foreach (var id in ids) db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk { TicketId = id, Content = Typed });
            return await db.SaveChangesAsync();
        });

        // One row to a page, so that the walk through the table has to go on page after page.
        var first = await _world.Db(db => ChunkContentRedactionBackfill.RunAsync(db, NullLogger.Instance, page: 1));
        var contents = await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.TicketId != null && ids.Contains(c.TicketId.Value)).Select(c => c.Content).ToListAsync());
        var second = await _world.Db(db => ChunkContentRedactionBackfill.RunAsync(db, NullLogger.Instance, page: 1));

        Assert.True(first >= 3);
        Assert.Equal(3, contents.Count);
        Assert.All(contents, c =>
        {
            Assert.Equal(PiiRedactor.Redact(Typed), c);
            Assert.DoesNotContain("@", c);
        });
        Assert.Equal(0, second);
    }

    [SqlFact]
    public async Task A_new_document_chunk_is_stored_without_the_personal_data_in_the_document()
    {
        var book = new NPOI.XSSF.UserModel.XSSFWorkbook();
        var row = book.CreateSheet("Kontakti").CreateRow(0);
        row.CreateCell(0).SetCellValue("Za pomoć pisati na test.osoba@primjer.tst ili zvati +387 61 000 000. Pristup: lozinka: Proba123.");
        using var bytes = new MemoryStream();
        book.Write(bytes, leaveOpen: true);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "files", "kontakti-" + Guid.NewGuid().ToString("N")[..6] + ".xlsx");

        var uploaded = await _world.Post(_world.Admin, "/api/knowledge-base/documents", form);
        Assert.Equal(200, uploaded.Code);
        var documentId = uploaded.Json[0].GetProperty("id").GetGuid();
        try
        {
            var content = await _world.Db(db => db.KnowledgeBaseChunks.Where(c => c.DocumentId == documentId).Select(c => c.Content).ToListAsync());

            Assert.NotEmpty(content);
            Assert.All(content, c =>
            {
                Assert.DoesNotContain("test.osoba@primjer.tst", c);
                Assert.DoesNotContain("+387 61 000 000", c);
                Assert.DoesNotContain("Proba123", c);
            });
            Assert.Contains(content, c => c.Contains("[email]") && c.Contains("[phone]"));
        }
        finally
        {
            await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/knowledge-base/documents/{documentId}");
        }
    }

    [Fact]
    public void Masking_text_that_is_already_masked_changes_nothing()
    {
        var once = PiiRedactor.Redact(Typed);

        Assert.Equal(once, PiiRedactor.Redact(once));
    }
}
