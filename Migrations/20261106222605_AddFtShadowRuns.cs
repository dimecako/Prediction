using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prediction.Migrations
{
    /// <inheritdoc />
    public partial class AddFtShadowRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ft_shadow_runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Experiment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedCount = table.Column<int>(type: "integer", nullable: false),
                    FrozenJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ft_shadow_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ft_shadow_runs_Experiment_MatchDate",
                table: "ft_shadow_runs",
                columns: new[] { "Experiment", "MatchDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ft_shadow_runs");
        }
    }
}
