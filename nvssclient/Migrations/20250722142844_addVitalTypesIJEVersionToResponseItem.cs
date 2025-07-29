using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NVSSClient.Migrations
{
    public partial class AddVitalTypesIJEVersionToResponseItem : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IGVersion",
                table: "ResponseItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VitalRecordType",
                table: "ResponseItems",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IGVersion",
                table: "ResponseItems");

            migrationBuilder.DropColumn(
                name: "VitalRecordType",
                table: "ResponseItems");
        }
    }
}
