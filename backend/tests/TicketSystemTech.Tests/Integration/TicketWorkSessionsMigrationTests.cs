using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The migration that replaced a ticket's single work start/end with a list of sessions has to carry existing data across.
/// This builds a database as it was just before that migration, fills it with every kind of old row there can be, migrates
/// forward and checks what became of each, then migrates back.
/// </summary>
public class TicketWorkSessionsMigrationTests
{
    private const string Before = "20261019100630_DatabaseIntegrityAndIndexes";

    /// <summary>The migration only runs SQL; nothing here reads or writes encrypted columns.</summary>
    private sealed class NoPii : IPiiProtector
    {
        public string? Encrypt(string? plaintext) => plaintext;
        public string? Decrypt(string? ciphertext) => ciphertext;
        public bool IsProtected(string? value) => false;
        public string? Hash(string? plaintext) => plaintext;
    }

    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Worker = Guid.NewGuid();
    private static readonly Guid Opener = Guid.NewGuid();
    private static readonly Guid Closer = Guid.NewGuid();

    private static readonly DateTime Start = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 3, 2, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Closed = new(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc);

    private static void Run(SqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? default! : (T)result;
    }

    private static void AddUser(SqlConnection c, Guid id) => Run(c,
        @"INSERT INTO AspNetUsers (Id, FirstName, LastName, Role, IsActive, CreatedAtUtc, MustChangePassword, PhoneNumberPrompted,
                                   EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
          VALUES (@id, 'T', 'T', 1, 1, SYSUTCDATETIME(), 0, 1, 1, 0, 0, 0, 0)", ("@id", id));

    /// <summary>One old-style ticket. <paramref name="number"/> is how the checks below find it again.</summary>
    private static void AddTicket(SqlConnection c, string number, DateTime? workStart, DateTime? workEnd,
        Guid? assigned = null, Guid? openedBy = null, Guid? closedBy = null, DateTime? closedAt = null) => Run(c,
        @"INSERT INTO Tickets (Id, TicketNumber, Title, Description, ClientId, CompanyId, Status, CreatedAt,
                               AssignedToUserId, OpenedByUserId, ClosedByUserId, ClosedAtUtc, WorkStartedAtUtc, WorkEndedAtUtc)
          VALUES (NEWID(), @n, 'x', 'x', @client, @company, 1, SYSUTCDATETIME(), @assigned, @opened, @closedBy, @closedAt, @ws, @we)",
        ("@n", number), ("@client", Client), ("@company", Company), ("@assigned", assigned), ("@opened", openedBy),
        ("@closedBy", closedBy), ("@closedAt", closedAt), ("@ws", workStart), ("@we", workEnd));

    private static (Guid User, DateTime Start, DateTime? End)? SessionOf(SqlConnection c, string number)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT s.UserId, s.StartedAtUtc, s.EndedAtUtc FROM TicketWorkSessions s JOIN Tickets t ON t.Id = s.TicketId WHERE t.TicketNumber = @n";
        cmd.Parameters.AddWithValue("@n", number);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var result = (reader.GetGuid(0), reader.GetDateTime(1), reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2));
        Assert.False(reader.Read(), "a ticket had more than one session after the migration");
        return result;
    }

    [SqlFact]
    public async Task Existing_work_times_become_sessions_for_the_right_person_and_can_be_taken_back()
    {
        var database = TestSql.DatabasePrefix + "mig_" + Guid.NewGuid().ToString("N")[..8];
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(TestSql.ConnectionStringFor(database)).Options;

        try
        {
            await using var db = new AppDbContext(options, new NoPii());
            var migrator = db.GetService<IMigrator>();

            // ---- the database as it was before ----
            await migrator.MigrateAsync(Before);
            await using var connection = new SqlConnection(TestSql.ConnectionStringFor(database));
            await connection.OpenAsync();

            foreach (var user in new[] { Client, Worker, Opener, Closer }) AddUser(connection, user);
            Run(connection, "INSERT INTO Companies (Id, Name, IsActive, CreatedAt) VALUES (@id, 'Acme', 1, SYSUTCDATETIME())", ("@id", Company));

            AddTicket(connection, "FULL", Start, End, assigned: Worker);                                   // the normal case
            AddTicket(connection, "NO-END-OPEN", Start, null, assigned: Worker);                           // still running
            AddTicket(connection, "NO-END-CLOSED", Start, null, assigned: Worker, closedAt: Closed);       // ended when it closed
            AddTicket(connection, "NO-ASSIGNEE", Start, End, openedBy: Opener, closedBy: Closer);          // falls back to who opened it
            AddTicket(connection, "ONLY-CLOSER", Start, End, closedBy: Closer);                            // ... or who closed it
            AddTicket(connection, "NOBODY", Start, End);                                                    // cannot be attributed: left behind
            AddTicket(connection, "END-ONLY", null, End, assigned: Worker);                                 // no start: no session
            AddTicket(connection, "BACKWARDS", End, Start, assigned: Worker);                               // ends before it starts: left behind
            AddTicket(connection, "NOTHING", null, null, assigned: Worker);                                 // never had work time

            // ---- forward ----
            await migrator.MigrateAsync();

            Assert.Equal(new (Guid, DateTime, DateTime?)?(( Worker, Start, End )), SessionOf(connection, "FULL"));
            Assert.Equal(new (Guid, DateTime, DateTime?)?((Worker, Start, null)), SessionOf(connection, "NO-END-OPEN"));
            Assert.Equal(new (Guid, DateTime, DateTime?)?((Worker, Start, Closed)), SessionOf(connection, "NO-END-CLOSED"));
            Assert.Equal(new (Guid, DateTime, DateTime?)?((Opener, Start, End)), SessionOf(connection, "NO-ASSIGNEE"));
            Assert.Equal(new (Guid, DateTime, DateTime?)?((Closer, Start, End)), SessionOf(connection, "ONLY-CLOSER"));
            Assert.Null(SessionOf(connection, "NOBODY"));
            Assert.Null(SessionOf(connection, "END-ONLY"));
            Assert.Null(SessionOf(connection, "BACKWARDS"));
            Assert.Null(SessionOf(connection, "NOTHING"));
            Assert.Equal(5, Scalar<int>(connection, "SELECT COUNT(*) FROM TicketWorkSessions"));

            // the two old columns are gone
            Assert.Equal(0, Scalar<int>(connection,
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tickets' AND COLUMN_NAME IN ('WorkStartedAtUtc', 'WorkEndedAtUtc')"));

            // ---- back again ----
            Run(connection, "UPDATE TicketWorkSessions SET EndedAtUtc = NULL WHERE StartedAtUtc = @s AND UserId = @u AND TicketId IN (SELECT Id FROM Tickets WHERE TicketNumber = 'FULL')",
                ("@s", Start), ("@u", Worker)); // a running session on the way down
            await migrator.MigrateAsync(Before);

            Assert.Equal(Start, Scalar<DateTime>(connection, "SELECT WorkStartedAtUtc FROM Tickets WHERE TicketNumber = 'FULL'"));
            Assert.Null(Scalar<DateTime?>(connection, "SELECT WorkEndedAtUtc FROM Tickets WHERE TicketNumber = 'FULL'")); // still running => no end
            Assert.Equal(Closed, Scalar<DateTime>(connection, "SELECT WorkEndedAtUtc FROM Tickets WHERE TicketNumber = 'NO-END-CLOSED'"));
            Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TicketWorkSessions'"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var master = TestSql.OpenMaster();
            Run(master, $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
        }
    }
}
