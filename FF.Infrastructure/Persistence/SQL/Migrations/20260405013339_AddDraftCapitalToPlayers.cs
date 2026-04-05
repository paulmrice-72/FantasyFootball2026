using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Persistence.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftCapitalToPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollegeTeam",
                table: "Players",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DraftPick",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DraftRound",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_YearsExperience",
                table: "Players",
                column: "YearsExperience");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_YearsExperience",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CollegeTeam",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DraftPick",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DraftRound",
                table: "Players");
        }
    }
}
