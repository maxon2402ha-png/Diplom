using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace КР_Ханников.Migrations
{
    /// <inheritdoc />
    public partial class AddArticlePublishState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "KnowledgeArticle",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "KnowledgeArticle");
        }
    }
}
