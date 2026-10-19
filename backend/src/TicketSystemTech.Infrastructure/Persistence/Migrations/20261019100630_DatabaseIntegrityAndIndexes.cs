using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseIntegrityAndIndexes : Migration
    {
        // (table, column, parent table, column is nullable)
        private static readonly (string Table, string Column, string Parent, bool Optional)[] References =
        {
            ("Tickets", "ClientId", "AspNetUsers", false),
            ("Tickets", "AssignedToUserId", "AspNetUsers", true),
            ("Tickets", "OpenedByUserId", "AspNetUsers", true),
            ("Tickets", "ClosedByUserId", "AspNetUsers", true),
            ("TicketMessages", "AuthorId", "AspNetUsers", false),
            ("TicketAttachments", "UploadedByUserId", "AspNetUsers", false),
            ("TicketAssignees", "UserId", "AspNetUsers", false),
            ("KnowledgeBaseDocuments", "UploadedByUserId", "AspNetUsers", false),
            ("WorkTasks", "CreatedByUserId", "AspNetUsers", false),
            ("WorkTasks", "AssignedByUserId", "AspNetUsers", false),
            ("WorkTasks", "AssignedToUserId", "AspNetUsers", false),
            ("WorkTaskAssignmentLogs", "AssignedByUserId", "AspNetUsers", false),
            ("WorkTaskAssignmentLogs", "AssignedToUserId", "AspNetUsers", false),
            ("EmailConnections", "ConnectedByUserId", "AspNetUsers", false),
            ("EmailConnections", "DepartmentId", "Departments", false),
            ("EmailTicketMarks", "MarkedByUserId", "AspNetUsers", false),
            ("EmailTicketMarks", "DepartmentId", "Departments", false),
            ("EmailTicketMarks", "ConvertedTicketId", "Tickets", true),
            ("AuditLogs", "UserId", "AspNetUsers", true),
            ("AspNetUsers", "CompanyId", "Companies", true),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Preflight: foreign keys can only be added over data that satisfies them. ----
            // A notification for a user that no longer exists is meaningless, so those are simply removed. For every other
            // reference the migration stops with a message naming the column, rather than failing half way through with
            // a bare constraint error and leaving the schema partly changed.
            migrationBuilder.Sql(
                "DELETE FROM [Notifications] WHERE NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = [Notifications].[UserId])");

            foreach (var (table, column, parent, optional) in References)
            {
                var test = optional
                    ? $"[{column}] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [{parent}] p WHERE p.[Id] = c.[{column}])"
                    : $"NOT EXISTS (SELECT 1 FROM [{parent}] p WHERE p.[Id] = c.[{column}])";
                migrationBuilder.Sql(
                    $"IF EXISTS (SELECT 1 FROM [{table}] c WHERE {test}) " +
                    $"THROW 50001, 'Cannot add a foreign key: {table}.{column} contains values that match no row in {parent}. " +
                    "Correct or remove those rows and start the application again; nothing was changed.', 1;");
            }

            // The preflight and the schema change below run in one migration transaction, so a failure leaves the database as it was.

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_AssignedToUserId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_DepartmentId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Priority_DueDateUtc",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_TicketMessages_TicketId",
                table: "TicketMessages");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseChunks_TicketId",
                table: "KnowledgeBaseChunks");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DepartmentId",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Tickets",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "EmailConnections",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                table: "AuditLogs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssignedByUserId",
                table: "WorkTasks",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssignedToUserId_StartedAtUtc",
                table: "WorkTasks",
                columns: new[] { "AssignedToUserId", "StartedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "EndedAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_CreatedByUserId",
                table: "WorkTasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAssignmentLogs_AssignedByUserId",
                table: "WorkTaskAssignmentLogs",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAssignmentLogs_AssignedToUserId",
                table: "WorkTaskAssignmentLogs",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ClosedByUserId",
                table: "Tickets",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_DepartmentId_CreatedAt",
                table: "Tickets",
                columns: new[] { "DepartmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_DepartmentId_Status",
                table: "Tickets",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_OpenedByUserId",
                table: "Tickets",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Priority_CreatedAt",
                table: "Tickets",
                columns: new[] { "Priority", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Status_NotYetNotified",
                table: "Tickets",
                column: "Status",
                filter: "[OverdueNotifiedAtUtc] IS NULL")
                .Annotation("SqlServer:Include", new[] { "DueDateUtc", "OpenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_AuthorId",
                table: "TicketMessages",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId_Type",
                table: "TicketMessages",
                columns: new[] { "TicketId", "Type" })
                .Annotation("SqlServer:Include", new[] { "AuthorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketAttachments_UploadedByUserId",
                table: "TicketAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssignees_UserId_TicketId",
                table: "TicketAssignees",
                columns: new[] { "UserId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "IsRead", "Type", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_UploadedByUserId",
                table: "KnowledgeBaseDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseChunks_TicketId",
                table: "KnowledgeBaseChunks",
                column: "TicketId",
                unique: true,
                filter: "[TicketId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTicketMarks_ConvertedTicketId",
                table: "EmailTicketMarks",
                column: "ConvertedTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTicketMarks_MarkedByUserId",
                table: "EmailTicketMarks",
                column: "MarkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailConnections_ConnectedByUserId",
                table: "EmailConnections",
                column: "ConnectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DepartmentId_Role_IsActive",
                table: "AspNetUsers",
                columns: new[] { "DepartmentId", "Role", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Companies_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_AspNetUsers_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailConnections_AspNetUsers_ConnectedByUserId",
                table: "EmailConnections",
                column: "ConnectedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailConnections_Departments_DepartmentId",
                table: "EmailConnections",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailTicketMarks_AspNetUsers_MarkedByUserId",
                table: "EmailTicketMarks",
                column: "MarkedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailTicketMarks_Departments_DepartmentId",
                table: "EmailTicketMarks",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailTicketMarks_Tickets_ConvertedTicketId",
                table: "EmailTicketMarks",
                column: "ConvertedTicketId",
                principalTable: "Tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBaseDocuments_AspNetUsers_UploadedByUserId",
                table: "KnowledgeBaseDocuments",
                column: "UploadedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_AspNetUsers_UserId",
                table: "Notifications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TicketAssignees_AspNetUsers_UserId",
                table: "TicketAssignees",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TicketAttachments_AspNetUsers_UploadedByUserId",
                table: "TicketAttachments",
                column: "UploadedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TicketMessages_AspNetUsers_AuthorId",
                table: "TicketMessages",
                column: "AuthorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedToUserId",
                table: "Tickets",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_ClientId",
                table: "Tickets",
                column: "ClientId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_ClosedByUserId",
                table: "Tickets",
                column: "ClosedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_OpenedByUserId",
                table: "Tickets",
                column: "OpenedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTaskAssignmentLogs_AspNetUsers_AssignedByUserId",
                table: "WorkTaskAssignmentLogs",
                column: "AssignedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTaskAssignmentLogs_AspNetUsers_AssignedToUserId",
                table: "WorkTaskAssignmentLogs",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_AspNetUsers_AssignedByUserId",
                table: "WorkTasks",
                column: "AssignedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_AspNetUsers_AssignedToUserId",
                table: "WorkTasks",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_AspNetUsers_CreatedByUserId",
                table: "WorkTasks",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---- Re-trust older foreign keys ----
            // The bulk import of the old osTicket data left several foreign keys "not trusted" (created or re-enabled
            // without checking existing rows). They still reject bad new rows, but the query optimiser may not rely on
            // them. Re-validating is cheap when the data is consistent; a constraint whose data is not stays as it is.
            migrationBuilder.Sql(@"
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'BEGIN TRY ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
             + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME(name) + N'; END TRY BEGIN CATCH END CATCH; '
FROM sys.foreign_keys WHERE is_not_trusted = 1 AND is_disabled = 0;
IF LEN(@sql) > 0 EXEC sp_executesql @sql;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Companies_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_AspNetUsers_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailConnections_AspNetUsers_ConnectedByUserId",
                table: "EmailConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailConnections_Departments_DepartmentId",
                table: "EmailConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailTicketMarks_AspNetUsers_MarkedByUserId",
                table: "EmailTicketMarks");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailTicketMarks_Departments_DepartmentId",
                table: "EmailTicketMarks");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailTicketMarks_Tickets_ConvertedTicketId",
                table: "EmailTicketMarks");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBaseDocuments_AspNetUsers_UploadedByUserId",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_AspNetUsers_UserId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_TicketAssignees_AspNetUsers_UserId",
                table: "TicketAssignees");

            migrationBuilder.DropForeignKey(
                name: "FK_TicketAttachments_AspNetUsers_UploadedByUserId",
                table: "TicketAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_TicketMessages_AspNetUsers_AuthorId",
                table: "TicketMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedToUserId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_ClientId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_ClosedByUserId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_OpenedByUserId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkTaskAssignmentLogs_AspNetUsers_AssignedByUserId",
                table: "WorkTaskAssignmentLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkTaskAssignmentLogs_AspNetUsers_AssignedToUserId",
                table: "WorkTaskAssignmentLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_AspNetUsers_AssignedByUserId",
                table: "WorkTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_AspNetUsers_AssignedToUserId",
                table: "WorkTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_AspNetUsers_CreatedByUserId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_AssignedByUserId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_AssignedToUserId_StartedAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_CreatedByUserId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTaskAssignmentLogs_AssignedByUserId",
                table: "WorkTaskAssignmentLogs");

            migrationBuilder.DropIndex(
                name: "IX_WorkTaskAssignmentLogs_AssignedToUserId",
                table: "WorkTaskAssignmentLogs");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_ClosedByUserId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_DepartmentId_CreatedAt",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_DepartmentId_Status",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_OpenedByUserId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Priority_CreatedAt",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Status_NotYetNotified",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_TicketMessages_AuthorId",
                table: "TicketMessages");

            migrationBuilder.DropIndex(
                name: "IX_TicketMessages_TicketId_Type",
                table: "TicketMessages");

            migrationBuilder.DropIndex(
                name: "IX_TicketAttachments_UploadedByUserId",
                table: "TicketAttachments");

            migrationBuilder.DropIndex(
                name: "IX_TicketAssignees_UserId_TicketId",
                table: "TicketAssignees");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseDocuments_UploadedByUserId",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseChunks_TicketId",
                table: "KnowledgeBaseChunks");

            migrationBuilder.DropIndex(
                name: "IX_EmailTicketMarks_ConvertedTicketId",
                table: "EmailTicketMarks");

            migrationBuilder.DropIndex(
                name: "IX_EmailTicketMarks_MarkedByUserId",
                table: "EmailTicketMarks");

            migrationBuilder.DropIndex(
                name: "IX_EmailConnections_ConnectedByUserId",
                table: "EmailConnections");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_EntityId_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DepartmentId_Role_IsActive",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "EmailConnections");

            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                table: "AuditLogs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssignedToUserId",
                table: "WorkTasks",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_DepartmentId",
                table: "Tickets",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Priority_DueDateUtc",
                table: "Tickets",
                columns: new[] { "Priority", "DueDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId",
                table: "TicketMessages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseChunks_TicketId",
                table: "KnowledgeBaseChunks",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId");
        }
    }
}
