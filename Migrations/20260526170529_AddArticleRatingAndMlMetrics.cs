using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace КР_Ханников.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleRatingAndMlMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HelpfulCount",
                table: "KnowledgeArticle",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NotHelpfulCount",
                table: "KnowledgeArticle",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ViewCount",
                table: "KnowledgeArticle",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MlModelMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModelType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrainedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MicroAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    MacroAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    LogLoss = table.Column<double>(type: "double precision", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MlModelMetrics", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MlModelMetrics");

            migrationBuilder.DropColumn(
                name: "HelpfulCount",
                table: "KnowledgeArticle");

            migrationBuilder.DropColumn(
                name: "NotHelpfulCount",
                table: "KnowledgeArticle");

            migrationBuilder.DropColumn(
                name: "ViewCount",
                table: "KnowledgeArticle");
        }
    }
}
