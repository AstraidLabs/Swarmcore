using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.ConfigurationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWarningMessageToTorrentPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "warning_message",
                table: "torrent_policies",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "warning_message",
                table: "torrent_policies");
        }
    }
}
