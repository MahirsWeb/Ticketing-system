using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A ticket could hold one start and one end of work, so the second time somebody worked on it the first was overwritten.
    /// Work is now a list of sessions per ticket. The one pair every ticket may have is carried over as that ticket's first
    /// session, for the person it is assigned to, before the two old columns are dropped.
    /// </summary>
    /// <inheritdoc />
    public partial class TicketWorkSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketWorkSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketWorkSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketWorkSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketWorkSessions_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketWorkSessions_TicketId_StartedAtUtc",
                table: "TicketWorkSessions",
                columns: new[] { "TicketId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketWorkSessions_UserId_StartedAtUtc",
                table: "TicketWorkSessions",
                columns: new[] { "UserId", "StartedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "EndedAtUtc", "TicketId" });

            // Carry the old pair over. The session belongs to whoever the ticket is assigned to (or, failing that, whoever opened
            // or closed it). A pair with no start, one that ends before it starts, or one that cannot be attributed to anybody
            // has no meaning as a session and is left behind. A ticket that was closed without an end time ends when it closed,
            // which is what the old screens assumed.
            migrationBuilder.Sql(@"
INSERT INTO TicketWorkSessions (Id, TicketId, UserId, StartedAtUtc, EndedAtUtc, CreatedAt)
SELECT NEWID(),
       t.Id,
       COALESCE(t.AssignedToUserId, t.OpenedByUserId, t.ClosedByUserId),
       t.WorkStartedAtUtc,
       CASE WHEN t.WorkEndedAtUtc IS NULL AND t.ClosedAtUtc IS NOT NULL THEN t.ClosedAtUtc ELSE t.WorkEndedAtUtc END,
       SYSUTCDATETIME()
FROM Tickets t
WHERE t.WorkStartedAtUtc IS NOT NULL
  AND COALESCE(t.AssignedToUserId, t.OpenedByUserId, t.ClosedByUserId) IS NOT NULL
  AND (CASE WHEN t.WorkEndedAtUtc IS NULL AND t.ClosedAtUtc IS NOT NULL THEN t.ClosedAtUtc ELSE t.WorkEndedAtUtc END IS NULL
       OR CASE WHEN t.WorkEndedAtUtc IS NULL AND t.ClosedAtUtc IS NOT NULL THEN t.ClosedAtUtc ELSE t.WorkEndedAtUtc END > t.WorkStartedAtUtc);");

            migrationBuilder.DropColumn(
                name: "WorkEndedAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "WorkStartedAtUtc",
                table: "Tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WorkEndedAtUtc",
                table: "Tickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkStartedAtUtc",
                table: "Tickets",
                type: "datetime2",
                nullable: true);

            // Back to one pair per ticket: from the first start to the last end (no end while any session is still running).
            migrationBuilder.Sql(@"
UPDATE t
SET t.WorkStartedAtUtc = x.FirstStart, t.WorkEndedAtUtc = x.LastEnd
FROM Tickets t
JOIN (SELECT TicketId,
             MIN(StartedAtUtc) AS FirstStart,
             CASE WHEN SUM(CASE WHEN EndedAtUtc IS NULL THEN 1 ELSE 0 END) > 0 THEN NULL ELSE MAX(EndedAtUtc) END AS LastEnd
      FROM TicketWorkSessions GROUP BY TicketId) x ON x.TicketId = t.Id;");

            migrationBuilder.DropTable(
                name: "TicketWorkSessions");
        }
    }
}
