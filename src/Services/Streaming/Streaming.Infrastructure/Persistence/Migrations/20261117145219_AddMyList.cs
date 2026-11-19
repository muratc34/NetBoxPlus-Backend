using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMyList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MyLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MyListEntries",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyListEntries", x => new { x.ProfileId, x.TitleId });
                    table.ForeignKey(
                        name: "FK_MyListEntries_MyLists_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "MyLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MyListEntries_ProfileId_AddedAt",
                table: "MyListEntries",
                columns: new[] { "ProfileId", "AddedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MyLists_UserId",
                table: "MyLists",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MyListEntries");

            migrationBuilder.DropTable(
                name: "MyLists");
        }
    }
}
