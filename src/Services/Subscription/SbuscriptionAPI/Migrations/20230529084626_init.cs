using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SubscriptionAPI.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Quality = table.Column<string>(type: "text", nullable: false),
                    ProfileCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionStartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubscriptionExpiration = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubscriptionStatus = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "ProfileCount", "Quality" },
                values: new object[,]
                {
                    { new Guid("1198a41e-043b-47f2-81ce-e0ed9e3313e2"), 2, "FHD" },
                    { new Guid("7c455f08-cb50-4b04-b0e0-bc5f827dfc18"), 3, "UHD" },
                    { new Guid("ba50cd36-001b-47ce-998e-4f6e53523120"), 1, "HD" }
                });

            migrationBuilder.InsertData(
                table: "Subscriptions",
                columns: new[] { "Id", "PlanId", "SubscriptionExpiration", "SubscriptionStartDate", "SubscriptionStatus", "UserId" },
                values: new object[] { new Guid("8ce76648-d034-44da-9376-dfba0e65f528"), new Guid("ba50cd36-001b-47ce-998e-4f6e53523120"), new DateTimeOffset(new DateTime(2023, 6, 28, 11, 46, 26, 307, DateTimeKind.Unspecified).AddTicks(3478), new TimeSpan(0, 3, 0, 0, 0)), new DateTimeOffset(new DateTime(2023, 5, 29, 11, 46, 26, 307, DateTimeKind.Unspecified).AddTicks(3516), new TimeSpan(0, 3, 0, 0, 0)), true, new Guid("00000000-0000-0000-0000-000000000000") });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                table: "Subscriptions",
                column: "PlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
