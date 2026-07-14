using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hiram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryAttemptProviderMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_message_id",
                schema: "notifications",
                table: "delivery_attempts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_provider_message_id",
                schema: "notifications",
                table: "delivery_attempts",
                column: "provider_message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_delivery_attempts_provider_message_id",
                schema: "notifications",
                table: "delivery_attempts");

            migrationBuilder.DropColumn(
                name: "provider_message_id",
                schema: "notifications",
                table: "delivery_attempts");
        }
    }
}
