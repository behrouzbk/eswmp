using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>
/// The first-generation Work Requirement model, built against the Work Requirement
/// Service Specification (Document 06) directly, before the 2026-07-17 target-architecture
/// reconciliation pass produced the richer Demand-sourced resolution model (see
/// docs/api/specs/02-work-requirement-model.md). Renamed out of the "WorkRequirement"
/// vocabulary — which the reconciled spec claims for its own aggregate root — and kept
/// alongside it rather than deleted, per an explicit decision to preserve this tested code
/// path instead of discarding it. Represents a reusable, versioned, named definition of
/// "what kind of work this is" (resource/capability/skill/certification needs, duration,
/// location constraints) — a simpler forerunner of the new model's RequirementTemplate.
/// </summary>
public enum RequirementDefinitionStatus
{
    Draft,
    Active,
    Inactive,
    Retired
}

public enum RequirementDefinitionVersionStatus
{
    Draft,
    Validated,
    Active,
    Superseded,
    Retired
}

public enum DefinitionDurationType
{
    Fixed,
    Range
}

public enum CapabilityImportance
{
    Mandatory,
    Preferred,
    Optional
}

public enum LocationConstraintMode
{
    FixedLocation,
    CustomerLocation,
    ResourceLocation,
    Virtual,
    Flexible
}

public class RequirementDefinition : TenantScopedEntity
{
    /// <summary>Unique per tenant.</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public RequirementDefinitionStatus Status { get; set; } = RequirementDefinitionStatus.Draft;
    public int CurrentVersionNumber { get; set; }
    public int? ActiveVersionNumber { get; set; }

    /// <summary>
    /// Optimistic-concurrency counter used as `expectedVersion` on version-activate/retire
    /// commands. Distinct from the domain's own per-version `VersionNumber` sequence.
    /// </summary>
    public int ConcurrencyVersion { get; set; } = 1;

    public List<RequirementDefinitionVersion> Versions { get; set; } = [];
}

/// <summary>
/// Immutable once Status is Active/Superseded/Retired — the controller rejects PATCH
/// against any version that isn't still Draft.
/// </summary>
public class RequirementDefinitionVersion : TenantScopedEntity
{
    public Guid RequirementDefinitionId { get; set; }
    public int VersionNumber { get; set; }
    public RequirementDefinitionVersionStatus Status { get; set; } = RequirementDefinitionVersionStatus.Draft;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? ChangeSummary { get; set; }

    public DefinitionDurationType DurationType { get; set; }
    public int? FixedDurationMinutes { get; set; }
    public int? MinimumDurationMinutes { get; set; }
    public int? ExpectedDurationMinutes { get; set; }
    public int? MaximumDurationMinutes { get; set; }
    public int PreWorkBufferMinutes { get; set; }
    public int PostWorkBufferMinutes { get; set; }

    public List<DefinitionResourceRequirement> ResourceRequirements { get; set; } = [];
    public List<LocationConstraint> LocationConstraints { get; set; } = [];
}

/// <summary>
/// A frozen copy of a RequirementDefinitionVersion (+ its children) at a point in time —
/// e.g. taken when a Demand is accepted against it, so later edits to the live
/// version never retroactively change already-accepted work. Immutable once
/// created; there is no update endpoint.
/// </summary>
public class RequirementDefinitionSnapshot : TenantScopedEntity
{
    public Guid SourceRequirementId { get; set; }
    public int SourceVersionNumber { get; set; }

    /// <summary>jsonb — a serialized copy of the RequirementDefinitionVersion + its children at freeze time.</summary>
    public required string DefinitionJson { get; set; }
    public string? Reason { get; set; }
}

public class DefinitionResourceRequirement : BaseEntity
{
    public Guid RequirementDefinitionVersionId { get; set; }
    public required string ResourceTypeCode { get; set; }
    public string? Role { get; set; }
    public int MinimumQuantity { get; set; }
    public int PreferredQuantity { get; set; }
    public int MaximumQuantity { get; set; }
    public bool Mandatory { get; set; }

    public List<DefinitionCapabilityRequirement> CapabilityRequirements { get; set; } = [];
    public List<DefinitionSkillRequirement> SkillRequirements { get; set; } = [];
    public List<DefinitionCertificationRequirement> CertificationRequirements { get; set; } = [];
}

public class DefinitionCapabilityRequirement : BaseEntity
{
    public Guid DefinitionResourceRequirementId { get; set; }
    public required string CapabilityCode { get; set; }
    public int? MinimumLevel { get; set; }
    public CapabilityImportance Importance { get; set; } = CapabilityImportance.Preferred;
}

public class DefinitionSkillRequirement : BaseEntity
{
    public Guid DefinitionResourceRequirementId { get; set; }
    public required string SkillCode { get; set; }
    public int? MinimumLevel { get; set; }
    public bool Mandatory { get; set; }
}

public class DefinitionCertificationRequirement : BaseEntity
{
    public Guid DefinitionResourceRequirementId { get; set; }
    public required string CertificationTypeCode { get; set; }
    public bool Mandatory { get; set; }
}

public class LocationConstraint : BaseEntity
{
    public Guid RequirementDefinitionVersionId { get; set; }
    public LocationConstraintMode Mode { get; set; }
    public decimal? MaximumTravelDistanceKm { get; set; }
    public int? MaximumTravelTimeMinutes { get; set; }
}
