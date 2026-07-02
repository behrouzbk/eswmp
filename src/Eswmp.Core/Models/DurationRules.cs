using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

/// <summary>
/// Replaces a hardcoded weight/size → base-minutes switch statement with a
/// tenant-configurable table. <see cref="SizeValue"/> is deliberately generic —
/// it might be a weight in kg, a square-footage, a party size, anything the
/// tenant's ResourceType cares about. Rows are matched by "SizeValue &lt;=
/// MaxSizeValue, ascending" the same way the original threshold brackets worked.
/// </summary>
public class DurationSizeBracket : TenantScopedEntity
{
    public required string ResourceType { get; set; }
    public required decimal MaxSizeValue { get; set; }
    public required int BaseMinutes { get; set; }
}

/// <summary>
/// Replaces hardcoded buffer logic (e.g. "Anxious +15min", "Matted ×1.5") with
/// tenant-configurable rows matched against caller-supplied attribute tags.
/// A tag can add flat minutes, apply a percentage multiplier, and/or raise a
/// safety alert — the same three effects the original rule set needed, just
/// data-driven instead of compiled in.
/// </summary>
public class DurationTagRule : TenantScopedEntity
{
    public string? ResourceType { get; set; }
    public required string Tag { get; set; }
    public int AdditionalMinutes { get; set; }
    public decimal? MultiplierPercent { get; set; }
    public string? SafetyAlertMessage { get; set; }
}
