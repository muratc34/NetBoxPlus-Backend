using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TranscodeJobs",
                table: "TranscodeJobs");

            migrationBuilder.AddColumn<string>(
                name: "AudioLayout",
                table: "Videos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Muxed");

            // Converted videos keep their sound inside the renditions; those still to be converted get the new layout.
            migrationBuilder.Sql("""UPDATE "Videos" SET "AudioLayout" = 'Separate' WHERE "Status" IN ('AwaitingUpload', 'Queued');""");

            migrationBuilder.AddColumn<string>(
                name: "OriginalAudioLanguage",
                table: "Videos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "tr-TR");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "TranscodeJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "AudioTrackId",
                table: "TranscodeJobs",
                type: "uuid",
                nullable: true);

            // A video's own job is keyed by the video's id, as before.
            migrationBuilder.Sql("""UPDATE "TranscodeJobs" SET "Id" = "VideoId";""");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TranscodeJobs",
                table: "TranscodeJobs",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "VideoAudioTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsOriginal = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UploadExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Bandwidth = table.Column<int>(type: "integer", nullable: true),
                    Playlist = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VideoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAudioTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoAudioTracks_Videos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranscodeJobs_VideoId",
                table: "TranscodeJobs",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAudioTracks_VideoId_Language",
                table: "VideoAudioTracks",
                columns: new[] { "VideoId", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoAudioTracks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TranscodeJobs",
                table: "TranscodeJobs");

            migrationBuilder.DropIndex(
                name: "IX_TranscodeJobs_VideoId",
                table: "TranscodeJobs");

            migrationBuilder.DropColumn(
                name: "AudioLayout",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "OriginalAudioLanguage",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "TranscodeJobs");

            migrationBuilder.DropColumn(
                name: "AudioTrackId",
                table: "TranscodeJobs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TranscodeJobs",
                table: "TranscodeJobs",
                column: "VideoId");
        }
    }
}
