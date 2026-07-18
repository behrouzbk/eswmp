using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Work.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameLegacyWorkRequirementModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocationConstraints_RequirementVersions_RequirementVersionId",
                schema: "requirements",
                table: "LocationConstraints");

            migrationBuilder.DropTable(
                name: "CapabilityRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "CertificationRequirements",
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

            migrationBuilder.RenameColumn(
                name: "RequirementVersionId",
                schema: "requirements",
                table: "LocationConstraints",
                newName: "RequirementDefinitionVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_LocationConstraints_RequirementVersionId",
                schema: "requirements",
                table: "LocationConstraints",
                newName: "IX_LocationConstraints_RequirementDefinitionVersionId");

            migrationBuilder.CreateTable(
                name: "RequirementDefinitions",
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
                    table.PrimaryKey("PK_RequirementDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementDefinitionSnapshots",
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
                    table.PrimaryKey("PK_RequirementDefinitionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementDefinitionVersions",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_RequirementDefinitionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequirementDefinitionVersions_RequirementDefinitions_Requir~",
                        column: x => x.RequirementDefinitionId,
                        principalSchema: "requirements",
                        principalTable: "RequirementDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionResourceRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementDefinitionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_DefinitionResourceRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefinitionResourceRequirements_RequirementDefinitionVersion~",
                        column: x => x.RequirementDefinitionVersionId,
                        principalSchema: "requirements",
                        principalTable: "RequirementDefinitionVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionCapabilityRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_DefinitionCapabilityRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefinitionCapabilityRequirements_DefinitionResourceRequirem~",
                        column: x => x.DefinitionResourceRequirementId,
                        principalSchema: "requirements",
                        principalTable: "DefinitionResourceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionCertificationRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificationTypeCode = table.Column<string>(type: "text", nullable: false),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinitionCertificationRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefinitionCertificationRequirements_DefinitionResourceRequi~",
                        column: x => x.DefinitionResourceRequirementId,
                        principalSchema: "requirements",
                        principalTable: "DefinitionResourceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionSkillRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_DefinitionSkillRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefinitionSkillRequirements_DefinitionResourceRequirements_~",
                        column: x => x.DefinitionResourceRequirementId,
                        principalSchema: "requirements",
                        principalTable: "DefinitionResourceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionCapabilityRequirements_DefinitionResourceRequirem~",
                schema: "requirements",
                table: "DefinitionCapabilityRequirements",
                column: "DefinitionResourceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionCertificationRequirements_DefinitionResourceRequi~",
                schema: "requirements",
                table: "DefinitionCertificationRequirements",
                column: "DefinitionResourceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionResourceRequirements_RequirementDefinitionVersion~",
                schema: "requirements",
                table: "DefinitionResourceRequirements",
                column: "RequirementDefinitionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionSkillRequirements_DefinitionResourceRequirementId",
                schema: "requirements",
                table: "DefinitionSkillRequirements",
                column: "DefinitionResourceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_RequirementDefinitions_TenantId_Code",
                schema: "requirements",
                table: "RequirementDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementDefinitionVersions_RequirementDefinitionId",
                schema: "requirements",
                table: "RequirementDefinitionVersions",
                column: "RequirementDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RequirementDefinitionVersions_TenantId_VersionNumber_Requir~",
                schema: "requirements",
                table: "RequirementDefinitionVersions",
                columns: new[] { "TenantId", "VersionNumber", "RequirementDefinitionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LocationConstraints_RequirementDefinitionVersions_Requireme~",
                schema: "requirements",
                table: "LocationConstraints",
                column: "RequirementDefinitionVersionId",
                principalSchema: "requirements",
                principalTable: "RequirementDefinitionVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocationConstraints_RequirementDefinitionVersions_Requireme~",
                schema: "requirements",
                table: "LocationConstraints");

            migrationBuilder.DropTable(
                name: "DefinitionCapabilityRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "DefinitionCertificationRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "DefinitionSkillRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "RequirementDefinitionSnapshots",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "DefinitionResourceRequirements",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "RequirementDefinitionVersions",
                schema: "requirements");

            migrationBuilder.DropTable(
                name: "RequirementDefinitions",
                schema: "requirements");

            migrationBuilder.RenameColumn(
                name: "RequirementDefinitionVersionId",
                schema: "requirements",
                table: "LocationConstraints",
                newName: "RequirementVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_LocationConstraints_RequirementDefinitionVersionId",
                schema: "requirements",
                table: "LocationConstraints",
                newName: "IX_LocationConstraints_RequirementVersionId");

            migrationBuilder.CreateTable(
                name: "RequirementSnapshots",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    SourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    ActiveVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CurrentVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    ChangeSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    DurationType = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    FixedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaximumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MinimumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    PostWorkBufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    PreWorkBufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "ResourceRequirements",
                schema: "requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    MaximumQuantity = table.Column<int>(type: "integer", nullable: false),
                    MinimumQuantity = table.Column<int>(type: "integer", nullable: false),
                    PreferredQuantity = table.Column<int>(type: "integer", nullable: false),
                    RequirementVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceTypeCode = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: true),
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
                    CapabilityCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    Importance = table.Column<int>(type: "integer", nullable: false),
                    MinimumLevel = table.Column<int>(type: "integer", nullable: true),
                    ResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    CertificationTypeCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    ResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumLevel = table.Column<int>(type: "integer", nullable: true),
                    ResourceRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillCode = table.Column<string>(type: "text", nullable: false),
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

            migrationBuilder.AddForeignKey(
                name: "FK_LocationConstraints_RequirementVersions_RequirementVersionId",
                schema: "requirements",
                table: "LocationConstraints",
                column: "RequirementVersionId",
                principalSchema: "requirements",
                principalTable: "RequirementVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
