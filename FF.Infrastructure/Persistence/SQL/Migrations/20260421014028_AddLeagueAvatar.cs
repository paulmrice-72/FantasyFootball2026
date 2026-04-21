using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Persistence.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Avatar",
                table: "Leagues",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Avatar",
                table: "Leagues");
        }
    }
}
