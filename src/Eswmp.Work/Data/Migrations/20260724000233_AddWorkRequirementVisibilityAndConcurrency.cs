using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Work.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkRequirementVisibilityAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "requirement",
                table: "RequirementTemplateVersions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "RequirementLineVisibility",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineType = table.Column<string>(type: "text", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisibilityLevel = table.Column<int>(type: "integer", nullable: false),
                    CustomerVisible = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementLineVisibility", x => x.Id);
                    table.CheckConstraint("CK_RLV_Consistent", "\"CustomerVisible\" = false OR \"VisibilityLevel\" = 0");
                    table.ForeignKey(
                        name: "FK_RequirementLineVisibility_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequirementLineVisibility_LineType_LineId",
                schema: "requirement",
                table: "RequirementLineVisibility",
                columns: new[] { "LineType", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementLineVisibility_TenantId_WorkRequirementId_Visibi~",
                schema: "requirement",
                table: "RequirementLineVisibility",
                columns: new[] { "TenantId", "WorkRequirementId", "VisibilityLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_RequirementLineVisibility_WorkRequirementId",
                schema: "requirement",
                table: "RequirementLineVisibility",
                column: "WorkRequirementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequirementLineVisibility",
                schema: "requirement");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "requirement",
                table: "RequirementTemplateVersions");
        }
    }
}
