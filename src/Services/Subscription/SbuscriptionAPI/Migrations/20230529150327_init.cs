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
                    PlanName = table.Column<string>(type: "text", nullable: true),
                    Quality = table.Column<string>(type: "text", nullable: false),
                    ProfileCount = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false)
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
                });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "Amount", "PlanName", "ProfileCount", "Quality" },
                values: new object[,]
                {
                    { new Guid("0318768b-675e-4647-b8bb-d53e9743917b"), 0m, null, 2, "FHD" },
                    { new Guid("0494158e-41b6-4399-97e5-60cd004ca972"), 0m, null, 1, "HD" },
                    { new Guid("b3d21573-3eb0-48c8-8f68-519bde071d52"), 0m, null, 3, "UHD" }
                });

            migrationBuilder.InsertData(
                table: "Subscriptions",
                columns: new[] { "Id", "PlanId", "SubscriptionExpiration", "SubscriptionStartDate", "SubscriptionStatus", "UserId" },
                values: new object[] { new Guid("edd2a38a-5670-41fb-b997-52042cc68e36"), new Guid("0494158e-41b6-4399-97e5-60cd004ca972"), new DateTimeOffset(new DateTime(2023, 6, 28, 18, 3, 27, 464, DateTimeKind.Unspecified).AddTicks(3125), new TimeSpan(0, 3, 0, 0, 0)), new DateTimeOffset(new DateTime(2023, 5, 29, 18, 3, 27, 464, DateTimeKind.Unspecified).AddTicks(3165), new TimeSpan(0, 3, 0, 0, 0)), true, new Guid("00000000-0000-0000-0000-000000000000") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropTable(
                name: "Subscriptions");
        }
    }
}
