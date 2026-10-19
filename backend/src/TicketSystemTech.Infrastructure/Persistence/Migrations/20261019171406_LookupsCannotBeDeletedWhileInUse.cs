using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LookupsCannotBeDeletedWhileInUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_HelpTopics_HelpTopicId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_SlaPlans_SlaPlanId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_TicketCategories_CategoryId",
                table: "Tickets");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_HelpTopics_HelpTopicId",
                table: "Tickets",
                column: "HelpTopicId",
                principalTable: "HelpTopics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_SlaPlans_SlaPlanId",
                table: "Tickets",
                column: "SlaPlanId",
                principalTable: "SlaPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_TicketCategories_CategoryId",
                table: "Tickets",
                column: "CategoryId",
                principalTable: "TicketCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_HelpTopics_HelpTopicId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_SlaPlans_SlaPlanId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_TicketCategories_CategoryId",
                table: "Tickets");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_HelpTopics_HelpTopicId",
                table: "Tickets",
                column: "HelpTopicId",
                principalTable: "HelpTopics",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_SlaPlans_SlaPlanId",
                table: "Tickets",
                column: "SlaPlanId",
                principalTable: "SlaPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_TicketCategories_CategoryId",
                table: "Tickets",
                column: "CategoryId",
                principalTable: "TicketCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
