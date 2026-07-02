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
        var rules = await db.AvailabilityRules
            .Where(r => r.ResourceId == id && r.DayOfWeek == date.DayOfWeek)
            .Where(r => r.EffectiveFrom == null || r.EffectiveFrom <= date)
            .Where(r => r.EffectiveTo == null || r.EffectiveTo >= date)
            .ToListAsync();

        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.ResourceId == id)
            .Where(e => e.StartTime.Date <= date.ToDateTime(TimeOnly.MaxValue) && e.EndTime.Date >= date.ToDateTime(TimeOnly.MinValue))
            .ToListAsync();

        var windows = rules
            .Select(r => new AvailabilityWindow(
                date.ToDateTime(r.StartTime, DateTimeKind.Utc),
                date.ToDateTime(r.EndTime, DateTimeKind.Utc)))
            .Where(w => !exceptions.Any(e => e.StartTime < w.End && e.EndTime > w.Start))
            .ToList();

        return Ok(new AvailabilityWindowsResponse(id, date, windows));
    }
}
