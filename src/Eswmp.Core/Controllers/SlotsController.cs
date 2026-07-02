using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Core.Services;
using Eswmp.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Controllers;

public record SlotSearchRequest(Guid ResourceId, DateOnly Date, int RequestedDurationMinutes, int MinimumBookableDurationMinutes = 30);
public record SlotSearchResponse(IReadOnlyList<DateTimeOffset> Slots);

[ApiController]
[Route("api/v1/slots")]
public class SlotsController(CoreDbContext db, SlotOptimizer optimizer) : ControllerBase
{
    [HttpPost("search")]
    [RequirePermission(EswmpPermissions.AvailabilityRead)]
    public async Task<ActionResult<SlotSearchResponse>> Search(SlotSearchRequest request)
    {
        var dayStart = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = request.Date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var existingReservations = await db.Reservations
            .Where(r => r.ResourceId == request.ResourceId)
            .Where(r => r.Status == ReservationStatus.Held || r.Status == ReservationStatus.Confirmed)
            .Where(r => r.StartTime < dayEnd && r.EndTime > dayStart)
            .Select(r => new ExistingReservationBlock { Start = r.StartTime, End = r.EndTime })
            .ToListAsync();

        var slots = optimizer.GetOptimisedSlots(
            request.Date,
            request.RequestedDurationMinutes,
            existingReservations,
            new SlotOptimizerOptions { MinimumBookableDurationMinutes = request.MinimumBookableDurationMinutes });

        return Ok(new SlotSearchResponse(slots));
    }
}
