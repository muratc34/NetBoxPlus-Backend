using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePasskeyEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Passkeys were removed: an e-mail of their kinds could no longer be read back.
            migrationBuilder.Sql("DELETE FROM \"OutgoingEmails\" WHERE \"Kind\" IN ('PasskeyAdded', 'PasskeyRemoved');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
