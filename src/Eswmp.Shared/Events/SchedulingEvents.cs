namespace Eswmp.Shared.Events;

// Domain events published by Eswmp.Core — see docs/ESWMP_VISION.md §9 "Events to publish".
// Every event carries CorrelationId (propagated from the HTTP traceparent header) and TenantId.
// None of these events ever carry caller-domain fields (no pet names, no client names —
// only ExternalReferenceType/ExternalReferenceId, which the platform treats as opaque).

public record SlotReservedEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid ResourceId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string ExternalReferenceType,
    string ExternalReferenceId,
    Guid CorrelationId);

public record ReservationConfirmedEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid ResourceId,
    Guid AppointmentId,
    string ExternalReferenceType,
    string ExternalReferenceId,
    Guid CorrelationId);

public record ReservationCancelledEvent(
    Guid ReservationId,
    Guid TenantId,
    string Reason,
    Guid CorrelationId);

public record ReservationExpiredEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid CorrelationId);

public record AvailabilityChangedEvent(
    Guid TenantId,
    Guid ResourceId,
    DateOnly AffectedDate,
    Guid CorrelationId);

public record ResourceUnavailableEvent(
    Guid TenantId,
    Guid ResourceId,
    DateTimeOffset From,
    DateTimeOffset To,
    string Reason,
    Guid CorrelationId);
