using Eswmp.Core.Services;
using Xunit;

namespace Eswmp.Core.Tests;

public class SlotOptimizerTests
{
    private readonly SlotOptimizer _optimizer = new();

    // Use a far-future date so all slots pass the "not in the past" filter
    private static readonly DateOnly TestDate = new(2030, 1, 15);

    private static readonly SlotOptimizerOptions Opts = new()
    {
        DayStart = new TimeOnly(9, 0),
        DayEnd = new TimeOnly(18, 0),
        MinimumBookableDurationMinutes = 30
    };

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new DateTimeOffset(TestDate.Year, TestDate.Month, TestDate.Day, hour, minute, 0, TimeSpan.Zero);

    private static ExistingReservationBlock Block(int startHour, int durationMinutes) => new()
    {
        Start = At(startHour),
        End = At(startHour).AddMinutes(durationMinutes)
    };

    [Fact]
    public void EmptyDay_ReturnsEntireDaySlots()
    {
        var slots = _optimizer.GetOptimisedSlots(TestDate, 60, [], Opts);
        Assert.NotEmpty(slots);
    }

    [Fact]
    public void GapExactlyEqualToRequestedDuration_SlotOffered()
    {
        // Existing: 09:00-10:00 and 11:00-12:00 -> gap 10:00-11:00 = 60 min
        var existing = new[]
        {
            Block(9, 60),
            Block(11, 60)
        };

        var slots = _optimizer.GetOptimisedSlots(TestDate, 60, existing, Opts);

        Assert.Contains(slots, s => s == At(10));
    }

    [Fact]
    public void GapSmallerThanRequestedDuration_NoSlotInGap()
    {
        // Existing: 09:00-10:00 and 10:30-11:30 -> gap 10:00-10:30 = 30 min (can't fit 60-min request)
        var existing = new[]
        {
            Block(9, 60),
            new ExistingReservationBlock { Start = At(10, 30), End = At(11, 30) }
        };

        var slots = _optimizer.GetOptimisedSlots(TestDate, 60, existing, Opts);

        Assert.DoesNotContain(slots, s => s == At(10));
    }

    [Fact]
    public void SlotInThePast_NotReturned()
    {
        var historicalDate = new DateOnly(2020, 1, 1);
        var slots = _optimizer.GetOptimisedSlots(historicalDate, 60, [], Opts);
        Assert.Empty(slots);
    }

    [Fact]
    public void FullyBookedDay_NoSlotsReturned()
    {
        // 9 consecutive 60-minute reservations fill 09:00-18:00
        var existing = Enumerable.Range(0, 9)
            .Select(i => Block(9 + i, 60))
            .ToArray();

        var slots = _optimizer.GetOptimisedSlots(TestDate, 60, existing, Opts);
        Assert.Empty(slots);
    }

    [Fact]
    public void LargeGap_OffersMultipleSlots()
    {
        // Existing: 09:00-10:00 and 14:00-15:00 -> 4-hour gap 10:00-14:00
        var existing = new[]
        {
            Block(9, 60),
            Block(14, 60)
        };

        var slots = _optimizer.GetOptimisedSlots(TestDate, 60, existing, Opts);
        Assert.True(slots.Count >= 2);
    }
}
