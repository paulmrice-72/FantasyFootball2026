using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Persistence.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddDepthChartJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepthPosition",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DepthTeam",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepthPosition",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepthTeam",
                table: "Players",
                type: "integer",
                nullable: true);
        }
    }
}
