using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AuthAPI.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditCards",
                columns: table => new
                {
                    CreditCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditCardBrand = table.Column<string>(type: "text", nullable: true),
                    CreditCardName = table.Column<string>(type: "text", nullable: true),
                    CreditCardNumber = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditCards", x => x.CreditCardId);
                });

            migrationBuilder.CreateTable(
                name: "OperationClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: "bytea", nullable: true),
                    PasswordHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    Status = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationClaimsUser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationClaimId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationClaimsUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationClaimsUser_OperationClaims_OperationClaimId",
                        column: x => x.OperationClaimId,
                        principalTable: "OperationClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OperationClaimsUser_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperationClaimUser",
                columns: table => new
                {
                    OperationClaimsId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsersId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationClaimUser", x => new { x.OperationClaimsId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_OperationClaimUser_OperationClaims_OperationClaimsId",
                        column: x => x.OperationClaimsId,
                        principalTable: "OperationClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OperationClaimUser_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileName = table.Column<string>(type: "text", nullable: false),
                    PinHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    PinSalt = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "OperationClaims",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("0dcb249a-00dd-427b-a096-5df2b5742d91"), "Admin" },
                    { new Guid("65e1c0c8-4d30-4edb-b428-92ea4a28d604"), "Kullanıcı" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Email", "FirstName", "LastName", "PasswordHash", "PasswordSalt", "PaymentId", "Status", "SubscriptionId" },
                values: new object[,]
                {
                    { new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b"), "murat@murat.com", "Murat", "Cinek", new byte[] { 159, 173, 87, 160, 192, 215, 69, 118, 59, 18, 169, 128, 135, 221, 92, 9, 107, 83, 143, 88, 96, 64, 231, 22, 150, 206, 46, 165, 10, 219, 23, 128, 251, 227, 226, 14, 37, 81, 92, 191, 76, 192, 113, 95, 251, 255, 168, 113, 35, 94, 117, 211, 86, 57, 148, 61, 126, 156, 80, 219, 77, 150, 90, 216 }, new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 }, new Guid("2102fbea-73a7-468d-8e16-7e639e0b61cb"), false, null },
                    { new Guid("cae0ce15-1958-49dd-8abb-d82be6129764"), "admin@admin.com", "Admin", "Admin", new byte[] { 179, 15, 57, 2, 244, 241, 230, 226, 58, 8, 5, 13, 232, 237, 114, 18, 173, 126, 77, 184, 143, 250, 180, 28, 214, 16, 102, 64, 245, 171, 155, 27, 77, 25, 159, 253, 120, 229, 229, 125, 36, 216, 162, 167, 182, 0, 244, 63, 109, 73, 20, 76, 158, 138, 80, 33, 225, 194, 124, 35, 150, 174, 233, 204 }, new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 }, new Guid("597c84d5-e472-4fc3-9d6f-d72a8501eb91"), false, null }
                });

            migrationBuilder.InsertData(
                table: "OperationClaimsUser",
                columns: new[] { "Id", "OperationClaimId", "UserId" },
                values: new object[,]
                {
                    { new Guid("157bd11f-7acf-4009-91be-90090aa8a787"), new Guid("65e1c0c8-4d30-4edb-b428-92ea4a28d604"), new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b") },
                    { new Guid("f5c908ec-2841-4041-af77-b4696e0d52f3"), new Guid("0dcb249a-00dd-427b-a096-5df2b5742d91"), new Guid("cae0ce15-1958-49dd-8abb-d82be6129764") }
                });

            migrationBuilder.InsertData(
                table: "Profiles",
                columns: new[] { "Id", "PinHash", "PinSalt", "ProfileName", "UserId" },
                values: new object[,]
                {
                    { new Guid("66bf0e71-b945-4acf-85ba-8e08c26fad18"), new byte[] { 218, 223, 11, 8, 202, 97, 134, 184, 190, 103, 215, 28, 154, 10, 223, 211, 218, 214, 164, 2, 168, 191, 18, 73, 233, 252, 5, 247, 186, 26, 189, 199, 176, 210, 179, 39, 182, 89, 91, 102, 15, 161, 246, 128, 14, 137, 117, 176, 62, 153, 170, 9, 217, 159, 25, 123, 110, 69, 112, 155, 71, 81, 183, 187 }, new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 }, "Default", new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b") },
                    { new Guid("a383c73c-2dc2-407c-a47b-103c201b93d6"), new byte[] { 22, 200, 83, 173, 54, 242, 33, 65, 206, 13, 201, 54, 40, 50, 89, 128, 7, 29, 80, 237, 203, 193, 6, 164, 221, 1, 193, 47, 136, 235, 2, 179, 171, 150, 77, 181, 177, 89, 220, 60, 4, 173, 127, 22, 40, 89, 7, 166, 136, 65, 242, 137, 182, 154, 12, 79, 210, 173, 205, 228, 92, 201, 204, 8 }, new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 }, "Test", new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationClaimsUser_OperationClaimId",
                table: "OperationClaimsUser",
                column: "OperationClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationClaimsUser_UserId",
                table: "OperationClaimsUser",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationClaimUser_UsersId",
                table: "OperationClaimUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditCards");

            migrationBuilder.DropTable(
                name: "OperationClaimsUser");

            migrationBuilder.DropTable(
                name: "OperationClaimUser");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "OperationClaims");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
