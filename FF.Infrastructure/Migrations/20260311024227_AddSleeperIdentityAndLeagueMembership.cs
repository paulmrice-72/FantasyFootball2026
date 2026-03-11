using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSleeperIdentityAndLeagueMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SleeperLinkedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SleeperUserId",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SleeperUsername",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeagueMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SleeperUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LeagueId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LeagueName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Season = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMemberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMemberships_SleeperUserId",
                table: "LeagueMemberships",
                column: "SleeperUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMemberships_UserId_LeagueId_Season",
                table: "LeagueMemberships",
                columns: new[] { "UserId", "LeagueId", "Season" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueMemberships");

            migrationBuilder.DropColumn(
                name: "SleeperLinkedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SleeperUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SleeperUsername",
                table: "AspNetUsers");
        }
    }
}
