using System.Text.Json;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using Eswmp.Work.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Consumers;

/// <summary>
/// The Demand Adapter (api spec §8, §7.2): "DemandAcceptedEvent → Demand Adapter → Resolution
/// Contract → Work Requirement Service." Consumes the event Demand Intake already publishes
/// on accept, normalizes the accepted Demand into a resolve request, and calls the same
/// resolution path POST /work-requirements/resolve uses — so there is exactly one way a Demand
/// becomes a Work Requirement, whether triggered by the event or by a manual API call.
///
/// The mapping from Demand.DemandType to a template code is 1:1 today (DemandType IS the
/// template code) — the simplest adapter that satisfies the spec's requirement that the core
/// service "never learns which upstream produced the work" without inventing a mapping table
/// this platform has no configuration surface for yet. If a Service Request service is ever
/// built (model §7.1's open question), it gets its own adapter; this class and the core
/// resolution logic are untouched.
/// </summary>
public class DemandAcceptedConsumer(
    WorkDbContext db,
    ITenantContext tenantContext,
    IOutboxPublisher outbox,
    IDemandRequirementLinkService linkService,
    ILogger<DemandAcceptedConsumer> logger) : IConsumer<DemandAcceptedEvent>
{
    public async Task Consume(ConsumeContext<DemandAcceptedEvent> context)
    {
        var evt = context.Message;

        // No HTTP request/TenantResolutionMiddleware runs for a consumer — the tenant comes
        // from the event itself, and every query below relies on WorkDbContext's HasQueryFilter
        // reading this value.
        tenantContext.TenantId = evt.TenantId;

        var demand = await db.Demands.FirstOrDefaultAsync(d => d.Id == evt.DemandId, context.CancellationToken);
        if (demand is null)
        {
            logger.LogWarning("DemandAcceptedConsumer: demand {DemandId} not found for tenant {TenantId}.", evt.DemandId, evt.TenantId);
            return;
        }

        var templateCode = demand.DemandType;
        var template = await db.RequirementTemplates.FirstOrDefaultAsync(t => t.Code == templateCode, context.CancellationToken);
        var activeVersion = template is null ? null : await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == template.Id && v.Status == TemplateVersionStatus.Active, context.CancellationToken);
        if (template is null || activeVersion is null)
        {
            logger.LogWarning(
                "DemandAcceptedConsumer: no Active RequirementTemplate '{TemplateCode}' for demand {DemandId}; the demand stays unlinked until one is activated and re-resolved.",
                templateCode, evt.DemandId);
            // v2 delta: the failure path that didn't exist — flag rather than leave the
            // demand silently stuck Accepted with no requirement ever created.
            await linkService.FlagResolutionFailedAsync(
                demand.Id, "TEMPLATE_NOT_ACTIVE", $"No Active RequirementTemplate '{templateCode}'.", evt.CorrelationId, context.CancellationToken);
            return;
        }

        var definitions = JsonSerializer.Deserialize<RequirementSetDto>(activeVersion.DefinitionJson, RequirementResolutionService.JsonOptions);
        if (definitions is null)
        {
            logger.LogWarning("DemandAcceptedConsumer: template '{TemplateCode}' version {Version} has no requirement definitions configured.", templateCode, activeVersion.Version);
            await linkService.FlagResolutionFailedAsync(
                demand.Id, "MISSING_DEFINITIONS", $"Template '{templateCode}' version {activeVersion.Version} has no requirement definitions configured.", evt.CorrelationId, context.CancellationToken);
            return;
        }

        var wr = new WorkRequirement
        {
            TenantId = evt.TenantId,
            SourceType = "Demand",
            SourceId = demand.Id.ToString(),
            SourceVersion = demand.Version,
            TemplateId = template.Id,
            TemplateVersion = activeVersion.Version,
            WorkType = template.WorkType,
            Status = WorkRequirementStatus.Draft,
            RequirementVersion = 1,
        };
        RequirementResolutionService.ApplyDefinitions(wr, definitions, evt.TenantId);
        RequirementResolutionService.ApplyInputs(wr, BuildInputs(demand), evt.TenantId);

        var issues = RequirementValidationService.Evaluate(wr);
        if (issues.Any(i => i.Severity == "Error"))
        {
            var errorDetail = string.Join("; ", issues.Where(i => i.Severity == "Error").Select(i => i.Message));
            logger.LogWarning(
                "DemandAcceptedConsumer: resolving demand {DemandId} against template '{TemplateCode}' failed validation: {Issues}",
                evt.DemandId, templateCode, errorDetail);
            await linkService.FlagResolutionFailedAsync(demand.Id, "RESOLUTION_VALIDATION_FAILED", errorDetail, evt.CorrelationId, context.CancellationToken);
            return;
        }

        wr.Status = WorkRequirementStatus.Valid;
        db.WorkRequirements.Add(wr);
        db.RequirementVersions.Add(new RequirementVersion
        {
            TenantId = evt.TenantId,
            WorkRequirementId = wr.Id,
            Version = 1,
            ChangeType = "Initial",
            SourceVersion = wr.SourceVersion,
            TemplateVersion = wr.TemplateVersion,
            SnapshotJson = JsonSerializer.Serialize(RequirementResolutionService.ToRequirementSet(wr), RequirementResolutionService.JsonOptions),
        });

        outbox.Enqueue(db, evt.TenantId, "WorkRequirementCreated", "WorkRequirement", wr.Id,
            new WorkRequirementCreatedEvent(wr.Id, evt.TenantId, wr.SourceType, wr.SourceId, evt.CorrelationId));
        outbox.Enqueue(db, evt.TenantId, "WorkRequirementResolved", "WorkRequirement", wr.Id,
            new WorkRequirementResolvedEvent(wr.Id, evt.TenantId, wr.SourceType, wr.SourceId, wr.RequirementVersion, wr.WorkType,
                ["Eligibility", "Capacity", "Scheduling"], evt.CorrelationId));

        await db.SaveChangesAsync(context.CancellationToken);

        // The reverse pointer (api spec §7's "Note on the domain join") — via the Demand
        // module's own service, never a raw write into demand.Demands from here.
        await linkService.LinkRequirementAsync(demand.Id, wr.Id, context.CancellationToken);
    }

    /// <summary>
    /// The only Demand fields normalized into resolve inputs today: the requested time window.
    /// Demand.LocationReference is an opaque jsonb blob with no agreed shape yet (no Location
    /// service exists in this platform — see Eswmp.Work.Models.Demand), so it is deliberately
    /// not forwarded rather than guessed at.
    /// </summary>
    private static JsonElement? BuildInputs(Demand demand)
    {
        if (demand.RequestedStartAtUtc is null || demand.RequestedEndAtUtc is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(new
        {
            requestedWindow = new { start = demand.RequestedStartAtUtc, end = demand.RequestedEndAtUtc },
        }, RequirementResolutionService.JsonOptions);

        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
