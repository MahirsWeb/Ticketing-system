using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketSystemTech.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves embeddings from JSON text to raw float32 bytes.
    ///
    /// The scaffolded version of this migration ran ALTER COLUMN nvarchar(max) -> varbinary(max) on the existing
    /// column. SQL Server would have accepted that and "converted" every JSON string to the UTF-16 bytes of its
    /// text (garbage, and a rewrite of well over a gigabyte). Instead the old column is renamed to EmbeddingJson
    /// and left exactly as it is, a new EmbeddingBinary column is added, and EmbeddingFormatBackfill fills it from
    /// the JSON at startup. The JSON is kept until the conversion has been verified; a later migration drops it.
    /// </summary>
    public partial class KnowledgeBaseBinaryEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Embedding",
                table: "KnowledgeBaseChunks",
                newName: "EmbeddingJson");

            migrationBuilder.AddColumn<byte[]>(
                name: "EmbeddingBinary",
                table: "KnowledgeBaseChunks",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "KnowledgeBaseChunks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Vectors created after the upgrade exist only in EmbeddingBinary and are lost by going back;
            // they can be recreated with a reindex.
            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "KnowledgeBaseChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingBinary",
                table: "KnowledgeBaseChunks");

            migrationBuilder.RenameColumn(
                name: "EmbeddingJson",
                table: "KnowledgeBaseChunks",
                newName: "Embedding");
        }
    }
}
