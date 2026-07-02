using Eswmp.Core.Data;
using Eswmp.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Controllers;

public record CalendarEntry(Guid ReservationId, DateTimeOffset Start, DateTimeOffset End, string Status);
public record CalendarResponse(Guid ResourceId, IReadOnlyList<CalendarEntry> Entries);

[ApiController]
[Route("api/v1/resources/{id:guid}/calendar")]
public class CalendarController(CoreDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(EswmpPermissions.ReservationRead)]
    public async Task<ActionResult<CalendarResponse>> Get(Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var entries = await db.Reservations
            .Where(r => r.ResourceId == id && r.StartTime < toUtc && r.EndTime > fromUtc)
            .OrderBy(r => r.StartTime)
            .Select(r => new CalendarEntry(r.Id, r.StartTime, r.EndTime, r.Status.ToString()))
            .ToListAsync();

        return Ok(new CalendarResponse(id, entries));
    }
}
