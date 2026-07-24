using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>
/// Disclosure level for a single requirement line (v2 delta, UX-03/UX-04). Explicit values so
/// the "Customer" ordinal a DB CHECK constraint references stays stable regardless of future
/// member reordering.
/// </summary>
public enum VisibilityLevel
{
    Customer = 0,
    Provider = 1,
    Internal = 2
}

/// <summary>
/// requirement.RequirementLineVisibility (v2 delta) — per-line disclosure control, keyed by
/// (LineType, LineId) rather than a column repeated across every requirement-line table, since
/// explain/resolved/compare assemble their output from many sources and a single lookup keeps
/// the audience filter in one place. LineType/LineId is a loosely-coupled discriminator with no
/// FK to the 13 disparate requirement-line tables — the same pattern already used for
/// WorkRequirement.SourceType/SourceId.
/// </summary>
public class RequirementLineVisibility : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }

    /// <summary>The requirement-line entity's C# type name, e.g. nameof(CapabilityRequirement).</summary>
    public required string LineType { get; set; }

    /// <summary>The Id of the row in that line's table.</summary>
    public Guid LineId { get; set; }

    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Internal;

    /// <summary>Derived from VisibilityLevel == Customer; drives approval scope (UX-04). Kept as
    /// its own column (rather than computed at read time) to mirror requirement-schema.sql, and
    /// enforced consistent with VisibilityLevel via CK_RLV_Consistent.</summary>
    public bool CustomerVisible { get; set; }
}
