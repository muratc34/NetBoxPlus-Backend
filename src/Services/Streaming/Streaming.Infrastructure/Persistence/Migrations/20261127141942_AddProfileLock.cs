using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "Profiles");
        }
    }
}
