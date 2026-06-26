using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hiram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    secret_protected = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_id",
                schema: "notifications",
                table: "webhook_endpoints",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_endpoints_tenant_url",
                schema: "notifications",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_endpoints",
                schema: "notifications");
        }
    }
}
