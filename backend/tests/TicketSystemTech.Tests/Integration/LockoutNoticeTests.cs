using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>NOVO-SEC-10: the owner of a locked account is told about it. (That the same notice is not repeated within a day is NoticeThrottleTests.)</summary>
[Collection(ApiCollection.Name)]
public class LockoutNoticeTests
{
    private readonly ApiWorld _world;

    public LockoutNoticeTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task Locking_an_account_by_wrong_passwords_tells_its_owner_once()
    {
        var person = await _world.CreateActorAsync("lockee", UserRole.Employee, _world.BranchA, null);

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var response = await _world.Post(null, "/api/auth/login", new { email = person.Email, password = "wrong-" + attempt });
            Assert.Contains(response.Code, new[] { 401, 429 });
        }

        // The mail goes through the background queue, so it arrives a moment later.
        const string subject = "Your account was temporarily locked";
        // The mail goes through the outbox, which keeps the recipient encrypted: the rows are read and compared after they are decrypted.
        int Notices() => _world.Db(async db => (await db.EmailOutbox.AsNoTracking().Where(m => m.Subject == subject).ToListAsync()).Count(m => m.ToEmail == person.Email)).GetAwaiter().GetResult();
        for (var wait = 0; wait < 50 && Notices() == 0; wait++) await Task.Delay(100);
        await Task.Delay(300); // long enough for a second one, if there were going to be one

        Assert.Equal(1, Notices());
    }
}
