using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

public enum ReservationStatus
{
    Held,
    Confirmed,
    Expired,
    Cancelled
}

/// <summary>
/// A hold (or confirmed booking) against a Resource for a time window.
/// <see cref="ExternalReferenceType"/>/<see cref="ExternalReferenceId"/> is the
/// ONLY link back to the caller's own domain — e.g. { "booking", "BK-12345" }.
/// ESWMP never stores or inspects what that reference means; see
/// docs/ESWMP_VISION.md "Key rule" and CLAUDE.md rule 1.
/// </summary>
public class Reservation : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Held;
    public DateTimeOffset? ExpiresAt { get; set; }

    public required string ExternalReferenceType { get; set; }
    public required string ExternalReferenceId { get; set; }
}

/// <summary>
/// Created when a Reservation is confirmed — the execution record. Kept as a
/// separate entity from Reservation (rather than just a status flag) so the
/// hold/confirm split from docs/ESWMP_VISION.md §6 "Main workflow" is explicit
/// in the data model, not just in application logic.
/// </summary>
public class Appointment : TenantScopedEntity
{
    public required Guid ReservationId { get; set; }
    public required Guid ResourceId { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public string Status { get; set; } = "Confirmed";
}
