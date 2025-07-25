using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NVSSClient.Migrations
{
    public partial class _20250722021200_addVitalTypesIJEVersionToResponseItem : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IJE_Version",
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
                name: "IJE_Version",
                table: "ResponseItems");

            migrationBuilder.DropColumn(
                name: "VitalRecordType",
                table: "ResponseItems");
        }
    }
}
