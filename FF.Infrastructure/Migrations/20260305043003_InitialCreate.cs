using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SleeperLeagueId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Season = table.Column<int>(type: "int", nullable: false),
                    TotalTeams = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NflTeam = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    JerseyNumber = table.Column<int>(type: "int", nullable: true),
                    SleeperPlayerId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Age = table.Column<int>(type: "int", nullable: true),
                    YearsExperience = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rosters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SleeperRosterId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rosters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rosters_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_SleeperLeagueId_Season",
                table: "Leagues",
                columns: new[] { "SleeperLeagueId", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Position",
                table: "Players",
                column: "Position");

            migrationBuilder.CreateIndex(
                name: "IX_Players_SleeperPlayerId",
                table: "Players",
                column: "SleeperPlayerId",
                unique: true,
                filter: "[SleeperPlayerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Status",
                table: "Players",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Rosters_LeagueId",
                table: "Rosters",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_Rosters_SleeperRosterId",
                table: "Rosters",
                column: "SleeperRosterId",
                unique: true,
                filter: "[SleeperRosterId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Rosters");

            migrationBuilder.DropTable(
                name: "Leagues");
        }
    }
}
