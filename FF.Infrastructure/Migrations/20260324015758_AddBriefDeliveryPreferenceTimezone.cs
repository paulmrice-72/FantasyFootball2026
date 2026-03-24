using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBriefDeliveryPreferenceTimezone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "BriefDeliveryPreferences",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Chicago");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "BriefDeliveryPreferences");
        }
    }
}
