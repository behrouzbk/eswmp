using Eswmp.Assignment.Models;

namespace Eswmp.Assignment.Services;

/// <summary>A ranking candidate presented to the Matching module.</summary>
public record MatchingCandidate
{
    public required string CandidateType { get; init; }
    public required Guid CandidateId { get; init; }
    public IReadOnlyCollection<string> Skills { get; init; } = [];
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int CurrentWorkload { get; init; }
}

/// <summary>The opaque work context a set of candidates is being matched against.</summary>
public record MatchingWorkContext
{
    public IReadOnlyCollection<string> RequiredSkills { get; init; } = [];
    public double? TargetLatitude { get; init; }
    public double? TargetLongitude { get; init; }
    public DateTimeOffset? RequiredWindowStart { get; init; }
    public DateTimeOffset? RequiredWindowEnd { get; init; }
}

/// <summary>One factor's configured weight, as parsed from a MatchingPolicyVersion's FactorConfigurationJson.</summary>
public record MatchFactorWeight
{
    public required string FactorCode { get; init; }
    public bool Enabled { get; init; } = true;
    public double Weight { get; init; }
    public string NormalizationMethod { get; init; } = "Linear";
}

/// <summary>Per-factor score contribution for one candidate, kept for persistence/explanation.</summary>
public record MatchFactorScore
{
    public required string FactorCode { get; init; }
    public double? RawValue { get; init; }
    public required double NormalizedScore { get; init; }
    public required double Weight { get; init; }
    public required double WeightedContribution { get; init; }
}

/// <summary>One ranked candidate produced by MatchingScorer.Score.</summary>
public record MatchedCandidate
{
    public required string CandidateType { get; init; }
    public required Guid CandidateId { get; init; }
    public required double RawScore { get; init; }
    public required double NormalizedScore { get; init; }
    public required int Rank { get; init; }
    public required RecommendationLevel RecommendationLevel { get; init; }
    public string? PrimaryReasonCode { get; init; }
    public required IReadOnlyList<MatchFactorScore> Factors { get; init; }
}

/// <summary>
/// Ranks candidates (Resources or, in future, other candidate types) against a
/// MatchingWorkContext using a configurable set of weighted factors.
///
/// Deliberately separate from Eswmp.Assignment's existing AssignmentScorer:
/// Matching ranks *eligible* candidates for planning/recommendation purposes
/// (spec #08), while AssignmentScorer conflates ranking with an eligibility
/// check when picking who actually fulfills a Reservation. The two are not
/// merged here — see docs/api's Matching spec and ARCHITECTURE.md's backlog
/// note on reconciling them once an Eligibility spec exists. Because
/// AssignmentScorer.cs is intentionally left untouched, this class re-derives
/// its own haversine distance rather than sharing AssignmentScorer's private
/// implementation.
/// </summary>
public class MatchingScorer
{
    public const string DistanceFactorCode = "DISTANCE";
    public const string SkillMatchFactorCode = "SKILL_MATCH";
    public const string WorkloadBalanceFactorCode = "WORKLOAD_BALANCE";

    /// <summary>Neutral score used whenever a factor can't be computed for a candidate — never zero.</summary>
    public const double NeutralScore = 50.0;

    public const string BalancedStrategyCode = "BALANCED";

    /// <summary>Default equal-weight policy used when no MatchingPolicyVersion is supplied.</summary>
    public static readonly IReadOnlyList<MatchFactorWeight> DefaultBalancedWeights =
    [
        new() { FactorCode = DistanceFactorCode, Weight = 1.0 / 3 },
        new() { FactorCode = SkillMatchFactorCode, Weight = 1.0 / 3 },
        new() { FactorCode = WorkloadBalanceFactorCode, Weight = 1.0 / 3 },
    ];

