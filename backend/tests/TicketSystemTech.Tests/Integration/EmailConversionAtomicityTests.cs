using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-31.1: turning an e-mail into a ticket copied its files onto the ticket in one save and marked the message as converted in another. A failure in
/// the second left files on a ticket for a message that was not marked (and a retry copied them again). Now it is one save: both happen or neither.
/// The real service and a real database; the mailbox is a Microsoft that hands out one small text file, and the file store only remembers what it was told.
/// </summary>
[Collection(ApiCollection.Name)]
public class EmailConversionAtomicityTests
{
    private readonly ApiWorld _world;

    public EmailConversionAtomicityTests(ApiWorld world) => _world = world;

    private sealed class OneFileMailbox : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[{"name":"log.txt","contentType":"text/plain","contentBytes":"aGVsbG8="}]}""", Encoding.UTF8, "application/json")
            });
    }

    private sealed class RememberingStorage : IFileStorage
    {
        public List<string> Saved { get; } = new();
        public List<string> Deleted { get; } = new();

        public Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
        {
            var reference = "/uploads/" + Guid.NewGuid().ToString("N") + Path.GetExtension(fileName);
            Saved.Add(reference);
            return Task.FromResult(reference);
        }

        public Task<string> SavePublicImageAsync(string fileName, string contentType, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);

        public Task<bool> DeleteAsync(string? reference, CancellationToken ct = default)
        {
            Deleted.Add(reference ?? string.Empty);
            return Task.FromResult(true);
        }

        public Task<bool> DeletePublicImageAsync(string? url, CancellationToken ct = default) => Task.FromResult(false);
    }

    private async Task WithConvertedMailAsync(Func<GraphEmailIntegrationService, RememberingStorage, Guid, Guid, Task> body)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 3, 14, 9, 0, 0, TimeSpan.Zero));
        var keys = DataProtectionProvider.Create("conversion-test");
        var protector = keys.CreateProtector("EmailConnection.OAuthTokens");
        using var scope = _world.Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var pii = scope.ServiceProvider.GetRequiredService<IPiiProtector>();
        var storage = new RememberingStorage();

        await using var db = new AppDbContext(options, pii);
        var service = new GraphEmailIntegrationService(
            db, new HttpClient(new OneFileMailbox()), storage, Options.Create(new MicrosoftGraphOptions { ClientId = "id", ClientSecret = "secret" }),
            NullLogger<GraphEmailIntegrationService>.Instance, keys, null!, clock);

        var department = new Department { Name = "conv " + Guid.NewGuid().ToString("N")[..6], Email = "conv-test@example.test" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var ticket = new Ticket
        {
            TicketNumber = "CV-" + Guid.NewGuid().ToString("N")[..8],
            Title = "iz maila",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.New,
            DepartmentId = department.Id,
            Source = TicketSource.TicketSystem
        };
        db.Tickets.Add(ticket);
        db.EmailConnections.Add(new EmailConnection
        {
            DepartmentId = department.Id,
            ConnectedByUserId = _world.Admin.Id,
            ConnectedEmail = "servis@example.test",
            AccessToken = protector.Protect("access"),
            RefreshToken = protector.Protect("refresh"),
            AccessTokenExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddHours(1),
        });
        await db.SaveChangesAsync();

        try
        {
            _world.Factory.Commands.Clear();
            await body(service, storage, department.Id, ticket.Id);
        }
        finally
        {
            await _world.Db(async cleanup =>
            {
                await cleanup.TicketAttachments.Where(a => a.TicketId == ticket.Id).ExecuteDeleteAsync();
                await cleanup.EmailTicketMarks.Where(m => m.DepartmentId == department.Id).ExecuteDeleteAsync();
                await cleanup.EmailConnections.Where(c => c.DepartmentId == department.Id).ExecuteDeleteAsync();
                await cleanup.Tickets.Where(t => t.Id == ticket.Id).ExecuteDeleteAsync();
                return await cleanup.Departments.Where(d => d.Id == department.Id).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task The_files_and_the_converted_mark_are_saved_together()
    {
        await WithConvertedMailAsync(async (service, storage, department, ticket) =>
        {
            await service.ImportAttachmentsAndMarkConvertedAsync(department, _world.EmpA1.Id, "message-1", ticket);

            Assert.Single(storage.Saved);
            Assert.Equal(1, await _world.Db(db => db.TicketAttachments.CountAsync(a => a.TicketId == ticket)));
            Assert.Equal(ticket, await _world.Db(db => db.EmailTicketMarks.Where(m => m.DepartmentId == department && m.MessageId == "message-1").Select(m => m.ConvertedTicketId).SingleAsync()));
            Assert.True(_world.Factory.Commands.AnyCommandNamingAll("TicketAttachments", "EmailTicketMarks"), "one save for both, not two");
        });
    }

    [SqlFact]
    public async Task When_the_mark_cannot_be_saved_the_files_are_not_left_on_the_ticket_either()
    {
        await WithConvertedMailAsync(async (service, storage, department, ticket) =>
        {
            var tooLong = new string('m', 600); // the index on (branch, message) holds 450 characters: the mark's row is refused

            await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAttachmentsAndMarkConvertedAsync(department, _world.EmpA1.Id, tooLong, ticket));

            Assert.Equal(0, await _world.Db(db => db.TicketAttachments.CountAsync(a => a.TicketId == ticket)));
            Assert.Equal(0, await _world.Db(db => db.EmailTicketMarks.CountAsync(m => m.DepartmentId == department)));
            Assert.Equal(storage.Saved, storage.Deleted); // and the file that had already been stored is taken back
        });
    }
}
