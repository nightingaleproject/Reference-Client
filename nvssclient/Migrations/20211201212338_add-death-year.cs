using Microsoft.EntityFrameworkCore.Migrations;

namespace NVSSClient.Migrations
{
    public partial class adddeathyear : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DeathYear",
                table: "ResponseItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeathYear",
                table: "MessageItems",
                type: "bigint",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeathYear",
                table: "ResponseItems");

            migrationBuilder.DropColumn(
                name: "DeathYear",
                table: "MessageItems");
        }
    }
}
