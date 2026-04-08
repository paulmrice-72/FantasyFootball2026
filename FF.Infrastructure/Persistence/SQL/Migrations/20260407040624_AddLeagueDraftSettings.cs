using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Persistence.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueDraftSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanTradePicks",
                table: "Leagues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DraftRounds",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "PickYearsOut",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanTradePicks",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "DraftRounds",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "PickYearsOut",
                table: "Leagues");
        }
    }
}
