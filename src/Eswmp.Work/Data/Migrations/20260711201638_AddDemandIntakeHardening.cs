using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Work.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandIntakeHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "demand",
                table: "DemandValidationResults",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentMode",
                schema: "demand",
                table: "Demands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DemandValidationResults_DemandId",
                schema: "demand",
                table: "DemandValidationResults",
                column: "DemandId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandValidationResults_TenantId_DemandId_ValidatedAt",
                schema: "demand",
                table: "DemandValidationResults",
                columns: new[] { "TenantId", "DemandId", "ValidatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Demands_TenantId_ExternalReferenceType_ExternalReferenceId",
                schema: "demand",
                table: "Demands",
                columns: new[] { "TenantId", "ExternalReferenceType", "ExternalReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Demands_TenantId_Status_CreatedAt",
                schema: "demand",
                table: "Demands",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Demands_TimeWindow",
                schema: "demand",
                table: "Demands",
                sql: "\"RequestedStartAtUtc\" IS NULL OR \"RequestedEndAtUtc\" IS NULL OR \"RequestedStartAtUtc\" < \"RequestedEndAtUtc\"");

            migrationBuilder.CreateIndex(
                name: "IX_DemandIdempotencyRecords_DemandId",
                schema: "demand",
                table: "DemandIdempotencyRecords",
                column: "DemandId");

            migrationBuilder.AddForeignKey(
                name: "FK_DemandIdempotencyRecords_Demands_DemandId",
                schema: "demand",
                table: "DemandIdempotencyRecords",
                column: "DemandId",
                principalSchema: "demand",
                principalTable: "Demands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DemandValidationResults_Demands_DemandId",
                schema: "demand",
                table: "DemandValidationResults",
                column: "DemandId",
                principalSchema: "demand",
                principalTable: "Demands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DemandIdempotencyRecords_Demands_DemandId",
                schema: "demand",
                table: "DemandIdempotencyRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_DemandValidationResults_Demands_DemandId",
                schema: "demand",
                table: "DemandValidationResults");

            migrationBuilder.DropIndex(
                name: "IX_DemandValidationResults_DemandId",
                schema: "demand",
                table: "DemandValidationResults");

            migrationBuilder.DropIndex(
                name: "IX_DemandValidationResults_TenantId_DemandId_ValidatedAt",
                schema: "demand",
                table: "DemandValidationResults");

            migrationBuilder.DropIndex(
                name: "IX_Demands_TenantId_ExternalReferenceType_ExternalReferenceId",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropIndex(
                name: "IX_Demands_TenantId_Status_CreatedAt",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Demands_TimeWindow",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropIndex(
                name: "IX_DemandIdempotencyRecords_DemandId",
                schema: "demand",
                table: "DemandIdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "demand",
                table: "DemandValidationResults");

            migrationBuilder.DropColumn(
                name: "FulfillmentMode",
                schema: "demand",
                table: "Demands");
        }
    }
}
