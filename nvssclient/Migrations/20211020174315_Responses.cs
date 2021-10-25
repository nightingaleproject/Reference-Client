using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace NVSSClient.Migrations
{
    public partial class Responses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordItems");

            migrationBuilder.DropColumn(
                name: "Record",
                table: "MessageItems");

            migrationBuilder.RenameColumn(
                name: "SentOn",
                table: "MessageItems",
                newName: "ExpirationDate");

            migrationBuilder.CreateTable(
                name: "ResponseItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Uid = table.Column<string>(type: "text", nullable: true),
                    StateAuxiliaryIdentifier = table.Column<string>(type: "text", nullable: true),
                    CertificateNumber = table.Column<long>(type: "bigint", nullable: true),
                    DeathJurisdictionID = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponseItems", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResponseItems");

            migrationBuilder.RenameColumn(
                name: "ExpirationDate",
                table: "MessageItems",
                newName: "SentOn");

            migrationBuilder.AddColumn<string>(
                name: "Record",
                table: "MessageItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecordItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Record = table.Column<string>(type: "text", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordItems", x => x.Id);
                });
        }
    }
}
