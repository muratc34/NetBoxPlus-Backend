using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Video_AudioLanguageList",
                table: "Movies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Video_SubtitleLanguageList",
                table: "Movies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Video_TracksRevision",
                table: "Movies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "Video_TracksVideoId",
                table: "Movies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Video_AudioLanguageList",
                table: "Episodes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Video_SubtitleLanguageList",
                table: "Episodes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Video_TracksRevision",
                table: "Episodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "Video_TracksVideoId",
                table: "Episodes",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Video_AudioLanguageList",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_SubtitleLanguageList",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_TracksRevision",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_TracksVideoId",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Video_AudioLanguageList",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Video_SubtitleLanguageList",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Video_TracksRevision",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Video_TracksVideoId",
                table: "Episodes");
        }
    }
}
