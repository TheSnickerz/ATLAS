using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATLAS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGreenboneSourceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "AssetVulnerabilities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AssetVulnerabilities",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "AssetVulnerabilities");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AssetVulnerabilities");
        }
    }
}
