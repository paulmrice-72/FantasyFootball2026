using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixRosterUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rosters_SleeperRosterId",
                table: "Rosters");

            migrationBuilder.CreateIndex(
                name: "IX_Rosters_LeagueId_SleeperRosterId",
                table: "Rosters",
                columns: new[] { "LeagueId", "SleeperRosterId" },
                unique: true,
                filter: "[SleeperRosterId] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rosters_LeagueId_SleeperRosterId",
                table: "Rosters");

            migrationBuilder.CreateIndex(
                name: "IX_Rosters_SleeperRosterId",
                table: "Rosters",
                column: "SleeperRosterId",
                unique: true,
                filter: "[SleeperRosterId] IS NOT NULL");
        }
    }
}
