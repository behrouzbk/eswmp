using Eswmp.Shared.DTOs;

namespace Eswmp.Rules.Models;

/// <summary>
/// A tenant-configurable business rule. <see cref="Definition"/> is stored as
/// jsonb — a flat key/value parameter bag whose shape is defined by
/// <see cref="RuleType"/> (e.g. "MaxDailyReservationsPerResource" expects a
/// { "max": 6 } definition). This is intentionally a thin foundation, not a
/// full rules-engine integration (Drools/Microsoft RulesEngine) — see
/// docs/ARCHITECTURE.md for why that's deferred until a real rule catalogue
/// exists to justify it.
/// </summary>
public class BusinessRule : TenantScopedEntity
{
    public required string Name { get; set; }
    public required string RuleType { get; set; }
    public string? ResourceType { get; set; }
    public required string DefinitionJson { get; set; }
    public bool IsActive { get; set; } = true;
}

public class WorkflowTransitionLog : TenantScopedEntity
{
    public required string ExternalReferenceType { get; set; }
    public required string ExternalReferenceId { get; set; }
    public required WorkflowState FromState { get; set; }
    public required WorkflowState ToState { get; set; }
    public DateTimeOffset TransitionedAt { get; set; } = DateTimeOffset.UtcNow;
}
