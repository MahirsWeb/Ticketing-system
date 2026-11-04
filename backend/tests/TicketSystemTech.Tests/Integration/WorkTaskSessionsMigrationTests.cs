using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The migration that replaced a task's single start/end with a list of sessions has to carry existing data across. This builds
/// a database as it was just before it, fills it with every kind of old row there can be, migrates forward and checks what
/// became of each, then migrates back.
/// </summary>
public class WorkTaskSessionsMigrationTests
{
    private const string Before = "20261019174606_EmailOutbox";

    private sealed class NoPii : IPiiProtector
    {
        public string? Encrypt(string? plaintext) => plaintext;
        public string? Decrypt(string? ciphertext) => ciphertext;
        public bool IsProtected(string? value) => false;
        public string? Hash(string? plaintext) => plaintext;
    }

    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Worker = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static readonly DateTime Start = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 3, 2, 10, 30, 0, DateTimeKind.Utc);

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

    private static void AddTask(SqlConnection c, string title, DateTime? started, DateTime? ended) => Run(c,
        @"INSERT INTO WorkTasks (Id, Title, Description, DepartmentId, CreatedByUserId, AssignedByUserId, AssignedToUserId, Status, StartedAtUtc, EndedAtUtc, CreatedAt)
          VALUES (NEWID(), @title, 'd', @branch, @other, @other, @worker, 0, @s, @e, SYSUTCDATETIME())",
        ("@title", title), ("@branch", Branch), ("@other", Other), ("@worker", Worker), ("@s", started), ("@e", ended));

    private static (Guid User, DateTime Start, DateTime? End)? SessionOf(SqlConnection c, string title)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT s.UserId, s.StartedAtUtc, s.EndedAtUtc FROM WorkTaskSessions s JOIN WorkTasks t ON t.Id = s.WorkTaskId WHERE t.Title = @t";
        cmd.Parameters.AddWithValue("@t", title);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var result = (reader.GetGuid(0), reader.GetDateTime(1), reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2));
        Assert.False(reader.Read(), "a task had more than one session after the migration");
        return result;
    }

    [SqlFact]
    public async Task Existing_task_times_become_sessions_for_the_assignee_and_can_be_taken_back()
    {
        var database = TestSql.DatabasePrefix + "migT_" + Guid.NewGuid().ToString("N")[..8];
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(TestSql.ConnectionStringFor(database)).Options;

        try
        {
            await using var db = new AppDbContext(options, new NoPii());
            var migrator = db.GetService<IMigrator>();

            // ---- the database as it was before ----
            await migrator.MigrateAsync(Before);
            await using var connection = new SqlConnection(TestSql.ConnectionStringFor(database));
            await connection.OpenAsync();

            foreach (var user in new[] { Worker, Other }) AddUser(connection, user);
            Run(connection, "INSERT INTO Departments (Id, Name, Email, IsActive, CreatedAt) VALUES (@id, 'Branch', 'b@x.test', 1, SYSUTCDATETIME())", ("@id", Branch));

            AddTask(connection, "FULL", Start, End);            // the normal case
            AddTask(connection, "RUNNING", Start, null);        // the timer was still on
            AddTask(connection, "NOT-STARTED", null, null);     // never had work time
            AddTask(connection, "END-ONLY", null, End);         // no start: no session
            AddTask(connection, "BACKWARDS", End, Start);       // ends before it starts: left behind
            AddTask(connection, "ZERO", Start, Start);          // starts and ends at the same instant: left behind

            // ---- forward ----
            await migrator.MigrateAsync();

            Assert.Equal(new (Guid, DateTime, DateTime?)?((Worker, Start, End)), SessionOf(connection, "FULL"));
            Assert.Equal(new (Guid, DateTime, DateTime?)?((Worker, Start, null)), SessionOf(connection, "RUNNING"));
            Assert.Null(SessionOf(connection, "NOT-STARTED"));
            Assert.Null(SessionOf(connection, "END-ONLY"));
            Assert.Null(SessionOf(connection, "BACKWARDS"));
            Assert.Null(SessionOf(connection, "ZERO"));
            Assert.Equal(2, Scalar<int>(connection, "SELECT COUNT(*) FROM WorkTaskSessions"));
            Assert.Equal(6, Scalar<int>(connection, "SELECT COUNT(*) FROM WorkTasks")); // every task is still there

            // the two old columns are gone, the concurrency token is there
            Assert.Equal(0, Scalar<int>(connection,
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WorkTasks' AND COLUMN_NAME IN ('StartedAtUtc', 'EndedAtUtc')"));
            Assert.Equal(1, Scalar<int>(connection,
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WorkTasks' AND COLUMN_NAME = 'RowVersion'"));

            // ---- back again ----
            Run(connection, "INSERT INTO WorkTaskSessions (Id, WorkTaskId, UserId, StartedAtUtc, EndedAtUtc, CreatedAt) " +
                            "SELECT NEWID(), Id, AssignedToUserId, @later, @laterEnd, SYSUTCDATETIME() FROM WorkTasks WHERE Title = 'FULL'",
                ("@later", End.AddHours(3)), ("@laterEnd", End.AddHours(4))); // a second session on the way down
            await migrator.MigrateAsync(Before);

            Assert.Equal(Start, Scalar<DateTime>(connection, "SELECT StartedAtUtc FROM WorkTasks WHERE Title = 'FULL'"));      // the first start
            Assert.Equal(End.AddHours(4), Scalar<DateTime>(connection, "SELECT EndedAtUtc FROM WorkTasks WHERE Title = 'FULL'")); // the last end
            Assert.Null(Scalar<DateTime?>(connection, "SELECT EndedAtUtc FROM WorkTasks WHERE Title = 'RUNNING'"));               // still running => no end
            Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkTaskSessions'"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var master = TestSql.OpenMaster();
            Run(master, $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
        }
    }
}
