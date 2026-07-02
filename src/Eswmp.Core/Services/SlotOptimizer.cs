namespace Eswmp.Core.Services;

public record ExistingReservationBlock
{
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
}

public record SlotOptimizerOptions
{
    /// <summary>
    /// The tenant's configured minimum bookable duration in minutes.
    /// A gap shorter than this cannot accommodate any new reservation,
    /// so gap-elimination optimisation must not offer a slot that creates such a gap.
    /// </summary>
    public int MinimumBookableDurationMinutes { get; init; } = 30;

    /// <summary>Resource's operating window start for the day.</summary>
    public TimeOnly DayStart { get; init; } = new(8, 0);

    /// <summary>Resource's operating window end for the day.</summary>
    public TimeOnly DayEnd { get; init; } = new(18, 0);
}

/// <summary>
/// Gap-elimination slot search: only surfaces slots that connect perfectly to
/// existing reservations — no slot is offered if it would create an unsellable
/// gap. Generic over any resource type; the original implementation of this
/// algorithm (proven against grooming-appointment scheduling) is unchanged here
/// beyond renaming "appointment" to "reservation" throughout.
/// </summary>
public class SlotOptimizer
{
    public IReadOnlyList<DateTimeOffset> GetOptimisedSlots(
        DateOnly date,
        int requestedDurationMinutes,
        IEnumerable<ExistingReservationBlock> existingReservations,
        SlotOptimizerOptions? options = null)
    {
        options ??= new SlotOptimizerOptions();

        var dayStart = date.ToDateTime(options.DayStart, DateTimeKind.Utc);
        var dayEnd = date.ToDateTime(options.DayEnd, DateTimeKind.Utc);

        var blocks = existingReservations
            .OrderBy(b => b.Start)
            .ToList();

        var candidateSlots = new List<DateTimeOffset>();

        // Consider the start of day as a potential slot start
        var freeWindows = BuildFreeWindows(dayStart, dayEnd, blocks);

        foreach (var (windowStart, windowEnd) in freeWindows)
        {
            var windowDuration = (windowEnd - windowStart).TotalMinutes;

            // The requested duration must fit in this window
            if (windowDuration < requestedDurationMinutes)
                continue;

            // A slot at the very start of the window never creates a leading gap
            candidateSlots.Add(windowStart);

            // A slot ending exactly at the window end never creates a trailing gap
            var slotStartAtEnd = windowEnd.AddMinutes(-requestedDurationMinutes);
            if (slotStartAtEnd > windowStart)
                candidateSlots.Add(slotStartAtEnd);

            // For the interior: only offer start times that leave NO gap
            // (or a gap >= minimumBookable on each side)
            var interiorStart = windowStart.AddMinutes(requestedDurationMinutes);
            while (interiorStart <= windowEnd.AddMinutes(-requestedDurationMinutes))
            {
                var gapAfter = (windowEnd - interiorStart.AddMinutes(requestedDurationMinutes)).TotalMinutes;
                var gapBefore = (interiorStart - windowStart).TotalMinutes;

                bool leadingGapOk = gapBefore == 0 || gapBefore >= options.MinimumBookableDurationMinutes;
                bool trailingGapOk = gapAfter == 0 || gapAfter >= options.MinimumBookableDurationMinutes;

                if (leadingGapOk && trailingGapOk)
                    candidateSlots.Add(interiorStart);

                interiorStart = interiorStart.AddMinutes(options.MinimumBookableDurationMinutes);
            }
        }

        return candidateSlots
            .Distinct()
            .Where(s => s >= DateTimeOffset.UtcNow.AddMinutes(30))  // Cannot book in the past or within 30min
            .OrderBy(s => s)
            .ToList();
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> BuildFreeWindows(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        IReadOnlyList<ExistingReservationBlock> blocks)
    {
        var windows = new List<(DateTimeOffset, DateTimeOffset)>();
        var cursor = dayStart;

        foreach (var block in blocks)
        {
            if (block.Start > cursor)
                windows.Add((cursor, block.Start));

            cursor = block.End > cursor ? block.End : cursor;
        }

        if (cursor < dayEnd)
            windows.Add((cursor, dayEnd));

        return windows;
    }
}
