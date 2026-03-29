using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.ConfigurationService.Infrastructure.Migrations
{
    public partial class AddPasskeyIdentity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                table: "passkeys",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_passkeys_id",
                table: "passkeys",
                column: "id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_passkeys_id",
                table: "passkeys");

            migrationBuilder.DropColumn(
                name: "id",
                table: "passkeys");
        }
    }
}
