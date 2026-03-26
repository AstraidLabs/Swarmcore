using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.SelfService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSelfServiceRbacSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity_selfservice");

            migrationBuilder.CreateTable(
                name: "admin_account_states",
                schema: "identity_selfservice",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_account_states", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "admin_user_profiles",
                schema: "identity_selfservice",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_user_profiles", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "permission_definitions",
                schema: "identity_selfservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_system_permission = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission_group_items",
                schema: "identity_selfservice",
                columns: table => new
                {
                    permission_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_group_items", x => new { x.permission_group_id, x.permission_definition_id });
                });

            migrationBuilder.CreateTable(
                name: "permission_groups",
                schema: "identity_selfservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    is_system_group = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rbac_state",
                schema: "identity_selfservice",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rbac_state", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "role_metadata",
                schema: "identity_selfservice",
                columns: table => new
                {
                    role_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_metadata", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "role_permission_groups",
                schema: "identity_selfservice",
                columns: table => new
                {
                    role_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    permission_group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permission_groups", x => new { x.role_id, x.permission_group_id });
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "identity_selfservice",
                columns: table => new
                {
                    role_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    permission_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.role_id, x.permission_definition_id });
                });

            migrationBuilder.CreateTable(
                name: "verification_tokens",
                schema: "identity_selfservice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_account_states_state",
                schema: "identity_selfservice",
                table: "admin_account_states",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_permission_definitions_key",
                schema: "identity_selfservice",
                table: "permission_definitions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permission_groups_name",
                schema: "identity_selfservice",
                table: "permission_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verification_tokens_expires",
                schema: "identity_selfservice",
                table: "verification_tokens",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_verification_tokens_hash_purpose",
                schema: "identity_selfservice",
                table: "verification_tokens",
                columns: new[] { "token_hash", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_tokens_user_purpose",
                schema: "identity_selfservice",
                table: "verification_tokens",
                columns: new[] { "user_id", "purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_account_states",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "admin_user_profiles",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "permission_definitions",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "permission_group_items",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "permission_groups",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "rbac_state",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "role_metadata",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "role_permission_groups",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity_selfservice");

            migrationBuilder.DropTable(
                name: "verification_tokens",
                schema: "identity_selfservice");
        }
    }
}
