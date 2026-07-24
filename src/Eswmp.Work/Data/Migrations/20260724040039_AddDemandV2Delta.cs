using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Work.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandV2Delta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedRole",
                schema: "demand",
                table: "Demands",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                schema: "demand",
                table: "Demands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttentionIssuesJson",
                schema: "demand",
                table: "Demands",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttentionReason",
                schema: "demand",
                table: "Demands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastResolutionError",
                schema: "demand",
                table: "Demands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceRule",
                schema: "demand",
                table: "Demands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolutionAttempts",
                schema: "demand",
                table: "Demands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                schema: "demand",
                table: "Demands",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DemandAuditEntries",
                schema: "demand",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "text", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    ActorRole = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    BeforeSummary = table.Column<string>(type: "jsonb", nullable: true),
                    AfterSummary = table.Column<string>(type: "jsonb", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandAuditEntries_Demands_DemandId",
                        column: x => x.DemandId,
                        principalSchema: "demand",
                        principalTable: "Demands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemandLineage",
                schema: "demand",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelatedId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relation = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandLineage", x => x.Id);
                    table.CheckConstraint("CK_Lineage_NotSelf", "\"DemandId\" <> \"RelatedId\"");
                    table.ForeignKey(
                        name: "FK_DemandLineage_Demands_DemandId",
                        column: x => x.DemandId,
                        principalSchema: "demand",
                        principalTable: "Demands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DemandLineage_Demands_RelatedId",
                        column: x => x.RelatedId,
                        principalSchema: "demand",
                        principalTable: "Demands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Demands_TenantId_AssignedRole_CreatedAt",
                schema: "demand",
                table: "Demands",
                columns: new[] { "TenantId", "AssignedRole", "CreatedAt" },
                filter: "\"Status\" = 7");

            migrationBuilder.CreateIndex(
                name: "IX_Demands_TenantId_SeriesId",
                schema: "demand",
                table: "Demands",
                columns: new[] { "TenantId", "SeriesId" },
                filter: "\"SeriesId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Demands_Attempts",
                schema: "demand",
                table: "Demands",
                sql: "\"ResolutionAttempts\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Demands_AttentionReason",
                schema: "demand",
                table: "Demands",
                sql: "\"Status\" <> 7 OR \"AttentionReason\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Demands_Recurrence",
                schema: "demand",
                table: "Demands",
                sql: "\"RecurrenceRule\" IS NULL OR \"FulfillmentMode\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_DemandAuditEntries_DemandId",
                schema: "demand",
                table: "DemandAuditEntries",
                column: "DemandId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandAuditEntries_TenantId_DemandId_OccurredAt",
                schema: "demand",
                table: "DemandAuditEntries",
                columns: new[] { "TenantId", "DemandId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DemandLineage_DemandId",
                schema: "demand",
                table: "DemandLineage",
                column: "DemandId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandLineage_RelatedId",
                schema: "demand",
                table: "DemandLineage",
                column: "RelatedId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandLineage_TenantId_DemandId",
                schema: "demand",
                table: "DemandLineage",
                columns: new[] { "TenantId", "DemandId" });

            migrationBuilder.CreateIndex(
                name: "IX_DemandLineage_TenantId_RelatedId",
                schema: "demand",
                table: "DemandLineage",
                columns: new[] { "TenantId", "RelatedId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemandAuditEntries",
                schema: "demand");

            migrationBuilder.DropTable(
                name: "DemandLineage",
                schema: "demand");

            migrationBuilder.DropIndex(
                name: "IX_Demands_TenantId_AssignedRole_CreatedAt",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropIndex(
                name: "IX_Demands_TenantId_SeriesId",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Demands_Attempts",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Demands_AttentionReason",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Demands_Recurrence",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "AssignedRole",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "AttentionIssuesJson",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "AttentionReason",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "LastResolutionError",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "RecurrenceRule",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "ResolutionAttempts",
                schema: "demand",
                table: "Demands");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                schema: "demand",
                table: "Demands");
        }
    }
}
