using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationOverlapIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ResourceId_StartTime_EndTime",
                table: "Reservations",
                columns: new[] { "ResourceId", "StartTime", "EndTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_ResourceId_StartTime_EndTime",
                table: "Reservations");
        }
    }
}
