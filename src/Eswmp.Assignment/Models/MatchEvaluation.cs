using Eswmp.Shared.DTOs;

namespace Eswmp.Assignment.Models;

/// <summary>
/// Overall status of a Matching evaluation run. Deliberately separate from
/// AssignmentLog/AssignmentMethod — Matching ranks eligible candidates against
/// a work context, it does not itself record a fulfillment decision. See
/// Services/MatchingScorer.cs for why this coexists with AssignmentScorer.
/// </summary>
public enum MatchEvaluationStatus
{
    Matched,
    NoMatch,
    Indeterminate,
    Invalidated,
}

/// <summary>
/// A single run of the Matching scorer against a set of candidates for an
/// (opaque) piece of work. WorkRequirementId/Version are opaque pointers into
/// Eswmp.Work — this service never calls back into it, per CLAUDE.md rule 1's
/// externalReferenceType/Id pattern generalised to a same-platform sibling.
/// </summary>
public class MatchEvaluation : TenantScopedEntity
{
    public Guid? WorkRequirementId { get; set; }
    public int? WorkRequirementVersion { get; set; }
    public Guid? MatchingPolicyId { get; set; }
    public int? MatchingPolicyVersion { get; set; }
    public required string StrategyCode { get; set; }
    public required MatchEvaluationStatus Status { get; set; }
    public int CandidateCount { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public string? CorrelationId { get; set; }

    public List<CandidateMatchResult> Results { get; set; } = [];
}

/// <summary>
/// Recommendation bucket a candidate's normalized 0-100 score falls into.
/// Thresholds (90/80/65/50) are fixed per spec #08, not tenant-configurable.
/// </summary>
public enum RecommendationLevel
{
    LowMatch,
    PossibleMatch,
    GoodMatch,
    StrongMatch,
    ExcellentMatch,
}

/// <summary>One ranked candidate's outcome within a MatchEvaluation.</summary>
public class CandidateMatchResult : BaseEntity
{
    public required Guid MatchEvaluationId { get; set; }
    public required string CandidateType { get; set; }
    public required Guid CandidateId { get; set; }
    public double RawScore { get; set; }
    public double NormalizedScore { get; set; }
    public int Rank { get; set; }
    public RecommendationLevel RecommendationLevel { get; set; }
    public string? PrimaryReasonCode { get; set; }

    public MatchEvaluation? MatchEvaluation { get; set; }
    public List<MatchFactorEvaluation> Factors { get; set; } = [];
}

/// <summary>Per-factor score breakdown backing a CandidateMatchResult's explanation.</summary>
public class MatchFactorEvaluation : BaseEntity
{
    public required Guid CandidateMatchResultId { get; set; }
    public required string FactorCode { get; set; }
    public double? RawValue { get; set; }
    public double NormalizedScore { get; set; }
    public double Weight { get; set; }
    public double WeightedContribution { get; set; }

    public CandidateMatchResult? CandidateMatchResult { get; set; }
}
