using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hiram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancyAndDeliveryStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The F0 terminal state 'Published' becomes 'Sent' under the F1 state machine.
            migrationBuilder.Sql(
                "UPDATE notifications.notification_requests SET status = 'Sent' WHERE status = 'Published';");

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "notifications",
                table: "notification_requests",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_provider_configs",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    secret_protected = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_provider_configs", x => new { x.tenant_id, x.channel });
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    delivery_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "notifications",
                table: "tenants",
                columns: new[] { "id", "created_at_utc", "delivery_mode", "name" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 6, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Live", "dev" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_requests_idempotency",
                schema: "notifications",
                table: "notification_requests",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_tenant_id",
                schema: "notifications",
                table: "api_keys",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_api_keys_key_hash",
                schema: "notifications",
                table: "api_keys",
                column: "key_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "tenant_provider_configs",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "notifications");

            migrationBuilder.DropIndex(
                name: "ux_notification_requests_idempotency",
                schema: "notifications",
                table: "notification_requests");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "notifications",
                table: "notification_requests");

            migrationBuilder.Sql(
                "UPDATE notifications.notification_requests SET status = 'Published' WHERE status = 'Sent';");
        }
    }
}
