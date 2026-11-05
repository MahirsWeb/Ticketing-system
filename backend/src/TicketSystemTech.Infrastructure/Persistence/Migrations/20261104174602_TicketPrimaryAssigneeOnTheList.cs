using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketPrimaryAssigneeOnTheList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "TicketAssignees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The person a ticket was "assigned to" is now the one marked on the list. Where the list already had them, mark that
            // row; where it did not (the two used to drift apart), add them, so nobody who was assigned is lost. A ticket whose list
            // has people but no main assignee stays that way: it was that way before.
            migrationBuilder.Sql(@"
                UPDATE a SET IsPrimary = 1
                FROM TicketAssignees a
                JOIN Tickets t ON t.Id = a.TicketId AND t.AssignedToUserId = a.UserId;");
            migrationBuilder.Sql(@"
                INSERT INTO TicketAssignees (Id, CreatedAt, TicketId, UserId, AssignedAtUtc, IsPrimary)
                SELECT NEWID(), SYSUTCDATETIME(), t.Id, t.AssignedToUserId, COALESCE(t.OpenedAtUtc, t.CreatedAt), 1
                FROM Tickets t
                WHERE t.AssignedToUserId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM TicketAssignees a WHERE a.TicketId = t.Id AND a.UserId = t.AssignedToUserId);");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedToUserId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AssignedToUserId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "UX_TicketAssignees_OnePrimaryPerTicket",
                table: "TicketAssignees",
                column: "TicketId",
                unique: true,
                filter: "[IsPrimary] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "Tickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE t SET AssignedToUserId = a.UserId
                FROM Tickets t
                JOIN TicketAssignees a ON a.TicketId = t.Id AND a.IsPrimary = 1;");

            migrationBuilder.DropIndex(
                name: "UX_TicketAssignees_OnePrimaryPerTicket",
                table: "TicketAssignees");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "TicketAssignees");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedToUserId",
                table: "Tickets",
                column: "AssignedToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedToUserId",
                table: "Tickets",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
