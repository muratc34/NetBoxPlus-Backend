using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every video so far is a movie's or an episode's own.
            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Videos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Feature");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Videos");
        }
    }
}
