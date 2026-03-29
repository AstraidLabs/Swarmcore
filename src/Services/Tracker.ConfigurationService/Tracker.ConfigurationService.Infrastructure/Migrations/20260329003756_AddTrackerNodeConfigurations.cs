using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.ConfigurationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackerNodeConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tracker_node_configurations",
                columns: table => new
                {
                    node_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    apply_mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requires_restart = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracker_node_configurations", x => x.node_key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracker_node_configurations");
        }
    }
}
