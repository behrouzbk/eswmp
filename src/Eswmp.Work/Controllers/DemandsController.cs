using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eswmp.Shared.Auth;
using Eswmp.Shared.DTOs;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Eswmp.Work.Controllers;

public record CreateDemandRequest(
    Guid? OrganizationId,
    string DemandType,
    string SourceSystem,
    string? SourceChannel,
    DemandPriority? Priority,
    string? Summary,
    string? Description,
    DateTimeOffset? RequestedStartAtUtc,
    DateTimeOffset? RequestedEndAtUtc,
    string? RequestedTimezone,
    string? LocationReference,
    string ExternalReferenceType,
    string ExternalReferenceId,
    DemandFulfillmentMode? FulfillmentMode = null);

public record UpdateDemandRequest(
    int ExpectedVersion,
    DemandPriority? Priority,
    string? Summary,
    string? Description,
    DateTimeOffset? RequestedStartAtUtc,
    DateTimeOffset? RequestedEndAtUtc,
    string? RequestedTimezone,
    string? LocationReference,
    DemandFulfillmentMode? FulfillmentMode = null);

public record DemandSearchRequest(
    DemandStatus? Status,
    DemandPriority? Priority,
    string? DemandType,
    DemandFulfillmentMode? FulfillmentMode,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page = 1,
    int PageSize = 20);

public record RejectDemandRequest(string ReasonCode, string? Comment);

// ── v2 delta request records ───────────────────────────────────────────────
public record FlagAttentionRequest(string Reason, string? IssuesJson = null, DemandAttentionOwner? AssignedRole = null);
public record AssignDemandRequest(string? AssignedTo, DemandAttentionOwner? AssignedRole);
public record EscalateDemandRequest(DemandPriority Priority, string? Reason);
public record BulkDemandRequest(IReadOnlyList<Guid> Ids, string? ReasonCode = null, string? Comment = null);
public record BulkDemandItemResult(Guid Id, bool Success, string? Error);
public record SplitChildRequest(string? Summary, string? Description, DateTimeOffset? RequestedStartAtUtc, DateTimeOffset? RequestedEndAtUtc, DemandPriority? Priority);
public record SplitDemandRequest(IReadOnlyList<SplitChildRequest> Children, string? Reason);
public record MergeDemandRequest(Guid SurvivorId, IReadOnlyList<Guid> MergedIds, string? Reason);

public record DemandValidationIssue(string? Field, string Code, string Severity, string Message);

/// <summary>
/// The error shape every 4xx from this controller shares — see
/// docs/api/specs/01-demand-intake-api.md §2. Issues is omitted (not emitted as
/// null) when there's no field-level detail to report.
/// </summary>
public record ErrorEnvelope(
    string Error,
    string Code,
    string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DemandValidationIssue>? Issues = null);

