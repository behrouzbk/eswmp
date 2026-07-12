using System.Text.Json;
using Eswmp.Assignment.Data;
using Eswmp.Assignment.Models;
using Eswmp.Assignment.Services;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Assignment.Controllers;

// ── Request / response shapes ───────────────────────────────────────────────

public record MatchingCandidateRequest(
    string Type,
    Guid Id,
    string[]? Skills,
    double? Latitude,
    double? Longitude,
    int CurrentWorkload = 0);

public record EvaluateMatchRequest(
    Guid? WorkRequirementId,
    int? WorkRequirementVersion,
    Guid? MatchingPolicyId,
    string? StrategyCode,
    string[]? RequiredSkills,
    double? TargetLatitude,
    double? TargetLongitude,
    IReadOnlyList<MatchingCandidateRequest> Candidates,
    int? Limit,
    string? CorrelationId);

public record RecalculateMatchRequest(
    IReadOnlyList<MatchingCandidateRequest> Candidates,
    string[]? RequiredSkills,
    double? TargetLatitude,
    double? TargetLongitude,
    string? StrategyCode,
    int? Limit);

public record MatchResultDto(
    Guid CandidateId,
    string CandidateType,
    int Rank,
    double Score,
    string RecommendationLevel,
    string? PrimaryReasonCode);

public record EvaluateMatchResponse(
    Guid MatchEvaluationId,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<MatchResultDto> Results);

public record MatchEvaluationDetailDto(
    Guid Id,
    Guid? WorkRequirementId,
    int? WorkRequirementVersion,
    Guid? MatchingPolicyId,
    int? MatchingPolicyVersion,
    string StrategyCode,
    string Status,
    int CandidateCount,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ExpiresAt,
    string? CorrelationId,
    IReadOnlyList<MatchResultDto> Results);

public record FactorExplanationDto(
    string FactorCode,
    double? RawValue,
    double NormalizedScore,
    double Weight,
    double WeightedContribution);

public record CandidateExplanationDto(
    Guid CandidateId,
    string CandidateType,
    double Score,
    string RecommendationLevel,
    IReadOnlyList<FactorExplanationDto> Factors);

public record MatchFactorConfigDto(string FactorCode, bool Enabled, double Weight, string? NormalizationMethod);

public record CreateMatchingPolicyRequest(string Code, string Name, string? Description);

public record CreateMatchingPolicyVersionRequest(string StrategyCode, IReadOnlyList<MatchFactorConfigDto> Factors);

// ── Controller ───────────────────────────────────────────────────────────

