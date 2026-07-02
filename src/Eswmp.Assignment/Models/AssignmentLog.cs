using Eswmp.Shared.DTOs;

namespace Eswmp.Assignment.Models;

public enum AssignmentMethod
{
    Auto,
    Manual
}

/// <summary>Audit record of which Resource was assigned to which Reservation, and how.</summary>
public class AssignmentLog : TenantScopedEntity
{
    public required Guid ReservationId { get; set; }
    public required Guid ResourceId { get; set; }
    public required AssignmentMethod Method { get; set; }
    public double? Score { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
