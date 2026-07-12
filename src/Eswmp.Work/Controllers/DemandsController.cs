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
            RequestedStartAtUtc = request.RequestedStartAtUtc,
            RequestedEndAtUtc = request.RequestedEndAtUtc,
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
        return demand is null ? NotFoundResult(id) : Ok(demand);
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
            query = query.Where(d => d.CreatedAt >= request.FromUtc);
        if (request.ToUtc is not null)
            query = query.Where(d => d.CreatedAt <= request.ToUtc);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<Demand>
        {
            Items = items,
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
        if (request.RequestedStartAtUtc is not null) demand.RequestedStartAtUtc = request.RequestedStartAtUtc;
        if (request.RequestedEndAtUtc is not null) demand.RequestedEndAtUtc = request.RequestedEndAtUtc;
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

        demand.Status = status == DemandValidationStatus.Invalid ? DemandStatus.Received : DemandStatus.Ready;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        return Ok(new { DemandId = demand.Id, result.Status, result.ValidatedAt, Issues = issues });
    }

    [HttpPost("{id:guid}/accept")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> Accept(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (demand.Status != DemandStatus.Ready)
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand must be Ready to accept; current status is {demand.Status}.");
        }

        demand.Status = DemandStatus.Accepted;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        await publishEndpoint.Publish(new DemandAcceptedEvent(demand.Id, demand.TenantId, Guid.NewGuid()));

        return Ok(demand);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> Reject(Guid id, RejectDemandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "reasonCode is required.",
                [new DemandValidationIssue("reasonCode", "MISSING_REASON_CODE", "Error", "reasonCode is required.")]);
        }

        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be rejected.");
        }

        demand.Status = DemandStatus.Rejected;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        await publishEndpoint.Publish(new DemandRejectedEvent(
            demand.Id, demand.TenantId, request.ReasonCode, request.Comment, Guid.NewGuid()));

        return Ok(demand);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(EswmpPermissions.DemandTransition)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var demand = await db.Demands.FindAsync(id);
        if (demand is null)
            return NotFoundResult(id);

        if (ImmutableStatuses.Contains(demand.Status))
        {
            return ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Demand is {demand.Status} and can no longer be cancelled.");
        }

        demand.Status = DemandStatus.Cancelled;
        demand.Version++;
        demand.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentModificationConflict();
        }

        await publishEndpoint.Publish(new DemandCancelledEvent(demand.Id, demand.TenantId, Guid.NewGuid()));

        return Ok(demand);
    }

    [HttpGet("{id:guid}/history")]
    [RequirePermission(EswmpPermissions.DemandRead)]
    public async Task<IActionResult> History(Guid id)
    {
        var exists = await db.Demands.AnyAsync(d => d.Id == id);
        if (!exists)
            return NotFoundResult(id);

        // No dedicated history/audit table exists yet for Eswmp.Work — deferred until
        // an audit trail need is demonstrated. Returns an empty list rather than 404/501
        // so callers can integrate against the final shape now.
        return Ok(Array.Empty<object>());
    }

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
