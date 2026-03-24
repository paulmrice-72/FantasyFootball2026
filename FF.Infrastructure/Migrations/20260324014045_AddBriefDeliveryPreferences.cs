using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBriefDeliveryPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BriefDeliveryPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DeliveryDayOfWeek = table.Column<int>(type: "int", nullable: false),
                    DeliveryHourUtc = table.Column<int>(type: "int", nullable: false),
                    IncludeBoomCandidates = table.Column<bool>(type: "bit", nullable: false),
                    IncludeBustRisks = table.Column<bool>(type: "bit", nullable: false),
                    IncludeLeagueSections = table.Column<bool>(type: "bit", nullable: false),
                    IncludeCoachRiley = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BriefDeliveryPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BriefDeliveryPreferences_UserId",
                table: "BriefDeliveryPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BriefDeliveryPreferences");
        }
    }
}
