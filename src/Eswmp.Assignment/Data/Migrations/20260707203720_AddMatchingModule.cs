using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eswmp.Assignment.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matching");

            migrationBuilder.CreateTable(
                name: "MatchEvaluations",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRequirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkRequirementVersion = table.Column<int>(type: "integer", nullable: true),
                    MatchingPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchingPolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    StrategyCode = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CandidateCount = table.Column<int>(type: "integer", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchingPolicies",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchingPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CandidateMatchResults",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateType = table.Column<string>(type: "text", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawScore = table.Column<double>(type: "double precision", nullable: false),
                    NormalizedScore = table.Column<double>(type: "double precision", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    RecommendationLevel = table.Column<int>(type: "integer", nullable: false),
                    PrimaryReasonCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateMatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateMatchResults_MatchEvaluations_MatchEvaluationId",
                        column: x => x.MatchEvaluationId,
                        principalSchema: "matching",
                        principalTable: "MatchEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchingPolicyVersions",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchingPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StrategyCode = table.Column<string>(type: "text", nullable: false),
                    FactorConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchingPolicyVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchingPolicyVersions_MatchingPolicies_MatchingPolicyId",
                        column: x => x.MatchingPolicyId,
                        principalSchema: "matching",
                        principalTable: "MatchingPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchFactorEvaluations",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateMatchResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactorCode = table.Column<string>(type: "text", nullable: false),
                    RawValue = table.Column<double>(type: "double precision", nullable: true),
                    NormalizedScore = table.Column<double>(type: "double precision", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    WeightedContribution = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchFactorEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchFactorEvaluations_CandidateMatchResults_CandidateMatch~",
                        column: x => x.CandidateMatchResultId,
                        principalSchema: "matching",
                        principalTable: "CandidateMatchResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateMatchResults_MatchEvaluationId",
                schema: "matching",
                table: "CandidateMatchResults",
                column: "MatchEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchFactorEvaluations_CandidateMatchResultId",
                schema: "matching",
                table: "MatchFactorEvaluations",
                column: "CandidateMatchResultId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchingPolicies_TenantId_Code",
                schema: "matching",
                table: "MatchingPolicies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchingPolicyVersions_MatchingPolicyId_VersionNumber",
                schema: "matching",
                table: "MatchingPolicyVersions",
                columns: new[] { "MatchingPolicyId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchFactorEvaluations",
                schema: "matching");

            migrationBuilder.DropTable(
                name: "MatchingPolicyVersions",
                schema: "matching");

            migrationBuilder.DropTable(
                name: "CandidateMatchResults",
                schema: "matching");

            migrationBuilder.DropTable(
                name: "MatchingPolicies",
                schema: "matching");

            migrationBuilder.DropTable(
                name: "MatchEvaluations",
                schema: "matching");
        }
    }
}
