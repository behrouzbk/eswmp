using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Controllers;

public record CreateAvailabilityRuleRequest(
    Guid ResourceId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public record AvailabilityWindow(DateTimeOffset Start, DateTimeOffset End);
public record AvailabilityWindowsResponse(Guid ResourceId, DateOnly Date, IReadOnlyList<AvailabilityWindow> Windows);

public record CreateTimeOffRequest(string Type, DateTimeOffset StartTime, DateTimeOffset EndTime, string? ReasonCode);
public record CreateAvailabilityOverrideRequest(AvailabilityOverrideEffect Effect, DateTimeOffset StartTime, DateTimeOffset EndTime, string? ReasonCode);
public record ResolveAvailabilityRequest(Guid ResourceId, DateTimeOffset StartTime, DateTimeOffset EndTime);
public record ResolveAvailabilityResponse(Guid ResourceId, IReadOnlyList<AvailabilityWindow> FreeIntervals);
public record BatchResolveAvailabilityRequest(Guid[] ResourceIds, DateTimeOffset StartTime, DateTimeOffset EndTime);

[ApiController]
[Route("api/v1")]
public class AvailabilityController(CoreDbContext db, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("availability-rules")]
    [RequirePermission(EswmpPermissions.AvailabilityWrite)]
    public async Task<IActionResult> CreateRule(CreateAvailabilityRuleRequest request)
    {
        var rule = new AvailabilityRule
        {
            TenantId = tenantContext.RequiredTenantId,
            ResourceId = request.ResourceId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };

        db.AvailabilityRules.Add(rule);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(CreateRule), new { id = rule.Id }, rule);
    }

    [HttpGet("resources/{id:guid}/availability")]
    [RequirePermission(EswmpPermissions.AvailabilityRead)]
    public async Task<ActionResult<AvailabilityWindowsResponse>> GetAvailability(Guid id, [FromQuery] DateOnly date)
    {
        var startOfDay = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endOfDay = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var windows = await ResolveFreeIntervalsAsync(id, startOfDay, endOfDay, date);

        return Ok(new AvailabilityWindowsResponse(id, date, windows));
    }

    [HttpPost("resources/{id:guid}/time-off-requests")]
    [RequirePermission(EswmpPermissions.AvailabilityWrite)]
    public async Task<IActionResult> CreateTimeOffRequest(Guid id, CreateTimeOffRequest request)
    {
        var timeOff = new TimeOff
        {
            TenantId = tenantContext.RequiredTenantId,
            ResourceId = id,
            Type = request.Type,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ReasonCode = request.ReasonCode,
            ApprovalStatus = TimeOffApprovalStatus.PendingApproval,
        };

        db.TimeOffs.Add(timeOff);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(CreateTimeOffRequest), new { id }, timeOff);
    }

    [HttpPost("time-off-requests/{id:guid}/approve")]
    [RequirePermission(EswmpPermissions.AvailabilityWrite)]
    public Task<IActionResult> ApproveTimeOff(Guid id) => TransitionTimeOff(id, TimeOffApprovalStatus.Approved, [TimeOffApprovalStatus.PendingApproval]);

    [HttpPost("time-off-requests/{id:guid}/reject")]
    [RequirePermission(EswmpPermissions.AvailabilityWrite)]
    public Task<IActionResult> RejectTimeOff(Guid id) => TransitionTimeOff(id, TimeOffApprovalStatus.Rejected, [TimeOffApprovalStatus.PendingApproval]);

    [HttpPost("time-off-requests/{id:guid}/cancel")]
    [RequirePermission(EswmpPermissions.AvailabilityWrite)]
    public Task<IActionResult> CancelTimeOff(Guid id) =>
        TransitionTimeOff(id, TimeOffApprovalStatus.Cancelled, [TimeOffApprovalStatus.PendingApproval, TimeOffApprovalStatus.Approved]);

    private async Task<IActionResult> TransitionTimeOff(Guid id, TimeOffApprovalStatus target, TimeOffApprovalStatus[] allowedFrom)
    {
        var timeOff = await db.TimeOffs.FindAsync(id);
        if (timeOff is null)
            return NotFound();

        if (!allowedFrom.Contains(timeOff.ApprovalStatus))
            return Conflict(new { error = $"Time off request is {timeOff.ApprovalStatus}, cannot transition to {target}." });

        timeOff.ApprovalStatus = target;
        await db.SaveChangesAsync();

        return Ok(timeOff);
    }

    [HttpPost("resources/{id:guid}/availability-overrides")]
    [RequirePermission(EswmpPermissions.AvailabilityWrite)]
    public async Task<IActionResult> CreateAvailabilityOverride(Guid id, CreateAvailabilityOverrideRequest request)
    {
        var availabilityOverride = new AvailabilityOverride
        {
            TenantId = tenantContext.RequiredTenantId,
            ResourceId = id,
            Effect = request.Effect,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ReasonCode = request.ReasonCode,
        };

        db.AvailabilityOverrides.Add(availabilityOverride);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(CreateAvailabilityOverride), new { id }, availabilityOverride);
    }

    [HttpPost("availability/resolve")]
    [RequirePermission(EswmpPermissions.AvailabilityRead)]
    public async Task<ActionResult<ResolveAvailabilityResponse>> Resolve(ResolveAvailabilityRequest request)
    {
        var windows = await ResolveFreeIntervalsAsync(request.ResourceId, request.StartTime, request.EndTime, null);
        return Ok(new ResolveAvailabilityResponse(request.ResourceId, windows));
    }

    [HttpPost("availability/batch-resolve")]
    [RequirePermission(EswmpPermissions.AvailabilityRead)]
    public async Task<ActionResult<IReadOnlyList<ResolveAvailabilityResponse>>> BatchResolve(BatchResolveAvailabilityRequest request)
    {
        var results = new List<ResolveAvailabilityResponse>();
        foreach (var resourceId in request.ResourceIds)
        {
            var windows = await ResolveFreeIntervalsAsync(resourceId, request.StartTime, request.EndTime, null);
            results.Add(new ResolveAvailabilityResponse(resourceId, windows));
        }

        return Ok(results);
    }

    /// <summary>
    /// Shared availability-resolution logic used by both the legacy per-date
    /// endpoint and the new /availability/resolve and /availability/batch-resolve
    /// endpoints. Applies the priority stack: hard unavailability (AvailabilityOverride
    /// ForceUnavailable) > approved TimeOff > AvailabilityOverride ForceAvailable >
    /// AvailabilityException > recurring AvailabilityRule.
    /// </summary>
    private async Task<IReadOnlyList<AvailabilityWindow>> ResolveFreeIntervalsAsync(
        Guid resourceId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, DateOnly? singleDate)
    {
        // Base availability: the recurring rule windows for each day covered by the range.
        var candidateWindows = new List<AvailabilityWindow>();

        var dates = singleDate is not null
            ? [singleDate.Value]
            : EnumerateDates(DateOnly.FromDateTime(rangeStart.UtcDateTime), DateOnly.FromDateTime(rangeEnd.UtcDateTime));

        foreach (var date in dates)
        {
            var rules = await db.AvailabilityRules
                .Where(r => r.ResourceId == resourceId && r.DayOfWeek == date.DayOfWeek && r.Status == AvailabilityRuleStatus.Active)
                .Where(r => r.EffectiveFrom == null || r.EffectiveFrom <= date)
                .Where(r => r.EffectiveTo == null || r.EffectiveTo >= date)
                .ToListAsync();

            foreach (var rule in rules)
            {
                var windowStart = date.ToDateTime(rule.StartTime, DateTimeKind.Utc);
                var windowEnd = date.ToDateTime(rule.EndTime, DateTimeKind.Utc);
                if (windowEnd > rangeStart && windowStart < rangeEnd)
                {
                    candidateWindows.Add(new AvailabilityWindow(
                        windowStart < rangeStart ? rangeStart : windowStart,
                        windowEnd > rangeEnd ? rangeEnd : windowEnd));
                }
            }
        }

        // AdditionalAvailability exceptions extend the base windows.
        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.ResourceId == resourceId && e.StartTime < rangeEnd && e.EndTime > rangeStart)
            .ToListAsync();

        foreach (var addition in exceptions.Where(e => e.ExceptionType == AvailabilityExceptionType.AdditionalAvailability))
        {
            candidateWindows.Add(new AvailabilityWindow(
                addition.StartTime < rangeStart ? rangeStart : addition.StartTime,
                addition.EndTime > rangeEnd ? rangeEnd : addition.EndTime));
        }

        // AvailabilityOverride ForceAvailable also extends.
        var overrides = await db.AvailabilityOverrides
            .Where(o => o.ResourceId == resourceId && o.Status == AvailabilityOverrideStatus.Active
                && o.StartTime < rangeEnd && o.EndTime > rangeStart)
            .ToListAsync();

        foreach (var addition in overrides.Where(o => o.Effect == AvailabilityOverrideEffect.ForceAvailable))
        {
            candidateWindows.Add(new AvailabilityWindow(
                addition.StartTime < rangeStart ? rangeStart : addition.StartTime,
                addition.EndTime > rangeEnd ? rangeEnd : addition.EndTime));
        }

        // Blocking intervals, in priority order: ForceUnavailable overrides > approved TimeOff > Unavailable/ModifiedAvailability exceptions.
        var blocks = new List<AvailabilityWindow>();
        blocks.AddRange(overrides
            .Where(o => o.Effect == AvailabilityOverrideEffect.ForceUnavailable)
            .Select(o => new AvailabilityWindow(o.StartTime, o.EndTime)));

        var approvedTimeOff = await db.TimeOffs
            .Where(t => t.ResourceId == resourceId && t.ApprovalStatus == TimeOffApprovalStatus.Approved
                && t.StartTime < rangeEnd && t.EndTime > rangeStart)
            .ToListAsync();
        blocks.AddRange(approvedTimeOff.Select(t => new AvailabilityWindow(t.StartTime, t.EndTime)));

        blocks.AddRange(exceptions
            .Where(e => e.ExceptionType is AvailabilityExceptionType.Unavailable or AvailabilityExceptionType.ModifiedAvailability)
            .Select(e => new AvailabilityWindow(e.StartTime, e.EndTime)));

        return SubtractBlocks(candidateWindows, blocks);
    }

    private static IReadOnlyList<AvailabilityWindow> SubtractBlocks(
        IReadOnlyList<AvailabilityWindow> windows, IReadOnlyList<AvailabilityWindow> blocks)
    {
        var result = new List<AvailabilityWindow>();

        foreach (var window in windows)
        {
            var segments = new List<AvailabilityWindow> { window };

            foreach (var block in blocks)
            {
                var next = new List<AvailabilityWindow>();
                foreach (var segment in segments)
                {
                    if (block.End <= segment.Start || block.Start >= segment.End)
                    {
                        next.Add(segment);
                        continue;
                    }

                    if (block.Start > segment.Start)
                        next.Add(new AvailabilityWindow(segment.Start, block.Start));
                    if (block.End < segment.End)
                        next.Add(new AvailabilityWindow(block.End, segment.End));
                }
                segments = next;
            }

            result.AddRange(segments.Where(s => s.End > s.Start));
        }

        return result.OrderBy(w => w.Start).ToList();
    }

    private static IReadOnlyList<DateOnly> EnumerateDates(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
            dates.Add(d);
        return dates;
    }
}
