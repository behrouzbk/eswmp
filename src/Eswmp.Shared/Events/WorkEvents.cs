namespace Eswmp.Shared.Events;

// Domain events published by Eswmp.Work — see docs/api "Demand Intake #01" and
// "Work Requirement #02". Every event carries CorrelationId and TenantId, plus
// only opaque platform identifiers (DemandId, WorkRequirementId, VersionNumber) —
// never caller-domain fields, per CLAUDE.md rule 1.

public record DemandAcceptedEvent(
    Guid DemandId,
    Guid TenantId,
    Guid CorrelationId);

public record DemandRejectedEvent(
    Guid DemandId,
    Guid TenantId,
    string ReasonCode,
    string? Comment,
    Guid CorrelationId);

public record DemandCancelledEvent(
    Guid DemandId,
    Guid TenantId,
    Guid CorrelationId);

public record WorkRequirementCreatedEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    string Code,
    Guid CorrelationId);

public record RequirementVersionActivatedEvent(
    Guid WorkRequirementId,
    int VersionNumber,
    Guid TenantId,
    Guid CorrelationId);
