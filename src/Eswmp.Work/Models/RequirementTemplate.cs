using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>requirement.RequirementTemplates (model §4.2) — reusable operational defaults per tenant.</summary>
public enum TemplateStatus
{
    Draft,
    Active,
    Retired
}

/// <summary>Template versions are immutable after activation — a change is a new version, never an edit in place.</summary>
public enum TemplateVersionStatus
{
    Draft,
    Active,
    Superseded,
    Retired
}

public class RequirementTemplate : TenantScopedEntity
{
    /// <summary>e.g. DOG_WALK_STANDARD. UQ (TenantId, Code) — cross-tenant template use is prohibited.</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string WorkType { get; set; }
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;

    /// <summary>Pointer to the live version.</summary>
    public int CurrentVersion { get; set; } = 1;

    public List<RequirementTemplateVersion> Versions { get; set; } = [];
}

public class RequirementTemplateVersion : TenantScopedEntity
{
    public Guid TemplateId { get; set; }

    /// <summary>UQ with TemplateId.</summary>
    public int Version { get; set; }

    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public TemplateVersionStatus Status { get; set; } = TemplateVersionStatus.Draft;
    public string? ChangeReason { get; set; }

    /// <summary>jsonb — the requirement definitions this version resolves to. Immutable after activation.</summary>
    public string DefinitionJson { get; set; } = "{}";
}