[ApiController]
[Route("api/v1/matching")]
public class MatchingController(
    MatchingScorer scorer,
    AssignmentDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    private static readonly TimeSpan EvaluationLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("evaluations")]
    [RequirePermission(EswmpPermissions.MatchingExecute)]
    public async Task<ActionResult<EvaluateMatchResponse>> Evaluate(EvaluateMatchRequest request)
    {
        IReadOnlyList<MatchFactorWeight>? weights = null;
        int? policyVersionNumber = null;

        if (request.MatchingPolicyId is { } policyId)
        {
            var activeVersion = await db.MatchingPolicyVersions
                .FirstOrDefaultAsync(v => v.MatchingPolicyId == policyId && v.Status == MatchingPolicyVersionStatus.Active);

            if (activeVersion is null)
            {
                return Problem(
                    title: "No active MatchingPolicyVersion",
                    detail: $"MatchingPolicy {policyId} has no active version to score with.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            weights = ParseFactorConfiguration(activeVersion.FactorConfigurationJson);
            policyVersionNumber = activeVersion.VersionNumber;
        }

        var context = new MatchingWorkContext
        {
            RequiredSkills = request.RequiredSkills ?? [],
            TargetLatitude = request.TargetLatitude,
            TargetLongitude = request.TargetLongitude,
        };

        var candidates = ToScorerCandidates(request.Candidates);
        var strategyCode = request.StrategyCode ?? MatchingScorer.BalancedStrategyCode;

        var ranked = scorer.Score(context, candidates, weights);
        if (request.Limit is > 0)
            ranked = ranked.Take(request.Limit.Value).ToList();

        var evaluation = BuildEvaluation(
            request.WorkRequirementId,
            request.WorkRequirementVersion,
            request.MatchingPolicyId,
            policyVersionNumber,
            strategyCode,
            request.CorrelationId,
            candidates.Count,
            ranked);

        await using var transaction = await db.Database.BeginTransactionAsync();
        db.MatchEvaluations.Add(evaluation);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(ToEvaluateResponse(evaluation));
    }

    [HttpGet("evaluations/{id:guid}")]
    [RequirePermission(EswmpPermissions.MatchingRead)]
    public async Task<ActionResult<MatchEvaluationDetailDto>> GetEvaluation(Guid id)
    {
        var evaluation = await db.MatchEvaluations
            .Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evaluation is null)
            return NotFound();

        return Ok(ToDetailDto(evaluation));
    }

    [HttpGet("evaluations/{id:guid}/candidates/{candidateId:guid}/explanation")]
    [RequirePermission(EswmpPermissions.MatchingRead)]
    public async Task<ActionResult<CandidateExplanationDto>> GetExplanation(Guid id, Guid candidateId)
    {
        var result = await db.CandidateMatchResults
            .Include(r => r.Factors)
            .FirstOrDefaultAsync(r => r.MatchEvaluationId == id && r.CandidateId == candidateId);

        if (result is null)
            return NotFound();

        return Ok(new CandidateExplanationDto(
            result.CandidateId,
            result.CandidateType,
            result.NormalizedScore,
            result.RecommendationLevel.ToString(),
            result.Factors
                .Select(f => new FactorExplanationDto(f.FactorCode, f.RawValue, f.NormalizedScore, f.Weight, f.WeightedContribution))
                .ToList()));
    }

    [HttpPost("evaluations/{id:guid}/recalculate")]
    [RequirePermission(EswmpPermissions.MatchingExecute)]
    public async Task<ActionResult<EvaluateMatchResponse>> Recalculate(Guid id, RecalculateMatchRequest request)
    {
        var previous = await db.MatchEvaluations.FirstOrDefaultAsync(e => e.Id == id);
        if (previous is null)
            return NotFound();

        IReadOnlyList<MatchFactorWeight>? weights = null;
        if (previous.MatchingPolicyId is { } policyId)
        {
            var activeVersion = await db.MatchingPolicyVersions
                .FirstOrDefaultAsync(v => v.MatchingPolicyId == policyId && v.Status == MatchingPolicyVersionStatus.Active);
            if (activeVersion is not null)
                weights = ParseFactorConfiguration(activeVersion.FactorConfigurationJson);
        }

        var context = new MatchingWorkContext
        {
            RequiredSkills = request.RequiredSkills ?? [],
            TargetLatitude = request.TargetLatitude,
            TargetLongitude = request.TargetLongitude,
        };

        var candidates = ToScorerCandidates(request.Candidates);
        var strategyCode = request.StrategyCode ?? previous.StrategyCode;

        var ranked = scorer.Score(context, candidates, weights);
        if (request.Limit is > 0)
            ranked = ranked.Take(request.Limit.Value).ToList();

        var newEvaluation = BuildEvaluation(
            previous.WorkRequirementId,
            previous.WorkRequirementVersion,
            previous.MatchingPolicyId,
            previous.MatchingPolicyVersion,
            strategyCode,
            previous.CorrelationId,
            candidates.Count,
            ranked);

        await using var transaction = await db.Database.BeginTransactionAsync();
        previous.Status = MatchEvaluationStatus.Invalidated;
        db.MatchEvaluations.Add(newEvaluation);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(ToEvaluateResponse(newEvaluation));
    }

    [HttpPost("matching-policies")]
    [RequirePermission(EswmpPermissions.MatchingExecute)]
    public async Task<IActionResult> CreatePolicy(CreateMatchingPolicyRequest request)
    {
        var policy = new MatchingPolicy
        {
            TenantId = tenantContext.RequiredTenantId,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
        };

        db.MatchingPolicies.Add(policy);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetEvaluation), new { id = policy.Id }, policy);
    }

    [HttpPost("matching-policies/{id:guid}/versions")]
    [RequirePermission(EswmpPermissions.MatchingExecute)]
    public async Task<IActionResult> CreatePolicyVersion(Guid id, CreateMatchingPolicyVersionRequest request)
    {
        var policy = await db.MatchingPolicies.FirstOrDefaultAsync(p => p.Id == id);
        if (policy is null)
            return NotFound();

        var nextVersionNumber = policy.CurrentVersionNumber + 1;
        var version = new MatchingPolicyVersion
        {
            TenantId = tenantContext.RequiredTenantId,
            MatchingPolicyId = policy.Id,
            VersionNumber = nextVersionNumber,
            Status = MatchingPolicyVersionStatus.Draft,
            StrategyCode = request.StrategyCode,
            FactorConfigurationJson = JsonSerializer.Serialize(request.Factors, JsonOptions),
        };

        policy.CurrentVersionNumber = nextVersionNumber;

        db.MatchingPolicyVersions.Add(version);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(CreatePolicyVersion), new { id = policy.Id, version = version.VersionNumber }, version);
    }

    [HttpPost("matching-policies/{id:guid}/versions/{version:int}/activate")]
    [RequirePermission(EswmpPermissions.MatchingExecute)]
    public async Task<IActionResult> ActivatePolicyVersion(Guid id, int version)
    {
        var policyVersion = await db.MatchingPolicyVersions
            .FirstOrDefaultAsync(v => v.MatchingPolicyId == id && v.VersionNumber == version);

        if (policyVersion is null)
            return NotFound();

        if (policyVersion.Status == MatchingPolicyVersionStatus.Active)
            return Ok(policyVersion); // Already active — idempotent, and Active versions are immutable so nothing else to do.

        var factors = ParseFactorConfiguration(policyVersion.FactorConfigurationJson);
        var enabledWeightSum = factors.Where(f => f.Enabled).Sum(f => f.Weight);
        if (Math.Abs(enabledWeightSum - 1.0) > 0.02)
        {
            return Problem(
                title: "Factor weights must sum to ~1.0",
                detail: $"Enabled factor weights for version {version} sum to {enabledWeightSum:F4}, expected ~1.0.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var currentlyActive = await db.MatchingPolicyVersions
            .Where(v => v.MatchingPolicyId == id && v.Status == MatchingPolicyVersionStatus.Active)
            .ToListAsync();

        foreach (var active in currentlyActive)
            active.Status = MatchingPolicyVersionStatus.Superseded;

        policyVersion.Status = MatchingPolicyVersionStatus.Active;

        await db.SaveChangesAsync();

        return Ok(policyVersion);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static IReadOnlyList<MatchingCandidate> ToScorerCandidates(IReadOnlyList<MatchingCandidateRequest> candidates) =>
        candidates.Select(c => new MatchingCandidate
        {
            CandidateType = c.Type,
            CandidateId = c.Id,
            Skills = c.Skills ?? [],
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            CurrentWorkload = c.CurrentWorkload,
        }).ToList();

    private MatchEvaluation BuildEvaluation(
        Guid? workRequirementId,
        int? workRequirementVersion,
        Guid? matchingPolicyId,
        int? matchingPolicyVersion,
        string strategyCode,
        string? correlationId,
        int candidateCount,
        IReadOnlyList<MatchedCandidate> ranked)
    {
        var evaluatedAt = DateTimeOffset.UtcNow;

        var evaluation = new MatchEvaluation
        {
            TenantId = tenantContext.RequiredTenantId,
            WorkRequirementId = workRequirementId,
            WorkRequirementVersion = workRequirementVersion,
            MatchingPolicyId = matchingPolicyId,
            MatchingPolicyVersion = matchingPolicyVersion,
            StrategyCode = strategyCode,
            Status = ranked.Count > 0 ? MatchEvaluationStatus.Matched : MatchEvaluationStatus.NoMatch,
            CandidateCount = candidateCount,
            EvaluatedAt = evaluatedAt,
            ExpiresAt = evaluatedAt + EvaluationLifetime,
            CorrelationId = correlationId,
        };

        foreach (var candidate in ranked)
        {
            var result = new CandidateMatchResult
            {
                MatchEvaluationId = evaluation.Id,
                CandidateType = candidate.CandidateType,
                CandidateId = candidate.CandidateId,
                RawScore = candidate.RawScore,
                NormalizedScore = candidate.NormalizedScore,
                Rank = candidate.Rank,
                RecommendationLevel = candidate.RecommendationLevel,
                PrimaryReasonCode = candidate.PrimaryReasonCode,
            };

            foreach (var factor in candidate.Factors)
            {
                result.Factors.Add(new MatchFactorEvaluation
                {
                    CandidateMatchResultId = result.Id,
                    FactorCode = factor.FactorCode,
                    RawValue = factor.RawValue,
                    NormalizedScore = factor.NormalizedScore,
                    Weight = factor.Weight,
                    WeightedContribution = factor.WeightedContribution,
                });
            }

            evaluation.Results.Add(result);
        }

        return evaluation;
    }

    private static IReadOnlyList<MatchFactorWeight> ParseFactorConfiguration(string json)
    {
        var configs = JsonSerializer.Deserialize<List<MatchFactorConfigDto>>(json, JsonOptions) ?? [];
        return configs
            .Select(c => new MatchFactorWeight
            {
                FactorCode = c.FactorCode,
                Enabled = c.Enabled,
                Weight = c.Weight,
                NormalizationMethod = c.NormalizationMethod ?? "Linear",
            })
            .ToList();
    }

    private static EvaluateMatchResponse ToEvaluateResponse(MatchEvaluation evaluation) => new(
        evaluation.Id,
        evaluation.EvaluatedAt,
        evaluation.ExpiresAt,
        evaluation.Results
            .OrderBy(r => r.Rank)
            .Select(r => new MatchResultDto(r.CandidateId, r.CandidateType, r.Rank, r.NormalizedScore, r.RecommendationLevel.ToString(), r.PrimaryReasonCode))
            .ToList());

    private static MatchEvaluationDetailDto ToDetailDto(MatchEvaluation evaluation) => new(
        evaluation.Id,
        evaluation.WorkRequirementId,
        evaluation.WorkRequirementVersion,
        evaluation.MatchingPolicyId,
        evaluation.MatchingPolicyVersion,
        evaluation.StrategyCode,
        evaluation.Status.ToString(),
        evaluation.CandidateCount,
        evaluation.EvaluatedAt,
        evaluation.ExpiresAt,
        evaluation.CorrelationId,
        evaluation.Results
            .OrderBy(r => r.Rank)
            .Select(r => new MatchResultDto(r.CandidateId, r.CandidateType, r.Rank, r.NormalizedScore, r.RecommendationLevel.ToString(), r.PrimaryReasonCode))
            .ToList());
}
