using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hiram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgresOutboxLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "notifications",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "available_at",
                schema: "notifications",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                schema: "notifications",
                table: "outbox_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_until",
                schema: "notifications",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE notifications.outbox_messages
                SET available_at = COALESCE(dispatch_at, created_at_utc)
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "available_at",
                schema: "notifications",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_available",
                schema: "notifications",
                table: "outbox_messages",
                columns: new[] { "available_at", "created_at_utc" },
                filter: "processed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_available",
                schema: "notifications",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "notifications",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "available_at",
                schema: "notifications",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "last_error",
                schema: "notifications",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "lease_until",
                schema: "notifications",
                table: "outbox_messages");
        }
    }
}
