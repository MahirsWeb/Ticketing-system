using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-34.2: a ticket used to name the person it is assigned to in a column of its own AND in a list, and the two could say different
/// things. Now the list is the only record, with the main assignee marked on it. The migration has to carry every kind of old ticket
/// across without losing anybody; this builds a database as it was just before, fills it with each kind, migrates forward, checks,
/// and migrates back.
/// </summary>
public class TicketPrimaryAssigneeMigrationTests
{
    private const string Before = "20261104143647_BranchesCannotBeDeletedWhileInUse";

    private sealed class NoPii : IPiiProtector
    {
        public string? Encrypt(string? plaintext) => plaintext;
        public string? Decrypt(string? ciphertext) => ciphertext;
        public bool IsProtected(string? value) => false;
        public string? Hash(string? plaintext) => plaintext;
    }

    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Amra = Guid.NewGuid();
    private static readonly Guid Haris = Guid.NewGuid();

    private static void Run(SqlConnection c, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqlConnection c, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? default! : (T)result;
    }

    private static void AddUser(SqlConnection c, Guid id) => Run(c,
        @"INSERT INTO AspNetUsers (Id, FirstName, LastName, Role, IsActive, CreatedAtUtc, MustChangePassword, PhoneNumberPrompted,
                                   EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
          VALUES (@id, 'T', 'T', 1, 1, SYSUTCDATETIME(), 0, 1, 1, 0, 0, 0, 0)", ("@id", id));

    /// <summary>One old-style ticket: <paramref name="primary"/> is what its own column said, <paramref name="onList"/> who was on the list.</summary>
    private static void AddTicket(SqlConnection c, string number, Guid? primary, params Guid[] onList)
    {
        var id = Guid.NewGuid();
        Run(c, @"INSERT INTO Tickets (Id, TicketNumber, Title, Description, ClientId, CompanyId, Status, CreatedAt, AssignedToUserId)
                 VALUES (@id, @n, 'x', 'x', @client, @company, 1, SYSUTCDATETIME(), @primary)",
            ("@id", id), ("@n", number), ("@client", Client), ("@company", Company), ("@primary", primary));
        foreach (var user in onList)
            Run(c, "INSERT INTO TicketAssignees (Id, CreatedAt, TicketId, UserId, AssignedAtUtc) VALUES (NEWID(), SYSUTCDATETIME(), @t, @u, SYSUTCDATETIME())",
                ("@t", id), ("@u", user));
    }

    private static List<(Guid User, bool Primary)> ListOf(SqlConnection c, string number)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT a.UserId, a.IsPrimary FROM TicketAssignees a JOIN Tickets t ON t.Id = a.TicketId WHERE t.TicketNumber = @n ORDER BY a.UserId";
        cmd.Parameters.AddWithValue("@n", number);
        using var reader = cmd.ExecuteReader();
        var rows = new List<(Guid, bool)>();
        while (reader.Read()) rows.Add((reader.GetGuid(0), reader.GetBoolean(1)));
        return rows;
    }

    [SqlFact]
    public async Task The_person_a_ticket_was_assigned_to_becomes_the_marked_one_on_its_list_and_can_be_taken_back()
    {
        var database = TestSql.DatabasePrefix + "migP_" + Guid.NewGuid().ToString("N")[..8];
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(TestSql.ConnectionStringFor(database)).Options;

        try
        {
            await using var db = new AppDbContext(options, new NoPii());
            var migrator = db.GetService<IMigrator>();

            // ---- the database as it was before ----
            await migrator.MigrateAsync(Before);
            await using var connection = new SqlConnection(TestSql.ConnectionStringFor(database));
            await connection.OpenAsync();

            foreach (var user in new[] { Client, Amra, Haris }) AddUser(connection, user);
            Run(connection, "INSERT INTO Companies (Id, Name, IsActive, CreatedAt) VALUES (@id, 'Acme', 1, SYSUTCDATETIME())", ("@id", Company));

            AddTicket(connection, "BOTH", Amra, Amra, Haris);       // the normal case: the column and the list agree
            AddTicket(connection, "NOT-ON-LIST", Haris, Amra);       // the column names someone the list lacks: they must not be lost
            AddTicket(connection, "COLUMN-ONLY", Amra);              // the column names someone and the list is empty
            AddTicket(connection, "LIST-ONLY", null, Amra, Haris);   // the list has people and the column nobody: stays without a main assignee
            AddTicket(connection, "NOBODY", null);                   // nobody anywhere

            // ---- forward ----
            await migrator.MigrateAsync();

            Assert.Equal(new[] { (Amra, true), (Haris, false) }.OrderBy(x => x.Item1), ListOf(connection, "BOTH").OrderBy(x => x.User).Select(x => (x.User, x.Primary)));
            Assert.Equal(new[] { (Amra, false), (Haris, true) }.OrderBy(x => x.Item1), ListOf(connection, "NOT-ON-LIST").OrderBy(x => x.User).Select(x => (x.User, x.Primary)));
            Assert.Equal(new[] { (Amra, true) }, ListOf(connection, "COLUMN-ONLY").Select(x => (x.User, x.Primary)));
            Assert.Equal(new[] { (Amra, false), (Haris, false) }.OrderBy(x => x.Item1), ListOf(connection, "LIST-ONLY").OrderBy(x => x.User).Select(x => (x.User, x.Primary)));
            Assert.Empty(ListOf(connection, "NOBODY"));

            Assert.Equal(0, Scalar<int>(connection, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tickets' AND COLUMN_NAME = 'AssignedToUserId'"));

            // the database itself now refuses a second main assignee on one ticket
            var both = Scalar<Guid>(connection, "SELECT Id FROM Tickets WHERE TicketNumber = 'BOTH'");
            var refused = Assert.Throws<SqlException>(() => Run(connection, "UPDATE TicketAssignees SET IsPrimary = 1 WHERE TicketId = @t AND UserId = @u", ("@t", both), ("@u", Haris)));
            Assert.Contains("UX_TicketAssignees_OnePrimaryPerTicket", refused.Message);

            // ---- back again ----
            await migrator.MigrateAsync(Before);

            Assert.Equal(Amra, Scalar<Guid>(connection, "SELECT AssignedToUserId FROM Tickets WHERE TicketNumber = 'BOTH'"));
            Assert.Equal(Haris, Scalar<Guid>(connection, "SELECT AssignedToUserId FROM Tickets WHERE TicketNumber = 'NOT-ON-LIST'"));
            Assert.Equal(Amra, Scalar<Guid>(connection, "SELECT AssignedToUserId FROM Tickets WHERE TicketNumber = 'COLUMN-ONLY'"));
            Assert.Null(Scalar<Guid?>(connection, "SELECT AssignedToUserId FROM Tickets WHERE TicketNumber = 'LIST-ONLY'"));
            Assert.Null(Scalar<Guid?>(connection, "SELECT AssignedToUserId FROM Tickets WHERE TicketNumber = 'NOBODY'"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var master = TestSql.OpenMaster();
            Run(master, $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
        }
    }
}
