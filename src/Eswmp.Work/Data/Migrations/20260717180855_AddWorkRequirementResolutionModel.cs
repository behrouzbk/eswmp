using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Work.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkRequirementResolutionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "requirement");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseBodyJson = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementTemplates",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    WorkType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementTemplateVersions",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChangeReason = table.Column<string>(type: "text", nullable: true),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequirementTemplateVersions_RequirementTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "requirement",
                        principalTable: "RequirementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    SourceVersion = table.Column<int>(type: "integer", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    WorkType = table.Column<string>(type: "text", nullable: false),
                    WorkCategory = table.Column<string>(type: "text", nullable: true),
                    ServiceMode = table.Column<string>(type: "text", nullable: true),
                    ComplexityLevel = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequirementVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkRequirements_RequirementTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "requirement",
                        principalTable: "RequirementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BufferRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    BufferType = table.Column<int>(type: "integer", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    AppliesToRole = table.Column<string>(type: "text", nullable: true),
                    HardConstraint = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BufferRequirements", x => x.Id);
                    table.CheckConstraint("CK_Buf_Positive", "\"DurationMinutes\" > 0");
                    table.ForeignKey(
                        name: "FK_BufferRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Constraints",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConstraintType = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: true),
                    Operator = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<string>(type: "text", nullable: true),
                    HardConstraint = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Constraints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Constraints_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DependencyRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependencyType = table.Column<string>(type: "text", nullable: false),
                    DependsOnReferenceType = table.Column<string>(type: "text", nullable: true),
                    DependsOnReferenceId = table.Column<string>(type: "text", nullable: true),
                    LagMinutes = table.Column<int>(type: "integer", nullable: true),
                    HardConstraint = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DependencyRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DurationRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationType = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MinimumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaximumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    SetupDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    CleanupDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DurationRequirements", x => x.Id);
                    table.CheckConstraint("CK_Dur_Positive", "(\"EstimatedDurationMinutes\" IS NULL OR \"EstimatedDurationMinutes\" > 0) AND (\"MinimumDurationMinutes\" IS NULL OR \"MinimumDurationMinutes\" > 0) AND (\"MaximumDurationMinutes\" IS NULL OR \"MaximumDurationMinutes\" > 0) AND (\"MinimumDurationMinutes\" IS NULL OR \"MaximumDurationMinutes\" IS NULL OR \"MinimumDurationMinutes\" <= \"MaximumDurationMinutes\")");
                    table.ForeignKey(
                        name: "FK_DurationRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionMode = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationMode = table.Column<int>(type: "integer", nullable: false),
                    LocationReferenceType = table.Column<string>(type: "text", nullable: true),
                    LocationReferenceId = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    ServiceRadius = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    LocationFlexibility = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Preferences",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceType = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    Weight = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Preferences_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequirementVersions",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChangeType = table.Column<string>(type: "text", nullable: true),
                    ChangeReason = table.Column<string>(type: "text", nullable: true),
                    SourceVersion = table.Column<int>(type: "integer", nullable: true),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
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
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceRoleRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleCode = table.Column<string>(type: "text", nullable: false),
                    ResourceCategory = table.Column<int>(type: "integer", nullable: false),
                    MinimumQuantity = table.Column<int>(type: "integer", nullable: false),
                    MaximumQuantity = table.Column<int>(type: "integer", nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    SelectionMode = table.Column<int>(type: "integer", nullable: false),
                    SameResourceRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceRoleRequirements", x => x.Id);
                    table.CheckConstraint("CK_RRR_Quantity", "\"MinimumQuantity\" > 0 AND (\"MaximumQuantity\" IS NULL OR \"MaximumQuantity\" >= \"MinimumQuantity\")");
                    table.ForeignKey(
                        name: "FK_ResourceRoleRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeConstraintType = table.Column<int>(type: "integer", nullable: false),
                    EarliestStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatestStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EarliestFinish = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatestFinish = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FixedStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FixedEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Timezone = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeRequirements", x => x.Id);
                    table.CheckConstraint("CK_Time_Ordering", "(\"EarliestStart\" IS NULL OR \"LatestStart\" IS NULL OR \"EarliestStart\" <= \"LatestStart\") AND (\"FixedStart\" IS NULL OR \"FixedEnd\" IS NULL OR \"FixedStart\" < \"FixedEnd\") AND (\"EarliestStart\" IS NULL OR \"Deadline\" IS NULL OR \"EarliestStart\" <= \"Deadline\") AND (\"EarliestStart\" IS NULL OR \"LatestFinish\" IS NULL OR \"EarliestStart\" < \"LatestFinish\")");
                    table.ForeignKey(
                        name: "FK_TimeRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TravelRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    TravelRequired = table.Column<bool>(type: "boolean", nullable: false),
                    OriginMode = table.Column<string>(type: "text", nullable: true),
                    DestinationMode = table.Column<string>(type: "text", nullable: true),
                    MaximumTravelTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaximumTravelDistance = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    TravelTimeIncludedInWork = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceRoleRequirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapabilityCode = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: true),
                    MinimumExperience = table.Column<int>(type: "integer", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityRequirements_ResourceRoleRequirements_ResourceRol~",
                        column: x => x.ResourceRoleRequirementId,
                        principalSchema: "requirement",
                        principalTable: "ResourceRoleRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapabilityRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapacityRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceRoleRequirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    DimensionCode = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    AggregationScope = table.Column<string>(type: "text", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityRequirements", x => x.Id);
                    table.CheckConstraint("CK_CapaR_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_CapacityRequirements_ResourceRoleRequirements_ResourceRoleR~",
                        column: x => x.ResourceRoleRequirementId,
                        principalSchema: "requirement",
                        principalTable: "ResourceRoleRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapacityRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CertificationRequirements",
                schema: "requirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceRoleRequirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CertificationTypeCode = table.Column<string>(type: "text", nullable: false),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    MustBeValidThrough = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationLevel = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificationRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificationRequirements_ResourceRoleRequirements_Resource~",
                        column: x => x.ResourceRoleRequirementId,
                        principalSchema: "requirement",
                        principalTable: "ResourceRoleRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CertificationRequirements_WorkRequirements_WorkRequirementId",
                        column: x => x.WorkRequirementId,
                        principalSchema: "requirement",
                        principalTable: "WorkRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BufferRequirements_WorkRequirementId",
                schema: "requirement",
                table: "BufferRequirements",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityRequirements_ResourceRoleRequirementId",
                schema: "requirement",
                table: "CapabilityRequirements",
                column: "ResourceRoleRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityRequirements_WorkRequirementId",
                schema: "requirement",
                table: "CapabilityRequirements",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityRequirements_ResourceRoleRequirementId",
                schema: "requirement",
                table: "CapacityRequirements",
                column: "ResourceRoleRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityRequirements_WorkRequirementId",
                schema: "requirement",
                table: "CapacityRequirements",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificationRequirements_ResourceRoleRequirementId",
                schema: "requirement",
                table: "CertificationRequirements",
                column: "ResourceRoleRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificationRequirements_WorkRequirementId",
                schema: "requirement",
                table: "CertificationRequirements",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_Constraints_WorkRequirementId",
                schema: "requirement",
                table: "Constraints",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_DependencyRequirements_WorkRequirementId",
                schema: "requirement",
                table: "DependencyRequirements",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_DurationRequirements_WorkRequirementId",
                schema: "requirement",
                table: "DurationRequirements",
                column: "WorkRequirementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequirements_WorkRequirementId",
                schema: "requirement",
                table: "ExecutionRequirements",
                column: "WorkRequirementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_IdempotencyKey",
                schema: "requirement",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationRequirements_WorkRequirementId",
                schema: "requirement",
                table: "LocationRequirements",
                column: "WorkRequirementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OccurredAt",
                schema: "requirement",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "\"ProcessedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Preferences_WorkRequirementId",
                schema: "requirement",
                table: "Preferences",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_RequirementTemplates_TenantId_Code",
                schema: "requirement",
                table: "RequirementTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementTemplates_TenantId_WorkType_Status",
                schema: "requirement",
                table: "RequirementTemplates",
                columns: new[] { "TenantId", "WorkType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RequirementTemplateVersions_TemplateId_Version",
                schema: "requirement",
                table: "RequirementTemplateVersions",
                columns: new[] { "TemplateId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementVersions_WorkRequirementId_Version",
                schema: "requirement",
                table: "RequirementVersions",
                columns: new[] { "WorkRequirementId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceRoleRequirements_WorkRequirementId",
                schema: "requirement",
                table: "ResourceRoleRequirements",
                column: "WorkRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeRequirements_WorkRequirementId",
                schema: "requirement",
                table: "TimeRequirements",
                column: "WorkRequirementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelRequirements_WorkRequirementId",
                schema: "requirement",
                table: "TravelRequirements",
                column: "WorkRequirementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequirements_TemplateId",
                schema: "requirement",
                table: "WorkRequirements",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequirements_TenantId_SourceType_SourceId",
                schema: "requirement",
                table: "WorkRequirements",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequirements_TenantId_Status_CreatedAt",
                schema: "requirement",
                table: "WorkRequirements",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BufferRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "CapabilityRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "CapacityRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "CertificationRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "Constraints",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "DependencyRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "DurationRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "ExecutionRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "LocationRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "Preferences",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "RequirementTemplateVersions",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "RequirementVersions",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "TimeRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "TravelRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "ResourceRoleRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "WorkRequirements",
                schema: "requirement");

            migrationBuilder.DropTable(
                name: "RequirementTemplates",
                schema: "requirement");
        }
    }
}
