using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BranchesCannotBeDeletedWhileInUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_SubBranches_SubBranchId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Departments_DepartmentId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_SubBranches_SubBranchId",
                table: "Tickets");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_SubBranches_SubBranchId",
                table: "AspNetUsers",
                column: "SubBranchId",
                principalTable: "SubBranches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Departments_DepartmentId",
                table: "Tickets",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_SubBranches_SubBranchId",
                table: "Tickets",
                column: "SubBranchId",
                principalTable: "SubBranches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_SubBranches_SubBranchId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Departments_DepartmentId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_SubBranches_SubBranchId",
                table: "Tickets");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_SubBranches_SubBranchId",
                table: "AspNetUsers",
                column: "SubBranchId",
                principalTable: "SubBranches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Departments_DepartmentId",
                table: "Tickets",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_SubBranches_SubBranchId",
                table: "Tickets",
                column: "SubBranchId",
                principalTable: "SubBranches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
