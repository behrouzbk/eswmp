using Eswmp.Core.Data;
using Eswmp.Core.Services;
using Eswmp.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Controllers;

public record DurationEstimateRequest(
    string ResourceType,
    int DefaultBaseMinutes,
    decimal? SizeValue,
    string[]? AttributeTags);

[ApiController]
[Route("api/v1/duration")]
public class DurationController(CoreDbContext db, ReservationDurationEstimator estimator) : ControllerBase
{
    [HttpPost("estimate")]
    [RequirePermission(EswmpPermissions.AvailabilityRead)]
    public async Task<ActionResult<DurationEstimationResult>> Estimate(DurationEstimateRequest request)
    {
        var sizeBrackets = await db.DurationSizeBrackets
            .Where(b => b.ResourceType == request.ResourceType)
            .ToListAsync();

        var tagRules = await db.DurationTagRules
            .Where(r => r.ResourceType == null || r.ResourceType == request.ResourceType)
            .ToListAsync();

        var result = estimator.Estimate(
            new DurationEstimationInput
            {
                ResourceType = request.ResourceType,
                SizeValue = request.SizeValue,
                AttributeTags = request.AttributeTags ?? [],
            },
            sizeBrackets,
            tagRules,
            request.DefaultBaseMinutes);

        return Ok(result);
    }
}
