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
                name: "OperationClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    PasswordHash = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Status = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationClaimUser",
                columns: table => new
                {
                    OperationClaimsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsersId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "UserOperationClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOperationClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOperationClaims_OperationClaims_OperationClaimId",
                        column: x => x.OperationClaimId,
                        principalTable: "OperationClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserOperationClaims_Users_UserId",
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
                    { new Guid("a4d8c529-eaf8-4e9d-bea5-7209291fdc4c"), "Kullanıcı" },
                    { new Guid("ff5990a4-e68b-41c9-a917-cd81009252ef"), "Admin" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Email", "FirstName", "LastName", "PasswordHash", "PasswordSalt", "Status" },
                values: new object[,]
                {
                    { new Guid("99c05531-503f-4179-859b-5408a3a0f34b"), "admin@admin.com", "Admin", "Admin", new byte[] { 189, 130, 76, 211, 219, 98, 147, 90, 141, 111, 248, 18, 148, 213, 193, 118, 82, 127, 211, 225, 231, 130, 33, 228, 4, 254, 8, 22, 5, 74, 132, 204, 85, 195, 146, 236, 26, 24, 51, 68, 11, 10, 118, 121, 223, 30, 84, 137, 81, 89, 33, 95, 54, 119, 36, 186, 245, 135, 115, 177, 117, 41, 23, 97 }, new byte[] { 205, 237, 169, 93, 15, 247, 167, 197, 78, 227, 115, 156, 1, 46, 44, 237, 210, 252, 148, 198, 203, 163, 108, 203, 244, 66, 140, 241, 88, 205, 49, 125, 31, 104, 71, 22, 250, 146, 225, 76, 125, 100, 37, 176, 30, 253, 44, 196, 13, 57, 3, 141, 91, 21, 143, 130, 11, 44, 219, 124, 165, 255, 172, 53, 96, 37, 13, 72, 241, 44, 205, 159, 80, 203, 182, 0, 3, 216, 252, 196, 184, 152, 249, 53, 196, 65, 244, 242, 228, 78, 93, 65, 89, 166, 186, 6, 227, 96, 110, 83, 157, 242, 235, 219, 161, 114, 94, 174, 135, 85, 48, 0, 203, 246, 63, 73, 175, 69, 174, 65, 119, 144, 162, 173, 143, 58, 1, 180 }, false },
                    { new Guid("c5ec0a6f-9d71-4055-a44c-77a6f455de71"), "murat@murat.com", "Murat", "Cinek", new byte[] { 16, 94, 187, 234, 68, 156, 139, 52, 50, 176, 88, 182, 84, 141, 46, 93, 185, 188, 161, 73, 199, 245, 175, 88, 43, 147, 131, 239, 189, 44, 178, 240, 103, 234, 183, 196, 15, 253, 232, 129, 152, 180, 40, 170, 239, 113, 44, 142, 108, 27, 233, 141, 242, 115, 147, 206, 187, 57, 86, 228, 232, 212, 184, 127 }, new byte[] { 205, 237, 169, 93, 15, 247, 167, 197, 78, 227, 115, 156, 1, 46, 44, 237, 210, 252, 148, 198, 203, 163, 108, 203, 244, 66, 140, 241, 88, 205, 49, 125, 31, 104, 71, 22, 250, 146, 225, 76, 125, 100, 37, 176, 30, 253, 44, 196, 13, 57, 3, 141, 91, 21, 143, 130, 11, 44, 219, 124, 165, 255, 172, 53, 96, 37, 13, 72, 241, 44, 205, 159, 80, 203, 182, 0, 3, 216, 252, 196, 184, 152, 249, 53, 196, 65, 244, 242, 228, 78, 93, 65, 89, 166, 186, 6, 227, 96, 110, 83, 157, 242, 235, 219, 161, 114, 94, 174, 135, 85, 48, 0, 203, 246, 63, 73, 175, 69, 174, 65, 119, 144, 162, 173, 143, 58, 1, 180 }, false }
                });

            migrationBuilder.InsertData(
                table: "UserOperationClaims",
                columns: new[] { "Id", "OperationClaimId", "UserId" },
                values: new object[,]
                {
                    { new Guid("123d74c0-36c9-408e-8adb-64fbe2eee371"), new Guid("a4d8c529-eaf8-4e9d-bea5-7209291fdc4c"), new Guid("c5ec0a6f-9d71-4055-a44c-77a6f455de71") },
                    { new Guid("37625acc-172f-46e1-aede-05bcedf303d4"), new Guid("ff5990a4-e68b-41c9-a917-cd81009252ef"), new Guid("99c05531-503f-4179-859b-5408a3a0f34b") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationClaimUser_UsersId",
                table: "OperationClaimUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOperationClaims_OperationClaimId",
                table: "UserOperationClaims",
                column: "OperationClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOperationClaims_UserId",
                table: "UserOperationClaims",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationClaimUser");

            migrationBuilder.DropTable(
                name: "UserOperationClaims");

            migrationBuilder.DropTable(
                name: "OperationClaims");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
