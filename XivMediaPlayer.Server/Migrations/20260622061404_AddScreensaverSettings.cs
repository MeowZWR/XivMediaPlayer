using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XivMediaPlayer.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddScreensaverSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "ScreensaverColorB",
                table: "TvPlacements",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ScreensaverColorG",
                table: "TvPlacements",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ScreensaverColorR",
                table: "TvPlacements",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "ScreensaverStyle",
                table: "TvPlacements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreensaverColorB",
                table: "TvPlacements");

            migrationBuilder.DropColumn(
                name: "ScreensaverColorG",
                table: "TvPlacements");

            migrationBuilder.DropColumn(
                name: "ScreensaverColorR",
                table: "TvPlacements");

            migrationBuilder.DropColumn(
                name: "ScreensaverStyle",
                table: "TvPlacements");
        }
    }
}
