using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A task could hold one start and one end of work, so starting it a second time overwrote the first (and a manual
    /// correction could put the time years ahead). Work on a task is now a list of sessions, like work on a ticket. The one
    /// pair every task may have is carried over as that task's first session, for the person it is assigned to, before the
    /// two old columns are dropped. The migration also gives a task a concurrency token, so two people handing it over or
    /// correcting its time at once are told about the conflict instead of one silently erasing the other.
    /// </summary>
    /// <inheritdoc />
    public partial class WorkTaskSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkTaskSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkTaskSessions_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskSessions_UserId_StartedAtUtc",
                table: "WorkTaskSessions",
                columns: new[] { "UserId", "StartedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "EndedAtUtc", "WorkTaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskSessions_WorkTaskId_StartedAtUtc",
                table: "WorkTaskSessions",
                columns: new[] { "WorkTaskId", "StartedAtUtc" });

            // Carry the old pair over. The session belongs to whoever the task is assigned to. A pair with no start, or one that
            // ends at or before its start, has no meaning as a session and is left behind.
            migrationBuilder.Sql(@"
INSERT INTO WorkTaskSessions (Id, WorkTaskId, UserId, StartedAtUtc, EndedAtUtc, CreatedAt)
SELECT NEWID(), t.Id, t.AssignedToUserId, t.StartedAtUtc, t.EndedAtUtc, SYSUTCDATETIME()
FROM WorkTasks t
WHERE t.StartedAtUtc IS NOT NULL
  AND (t.EndedAtUtc IS NULL OR t.EndedAtUtc > t.StartedAtUtc);");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_AssignedToUserId_StartedAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "EndedAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "WorkTasks");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "WorkTasks",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssignedToUserId",
                table: "WorkTasks",
                column: "AssignedToUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_AssignedToUserId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "WorkTasks");

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAtUtc",
                table: "WorkTasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "WorkTasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssignedToUserId_StartedAtUtc",
                table: "WorkTasks",
                columns: new[] { "AssignedToUserId", "StartedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "EndedAtUtc", "Status" });

            // Back to one pair per task: from the first start to the last end (no end while any session is still running).
            migrationBuilder.Sql(@"
UPDATE t
SET t.StartedAtUtc = x.FirstStart, t.EndedAtUtc = x.LastEnd
FROM WorkTasks t
JOIN (SELECT WorkTaskId,
             MIN(StartedAtUtc) AS FirstStart,
             CASE WHEN SUM(CASE WHEN EndedAtUtc IS NULL THEN 1 ELSE 0 END) > 0 THEN NULL ELSE MAX(EndedAtUtc) END AS LastEnd
      FROM WorkTaskSessions GROUP BY WorkTaskId) x ON x.WorkTaskId = t.Id;");

            migrationBuilder.DropTable(
                name: "WorkTaskSessions");
        }
    }
}
