using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hiram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxDispatchAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_at",
                schema: "notifications",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dispatch_at",
                schema: "notifications",
                table: "outbox_messages");
        }
    }
}
