using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Work.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "requirements");

            migrationBuilder.EnsureSchema(
                name: "demand");

            migrationBuilder.CreateTable(
                name: "DemandIdempotencyRecords",
                schema: "demand",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    DemandId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseBodyJson = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Demands",
                schema: "demand",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DemandType = table.Column<string>(type: "text", nullable: false),
                    SourceSystem = table.Column<string>(type: "text", nullable: false),
                    SourceChannel = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RequestedStartAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedEndAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedTimezone = table.Column<string>(type: "text", nullable: true),
                    LocationReference = table.Column<string>(type: "jsonb", nullable: true),
                    RequirementReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalReferenceType = table.Column<string>(type: "text", nullable: false),
                    ExternalReferenceId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DemandValidationResults",
                schema: "demand",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandValidationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementSnapshots",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ActiveVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementVersions",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangeSummary = table.Column<string>(type: "text", nullable: true),
                    DurationType = table.Column<int>(type: "integer", nullable: false),
                    FixedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MinimumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    ExpectedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaximumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    PreWorkBufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    PostWorkBufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequirementVersions_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirements",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationConstraints",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    MaximumTravelDistanceKm = table.Column<decimal>(type: "numeric", nullable: true),
                    MaximumTravelTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationConstraints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationConstraints_RequirementVersions_RequirementVersionId",
                        column: x => x.RequirementVersionId,
                        principalSchema: "requirements",
                        principalTable: "RequirementVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceTypeCode = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: true),
                    MinimumQuantity = table.Column<int>(type: "integer", nullable: false),
                    PreferredQuantity = table.Column<int>(type: "integer", nullable: false),
                    MaximumQuantity = table.Column<int>(type: "integer", nullable: false),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceRequirements_RequirementVersions_RequirementVersion~",
                        column: x => x.RequirementVersionId,
                        principalSchema: "requirements",
                        principalTable: "RequirementVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityCode = table.Column<string>(type: "text", nullable: false),
                    MinimumLevel = table.Column<int>(type: "integer", nullable: true),
                    Importance = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityRequirements_ResourceRequirements_ResourceRequire~",
                        column: x => x.ResourceRequirementId,
                        principalSchema: "requirements",
                        principalTable: "ResourceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CertificationRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificationTypeCode = table.Column<string>(type: "text", nullable: false),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificationRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificationRequirements_ResourceRequirements_ResourceRequ~",
                        column: x => x.ResourceRequirementId,
                        principalSchema: "requirements",
                        principalTable: "ResourceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SkillRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillCode = table.Column<string>(type: "text", nullable: false),
                    MinimumLevel = table.Column<int>(type: "integer", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillRequirements_ResourceRequirements_ResourceRequirementId",
                        column: x => x.ResourceRequirementId,
                        principalSchema: "requirements",
                        principalTable: "ResourceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityRequirements_ResourceRequirementId",
                schema: "requirements",
                table: "CapabilityRequirements",
                column: "ResourceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificationRequirements_ResourceRequirementId",
                schema: "requirements",
                table: "CertificationRequirements",
                column: "ResourceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandIdempotencyRecords_TenantId_IdempotencyKey",
                schema: "demand",
                table: "DemandIdempotencyRecords",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationConstraints_RequirementVersionId",
                schema: "requirements",
                table: "LocationConstraints",
                column: "RequirementVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RequirementVersions_TenantId_VersionNumber_WorkRequirementId",
                schema: "requirements",
                table: "RequirementVersions",
                columns: new[] { "TenantId", "VersionNumber", "WorkRequirementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementVersions_WorkRequirementId",
                schema: "requirements",
                table: "RequirementVersions",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceRequirements_RequirementVersionId",
                schema: "requirements",
                table: "ResourceRequirements",
                column: "RequirementVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillRequirements_ResourceRequirementId",
                schema: "requirements",
                table: "SkillRequirements",
                column: "ResourceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequirements_TenantId_Code",
                schema: "requirements",
                table: "WorkRequirements",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapabilityRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "CertificationRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "DemandIdempotencyRecords",
                schema: "demand");

            migrationBuilder.DropTable(
                name: "Demands",
                schema: "demand");

            migrationBuilder.DropTable(
                name: "DemandValidationResults",
                schema: "demand");

            migrationBuilder.DropTable(
                name: "LocationConstraints",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "RequirementSnapshots",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "SkillRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "ResourceRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "RequirementVersions",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "WorkRequirements",
                schema: "requirements");
        }
    }
}
