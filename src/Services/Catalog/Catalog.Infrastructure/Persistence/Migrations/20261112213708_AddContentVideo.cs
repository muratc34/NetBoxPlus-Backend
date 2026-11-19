using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Video_LatestRevision",
                table: "Movies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Video_LatestState",
                table: "Movies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Video_LatestVideoId",
                table: "Movies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Video_PlayableVideoId",
                table: "Movies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Video_LatestRevision",
                table: "Episodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Video_LatestState",
                table: "Episodes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Video_LatestVideoId",
                table: "Episodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Video_PlayableVideoId",
                table: "Episodes",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Video_LatestRevision",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_LatestState",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_LatestVideoId",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_PlayableVideoId",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_LatestRevision",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Video_LatestState",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Video_LatestVideoId",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Video_PlayableVideoId",
                table: "Episodes");
        }
    }
}
