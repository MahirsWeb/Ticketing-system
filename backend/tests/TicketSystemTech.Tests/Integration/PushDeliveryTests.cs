using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// T-47: the two real-time events that no ticket event makes (the admins hear that an invited person has set a password; the staff of a branch
/// hear that a ticket has been left without anybody) are pushed by the services that know of them, under the names the browser listens for.
/// Each test makes (and afterwards removes) its own people and tickets.
/// </summary>
[Collection(ApiCollection.Name)]
public class PushDeliveryTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Actor> _people = new();
    private readonly List<Guid> _tickets = new();

    public PushDeliveryTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(db => db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync());
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var person in _people)
            if (await users.FindByIdAsync(person.Id.ToString()) is { } user) await users.DeleteAsync(user);
    }

    private async Task<Actor> NewEmployeeAsync(string key)
    {
        var person = await _world.CreateActorAsync(key + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, _world.BranchA, null);
        _people.Add(person);
        return person;
    }

    private static string TextOf(object payload, string property) =>
        JsonSerializer.SerializeToElement(payload).GetProperty(property).ToString();

    [SqlFact]
    public async Task The_admins_hear_that_an_invited_person_has_set_a_password()
    {
        var person = await NewEmployeeAsync("pushinvite");
        await _world.Db(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == person.Id);
            user.MustChangePassword = true;
            user.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(30);
            return await db.SaveChangesAsync();
        });
        _world.Factory.Pushes.Clear();

        var set = await _world.Post(null, "/api/auth/set-new-password",
            new { email = person.Email, temporaryPassword = ApiWorld.Password, newPassword = "Pushed-New-1234!", confirmNewPassword = "Pushed-New-1234!" });

        Assert.Equal(200, set.Code);
        var push = Assert.Single(_world.Factory.Pushes.Sent, p => p.Event == PushEvents.UserActivatedViaInvite);
        Assert.Equal("role:Admin", push.To);
        Assert.Equal(person.Id.ToString(), TextOf(push.Payload, "Id"));
        Assert.Equal("Employee", TextOf(push.Payload, "Role"));
    }

    [SqlFact]
    public async Task The_staff_of_the_branch_hear_that_a_ticket_was_left_without_anybody()
    {
        var leaving = await NewEmployeeAsync("pushleave");
        var ticket = new Ticket
        {
            TicketNumber = "PU-" + Guid.NewGuid().ToString("N")[..8],
            Title = "left alone",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            OpenedAtUtc = DateTime.UtcNow.AddDays(-1),
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
        };
        ticket.Assignees.Add(new TicketAssignee { UserId = leaving.Id, IsPrimary = true });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        _world.Factory.Pushes.Clear();

        var off = await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{leaving.Id}/active", new { isActive = false });

        Assert.Equal(200, off.Code);
        var pushes = _world.Factory.Pushes.Sent.Where(p => p.Event == PushEvents.TicketUnassigned).ToList();
        Assert.Contains(pushes, p => p.To == _world.EmpA1.Id.ToString() && TextOf(p.Payload, "TicketNumber") == ticket.TicketNumber);
        Assert.Contains(pushes, p => p.To == _world.EmpA2.Id.ToString() && TextOf(p.Payload, "TicketNumber") == ticket.TicketNumber);
        Assert.DoesNotContain(pushes, p => p.To == leaving.Id.ToString()); // not the one who was taken off
        Assert.DoesNotContain(pushes, p => p.To == _world.EmpB1.Id.ToString()); // not the staff of another branch
    }
}