[ApiController]
[Route("api/v1/demands")]
public class DemandsController(
    WorkDbContext db,
    ITenantContext tenantContext,
    IPublishEndpoint publishEndpoint) : ControllerBase
{
    private static readonly DemandStatus[] ImmutableStatuses =
        [DemandStatus.Accepted, DemandStatus.Rejected, DemandStatus.Cancelled, DemandStatus.Expired];

    /// <summary>v2 delta — narrower than ImmutableStatuses: Accepted must remain reachable by
    /// FlagAttention, since "entered from Accepted (resolution failure)" is exactly what this
    /// endpoint (and DemandRequirementLinkService.FlagResolutionFailedAsync) exists for.</summary>
    private static readonly DemandStatus[] TerminalStatuses =
        [DemandStatus.Rejected, DemandStatus.Cancelled, DemandStatus.Expired];

    /// <summary>
    /// Npgsql only accepts DateTimeOffset with Offset=0 for a `timestamp with time zone`
    /// column — every DateTimeOffset parsed from caller-supplied JSON (which may carry any
    /// offset) must be normalized before it reaches a query or an entity property.
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();

    /// <summary>
    /// v2 delta — a derived, customer-safe projection over the 8-value internal DemandStatus
    /// (UX-17: the internal enum must not leak to customer surfaces). The source doc's own
    /// example only states NeedsAttention -> NeedsAttention; the rest is an inferred, documented
    /// mapping (docs/API/specs/01-demand-intake-api.md v2 delta section spells out the full table).
    /// </summary>
    private static string DeriveExternalStatus(DemandStatus status) => status switch
    {
        DemandStatus.Received or DemandStatus.Validating => "Submitted",
        DemandStatus.Ready => "Received",
        DemandStatus.NeedsAttention => "NeedsAttention",
        DemandStatus.Accepted => "Confirmed",
        DemandStatus.Rejected or DemandStatus.Cancelled or DemandStatus.Expired => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static object WithExternalStatus(Demand demand) => new
    {
        demand.Id,
        demand.TenantId,
        demand.OrganizationId,
        demand.DemandType,
        demand.FulfillmentMode,
        demand.SourceSystem,
        demand.SourceChannel,
        demand.Status,
        ExternalStatus = DeriveExternalStatus(demand.Status),
        demand.Priority,
        demand.Summary,
        demand.Description,
        demand.RequestedStartAtUtc,
        demand.RequestedEndAtUtc,
        demand.RequestedTimezone,
        demand.LocationReference,
        demand.RequirementReferenceId,
        demand.ExternalReferenceType,
        demand.ExternalReferenceId,
        demand.Version,
        demand.AssignedTo,
        demand.AssignedRole,
        demand.AttentionReason,
        demand.AttentionIssuesJson,
        demand.ResolutionAttempts,
        demand.LastResolutionError,
        demand.RecurrenceRule,
        demand.SeriesId,
        demand.CreatedAt,
        demand.CreatedBy,
        demand.UpdatedAt,
        demand.UpdatedBy,
    };

    /// <summary>The JWT's user_id claim — best-effort actor attribution for the audit trail;
    /// null (not required) if the token doesn't carry one.</summary>
    private string? ActorId() => User?.FindFirst(EswmpClaimTypes.UserId)?.Value;

    /// <summary>v2 delta — records one DemandAuditEntry alongside the state change that caused
    /// it, in the same SaveChangesAsync. Not a separate outbox: this is a plain table row, not
    /// an event (see WorkEvents.cs's v2 delta section for the events themselves).</summary>
    private void AddAudit(Demand demand, string changeType, DemandStatus? fromStatus = null, DemandStatus? toStatus = null,
        string? reason = null, string? correlationId = null, object? before = null, object? after = null) =>
        db.DemandAuditEntries.Add(new DemandAuditEntry
        {
            TenantId = demand.TenantId,
            DemandId = demand.Id,
            ChangeType = changeType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorId = ActorId(),
            CorrelationId = correlationId,
            Reason = reason,
            BeforeSummary = before is null ? null : JsonSerializer.Serialize(before),
            AfterSummary = after is null ? null : JsonSerializer.Serialize(after),
        });

    [HttpPost]
    [RequirePermission(EswmpPermissions.DemandCreate)]
    public async Task<IActionResult> Create(
        CreateDemandRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "The Idempotency-Key header is required.");
        }

        var requestHash = HashRequest(request);
        var tenantId = tenantContext.RequiredTenantId;

        var existing = await db.DemandIdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey);

        if (existing is not null)
        {
            return existing.RequestHash != requestHash
                ? IdempotencyConflict(idempotencyKey)
                : CreatedAtAction(nameof(GetById), new { id = existing.DemandId },
                    JsonSerializer.Deserialize<Demand>(existing.ResponseBodyJson));
        }

        var demand = new Demand
        {
            TenantId = tenantId,
            OrganizationId = request.OrganizationId,
            DemandType = request.DemandType,
            FulfillmentMode = request.FulfillmentMode ?? DemandFulfillmentMode.Scheduled,
            SourceSystem = request.SourceSystem,
            SourceChannel = request.SourceChannel,
            Priority = request.Priority ?? DemandPriority.Normal,
            Summary = request.Summary,
            Description = request.Description,
            RequestedStartAtUtc = ToUtc(request.RequestedStartAtUtc),
            RequestedEndAtUtc = ToUtc(request.RequestedEndAtUtc),
            RequestedTimezone = request.RequestedTimezone,
            LocationReference = request.LocationReference,
            ExternalReferenceType = request.ExternalReferenceType,
            ExternalReferenceId = request.ExternalReferenceId,
        };

        // Same rule set POST /{id}/validate runs (docs/api/specs/01-demand-intake-api.md §6) —
        // a request that would be structurally Invalid is rejected up front rather than accepted
        // and left for the caller to discover via a later validate call.
        var issues = Evaluate(demand);
        if (issues.Any(i => i.Severity == "Error"))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "The request failed validation.", issues);
        }

        db.Demands.Add(demand);
        AddAudit(demand, "Created", toStatus: demand.Status);
        db.DemandIdempotencyRecords.Add(new DemandIdempotencyRecord
        {
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            DemandId = demand.Id,
            ResponseBodyJson = JsonSerializer.Serialize(demand),
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost the race for this idempotency key to a concurrent request that committed
            // first (spec §10.3) — replay the winner's response instead of surfacing the
            // constraint violation.
            var winner = await db.DemandIdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey);

            if (winner is null)
            {
                throw;
            }

            return winner.RequestHash != requestHash
                ? IdempotencyConflict(idempotencyKey)
                : CreatedAtAction(nameof(GetById), new { id = winner.DemandId },
                    JsonSerializer.Deserialize<Demand>(winner.ResponseBodyJson));
        }

        return CreatedAtAction(nameof(GetById), new { id = demand.Id }, demand);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        return demand is null ? NotFoundResult(id) : Ok(WithExternalStatus(demand));
    }

    [HttpPost("search")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> Search(DemandSearchRequest request)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var query = db.Demands.AsQueryable();

        if (request.Status is not null)
            query = query.Where(d => d.Status == request.Status);
        if (request.Priority is not null)
            query = query.Where(d => d.Priority == request.Priority);
        if (!string.IsNullOrWhiteSpace(request.DemandType))
            query = query.Where(d => d.DemandType == request.DemandType);
        if (request.FulfillmentMode is not null)
            query = query.Where(d => d.FulfillmentMode == request.FulfillmentMode);
        if (request.FromUtc is not null)
            query = query.Where(d => d.CreatedAt >= ToUtc(request.FromUtc));
        if (request.ToUtc is not null)
            query = query.Where(d => d.CreatedAt <= ToUtc(request.ToUtc));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<object>
        {
            Items = items.Select(WithExternalStatus).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    [HttpPatch("{id:guid}")]
    [RequirePermission(EswmpPermissions.DemandCreate)]
    public async Task<IActionResult> Update(Guid id, UpdateDemandRequest request)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (request.ExpectedVersion != demand.Version)
        {
            return VersionConflict(request.ExpectedVersion, demand.Version);
        }

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be modified.");
        }

        if (demand.Status == DemandStatus.Ready)
        {
            // Ready is restricted: only Priority may still change once a demand has
            // been validated — everything else requires re-validation from Received.
            var attemptsRestrictedField =
                request.Summary is not null ||
                request.Description is not null ||
                request.RequestedStartAtUtc is not null ||
                request.RequestedEndAtUtc is not null ||
                request.RequestedTimezone is not null ||
                request.LocationReference is not null ||
                request.FulfillmentMode is not null;

            if (attemptsRestrictedField)
            {
                return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", "Demand is Ready; only Priority may be updated without re-validating.");
            }
        }

        if (request.Priority is not null) demand.Priority = request.Priority.Value;
        if (request.Summary is not null) demand.Summary = request.Summary;
        if (request.Description is not null) demand.Description = request.Description;
        if (request.RequestedStartAtUtc is not null) demand.RequestedStartAtUtc = ToUtc(request.RequestedStartAtUtc);
        if (request.RequestedEndAtUtc is not null) demand.RequestedEndAtUtc = ToUtc(request.RequestedEndAtUtc);
        if (request.RequestedTimezone is not null) demand.RequestedTimezone = request.RequestedTimezone;
        if (request.LocationReference is not null) demand.LocationReference = request.LocationReference;
        if (request.FulfillmentMode is not null) demand.FulfillmentMode = request.FulfillmentMode.Value;

        // Mirrors CK_Demands_TimeWindow — the one structural rule a PATCH can actually violate,
        // since it's the only pair of fields it's allowed to change together.
        if (demand.RequestedStartAtUtc is not null && demand.RequestedEndAtUtc is not null &&
            demand.RequestedStartAtUtc >= demand.RequestedEndAtUtc)
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "The request failed validation.",
                [new DemandValidationIssue("requestedEndAtUtc", "INVALID_TIME_WINDOW", "Error", "RequestedStartAtUtc must be before RequestedEndAtUtc.")]);
        }

        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersionConflict(request.ExpectedVersion, null);
        }

        return Ok(demand);
    }

    [HttpPost("{id:guid}/validate")]
    [RequirePermission(EswmpPermissions.DemandCreate)]
    public async Task<IActionResult> Validate(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be validated.");
        }

        var issues = Evaluate(demand);
        var hasErrors = issues.Any(i => i.Severity == "Error");
        var hasWarnings = issues.Any(i => i.Severity == "Warning");

        var status = hasErrors
            ? DemandValidationStatus.Invalid
            : hasWarnings
                ? DemandValidationStatus.ValidWithWarnings
                : DemandValidationStatus.Valid;

        var result = new DemandValidationResult
        {
            TenantId = demand.TenantId,
            DemandId = demand.Id,
            Status = status,
            IssuesJson = JsonSerializer.Serialize(issues),
        };

        db.DemandValidationResults.Add(result);

        var fromStatus = demand.Status;
        if (status == DemandValidationStatus.Invalid)
        {
            // v2 delta (UX-09/UX-14): a demand that fails validation now surfaces as
            // NeedsAttention instead of silently sitting in Received.
            demand.Status = DemandStatus.NeedsAttention;
            demand.AttentionReason = "VALIDATION_FAILED";
            demand.AttentionIssuesJson = JsonSerializer.Serialize(issues);
            demand.AssignedRole = DemandAttentionOwner.Dispatcher;
        }
        else
        {
            demand.Status = DemandStatus.Ready;
        }
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        AddAudit(demand, "Validated", fromStatus, demand.Status,
            reason: status == DemandValidationStatus.Invalid ? "VALIDATION_FAILED" : null);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        if (status == DemandValidationStatus.Invalid)
        {
            await publishEndpoint.Publish(new DemandNeedsAttentionEvent(demand.Id, demand.TenantId, "VALIDATION_FAILED", Guid.NewGuid()));
        }

        return Ok(new { DemandId = demand.Id, result.Status, result.ValidatedAt, Issues = issues });
    }

    [HttpPost("{id:guid}/accept")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> Accept(Guid id)
    {
        var (statusCode, code, message, demand) = await AcceptCore(id);
        return demand is not null ? Ok(demand) : ErrorResult(statusCode, code!, message!);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> Reject(Guid id, RejectDemandRequest request)
    {
        var (statusCode, code, message, demand) = await RejectCore(id, request.ReasonCode, request.Comment);
        if (demand is not null)
        {
            return Ok(demand);
        }

        return statusCode == StatusCodes.Status400BadRequest
            ? ErrorResult(statusCode, code!, message!, [new DemandValidationIssue("reasonCode", "MISSING_REASON_CODE", "Error", message!)])
            : ErrorResult(statusCode, code!, message!);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var (statusCode, code, message, demand) = await CancelCore(id);
        return demand is not null ? Ok(demand) : ErrorResult(statusCode, code!, message!);
    }

    /// <summary>v2 delta (P3) — accept many; per-item results, one failure doesn't abort the batch.</summary>
    [HttpPost("bulk/accept")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> BulkAccept(BulkDemandRequest request)
    {
        var results = new List<BulkDemandItemResult>();
        foreach (var id in request.Ids)
        {
            var (_, _, message, demand) = await AcceptCore(id);
            results.Add(new BulkDemandItemResult(id, demand is not null, message));
        }
        return Ok(new { Results = results });
    }

    /// <summary>v2 delta (P3) — reject many with a shared reasonCode/comment.</summary>
    [HttpPost("bulk/reject")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> BulkReject(BulkDemandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "reasonCode is required.",
                [new DemandValidationIssue("reasonCode", "MISSING_REASON_CODE", "Error", "reasonCode is required.")]);
        }

        var results = new List<BulkDemandItemResult>();
        foreach (var id in request.Ids)
        {
            var (_, _, message, demand) = await RejectCore(id, request.ReasonCode, request.Comment);
            results.Add(new BulkDemandItemResult(id, demand is not null, message));
        }
        return Ok(new { Results = results });
    }

    /// <summary>v2 delta (P3) — cancel many; per-item results.</summary>
    [HttpPost("bulk/cancel")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> BulkCancel(BulkDemandRequest request)
    {
        var results = new List<BulkDemandItemResult>();
        foreach (var id in request.Ids)
        {
            var (_, _, message, demand) = await CancelCore(id);
            results.Add(new BulkDemandItemResult(id, demand is not null, message));
        }
        return Ok(new { Results = results });
    }

    /// <summary>v2 delta (P3) — counts by status, fulfillment mode, priority, and age band.
    /// Tenant-scoped automatically via Demand's HasQueryFilter.</summary>
    [HttpGet("metrics")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> Metrics()
    {
        var byStatus = await db.Demands.GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var byMode = await db.Demands.GroupBy(d => d.FulfillmentMode)
            .Select(g => new { FulfillmentMode = g.Key, Count = g.Count() }).ToListAsync();
        var byPriority = await db.Demands.GroupBy(d => d.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() }).ToListAsync();

        // Age bands computed in memory after pulling just CreatedAt — a bucketed CASE
        // expression isn't reliably portable to translate across EF providers here.
        var now = DateTimeOffset.UtcNow;
        var createdTimestamps = await db.Demands.Select(d => d.CreatedAt).ToListAsync();
        var ageBands = new
        {
            UnderOneHour = createdTimestamps.Count(t => now - t < TimeSpan.FromHours(1)),
            OneHourToOneDay = createdTimestamps.Count(t => now - t >= TimeSpan.FromHours(1) && now - t < TimeSpan.FromDays(1)),
            OneDayToOneWeek = createdTimestamps.Count(t => now - t >= TimeSpan.FromDays(1) && now - t < TimeSpan.FromDays(7)),
            OverOneWeek = createdTimestamps.Count(t => now - t >= TimeSpan.FromDays(7)),
        };

        return Ok(new { ByStatus = byStatus, ByFulfillmentMode = byMode, ByPriority = byPriority, AgeBands = ageBands });
    }

    private async Task<(int StatusCode, string? Code, string? Message, Demand? Demand)> AcceptCore(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
        {
            return (StatusCodes.Status404NotFound, "NOT_FOUND", $"No demand '{id}' was found.", null);
        }

        if (demand.Status != DemandStatus.Ready)
        {
            return (StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand must be Ready to accept; current status is {demand.Status}.", null);
        }

        var fromStatus = demand.Status;
        demand.Status = DemandStatus.Accepted;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "Accepted", fromStatus, demand.Status);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (StatusCodes.Status409Conflict, "STATUS_CONFLICT", "The demand was modified concurrently; reload and retry.", null);
        }

        await publishEndpoint.Publish(new DemandAcceptedEvent(demand.Id, demand.TenantId, Guid.NewGuid()));
        return (StatusCodes.Status200OK, null, null, demand);
    }

    private async Task<(int StatusCode, string? Code, string? Message, Demand? Demand)> RejectCore(Guid id, string reasonCode, string? comment)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return (StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "reasonCode is required.", null);
        }

        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
        {
            return (StatusCodes.Status404NotFound, "NOT_FOUND", $"No demand '{id}' was found.", null);
        }

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return (StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be rejected.", null);
        }

        var fromStatus = demand.Status;
        demand.Status = DemandStatus.Rejected;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "Rejected", fromStatus, demand.Status, reason: reasonCode);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (StatusCodes.Status409Conflict, "STATUS_CONFLICT", "The demand was modified concurrently; reload and retry.", null);
        }

        await publishEndpoint.Publish(new DemandRejectedEvent(demand.Id, demand.TenantId, reasonCode, comment, Guid.NewGuid()));
        return (StatusCodes.Status200OK, null, null, demand);
    }

    private async Task<(int StatusCode, string? Code, string? Message, Demand? Demand)> CancelCore(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
        {
            return (StatusCodes.Status404NotFound, "NOT_FOUND", $"No demand '{id}' was found.", null);
        }

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return (StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be cancelled.", null);
        }

        var fromStatus = demand.Status;
        demand.Status = DemandStatus.Cancelled;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "Cancelled", fromStatus, demand.Status);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (StatusCodes.Status409Conflict, "STATUS_CONFLICT", "The demand was modified concurrently; reload and retry.", null);
        }

        await publishEndpoint.Publish(new DemandCancelledEvent(demand.Id, demand.TenantId, Guid.NewGuid()));
        return (StatusCodes.Status200OK, null, null, demand);
    }

    /// <summary>v2 delta (UX-10) — move a demand to NeedsAttention with a reason. Called by a
    /// human/dispatcher; the resolution-failure path calls DemandRequirementLinkService
    /// directly instead (CLAUDE.md rule 11), not this HTTP endpoint.</summary>
    [HttpPost("{id:guid}/flag-attention")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> FlagAttention(Guid id, FlagAttentionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "reason is required.",
                [new DemandValidationIssue("reason", "MISSING_REASON", "Error", "reason is required.")]);
        }

        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (TerminalStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be flagged.");
        }

        var fromStatus = demand.Status;
        demand.Status = DemandStatus.NeedsAttention;
        demand.AttentionReason = request.Reason;
        demand.AttentionIssuesJson = request.IssuesJson;
        demand.AssignedRole = request.AssignedRole ?? DemandAttentionOwner.Dispatcher;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "FlaggedForAttention", fromStatus, demand.Status, reason: request.Reason);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        await publishEndpoint.Publish(new DemandNeedsAttentionEvent(demand.Id, demand.TenantId, request.Reason, Guid.NewGuid()));

        return Ok(demand);
    }

    /// <summary>v2 delta (UX-14) — re-emits DemandAcceptedEvent so DemandAcceptedConsumer
    /// re-attempts resolution. Bounded by ResolutionAttempts but not capped — see the open
    /// D-02 product decision in v2-delta-summary.docx, tracked but not enforced here.</summary>
    [HttpPost("{id:guid}/retry-resolution")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> RetryResolution(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (demand.Status != DemandStatus.NeedsAttention)
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand must be NeedsAttention to retry resolution; current status is {demand.Status}.");
        }

        demand.ResolutionAttempts++;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "ResolutionRetryRequested", demand.Status, demand.Status, reason: demand.AttentionReason);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        // Same event DemandAcceptedConsumer already handles — resolution retries
        // asynchronously; a successful retry clears NeedsAttention via
        // DemandRequirementLinkService.LinkRequirementAsync.
        await publishEndpoint.Publish(new DemandAcceptedEvent(demand.Id, demand.TenantId, Guid.NewGuid()));

        return Ok(demand);
    }

    /// <summary>v2 delta (UX-10) — triage ownership, independent of NeedsAttention.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequirePermission(EswmpPermissions.DemandAssign)]
    public async Task<IActionResult> Assign(Guid id, AssignDemandRequest request)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be assigned.");
        }

        var before = new { demand.AssignedTo, demand.AssignedRole };
        if (request.AssignedTo is not null) demand.AssignedTo = request.AssignedTo;
        if (request.AssignedRole is not null) demand.AssignedRole = request.AssignedRole;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "Assigned", before: before, after: new { demand.AssignedTo, demand.AssignedRole });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        await publishEndpoint.Publish(new DemandAssignedEvent(demand.Id, demand.TenantId, demand.AssignedTo, demand.AssignedRole?.ToString(), Guid.NewGuid()));

        return Ok(demand);
    }

    /// <summary>v2 delta — raise priority with a reason. Escalation only raises; use PATCH to
    /// lower priority.</summary>
    [HttpPost("{id:guid}/escalate")]
    [RequirePermission(EswmpPermissions.DemandEscalate)]
    public async Task<IActionResult> Escalate(Guid id, EscalateDemandRequest request)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be escalated.");
        }

        if (request.Priority <= demand.Priority)
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", $"Escalation must raise priority above the current {demand.Priority}.",
                [new DemandValidationIssue("priority", "ESCALATION_MUST_RAISE_PRIORITY", "Error", $"Escalation must raise priority above the current {demand.Priority}.")]);
        }

        var fromPriority = demand.Priority;
        demand.Priority = request.Priority;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(demand, "Escalated", reason: request.Reason, before: new { Priority = fromPriority }, after: new { Priority = request.Priority });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        await publishEndpoint.Publish(new DemandEscalatedEvent(demand.Id, demand.TenantId, fromPriority.ToString(), request.Priority.ToString(), request.Reason, Guid.NewGuid()));

        return Ok(demand);
    }

    /// <summary>v2 delta — was always `[]` (no audit table existed). Now returns the real
    /// DemandAuditEntries trail; same shape as the new GET /{id}/audit.</summary>
    [HttpGet("{id:guid}/history")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> History(Guid id)
    {
        var exists = await db.Demands.AnyAsync(d => d.Id == id);
        if (!exists)
            return NotFoundResult(id);

        return Ok(await LoadAuditEntries(id));
    }

    /// <summary>v2 delta (P3, new) — the real audit trail, same shape as {id}/history above
    /// (kept as a distinct route since it's the name the reviewed UX doc calls it by).</summary>
    [HttpGet("{id:guid}/audit")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> Audit(Guid id)
    {
        var exists = await db.Demands.AnyAsync(d => d.Id == id);
        if (!exists)
            return NotFoundResult(id);

        return Ok(new { DemandId = id, Entries = await LoadAuditEntries(id) });
    }

    /// <summary>v2 delta (P5, new) — customer-scoped history by external reference. Lighter than
    /// {id}/audit: only customer-safe fields, no internal attention/audit detail, no permission
    /// to look up an arbitrary internal id — the caller must already know their own external
    /// reference pair.</summary>
    [HttpGet("history")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> HistoryByExternalReference([FromQuery] string externalReferenceType, [FromQuery] string externalReferenceId)
    {
        if (string.IsNullOrWhiteSpace(externalReferenceType) || string.IsNullOrWhiteSpace(externalReferenceId))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "externalReferenceType and externalReferenceId are required.");
        }

        var demands = await db.Demands
            .Where(d => d.ExternalReferenceType == externalReferenceType && d.ExternalReferenceId == externalReferenceId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Ok(demands.Select(d => new
        {
            d.Id,
            ExternalStatus = DeriveExternalStatus(d.Status),
            d.Summary,
            d.CreatedAt,
            d.UpdatedAt,
        }));
    }

    /// <summary>v2 delta (P3) — children are fresh Demand rows (new Id, Status = Received)
    /// cloning every parent field not overridden by the request, each linked back via a
    /// DemandLineage row. The parent is NOT auto-cancelled — a dispatcher cancels it
    /// separately if desired; this is a deliberate simplification, not a spec requirement.</summary>
    [HttpPost("{id:guid}/split")]
    [RequirePermission(EswmpPermissions.DemandSplit)]
    public async Task<IActionResult> Split(Guid id, SplitDemandRequest request)
    {
        if (request.Children.Count == 0)
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "At least one child is required.",
                [new DemandValidationIssue("children", "MISSING_CHILDREN", "Error", "At least one child is required.")]);
        }

        var parent = await db.Demands.FindAsync(id);
        if (parent is null)
            return NotFoundResult(id);

        if (ImmutableStatuses.Contains(parent.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {parent.Status} and can no longer be split.");
        }

        var children = new List<Demand>();
        foreach (var childSpec in request.Children)
        {
            var child = new Demand
            {
                TenantId = parent.TenantId,
                OrganizationId = parent.OrganizationId,
                DemandType = parent.DemandType,
                FulfillmentMode = parent.FulfillmentMode,
                SourceSystem = parent.SourceSystem,
                SourceChannel = parent.SourceChannel,
                Priority = childSpec.Priority ?? parent.Priority,
                Summary = childSpec.Summary ?? parent.Summary,
                Description = childSpec.Description ?? parent.Description,
                RequestedStartAtUtc = ToUtc(childSpec.RequestedStartAtUtc) ?? parent.RequestedStartAtUtc,
                RequestedEndAtUtc = ToUtc(childSpec.RequestedEndAtUtc) ?? parent.RequestedEndAtUtc,
                RequestedTimezone = parent.RequestedTimezone,
                LocationReference = parent.LocationReference,
                ExternalReferenceType = parent.ExternalReferenceType,
                ExternalReferenceId = parent.ExternalReferenceId,
            };
            db.Demands.Add(child);
            children.Add(child);
            AddAudit(child, "CreatedViaSplit", toStatus: child.Status, reason: request.Reason);
            db.DemandLineages.Add(new DemandLineage
            {
                TenantId = parent.TenantId,
                DemandId = child.Id,
                RelatedId = parent.Id,
                Relation = DemandLineageRelation.SplitFrom,
                ActorId = ActorId(),
                Reason = request.Reason,
            });
        }

        AddAudit(parent, "Split", parent.Status, parent.Status, reason: request.Reason,
            after: new { ChildIds = children.Select(c => c.Id) });

        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new DemandSplitEvent(parent.Id, parent.TenantId, children.Select(c => c.Id).ToList(), Guid.NewGuid()));

        return StatusCode(StatusCodes.Status201Created, new { ParentId = parent.Id, Children = children });
    }

    /// <summary>v2 delta (P3) — each mergedId -> Cancelled (reuses the existing terminal state)
    /// plus a DemandLineage(MergedInto) row. Per-item results, same as the bulk endpoints.</summary>
    [HttpPost("merge")]
    [RequirePermission(EswmpPermissions.DemandMerge)]
    public async Task<IActionResult> Merge(MergeDemandRequest request)
    {
        var survivor = await db.Demands.FindAsync(request.SurvivorId);
        if (survivor is null)
            return NotFoundResult(request.SurvivorId);

        if (ImmutableStatuses.Contains(survivor.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Survivor demand is {survivor.Status} and cannot receive a merge.");
        }

        var results = new List<BulkDemandItemResult>();
        foreach (var mergedId in request.MergedIds)
        {
            if (mergedId == request.SurvivorId)
            {
                results.Add(new BulkDemandItemResult(mergedId, false, "A demand cannot be merged into itself."));
                continue;
            }

            var merged = await db.Demands.FindAsync(mergedId);
            if (merged is null)
            {
                results.Add(new BulkDemandItemResult(mergedId, false, $"No demand '{mergedId}' was found."));
                continue;
            }

            if (ImmutableStatuses.Contains(merged.Status))
            {
                results.Add(new BulkDemandItemResult(mergedId, false, $"Demand is {merged.Status} and can no longer be merged."));
                continue;
            }

            var fromStatus = merged.Status;
            merged.Status = DemandStatus.Cancelled;
            merged.Version++;
            merged.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(merged, "Merged", fromStatus, merged.Status, reason: request.Reason);
            db.DemandLineages.Add(new DemandLineage
            {
                TenantId = merged.TenantId,
                DemandId = merged.Id,
                RelatedId = survivor.Id,
                Relation = DemandLineageRelation.MergedInto,
                ActorId = ActorId(),
                Reason = request.Reason,
            });

            try
            {
                await db.SaveChangesAsync();
                results.Add(new BulkDemandItemResult(mergedId, true, null));
            }
            catch (DbUpdateConcurrencyException)
            {
                results.Add(new BulkDemandItemResult(mergedId, false, "The demand was modified concurrently; reload and retry."));
            }
        }

        var mergedOk = results.Where(r => r.Success).Select(r => r.Id).ToList();
        if (mergedOk.Count > 0)
        {
            await publishEndpoint.Publish(new DemandMergedEvent(survivor.Id, survivor.TenantId, mergedOk, Guid.NewGuid()));
        }

        return Ok(new { SurvivorId = survivor.Id, Results = results });
    }

    private async Task<List<object>> LoadAuditEntries(Guid demandId) =>
        await db.DemandAuditEntries
            .Where(a => a.DemandId == demandId)
            .OrderBy(a => a.OccurredAt)
            .Select(a => (object)new
            {
                a.ChangeType,
                a.FromStatus,
                a.ToStatus,
                a.ActorId,
                a.ActorRole,
                a.CorrelationId,
                a.Reason,
                a.OccurredAt,
            })
            .ToListAsync();

    /// <summary>
    /// The validation rule set — run by POST /validate, and also on create/patch, per
    /// docs/api/specs/01-demand-intake-api.md §6. Any Error keeps/returns the demand to
    /// Received; warnings-only allows Ready.
    /// </summary>
    private static List<DemandValidationIssue> Evaluate(Demand demand)
    {
        var issues = new List<DemandValidationIssue>();

        if (string.IsNullOrWhiteSpace(demand.ExternalReferenceType) || string.IsNullOrWhiteSpace(demand.ExternalReferenceId))
        {
            issues.Add(new DemandValidationIssue("externalReferenceType", "MISSING_EXTERNAL_REFERENCE", "Error", "ExternalReferenceType/ExternalReferenceId are required."));
        }

        if (demand.RequestedStartAtUtc is not null && demand.RequestedEndAtUtc is not null &&
            demand.RequestedStartAtUtc >= demand.RequestedEndAtUtc)
        {
            issues.Add(new DemandValidationIssue("requestedEndAtUtc", "INVALID_TIME_WINDOW", "Error", "RequestedStartAtUtc must be before RequestedEndAtUtc."));
        }

        if (string.IsNullOrWhiteSpace(demand.Summary) && string.IsNullOrWhiteSpace(demand.Description))
        {
            issues.Add(new DemandValidationIssue("description", "MISSING_DESCRIPTION", "Warning", "Neither Summary nor Description was provided."));
        }

        // The two MODE_ rules make the fulfillmentMode axis enforceable: a Scheduled demand
        // must carry a requested window; an OnDemand demand carrying a future one is unusual
        // but not blocking.
        if (demand.FulfillmentMode == DemandFulfillmentMode.Scheduled &&
            (demand.RequestedStartAtUtc is null || demand.RequestedEndAtUtc is null))
        {
            issues.Add(new DemandValidationIssue("requestedStartAtUtc", "MODE_WINDOW_REQUIRED", "Error", "A Scheduled demand requires a requested start/end window."));
        }

        if (demand.FulfillmentMode == DemandFulfillmentMode.OnDemand &&
            demand.RequestedStartAtUtc is not null && demand.RequestedStartAtUtc > DateTimeOffset.UtcNow)
        {
            issues.Add(new DemandValidationIssue("requestedStartAtUtc", "MODE_WINDOW_UNEXPECTED", "Warning", "An OnDemand demand should not carry a future requested window."));
        }

        return issues;
    }

    private static string HashRequest(CreateDemandRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    private ObjectResult ErrorResult(int statusCode, string code, string message, IReadOnlyList<DemandValidationIssue>? issues = null) =>
        StatusCode(statusCode, new ErrorEnvelope(message, code, HttpContext?.TraceIdentifier ?? "", issues));

    private ObjectResult NotFoundResult(Guid id) =>
        ErrorResult(StatusCodes.Status404NotFound, "NOT_FOUND", $"No demand '{id}' was found.");

    private ObjectResult IdempotencyConflict(string idempotencyKey) =>
        ErrorResult(StatusCodes.Status409Conflict, "IDEMPOTENCY_CONFLICT",
            $"Idempotency-Key '{idempotencyKey}' was already used with a different request body.");

    private ObjectResult VersionConflict(int expectedVersion, int? currentVersion) =>
        ErrorResult(StatusCodes.Status412PreconditionFailed, "VERSION_CONFLICT",
            currentVersion is null
                ? $"Expected version {expectedVersion} no longer matches the demand's current version."
                : $"Expected version {expectedVersion} does not match current version {currentVersion}.");

    private ObjectResult ConcurrentModificationConflict() =>
        ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", "The demand was modified concurrently; reload and retry.");
}
