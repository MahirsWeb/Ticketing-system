using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Two small protections that had no test: a password reset can be asked for again and again but fills the owner's mailbox only once in a
/// while, and an upload that is refused leaves nothing behind (no file on disk without a row, no row without a file).
/// </summary>
[Collection(ApiCollection.Name)]
public class AccountEndpointLimitTests
{
    private readonly ApiWorld _world;

    public AccountEndpointLimitTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task Asking_for_a_password_reset_again_and_again_sends_one_mail_and_answers_the_same_every_time()
    {
        var person = await _world.CreateActorAsync("resetter", UserRole.Employee, _world.BranchA, null);

        var answers = new List<(int Code, string Text)>();
        for (var i = 0; i < 6; i++)
        {
            var response = await _world.Post(null, "/api/auth/forgot-password", new { email = person.Email });
            answers.Add((response.Code, response.Text));
        }
        Assert.All(answers, a => Assert.Equal(answers[0], a)); // nothing in the answer tells the first request from the sixth

        // The mail goes through the background queue and then the outbox (which keeps the address encrypted): read and compare after decrypting.
        int Mails() => _world.Db(async db => (await db.EmailOutbox.AsNoTracking().Where(m => m.Subject == "Reset your password").ToListAsync())
            .Count(m => m.ToEmail == person.Email)).GetAwaiter().GetResult();
        for (var wait = 0; wait < 50 && Mails() == 0; wait++) await Task.Delay(100);
        await Task.Delay(300); // long enough for more of them, if there were going to be more

        Assert.Equal(1, Mails());
    }

    private static MultipartFormDataContent Form(params (string Name, string Type, string Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, type, content) in files)
        {
            var part = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            form.Add(part, "files", name);
        }
        return form;
    }

    private int FilesOnDisk()
    {
        var root = _world.Factory.Services.GetRequiredService<IConfiguration>()["Storage:UploadsPath"]!;
        return Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;
    }

    [SqlFact]
    public async Task An_upload_with_one_refused_file_stores_none_of_them()
    {
        var url = $"/api/tickets/{_world.Tickets["A1"]}/attachments";
        var rowsBefore = await _world.Db(db => db.TicketAttachments.CountAsync());
        var filesBefore = FilesOnDisk();

        // the first file is fine, the second is a type that is not allowed: the whole upload is refused
        var refused = await _world.Post(_world.EmpA1, url, Form(("fine.txt", "text/plain", "ok"), ("virus.exe", "application/octet-stream", "MZ")));

        Assert.Equal(400, refused.Code);
        Assert.Equal(filesBefore, FilesOnDisk()); // the good file was not written either
        Assert.Equal(rowsBefore, await _world.Db(db => db.TicketAttachments.CountAsync()));

        // and the same two files, both fine, are stored together (so the refusal above was about the type, not the shape of the request)
        var accepted = await _world.Post(_world.EmpA1, url, Form(("one.txt", "text/plain", "1"), ("two.txt", "text/plain", "2")));
        Assert.Equal(200, accepted.Code);
        Assert.Equal(filesBefore + 2, FilesOnDisk());
        await _world.Db(db => db.TicketAttachments.Where(a => a.FileName == "one.txt" || a.FileName == "two.txt").ExecuteDeleteAsync());
    }
}
