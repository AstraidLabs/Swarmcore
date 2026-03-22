using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.ConfigurationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialConfigurationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("create extension if not exists pgcrypto;");

            migrationBuilder.CreateTable(
                name: "audit_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bans",
                columns: table => new
                {
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bans", x => new { x.scope, x.subject });
                });

            migrationBuilder.CreateTable(
                name: "maintenance_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "passkeys",
                columns: table => new
                {
                    passkey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkeys", x => x.passkey);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    can_leech = table.Column<bool>(type: "boolean", nullable: false),
                    can_seed = table.Column<bool>(type: "boolean", nullable: false),
                    can_scrape = table.Column<bool>(type: "boolean", nullable: false),
                    can_use_private_tracker = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "torrents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    info_hash = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_torrents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "torrent_policies",
                columns: table => new
                {
                    torrent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    announce_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    min_announce_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    default_numwant = table.Column<int>(type: "integer", nullable: false),
                    max_numwant = table.Column<int>(type: "integer", nullable: false),
                    allow_scrape = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_torrent_policies", x => x.torrent_id);
                    table.ForeignKey(
                        name: "FK_torrent_policies_torrents_torrent_id",
                        column: x => x.torrent_id,
                        principalTable: "torrents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_occurred_at_utc",
                table: "audit_records",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_bans_expires_at_utc",
                table: "bans",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_runs_requested_at_utc",
                table: "maintenance_runs",
                column: "requested_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_passkeys_user_id",
                table: "passkeys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_torrents_info_hash",
                table: "torrents",
                column: "info_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_records");

            migrationBuilder.DropTable(
                name: "bans");

            migrationBuilder.DropTable(
                name: "maintenance_runs");

            migrationBuilder.DropTable(
                name: "passkeys");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "torrent_policies");

            migrationBuilder.DropTable(
                name: "torrents");
        }
    }
}
