using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

/// <summary>
/// A minimal tenant record — just enough for multi-tenant isolation and default
/// scheduling parameters. ESWMP does not own identity/user management; the
/// consuming product's auth system is the source of truth for who a tenant's
/// users are. This record exists so Core has somewhere to hang tenant-level
/// scheduling defaults (business hours, timezone) without reaching into another
/// service's database.
/// </summary>
public class Tenant : BaseEntity
{
    public required string Name { get; set; }
    public required string Timezone { get; set; }
    public TimeOnly DefaultDayStart { get; set; } = new(8, 0);
    public TimeOnly DefaultDayEnd { get; set; } = new(18, 0);
    public int DefaultMinimumBookableDurationMinutes { get; set; } = 30;
}
