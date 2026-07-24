using System.Text.Json;
using Eswmp.Shared.Events;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Services;

/// <summary>
/// The Demand Intake module's own service for setting Demand.RequirementReferenceId (api spec
/// §7: "Demand.requirementReferenceId &lt;-- WorkRequirementResolved") and, as of the v2 delta,
/// for flagging a Demand NeedsAttention when resolution fails. Exists so
/// <see cref="Eswmp.Work.Consumers.DemandAcceptedConsumer"/> — which lives on the Work
/// Requirement side of the module boundary — never writes to the demand schema's tables
/// directly; it calls into this Demand-module-owned service instead (CLAUDE.md rule 11: a
/// module's tables are only ever touched by that module's own service/repository class).
/// </summary>
public interface IDemandRequirementLinkService
{
    Task LinkRequirementAsync(Guid demandId, Guid workRequirementId, CancellationToken ct = default);

    /// <summary>v2 delta (UX-09/UX-14) — the failure path that didn't exist: called from every
    /// DemandAcceptedConsumer early-return branch instead of just logging, so a failed
    /// resolution is never silent.</summary>
    Task FlagResolutionFailedAsync(Guid demandId, string reasonCode, string? errorDetail, Guid correlationId, CancellationToken ct = default);
}

public class DemandRequirementLinkService(WorkDbContext db, IPublishEndpoint publishEndpoint) : IDemandRequirementLinkService
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

        // v2 delta: a successful (re-)resolution clears NeedsAttention — this is how a
        // demand that was flagged after a failed resolve gets back to Accepted once
        // retry-resolution succeeds.
        var wasNeedsAttention = demand.Status == DemandStatus.NeedsAttention;
        if (wasNeedsAttention)
        {
            demand.Status = DemandStatus.Accepted;
            demand.AttentionReason = null;
            demand.AttentionIssuesJson = null;
            demand.AssignedTo = null;
            demand.AssignedRole = null;
            demand.LastResolutionError = null;

            db.DemandAuditEntries.Add(new DemandAuditEntry
            {
                TenantId = demand.TenantId,
                DemandId = demand.Id,
                ChangeType = "ResolutionRetrySucceeded",
                FromStatus = DemandStatus.NeedsAttention,
                ToStatus = DemandStatus.Accepted,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task FlagResolutionFailedAsync(Guid demandId, string reasonCode, string? errorDetail, Guid correlationId, CancellationToken ct = default)
    {
        var demand = await db.Demands.FirstOrDefaultAsync(d => d.Id == demandId, ct);
        if (demand is null)
        {
            return;
        }

        var fromStatus = demand.Status;
        demand.Status = DemandStatus.NeedsAttention;
        demand.AttentionReason = reasonCode;
        demand.AttentionIssuesJson = JsonSerializer.Serialize(new { reasonCode, errorDetail });
        demand.AssignedRole = DemandAttentionOwner.Dispatcher;
        demand.ResolutionAttempts++;
        demand.LastResolutionError = errorDetail;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        db.DemandAuditEntries.Add(new DemandAuditEntry
        {
            TenantId = demand.TenantId,
            DemandId = demand.Id,
            ChangeType = "FlaggedForAttention",
            FromStatus = fromStatus,
            ToStatus = DemandStatus.NeedsAttention,
            CorrelationId = correlationId.ToString(),
            Reason = reasonCode,
        });

        await db.SaveChangesAsync(ct);

        await publishEndpoint.Publish(new DemandNeedsAttentionEvent(demand.Id, demand.TenantId, reasonCode, correlationId), ct);
    }
}
