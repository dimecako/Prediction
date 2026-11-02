using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prediction.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulatedTicketsAndLeague : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "League",
                table: "matches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "simulated_tickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Stake = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulated_tickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "simulated_ticket_selections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    MatchId = table.Column<long>(type: "bigint", nullable: false),
                    Fixture = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    League = table.Column<string>(type: "text", nullable: true),
                    KickoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Market = table.Column<string>(type: "text", nullable: false),
                    Pick = table.Column<string>(type: "text", nullable: false),
                    CalibrationKey = table.Column<string>(type: "text", nullable: false),
                    Odds = table.Column<decimal>(type: "numeric", nullable: false),
                    Accuracy = table.Column<double>(type: "double precision", nullable: false),
                    Safe = table.Column<double>(type: "double precision", nullable: false),
                    Sample = table.Column<int>(type: "integer", nullable: false),
                    BaseOdds = table.Column<decimal>(type: "numeric", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulated_ticket_selections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_simulated_ticket_selections_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulated_ticket_selections_simulated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "simulated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_simulated_ticket_selections_MatchId",
                table: "simulated_ticket_selections",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_simulated_ticket_selections_TicketId_MatchId",
                table: "simulated_ticket_selections",
                columns: new[] { "TicketId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulated_tickets_Fingerprint",
                table: "simulated_tickets",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulated_tickets_MatchDate",
                table: "simulated_tickets",
                column: "MatchDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "simulated_ticket_selections");

            migrationBuilder.DropTable(
                name: "simulated_tickets");

            migrationBuilder.DropColumn(
                name: "League",
                table: "matches");
        }
    }
}
