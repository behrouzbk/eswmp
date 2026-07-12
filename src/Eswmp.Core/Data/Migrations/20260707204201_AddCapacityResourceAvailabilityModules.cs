using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCapacityResourceAvailabilityModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "availability");

            migrationBuilder.EnsureSchema(
                name: "capacity");

            migrationBuilder.EnsureSchema(
                name: "resource");

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceTypeId",
                table: "Resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationStatus",
                table: "Resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "AvailabilityProfileId",
                table: "AvailabilityRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "AvailabilityRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecurrencePattern",
                table: "AvailabilityRules",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuleType",
                table: "AvailabilityRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AvailabilityRules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "AvailabilityRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExceptionType",
                table: "AvailabilityExceptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "AvailabilityExceptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AvailabilityOverrides",
                schema: "availability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Effect = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilityProfiles",
                schema: "availability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AvailabilityVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityConsumptions",
                schema: "capacity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapacityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityConsumptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityDefinitions",
                schema: "capacity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapacityProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CapacityModel = table.Column<int>(type: "integer", nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaximumQuantity = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<int>(type: "integer", nullable: false),
                    TimeBasis = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityHolds",
                schema: "capacity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapacityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityLedgerEntries",
                schema: "capacity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapacityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    DimensionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityOverrides",
                schema: "capacity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapacityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverrideType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Effect = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityProfiles",
                schema: "capacity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCapabilities",
                schema: "resource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCapabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCertifications",
                schema: "resource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificationTypeCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CredentialReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCertifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceSkills",
                schema: "resource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    YearsOfExperience = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceSkills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTypes",
                schema: "resource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffs",
                schema: "availability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityRules_AvailabilityProfileId",
                table: "AvailabilityRules",
                column: "AvailabilityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityOverrides_ResourceId_StartTime_EndTime",
                schema: "availability",
                table: "AvailabilityOverrides",
                columns: new[] { "ResourceId", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityProfiles_ResourceId",
                schema: "availability",
                table: "AvailabilityProfiles",
                column: "ResourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacityConsumptions_CapacityDefinitionId_StartTime_EndTime",
                schema: "capacity",
                table: "CapacityConsumptions",
                columns: new[] { "CapacityDefinitionId", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityDefinitions_CapacityProfileId",
                schema: "capacity",
                table: "CapacityDefinitions",
                column: "CapacityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityDefinitions_CapacityProfileId_DimensionCode",
                schema: "capacity",
                table: "CapacityDefinitions",
                columns: new[] { "CapacityProfileId", "DimensionCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityHolds_CapacityDefinitionId_StartTime_EndTime",
                schema: "capacity",
                table: "CapacityHolds",
                columns: new[] { "CapacityDefinitionId", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityHolds_TenantId_IdempotencyKey",
                schema: "capacity",
                table: "CapacityHolds",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacityLedgerEntries_CapacityDefinitionId",
                schema: "capacity",
                table: "CapacityLedgerEntries",
                column: "CapacityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityLedgerEntries_CorrelationId",
                schema: "capacity",
                table: "CapacityLedgerEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityOverrides_CapacityDefinitionId_StartTime_EndTime",
                schema: "capacity",
                table: "CapacityOverrides",
                columns: new[] { "CapacityDefinitionId", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityProfiles_ResourceId",
                schema: "capacity",
                table: "CapacityProfiles",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCapabilities_ResourceId",
                schema: "resource",
                table: "ResourceCapabilities",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCertifications_ResourceId",
                schema: "resource",
                table: "ResourceCertifications",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSkills_ResourceId",
                schema: "resource",
                table: "ResourceSkills",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypes_TenantId_Code",
                schema: "resource",
                table: "ResourceTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffs_ResourceId_StartTime_EndTime",
                schema: "availability",
                table: "TimeOffs",
                columns: new[] { "ResourceId", "StartTime", "EndTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityOverrides",
                schema: "availability");

            migrationBuilder.DropTable(
                name: "AvailabilityProfiles",
                schema: "availability");

            migrationBuilder.DropTable(
                name: "CapacityConsumptions",
                schema: "capacity");

            migrationBuilder.DropTable(
                name: "CapacityDefinitions",
                schema: "capacity");

            migrationBuilder.DropTable(
                name: "CapacityHolds",
                schema: "capacity");

            migrationBuilder.DropTable(
                name: "CapacityLedgerEntries",
                schema: "capacity");

            migrationBuilder.DropTable(
                name: "CapacityOverrides",
                schema: "capacity");

            migrationBuilder.DropTable(
                name: "CapacityProfiles",
                schema: "capacity");

            migrationBuilder.DropTable(
                name: "ResourceCapabilities",
                schema: "resource");

            migrationBuilder.DropTable(
                name: "ResourceCertifications",
                schema: "resource");

            migrationBuilder.DropTable(
                name: "ResourceSkills",
                schema: "resource");

            migrationBuilder.DropTable(
                name: "ResourceTypes",
                schema: "resource");

            migrationBuilder.DropTable(
                name: "TimeOffs",
                schema: "availability");

            migrationBuilder.DropIndex(
                name: "IX_AvailabilityRules_AvailabilityProfileId",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "ResourceTypeId",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "AvailabilityProfileId",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "RecurrencePattern",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "RuleType",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AvailabilityRules");

            migrationBuilder.DropColumn(
                name: "ExceptionType",
                table: "AvailabilityExceptions");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "AvailabilityExceptions");
        }
    }
}
