using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prediction.Migrations
{
    /// <inheritdoc />
    public partial class AddHalfTimeResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HalfTimeAwayGoals",
                table: "match_results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HalfTimeHomeGoals",
                table: "match_results",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HalfTimeAwayGoals",
                table: "match_results");

            migrationBuilder.DropColumn(
                name: "HalfTimeHomeGoals",
                table: "match_results");
        }
    }
}
