using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prediction.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionSourceMatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceMatchId",
                table: "prediction_snapshots",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_prediction_snapshots_Source_SourceMatchId",
                table: "prediction_snapshots",
                columns: new[] { "Source", "SourceMatchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_prediction_snapshots_Source_SourceMatchId",
                table: "prediction_snapshots");

            migrationBuilder.DropColumn(
                name: "SourceMatchId",
                table: "prediction_snapshots");
        }
    }
}
