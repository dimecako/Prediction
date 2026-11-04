using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prediction.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prediction_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MatchId = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PredictedResult = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PredictedScore = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Btts = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Goals = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prediction_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_prediction_snapshots_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_prediction_snapshots_MatchId",
                table: "prediction_snapshots",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_prediction_snapshots_MatchId_Source_CapturedAtUtc",
                table: "prediction_snapshots",
                columns: new[] { "MatchId", "Source", "CapturedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prediction_snapshots");
        }
    }
}
