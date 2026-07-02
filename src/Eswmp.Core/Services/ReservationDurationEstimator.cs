using Eswmp.Core.Models;

namespace Eswmp.Core.Services;

public record DurationEstimationInput
{
    public required string ResourceType { get; init; }
    public decimal? SizeValue { get; init; }
    public IReadOnlyCollection<string> AttributeTags { get; init; } = [];
}

public record DurationEstimationResult
{
    public required int EstimatedMinutes { get; init; }
    public required int BaseMinutes { get; init; }
    public required int BufferMinutes { get; init; }
    public string? SafetyAlertReason { get; init; }
    public bool RequiresSafetyAlert => SafetyAlertReason is not null;
}

/// <summary>
/// Generalised replacement for a hardcoded duration/buffer switch statement.
/// The shape of the original algorithm (base duration from a size bracket, plus
/// additive tag buffers, plus an optional multiplicative modifier, plus an
/// optional safety alert) is preserved exactly — only the source of the rules
/// changed, from compiled C# to tenant-configurable <see cref="DurationSizeBracket"/>
/// and <see cref="DurationTagRule"/> rows. See CLAUDE.md rule 8.
/// </summary>
public class ReservationDurationEstimator
{
    public DurationEstimationResult Estimate(
        DurationEstimationInput input,
        IReadOnlyCollection<DurationSizeBracket> sizeBrackets,
        IReadOnlyCollection<DurationTagRule> tagRules,
        int defaultBaseMinutes = 60)
    {
        int baseMinutes = ResolveBaseMinutes(input, sizeBrackets, defaultBaseMinutes);
        var (bufferMinutes, safetyAlert, multiplierPercent) = ApplyTagRules(input, tagRules);

        if (multiplierPercent is not null)
        {
            baseMinutes = (int)(baseMinutes * (multiplierPercent.Value / 100m));
        }

        return new DurationEstimationResult
        {
            BaseMinutes = baseMinutes,
            BufferMinutes = bufferMinutes,
            EstimatedMinutes = baseMinutes + bufferMinutes,
            SafetyAlertReason = safetyAlert,
        };
    }

    private static int ResolveBaseMinutes(
        DurationEstimationInput input,
        IReadOnlyCollection<DurationSizeBracket> sizeBrackets,
        int defaultBaseMinutes)
    {
        if (input.SizeValue is null)
            return defaultBaseMinutes;

        var bracket = sizeBrackets
            .Where(b => b.ResourceType == input.ResourceType && b.MaxSizeValue >= input.SizeValue.Value)
            .OrderBy(b => b.MaxSizeValue)
            .FirstOrDefault();

        return bracket?.BaseMinutes ?? defaultBaseMinutes;
    }

    private static (int bufferMinutes, string? safetyAlert, decimal? multiplierPercent) ApplyTagRules(
        DurationEstimationInput input,
        IReadOnlyCollection<DurationTagRule> tagRules)
    {
        int buffer = 0;
        string? safetyAlert = null;
        decimal? multiplierPercent = null;

        var tags = input.AttributeTags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var applicableRules = tagRules.Where(r =>
            tags.Contains(r.Tag) &&
            (r.ResourceType is null || r.ResourceType == input.ResourceType));

        foreach (var rule in applicableRules)
        {
            buffer += rule.AdditionalMinutes;

            if (rule.MultiplierPercent is not null)
                multiplierPercent = rule.MultiplierPercent;

            if (rule.SafetyAlertMessage is not null)
                safetyAlert = rule.SafetyAlertMessage;
        }

        return (buffer, safetyAlert, multiplierPercent);
    }
}
