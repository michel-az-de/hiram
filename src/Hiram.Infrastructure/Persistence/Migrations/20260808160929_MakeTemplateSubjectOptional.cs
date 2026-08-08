using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hiram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeTemplateSubjectOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "subject",
                schema: "notifications",
                table: "templates",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "subject",
                schema: "notifications",
                table: "templates",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
