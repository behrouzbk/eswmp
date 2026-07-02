using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

/// <summary>Recurring weekly availability window for a Resource.</summary>
public class AvailabilityRule : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required DayOfWeek DayOfWeek { get; set; }
    public required TimeOnly StartTime { get; set; }
    public required TimeOnly EndTime { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>
/// A one-off exception to the recurring rule — time off, a holiday, an
/// emergency block. Named AvailabilityException rather than "TimeOff" so it
/// reads naturally for non-workforce resources too (a room under maintenance,
/// a van in for service).
/// </summary>
public class AvailabilityException : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public required string Reason { get; set; }
}
