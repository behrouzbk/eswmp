using Eswmp.Work.Data;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Services;

/// <summary>
/// The Demand Intake module's own service for setting Demand.RequirementReferenceId (api spec
/// §7: "Demand.requirementReferenceId &lt;-- WorkRequirementResolved"). Exists so
/// <see cref="Eswmp.Work.Consumers.DemandAcceptedConsumer"/> — which lives on the Work
/// Requirement side of the module boundary — never writes to the demand schema's tables
/// directly; it calls into this Demand-module-owned service instead (CLAUDE.md rule 11: a
/// module's tables are only ever touched by that module's own service/repository class).
/// </summary>
public interface IDemandRequirementLinkService
{
    Task LinkRequirementAsync(Guid demandId, Guid workRequirementId, CancellationToken ct = default);
}

public class DemandRequirementLinkService(WorkDbContext db) : IDemandRequirementLinkService
{
    public async Task LinkRequirementAsync(Guid demandId, Guid workRequirementId, CancellationToken ct = default)
    {
        var demand = await db.Demands.FirstOrDefaultAsync(d => d.Id == demandId, ct);
        if (demand is null)
        {
            return;
        }

        demand.RequirementReferenceId = workRequirementId;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
