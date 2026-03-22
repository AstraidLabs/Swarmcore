using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tracker.TelemetryIngest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTelemetrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announce_telemetry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InfoHash = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Passkey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedPeers = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announce_telemetry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_announce_telemetry_InfoHash",
                table: "announce_telemetry",
                column: "InfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_announce_telemetry_OccurredAtUtc",
                table: "announce_telemetry",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announce_telemetry");
        }
    }
}
