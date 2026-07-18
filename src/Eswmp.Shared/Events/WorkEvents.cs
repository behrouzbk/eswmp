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

// ── Requirement Definition (first-generation Work Requirement model) ──────────
// See the provenance note on Eswmp.Work.Models.RequirementDefinition.

public record RequirementDefinitionCreatedEvent(
    Guid RequirementDefinitionId,
    Guid TenantId,
    string Code,
    Guid CorrelationId);

public record RequirementDefinitionVersionActivatedEvent(
    Guid RequirementDefinitionId,
    int VersionNumber,
    Guid TenantId,
    Guid CorrelationId);

// ── Work Requirement (docs/api/specs/02-work-requirement-api.md §7.1) ─────────
// All published through the transactional outbox — state change and outbox row
// commit together, so an event can never diverge from the state that caused it.

public record RequirementTemplateCreatedEvent(
    Guid TemplateId,
    Guid TenantId,
    string Code,
    Guid CorrelationId);

public record RequirementTemplateVersionCreatedEvent(
    Guid TemplateId,
    int Version,
    Guid TenantId,
    Guid CorrelationId);

public record RequirementTemplateVersionActivatedEvent(
    Guid TemplateId,
    int Version,
    Guid TenantId,
    Guid CorrelationId);

public record RequirementTemplateRetiredEvent(
    Guid TemplateId,
    Guid TenantId,
    Guid CorrelationId);

public record WorkRequirementCreatedEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    string SourceType,
    string SourceId,
    Guid CorrelationId);

/// <summary>The key downstream trigger — read by Eligibility, Capacity, and Scheduling.</summary>
public record WorkRequirementResolvedEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    string SourceType,
    string SourceId,
    int RequirementVersion,
    string WorkType,
    IReadOnlyList<string> AffectedAreas,
    Guid CorrelationId);

public record WorkRequirementChangedEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    int RequirementVersion,
    IReadOnlyList<string> ChangedCategories,
    IReadOnlyList<string> AffectedResourceRoles,
    IReadOnlyList<string> AffectedCapacityDimensions,
    Guid CorrelationId);

public record WorkRequirementValidatedEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    int RequirementVersion,
    Guid CorrelationId);

public record WorkRequirementInvalidatedEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    int RequirementVersion,
    IReadOnlyList<string> Reasons,
    Guid CorrelationId);

public record WorkRequirementSupersededEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    int SupersededVersion,
    int NewVersion,
    Guid CorrelationId);

public record WorkRequirementCancelledEvent(
    Guid WorkRequirementId,
    Guid TenantId,
    Guid CorrelationId);
