using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>
/// The reconciled Work Requirement Service model — see docs/api/specs/02-work-requirement-model.md
/// and requirement-schema.sql. The authoritative owner of what must be true for a unit of
/// work to be performed: it answers what is operationally required, never who performs it,
/// when it is scheduled, or which candidate is best (§1). It never copies its source Demand;
/// SourceType/SourceId is the only coupling to Demand Intake (see the provenance note on
/// Eswmp.Work.Models.RequirementDefinition for the earlier, simpler model this supersedes).
/// </summary>
public enum WorkRequirementStatus
{
    Draft,
    Validating,
    Valid,
    Invalid,
    Superseded,
    Cancelled,
    Completed
}

/// <summary>Mirrors demand.DemandPriority so the domain's priority vocabulary stays one shape.</summary>
public enum RequirementPriority
{
    Low,
    Normal,
    High,
    Urgent,
    Critical
}

/// <summary>
/// requirement.WorkRequirements — aggregate root (model §4.1). Templates generate Work
/// Requirements; TemplateId/TemplateVersion is frozen provenance so work created under
/// version 1 never silently becomes version 2.
/// </summary>
public class WorkRequirement : TenantScopedEntity
{
    /// <summary>Origin kind — 'Demand' today. Open text so a future Service Request
    /// upstream needs no schema change (model §7.1).</summary>
    public required string SourceType { get; set; }

    /// <summary>The source object's id (e.g. the Demand id). The source object itself is
    /// never copied into this schema (model §1.2).</summary>
    public required string SourceId { get; set; }

    /// <summary>Source.Version at resolution time.</summary>
    public int? SourceVersion { get; set; }

    public Guid? TemplateId { get; set; }
    public int? TemplateVersion { get; set; }

    public required string WorkType { get; set; }
    public string? WorkCategory { get; set; }
    public string? ServiceMode { get; set; }
    public string? ComplexityLevel { get; set; }

    public WorkRequirementStatus Status { get; set; } = WorkRequirementStatus.Draft;
    public RequirementPriority Priority { get; set; } = RequirementPriority.Normal;

    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>Optimistic-concurrency counter — supplied as `expectedVersion` on revise (model §4.1, api §11.2).</summary>
    public int RequirementVersion { get; set; } = 1;

    public List<ResourceRoleRequirement> ResourceRequirements { get; set; } = [];
    public List<CapabilityRequirement> CapabilityRequirements { get; set; } = [];
    public List<CertificationRequirement> CertificationRequirements { get; set; } = [];
    public List<CapacityRequirement> CapacityRequirements { get; set; } = [];
    public DurationRequirement? DurationRequirement { get; set; }
    public TimeRequirement? TimeRequirement { get; set; }
    public LocationRequirement? LocationRequirement { get; set; }
    public ExecutionRequirement? ExecutionRequirement { get; set; }
    public TravelRequirement? TravelRequirement { get; set; }
    public List<BufferRequirement> BufferRequirements { get; set; } = [];
    public List<DependencyRequirement> DependencyRequirements { get; set; } = [];
    public List<RequirementConstraint> Constraints { get; set; } = [];
    public List<RequirementPreference> Preferences { get; set; } = [];
}

/// <summary>
/// requirement.RequirementVersions (model §4.7) — one row per revision. SnapshotJson holds
/// the immutable resolved snapshot at that revision, so GET .../versions/{version} and the
/// compare endpoint never re-derive history from the live (mutable) aggregate.
/// </summary>
public class RequirementVersion : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public int Version { get; set; }

    /// <summary>e.g. Material, Minor — whether downstream consumers must recalculate.</summary>
    public string? ChangeType { get; set; }
    public string? ChangeReason { get; set; }
    public int? SourceVersion { get; set; }
    public int? TemplateVersion { get; set; }

    /// <summary>jsonb — the immutable resolved requirement contract (§3.2) as of this version.</summary>
    public required string SnapshotJson { get; set; }
}
