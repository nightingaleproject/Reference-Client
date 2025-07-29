using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NVSSClient.Migrations
{
    public partial class _20250702220201_changeVRDRspecificNames : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DeathYear",
                table: "ResponseItems",
                newName: "EventYear");

            migrationBuilder.RenameColumn(
                name: "DeathJurisdictionID",
                table: "ResponseItems",
                newName: "JurisdictionID");

            migrationBuilder.RenameColumn(
                name: "DeathYear",
                table: "MessageItems",
                newName: "EventYear");

            migrationBuilder.RenameColumn(
                name: "DeathJurisdictionID",
                table: "MessageItems",
                newName: "JurisdictionID");

            migrationBuilder.AddColumn<string>(
                name: "IGVersion",
                table: "MessageItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VitalRecordType",
                table: "MessageItems",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "JurisdictionID",
                table: "ResponseItems",
                newName: "DeathJurisdictionID");

            migrationBuilder.RenameColumn(
                name: "EventYear",
                table: "ResponseItems",
                newName: "DeathYear");

            migrationBuilder.RenameColumn(
                name: "JurisdictionID",
                table: "MessageItems",
                newName: "DeathJurisdictionID");

            migrationBuilder.RenameColumn(
                name: "EventYear",
                table: "MessageItems",
                newName: "DeathYear"); 
            
            migrationBuilder.DropColumn(
                name: "IGVersion",
                table: "MessageItems");

            migrationBuilder.DropColumn(
                name: "VitalRecordType",
                table: "MessageItems");
        }
    }
}
