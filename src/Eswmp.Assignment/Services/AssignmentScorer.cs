namespace Eswmp.Assignment.Services;

public record AssignmentCandidate
{
    public required Guid ResourceId { get; init; }
    public double? CurrentLatitude { get; init; }
    public double? CurrentLongitude { get; init; }
    public IReadOnlyCollection<string> Skills { get; init; } = [];
    public int ActiveReservationCount { get; init; }
    public double? AverageCompletionMinutes { get; init; }
}

public record ScoreAssignmentInput
{
    public required Guid ReservationId { get; init; }
    public double? TargetLatitude { get; init; }
    public double? TargetLongitude { get; init; }
    public IReadOnlyCollection<string> RequiredSkills { get; init; } = [];
    public int? EstimatedDurationMinutes { get; init; }
    public required IReadOnlyCollection<AssignmentCandidate> Candidates { get; init; }
}

public record ScoredCandidate
{
    public required Guid ResourceId { get; init; }
    public required double Score { get; init; }
    public double? DistanceMetres { get; init; }
    public required bool SkillMatch { get; init; }
    public required string Rationale { get; init; }
}

public record AssignmentWeights
{
    public double Proximity { get; init; } = 0.35;
    public double SkillMatch { get; init; } = 0.30;
    public double Workload { get; init; } = 0.20;
    public double SpeedFit { get; init; } = 0.15;
}

/// <summary>
/// Ranks candidate Resources for a Reservation. Deliberately separate from
/// Eswmp.Core's scheduling logic — "separate scheduling from assignment" per
/// docs/ESWMP_VISION.md §8. Scoring factors mirror the design PetZiv's own
/// Cluster Engineering Guide specified for its (never-built) auto-assignment
/// scorer — proximity, specialisation match, current workload, speed-profile
/// fit — generalised here from "provider" to generic Resource so it isn't tied
/// to a workforce/employee model. See CLAUDE.md rule 1.
/// </summary>
public class AssignmentScorer
{
    public IReadOnlyList<ScoredCandidate> Score(ScoreAssignmentInput input, AssignmentWeights? weights = null)
    {
        weights ??= new AssignmentWeights();

        if (input.Candidates.Count == 0)
            return [];

        var distances = input.Candidates.ToDictionary(
            c => c.ResourceId,
            c => ComputeDistanceMetres(input.TargetLatitude, input.TargetLongitude, c.CurrentLatitude, c.CurrentLongitude));

        var maxDistance = distances.Values.Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty(0).Max();
        var maxWorkload = input.Candidates.Select(c => c.ActiveReservationCount).DefaultIfEmpty(0).Max();

        var scored = input.Candidates.Select(candidate =>
        {
            var distance = distances[candidate.ResourceId];
            var proximityScore = ScoreProximity(distance, maxDistance);
            var skillMatch = HasRequiredSkills(candidate, input.RequiredSkills);
            var skillScore = skillMatch ? 1.0 : 0.0;
            var workloadScore = ScoreWorkload(candidate.ActiveReservationCount, maxWorkload);
            var speedFitScore = ScoreSpeedFit(candidate.AverageCompletionMinutes, input.EstimatedDurationMinutes);

            var totalScore =
                (proximityScore * weights.Proximity) +
                (skillScore * weights.SkillMatch) +
                (workloadScore * weights.Workload) +
                (speedFitScore * weights.SpeedFit);

            return new ScoredCandidate
            {
                ResourceId = candidate.ResourceId,
                Score = Math.Round(totalScore, 4),
                DistanceMetres = distance,
                SkillMatch = skillMatch,
                Rationale = $"proximity={proximityScore:F2} skill={skillScore:F2} workload={workloadScore:F2} speedFit={speedFitScore:F2}",
            };
        });

        return scored.OrderByDescending(c => c.Score).ToList();
    }

    private static bool HasRequiredSkills(AssignmentCandidate candidate, IReadOnlyCollection<string> requiredSkills)
    {
        if (requiredSkills.Count == 0)
            return true;

        var candidateSkills = candidate.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requiredSkills.All(candidateSkills.Contains);
    }

    private static double ScoreProximity(double? distanceMetres, double maxDistance)
    {
        if (distanceMetres is null || maxDistance <= 0)
            return 0.5; // No location data — neutral score, don't penalise or favour

        return 1.0 - (distanceMetres.Value / maxDistance);
    }

    private static double ScoreWorkload(int activeReservationCount, int maxWorkload)
    {
        if (maxWorkload <= 0)
            return 1.0;

        return 1.0 - ((double)activeReservationCount / maxWorkload);
    }

    private static double ScoreSpeedFit(double? averageCompletionMinutes, int? estimatedDurationMinutes)
    {
        if (averageCompletionMinutes is null || estimatedDurationMinutes is null || estimatedDurationMinutes == 0)
            return 0.5;

        // Closer to 1.0 the closer the candidate's historical average is to the estimate;
        // candidates who consistently run faster than estimated score highest.
        var ratio = averageCompletionMinutes.Value / estimatedDurationMinutes.Value;
        return Math.Clamp(1.0 - Math.Abs(1.0 - ratio), 0.0, 1.0);
    }

    /// <summary>Haversine great-circle distance in metres.</summary>
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