    public IReadOnlyList<MatchedCandidate> Score(
        MatchingWorkContext context,
        IReadOnlyCollection<MatchingCandidate> candidates,
        IReadOnlyCollection<MatchFactorWeight>? weights = null)
    {
        if (candidates.Count == 0)
            return [];

        var effectiveWeights = (weights is { Count: > 0 } ? weights : DefaultBalancedWeights)
            .Where(w => w.Enabled)
            .ToList();

        if (effectiveWeights.Count == 0)
            effectiveWeights = DefaultBalancedWeights.ToList();

        var totalWeight = effectiveWeights.Sum(w => w.Weight);
        if (totalWeight <= 0)
            totalWeight = 1.0;

        var distances = candidates.ToDictionary(
            c => c.CandidateId,
            c => ComputeDistanceMetres(context.TargetLatitude, context.TargetLongitude, c.Latitude, c.Longitude));

        var maxDistance = distances.Values.Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty(0).Max();
        var maxWorkload = candidates.Select(c => c.CurrentWorkload).DefaultIfEmpty(0).Max();

        var scored = candidates.Select(candidate =>
        {
            var distance = distances[candidate.CandidateId];

            var factors = new List<MatchFactorScore>();
            foreach (var weight in effectiveWeights)
            {
                var (rawValue, normalized) = weight.FactorCode switch
                {
                    DistanceFactorCode => ScoreDistance(distance, maxDistance),
                    SkillMatchFactorCode => ScoreSkillMatch(candidate, context.RequiredSkills),
                    WorkloadBalanceFactorCode => ScoreWorkloadBalance(candidate.CurrentWorkload, maxWorkload),
                    _ => ((double?)null, NeutralScore),
                };

                var normalizedWeight = weight.Weight / totalWeight;
                factors.Add(new MatchFactorScore
                {
                    FactorCode = weight.FactorCode,
                    RawValue = rawValue,
                    NormalizedScore = normalized,
                    Weight = normalizedWeight,
                    WeightedContribution = Math.Round(normalized * normalizedWeight, 4),
                });
            }

            var totalScore = Math.Round(factors.Sum(f => f.WeightedContribution), 4);
            var primaryReasonCode = factors.OrderByDescending(f => f.WeightedContribution).FirstOrDefault()?.FactorCode;

            return new
            {
                candidate.CandidateType,
                candidate.CandidateId,
                RawScore = totalScore,
                NormalizedScore = totalScore,
                RecommendationLevel = ToRecommendationLevel(totalScore),
                PrimaryReasonCode = primaryReasonCode,
                Factors = (IReadOnlyList<MatchFactorScore>)factors,
            };
        })
        .OrderByDescending(c => c.NormalizedScore)
        .ToList();

        return scored.Select((c, index) => new MatchedCandidate
        {
            CandidateType = c.CandidateType,
            CandidateId = c.CandidateId,
            RawScore = c.RawScore,
            NormalizedScore = c.NormalizedScore,
            Rank = index + 1,
            RecommendationLevel = c.RecommendationLevel,
            PrimaryReasonCode = c.PrimaryReasonCode,
            Factors = c.Factors,
        }).ToList();
    }

    /// <summary>Maps a normalized 0-100 score to a recommendation bucket per spec #08's 90/80/65/50 thresholds.</summary>
    public static RecommendationLevel ToRecommendationLevel(double normalizedScore) => normalizedScore switch
    {
        >= 90 => RecommendationLevel.ExcellentMatch,
        >= 80 => RecommendationLevel.StrongMatch,
        >= 65 => RecommendationLevel.GoodMatch,
        >= 50 => RecommendationLevel.PossibleMatch,
        _ => RecommendationLevel.LowMatch,
    };

    private static (double? raw, double normalized) ScoreDistance(double? distanceMetres, double maxDistance)
    {
        if (distanceMetres is null || maxDistance <= 0)
            return (distanceMetres, NeutralScore); // No location data — neutral, never zero.

        var normalized = 100.0 * (1.0 - (distanceMetres.Value / maxDistance));
        return (distanceMetres, Math.Clamp(normalized, 0, 100));
    }

    private static (double? raw, double normalized) ScoreSkillMatch(MatchingCandidate candidate, IReadOnlyCollection<string> requiredSkills)
    {
        if (requiredSkills.Count == 0)
            return (1.0, 100.0);

        var candidateSkills = candidate.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = requiredSkills.Count(candidateSkills.Contains);
        var ratio = (double)matched / requiredSkills.Count;
        return (ratio, ratio * 100.0);
    }

    private static (double? raw, double normalized) ScoreWorkloadBalance(int currentWorkload, int maxWorkload)
    {
        if (maxWorkload <= 0)
            return (currentWorkload, 100.0);

        var normalized = 100.0 * (1.0 - ((double)currentWorkload / maxWorkload));
        return (currentWorkload, Math.Clamp(normalized, 0, 100));
    }

    /// <summary>Haversine great-circle distance in metres. Returns null if either point is unknown.</summary>
    private static double? ComputeDistanceMetres(double? lat1, double? lon1, double? lat2, double? lon2)
    {
        if (lat1 is null || lon1 is null || lat2 is null || lon2 is null)
            return null;

        const double earthRadiusMetres = 6_371_000;
        var dLat = ToRadians(lat2.Value - lat1.Value);
        var dLon = ToRadians(lon2.Value - lon1.Value);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1.Value)) * Math.Cos(ToRadians(lat2.Value)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMetres * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
