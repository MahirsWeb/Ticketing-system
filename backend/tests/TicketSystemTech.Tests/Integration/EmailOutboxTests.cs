using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Api.BackgroundJobs;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// T-07: e-mail used to be sent while the request was still open, and one that did not go out was lost. Now it is filed in a table
/// and a worker delivers it. These tests drive the real chain (the application's own IEmailSender, the real table, the real
/// worker) with a fake in place of the mail server, and run the worker by hand at moments of their choosing.
/// </summary>
[Collection(ApiCollection.Name)]
public class EmailOutboxTests : IAsyncLifetime
{
    private const string Recipient = "someone@outbox.test";

    private readonly ApiWorld _world;
    private FakeEmailTransport Transport => _world.Factory.Transport;

    public EmailOutboxTests(ApiWorld world) => _world = world;

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        Transport.Reset();
        await ClearOutboxAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        Transport.Reset();
        await ClearOutboxAsync();
    }

    private Task<int> ClearOutboxAsync() => _world.Db(db => db.EmailOutbox.ExecuteDeleteAsync());

    private IEmailSender Sender => _world.Factory.Services.GetRequiredService<IEmailSender>();
    private EmailOutboxDispatcher Worker => _world.Factory.Services.GetRequiredService<EmailOutboxDispatcher>();

    private Task<List<EmailOutboxItem>> RowsAsync() =>
        _world.Db(db => db.EmailOutbox.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync());

    private async Task<EmailOutboxItem> OnlyRowAsync() => Assert.Single(await RowsAsync());

    private static void AssertSameInstant(DateTime expected, DateTime? actual)
    {
        Assert.NotNull(actual);
        Assert.True(Math.Abs((actual!.Value - expected).TotalMilliseconds) < 5, $"expected {expected:O}, got {actual:O}");
    }

    // ======================= filing a message =======================

    [SqlFact]
    public async Task The_application_files_a_message_in_the_outbox_and_does_not_call_the_provider()
    {
        Assert.IsType<OutboxEmailSender>(Sender); // what Program.cs registers is the outbox, not a direct sender

        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");

        var row = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Equal(Recipient, row.ToEmail);
        Assert.Equal("Hello", row.Subject);
        Assert.Equal("<p>Body</p>", row.HtmlBody);
        Assert.Equal(0, row.Attempts);
        Assert.Empty(Transport.Calls);
    }

    [SqlFact]
    public async Task The_body_is_encrypted_at_rest_and_dropped_once_the_message_is_sent()
    {
        const string secret = "Temporary password: Hunter2 SECRET-TOKEN reset?token=abc123";
        await Sender.SendAsync(Recipient, "Reset your password", $"<p>{secret}</p>");

        // A copy of the database alone must not reveal the reset link or the temporary password: the stored column is ciphertext.
        var rawBodies = await _world.Db(db => db.Database.SqlQueryRaw<string>("SELECT HtmlBody AS Value FROM EmailOutbox").ToListAsync());
        Assert.DoesNotContain(rawBodies, b => b.Contains("SECRET-TOKEN"));

        // Through the key ring it round-trips back to the real body, so the worker still sends the right thing.
        Assert.Contains("SECRET-TOKEN", (await OnlyRowAsync()).HtmlBody);

        await Worker.ProcessOnceAsync(DateTime.UtcNow);

        var afterSend = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Sent, afterSend.Status);
        Assert.Equal(string.Empty, afterSend.HtmlBody); // not kept in the table for days after it has gone out
    }

    [SqlFact]
    public void The_worker_that_delivers_the_outbox_is_part_of_the_running_application()
    {
        var hosted = _world.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        Assert.Contains(hosted, h => h is EmailOutboxDispatcher);
    }

    [SqlFact]
    public async Task The_recipient_address_is_encrypted_in_the_table()
    {
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");

        var stored = await _world.Db(db => db.Database.SqlQueryRaw<string>("SELECT ToEmail AS Value FROM EmailOutbox").ToListAsync());

        var value = Assert.Single(stored);
        Assert.DoesNotContain("outbox.test", value);
        Assert.DoesNotContain("someone", value);
    }

    [SqlFact]
    public async Task A_very_long_subject_is_shortened_instead_of_losing_the_message()
    {
        await Sender.SendAsync(Recipient, new string('x', 900), "<p>Body</p>");

        var row = await OnlyRowAsync();
        Assert.Equal(500, row.Subject.Length);
    }

    [SqlFact]
    public async Task A_failure_to_file_the_message_is_logged_and_does_not_fail_the_request_that_asked()
    {
        var broken = new OutboxEmailSender(new ThrowingScopeFactory(), NullLogger<OutboxEmailSender>.Instance);

        await broken.SendAsync(Recipient, "Hello", "<p>Body</p>"); // must not throw

        Assert.Empty(await RowsAsync());
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("the database is down");
    }

    [SqlFact]
    public async Task An_action_in_the_application_leaves_its_mail_in_the_outbox_and_the_worker_delivers_it()
    {
        var address = "outbox-branch@e2e.test";
        try
        {
            var created = await _world.Post(_world.Admin, "/api/departments", new { name = "Outbox Branch", email = address });
            Assert.Equal(200, created.Code);

            var row = await OnlyRowAsync();
            Assert.Equal(address, row.ToEmail);
            Assert.Equal("Welcome to Ticket System Tech", row.Subject);
            Assert.Empty(Transport.Calls); // the request is over and nothing has gone to the provider yet

            Assert.Equal(1, await Worker.ProcessOnceAsync(DateTime.UtcNow.AddSeconds(1)));

            var sent = Assert.Single(Transport.Calls);
            Assert.Equal((address, "Welcome to Ticket System Tech"), sent);
            Assert.Equal(EmailOutboxStatus.Sent, (await OnlyRowAsync()).Status);
        }
        finally
        {
            await _world.Db(db => db.Departments.Where(d => d.Email == address).ExecuteDeleteAsync());
        }
    }

    // ======================= delivering it =======================

    [SqlFact]
    public async Task A_due_message_is_sent_once_and_then_left_alone()
    {
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var now = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(1, await Worker.ProcessOnceAsync(now));

        Assert.Equal((Recipient, "Hello"), Assert.Single(Transport.Calls));
        var row = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Sent, row.Status);
        Assert.Equal(1, row.Attempts);
        AssertSameInstant(now, row.CompletedAtUtc);
        Assert.Null(row.LeasedUntilUtc);
        Assert.Null(row.LastError);

        Assert.Equal(0, await Worker.ProcessOnceAsync(now.AddMinutes(10)));
        Assert.Single(Transport.Calls);
    }

    [SqlFact]
    public async Task A_message_waiting_for_its_next_attempt_is_not_sent_early()
    {
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        await _world.Db(db => db.EmailOutbox.ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAtUtc, DateTime.UtcNow.AddHours(1))));

        Assert.Equal(0, await Worker.ProcessOnceAsync(DateTime.UtcNow.AddSeconds(1)));

        Assert.Empty(Transport.Calls);
        Assert.Equal(EmailOutboxStatus.Pending, (await OnlyRowAsync()).Status);
    }

    [SqlFact]
    public async Task A_provider_that_cannot_take_the_message_is_asked_again_later_with_growing_waits()
    {
        Transport.Answer = _ => EmailSendResult.Retry("The SMTP server answered 451");
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var t0 = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(1, await Worker.ProcessOnceAsync(t0));
        var first = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Pending, first.Status);
        Assert.Equal(1, first.Attempts);
        Assert.Equal("The SMTP server answered 451", first.LastError);
        AssertSameInstant(t0.AddMinutes(1), first.NextAttemptAtUtc);
        Assert.Null(first.LeasedUntilUtc);

        // Half a minute later is too early; just over a minute later is time.
        Assert.Equal(0, await Worker.ProcessOnceAsync(t0.AddSeconds(30)));
        Assert.Single(Transport.Calls);

        var t1 = t0.AddSeconds(61);
        Assert.Equal(1, await Worker.ProcessOnceAsync(t1));
        var second = await OnlyRowAsync();
        Assert.Equal(2, second.Attempts);
        AssertSameInstant(t1.AddMinutes(2), second.NextAttemptAtUtc);

        // The provider recovers: the same message goes out and is closed.
        Transport.Answer = _ => EmailSendResult.Sent();
        var t2 = t1.AddMinutes(3);
        Assert.Equal(1, await Worker.ProcessOnceAsync(t2));
        var third = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Sent, third.Status);
        Assert.Equal(3, third.Attempts);
        Assert.Equal(3, Transport.Calls.Count);
    }

    [SqlFact]
    public async Task A_message_that_never_gets_through_is_given_up_after_the_last_attempt_and_kept_for_inspection()
    {
        Transport.Answer = _ => EmailSendResult.Retry("The SMTP server answered 451");
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var now = DateTime.UtcNow.AddSeconds(1);

        for (var attempt = 1; attempt <= EmailRetryPolicy.MaxAttempts; attempt++)
        {
            Assert.Equal(1, await Worker.ProcessOnceAsync(now));
            now = now.AddHours(1); // longer than any wait
        }

        var row = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Failed, row.Status);
        Assert.Equal(EmailRetryPolicy.MaxAttempts, row.Attempts);
        Assert.Contains("Gave up", row.LastError);
        Assert.Contains("451", row.LastError);
        Assert.NotNull(row.CompletedAtUtc);

        Assert.Equal(0, await Worker.ProcessOnceAsync(now.AddDays(1)));
        Assert.Equal(EmailRetryPolicy.MaxAttempts, Transport.Calls.Count);
    }

    [SqlFact]
    public async Task A_message_the_provider_refuses_for_good_is_not_tried_again()
    {
        Transport.Answer = _ => EmailSendResult.Failed("The SMTP server answered 550");
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var now = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(1, await Worker.ProcessOnceAsync(now));

        var row = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("The SMTP server answered 550", row.LastError);
        Assert.NotNull(row.CompletedAtUtc);

        Assert.Equal(0, await Worker.ProcessOnceAsync(now.AddHours(5)));
        Assert.Single(Transport.Calls);
    }

    [SqlFact]
    public async Task A_message_that_is_deliberately_not_sent_is_closed_without_retries()
    {
        Transport.Answer = _ => EmailSendResult.Skipped("The recipient's account is inactive.");
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var now = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(1, await Worker.ProcessOnceAsync(now));

        var row = await OnlyRowAsync();
        Assert.Equal(EmailOutboxStatus.Skipped, row.Status);
        Assert.Equal("The recipient's account is inactive.", row.LastError);

        Assert.Equal(0, await Worker.ProcessOnceAsync(now.AddHours(5)));
        Assert.Single(Transport.Calls);
    }

    [SqlFact]
    public async Task A_transport_that_throws_counts_as_a_failed_attempt_and_does_not_break_the_round()
    {
        Transport.Answer = to => to.StartsWith("bad", StringComparison.Ordinal)
            ? throw new InvalidOperationException("boom")
            : EmailSendResult.Sent();
        await Sender.SendAsync("bad@outbox.test", "First", "<p>1</p>");
        await Sender.SendAsync("good@outbox.test", "Second", "<p>2</p>");

        var taken = await Worker.ProcessOnceAsync(DateTime.UtcNow.AddSeconds(1));

        Assert.Equal(2, taken);
        var rows = await RowsAsync();
        var bad = Assert.Single(rows, r => r.ToEmail == "bad@outbox.test");
        var good = Assert.Single(rows, r => r.ToEmail == "good@outbox.test");
        Assert.Equal(EmailOutboxStatus.Pending, bad.Status);
        Assert.Equal(1, bad.Attempts);
        Assert.Equal(nameof(InvalidOperationException), bad.LastError);
        Assert.Equal(EmailOutboxStatus.Sent, good.Status);
    }

    [SqlFact]
    public async Task Mail_to_a_deactivated_account_is_skipped_by_the_worker_whatever_the_transport_is()
    {
        var person = await _world.CreateActorAsync("outboxInactive", UserRole.Client, null, _world.CompanyId);
        try
        {
            await Sender.SendAsync(person.Email, "While active", "<p>1</p>");
            Assert.Equal(1, await Worker.ProcessOnceAsync(DateTime.UtcNow.AddSeconds(1)));
            Assert.Equal((person.Email, "While active"), Assert.Single(Transport.Calls));

            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
            await Sender.SendAsync(person.Email, "After deactivation", "<p>2</p>");
            Assert.Equal(1, await Worker.ProcessOnceAsync(DateTime.UtcNow.AddSeconds(2)));

            Assert.Single(Transport.Calls); // the second one never reached the transport
            var skipped = Assert.Single(await RowsAsync(), r => r.Subject == "After deactivation");
            Assert.Equal(EmailOutboxStatus.Skipped, skipped.Status);
            Assert.Contains("inactive", skipped.LastError);
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteDeleteAsync());
        }
    }

    // ======================= several workers, a worker that dies =======================

    [SqlFact]
    public async Task Workers_running_at_the_same_moment_send_a_message_only_once()
    {
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var now = DateTime.UtcNow.AddSeconds(1);

        var taken = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Worker.ProcessOnceAsync(now))));

        Assert.Equal(1, taken.Sum());
        Assert.Single(Transport.Calls);
        Assert.Equal(EmailOutboxStatus.Sent, (await OnlyRowAsync()).Status);
    }

    [SqlFact]
    public async Task A_message_another_worker_is_sending_is_left_alone_until_its_lease_runs_out()
    {
        await Sender.SendAsync(Recipient, "Hello", "<p>Body</p>");
        var now = DateTime.UtcNow.AddSeconds(1);
        var leasedUntil = now.AddMinutes(2);
        await _world.Db(db => db.EmailOutbox.ExecuteUpdateAsync(s => s.SetProperty(m => m.LeasedUntilUtc, (DateTime?)leasedUntil)));

        // Still leased: someone else is on it.
        Assert.Equal(0, await Worker.ProcessOnceAsync(now.AddMinutes(1)));
        Assert.Empty(Transport.Calls);

        // The lease ran out (that worker died): the message is picked up after all, not lost.
        Assert.Equal(1, await Worker.ProcessOnceAsync(now.AddMinutes(3)));
        Assert.Single(Transport.Calls);
        Assert.Equal(EmailOutboxStatus.Sent, (await OnlyRowAsync()).Status);
    }

    [SqlFact]
    public async Task One_round_takes_at_most_a_batch_and_the_rest_wait_for_the_next()
    {
        var batch = _world.Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TicketSystemTech.Application.Common.Options.EmailOutboxOptions>>().Value.BatchSize;
        for (var i = 0; i < batch + 3; i++) await Sender.SendAsync($"m{i}@outbox.test", $"Mail {i}", "<p>x</p>");
        var now = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(batch, await Worker.ProcessOnceAsync(now));
        Assert.Equal(3, await Worker.ProcessOnceAsync(now));
        Assert.Equal(batch + 3, Transport.Calls.Count);
    }

    // ======================= tidying up =======================

    [SqlFact]
    public async Task Old_finished_messages_are_removed_and_recent_or_waiting_ones_are_kept()
    {
        var now = DateTime.UtcNow;
        EmailOutboxItem Item(string subject, EmailOutboxStatus status, DateTime? completed) => new()
        {
            ToEmail = Recipient,
            Subject = subject,
            HtmlBody = "<p>x</p>",
            Status = status,
            CompletedAtUtc = completed
        };

        await _world.Db(async db =>
        {
            db.EmailOutbox.AddRange(
                Item("sent-old", EmailOutboxStatus.Sent, now.AddDays(-8)),
                Item("sent-recent", EmailOutboxStatus.Sent, now.AddDays(-1)),
                Item("skipped-old", EmailOutboxStatus.Skipped, now.AddDays(-8)),
                Item("failed-8-days", EmailOutboxStatus.Failed, now.AddDays(-8)),
                Item("failed-31-days", EmailOutboxStatus.Failed, now.AddDays(-31)),
                Item("pending-ancient", EmailOutboxStatus.Pending, null));
            return await db.SaveChangesAsync();
        });

        var removed = await Worker.DeleteOldAsync(now);

        Assert.Equal(3, removed);
        var left = (await RowsAsync()).Select(r => r.Subject).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "failed-8-days", "pending-ancient", "sent-recent" }, left);
    }
}
