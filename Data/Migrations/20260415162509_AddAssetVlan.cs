using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATLAS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetVlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Vlan",
                table: "Assets",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Vlan",
                table: "Assets");
        }
    }
}
