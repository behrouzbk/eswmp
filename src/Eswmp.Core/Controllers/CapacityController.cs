using System.Collections.Concurrent;
using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Controllers;

public record CreateCapacityProfileRequest(Guid ResourceId, string Name, string Timezone);

public record CreateCapacityDefinitionRequest(
    string Name,
    CapacityModel CapacityModel,
    string DimensionCode,
    int MaximumQuantity,
    CapacityUnit Unit,
    CapacityTimeBasis TimeBasis,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public record UpdateCapacityDefinitionRequest(
    string? Name,
    int? MaximumQuantity,
    CapacityStatus? Status,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public record ResolveCapacityRequest(
    Guid ResourceId,
    string DimensionCode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int RequiredQuantity);

public record ResolveCapacityResponse(
    int EffectiveCapacity,
    int ConsumedCapacity,
    int HeldCapacity,
    int RemainingCapacity,
    bool CanFulfil);

public record ExplainCapacityResponse(
    int DefinedCapacity,
    int EffectiveCapacity,
    int ActiveHolds,
    int ConfirmedConsumption,
    int RemainingCapacity);

public record CreateCapacityHoldRequest(
    Guid ResourceId,
    string DimensionCode,
    int Quantity,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int HoldDurationSeconds,
    string? SourceType,
    string? SourceId,
    string IdempotencyKey);

/// <summary>
/// Generic capacity ledger for Resources — how much of a given dimension
/// (concurrent jobs, daily job count, cargo weight, ...) is defined, held, and
/// consumed for a time window. Deliberately separate from Reservation/Appointment:
/// a Resource can be fully booked calendar-wise yet still have spare capacity on a
/// dimension (e.g. a van with two bays), or vice versa.
/// </summary>
[ApiController]
[Route("api/v1/capacity")]
public class CapacityController(CoreDbContext db, ITenantContext tenantContext) : ControllerBase
{
    // Per-CapacityDefinitionId in-process lock serializing hold creation so the
    // check-then-insert below cannot oversell within a single service instance.
    // Combined with the transactional re-check, this is sufficient for the
    // single-instance topology this platform runs today; a multi-instance
    // deployment would additionally want a Postgres advisory lock
    // (pg_advisory_xact_lock) inside the transaction below.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> DefinitionLocks = new();

    private static SemaphoreSlim LockFor(Guid definitionId) =>
        DefinitionLocks.GetOrAdd(definitionId, _ => new SemaphoreSlim(1, 1));

    [HttpPost("profiles")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> CreateProfile(CreateCapacityProfileRequest request)
    {
        var profile = new CapacityProfile
        {
            TenantId = tenantContext.RequiredTenantId,
            ResourceId = request.ResourceId,
            Name = request.Name,
            Timezone = request.Timezone,
            Status = CapacityStatus.Draft,
        };

        db.CapacityProfiles.Add(profile);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProfile), new { id = profile.Id }, profile);
    }

    [HttpGet("profiles/{id:guid}")]
    [RequirePermission(EswmpPermissions.CapacityRead)]
    public async Task<IActionResult> GetProfile(Guid id)
    {
        var profile = await db.CapacityProfiles.FindAsync(id);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPost("profiles/{id:guid}/definitions")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> CreateDefinition(Guid id, CreateCapacityDefinitionRequest request)
    {
        var profile = await db.CapacityProfiles.FindAsync(id);
        if (profile is null)
            return NotFound();

        var definition = new CapacityDefinition
        {
            TenantId = tenantContext.RequiredTenantId,
            CapacityProfileId = id,
            Name = request.Name,
            CapacityModel = request.CapacityModel,
            DimensionCode = request.DimensionCode,
            MaximumQuantity = request.MaximumQuantity,
            Unit = request.Unit,
            TimeBasis = request.TimeBasis,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Status = CapacityStatus.Active,
        };

        db.CapacityDefinitions.Add(definition);
        WriteLedger(definition.Id, profile.ResourceId, CapacityLedgerEntryType.CapacityDefined, definition.DimensionCode, definition.MaximumQuantity);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(CreateDefinition), new { id = definition.Id }, definition);
    }

    [HttpPatch("definitions/{id:guid}")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> UpdateDefinition(Guid id, UpdateCapacityDefinitionRequest request)
    {
        var definition = await db.CapacityDefinitions.FindAsync(id);
        if (definition is null)
            return NotFound();

        if (request.Name is not null)
            definition.Name = request.Name;
        if (request.MaximumQuantity is not null)
            definition.MaximumQuantity = request.MaximumQuantity.Value;
        if (request.Status is not null)
            definition.Status = request.Status.Value;
        if (request.EffectiveFrom is not null)
            definition.EffectiveFrom = request.EffectiveFrom;
        if (request.EffectiveTo is not null)
            definition.EffectiveTo = request.EffectiveTo;

        definition.Version++;

        WriteLedger(definition.Id, null, CapacityLedgerEntryType.CapacityChanged, definition.DimensionCode, definition.MaximumQuantity);
        await db.SaveChangesAsync();

        return Ok(definition);
    }

    [HttpPost("resolve")]
    [RequirePermission(EswmpPermissions.CapacityRead)]
    public async Task<ActionResult<ResolveCapacityResponse>> Resolve(ResolveCapacityRequest request)
    {
        var definition = await FindActiveDefinitionAsync(request.ResourceId, request.DimensionCode);
        if (definition is null)
            return NotFound(new { error = $"No active capacity definition for resource {request.ResourceId} / dimension {request.DimensionCode}." });

        var (effective, held, consumed) = await ComputeCapacityAsync(definition, request.StartTime, request.EndTime);
        var remaining = effective - held - consumed;

        return Ok(new ResolveCapacityResponse(effective, consumed, held, remaining, remaining >= request.RequiredQuantity));
    }

    [HttpPost("explain")]
    [RequirePermission(EswmpPermissions.CapacityRead)]
    public async Task<ActionResult<ExplainCapacityResponse>> Explain(ResolveCapacityRequest request)
    {
        var definition = await FindActiveDefinitionAsync(request.ResourceId, request.DimensionCode);
        if (definition is null)
            return NotFound(new { error = $"No active capacity definition for resource {request.ResourceId} / dimension {request.DimensionCode}." });

        var (effective, held, consumed) = await ComputeCapacityAsync(definition, request.StartTime, request.EndTime);

        return Ok(new ExplainCapacityResponse(definition.MaximumQuantity, effective, held, consumed, effective - held - consumed));
    }

    [HttpPost("holds")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> CreateHold(CreateCapacityHoldRequest request)
    {
        var existing = await db.CapacityHolds
            .FirstOrDefaultAsync(h => h.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
            return Ok(existing);

        var definition = await FindActiveDefinitionAsync(request.ResourceId, request.DimensionCode);
        if (definition is null)
            return NotFound(new { error = $"No active capacity definition for resource {request.ResourceId} / dimension {request.DimensionCode}." });

        var definitionLock = LockFor(definition.Id);
        await definitionLock.WaitAsync();
        try
        {
            // NpgsqlRetryingExecutionStrategy (EnableRetryOnFailure) refuses to run inside a
            // manually-created transaction unless the whole attempt — including the BeginTransactionAsync
            // itself — is retried as one unit via CreateExecutionStrategy().ExecuteAsync; confirmed live
            // 2026-07-19 (InvalidOperationException: "does not support user-initiated transactions").
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();

                // Re-fetch the definition and re-run the capacity computation inside the
                // lock/transaction so a concurrent hold committed between the check above
                // and now is accounted for — this is the "check-then-insert" atomicity guard.
                var (effective, held, consumed) = await ComputeCapacityAsync(definition, request.StartTime, request.EndTime);
                var remaining = effective - held - consumed;

                if (remaining < request.Quantity)
                {
                    return Conflict(new
                    {
                        error = $"Requested quantity {request.Quantity} exceeds remaining capacity {remaining} for dimension {request.DimensionCode}.",
                    });
                }

                var hold = new CapacityHold
                {
                    TenantId = tenantContext.RequiredTenantId,
                    CapacityDefinitionId = definition.Id,
                    ResourceId = request.ResourceId,
                    DimensionCode = request.DimensionCode,
                    Quantity = request.Quantity,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    Status = CapacityHoldStatus.Active,
                    ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(request.HoldDurationSeconds <= 0 ? 60 : request.HoldDurationSeconds),
                    IdempotencyKey = request.IdempotencyKey,
                    SourceType = request.SourceType,
                    SourceId = request.SourceId,
                };

                db.CapacityHolds.Add(hold);
                WriteLedger(definition.Id, request.ResourceId, CapacityLedgerEntryType.HoldAcquired, request.DimensionCode, request.Quantity);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetHold), new { id = hold.Id }, hold);
            });
        }
        finally
        {
            definitionLock.Release();
        }
    }

    [HttpGet("holds/{id:guid}")]
    [RequirePermission(EswmpPermissions.CapacityRead)]
    public async Task<IActionResult> GetHold(Guid id)
    {
        var hold = await db.CapacityHolds.FindAsync(id);
        return hold is null ? NotFound() : Ok(hold);
    }

    [HttpPost("holds/{id:guid}/commit")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> CommitHold(Guid id)
    {
        var hold = await db.CapacityHolds.FindAsync(id);
        if (hold is null)
            return NotFound();

        if (hold.Status != CapacityHoldStatus.Active)
            return Conflict(new { error = $"Hold is {hold.Status}, cannot commit." });

        hold.Status = CapacityHoldStatus.Committed;

        var consumption = new CapacityConsumption
        {
            TenantId = hold.TenantId,
            CapacityDefinitionId = hold.CapacityDefinitionId,
            ResourceId = hold.ResourceId,
            DimensionCode = hold.DimensionCode,
            Quantity = hold.Quantity,
            StartTime = hold.StartTime,
            EndTime = hold.EndTime,
            SourceType = hold.SourceType,
            SourceId = hold.SourceId,
            Status = CapacityConsumptionStatus.Committed,
        };
        db.CapacityConsumptions.Add(consumption);

        WriteLedger(hold.CapacityDefinitionId, hold.ResourceId, CapacityLedgerEntryType.ConsumptionCommitted, hold.DimensionCode, hold.Quantity);
        await db.SaveChangesAsync();

        return Ok(consumption);
    }

    [HttpPost("holds/{id:guid}/release")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> ReleaseHold(Guid id)
    {
        var hold = await db.CapacityHolds.FindAsync(id);
        if (hold is null)
            return NotFound();

        if (hold.Status != CapacityHoldStatus.Active)
            return Conflict(new { error = $"Hold is {hold.Status}, cannot release." });

        hold.Status = CapacityHoldStatus.Released;

        WriteLedger(hold.CapacityDefinitionId, hold.ResourceId, CapacityLedgerEntryType.HoldReleased, hold.DimensionCode, hold.Quantity);
        await db.SaveChangesAsync();

        return Ok(hold);
    }

    [HttpPost("consumptions/{id:guid}/release")]
    [RequirePermission(EswmpPermissions.CapacityWrite)]
    public async Task<IActionResult> ReleaseConsumption(Guid id)
    {
        var consumption = await db.CapacityConsumptions.FindAsync(id);
        if (consumption is null)
            return NotFound();

        if (consumption.Status != CapacityConsumptionStatus.Committed)
            return Conflict(new { error = $"Consumption is {consumption.Status}, cannot release." });

        consumption.Status = CapacityConsumptionStatus.Released;

        WriteLedger(consumption.CapacityDefinitionId, consumption.ResourceId, CapacityLedgerEntryType.ConsumptionReleased, consumption.DimensionCode, consumption.Quantity);
        await db.SaveChangesAsync();

        return Ok(consumption);
    }

    private async Task<CapacityDefinition?> FindActiveDefinitionAsync(Guid resourceId, string dimensionCode) =>
        await (from def in db.CapacityDefinitions
               join profile in db.CapacityProfiles on def.CapacityProfileId equals profile.Id
               where profile.ResourceId == resourceId
                   && def.DimensionCode == dimensionCode
                   && def.Status == CapacityStatus.Active
               select def).FirstOrDefaultAsync();

    /// <summary>Applies any active CapacityOverride to the definition's MaximumQuantity, then sums active holds/consumption overlapping the window.</summary>
    private async Task<(int Effective, int Held, int Consumed)> ComputeCapacityAsync(
        CapacityDefinition definition, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        var utcNow = DateTimeOffset.UtcNow;

        var overrides = await db.CapacityOverrides
            .Where(o => o.CapacityDefinitionId == definition.Id
                && o.Status == CapacityOverrideStatus.Active
                && o.StartTime < endTime && startTime < o.EndTime)
            .OrderByDescending(o => o.StartTime)
            .ToListAsync();

        var effective = definition.MaximumQuantity;
        foreach (var o in overrides)
        {
            effective = o.Effect switch
            {
                CapacityOverrideEffect.Replace => o.Quantity,
                CapacityOverrideEffect.Increase => effective + o.Quantity,
                CapacityOverrideEffect.Decrease => Math.Max(0, effective - o.Quantity),
                CapacityOverrideEffect.Close => 0,
                _ => effective,
            };
        }

        var held = await db.CapacityHolds
            .Where(h => h.CapacityDefinitionId == definition.Id
                && h.Status == CapacityHoldStatus.Active
                && h.ExpiresAt > utcNow
                && h.StartTime < endTime && startTime < h.EndTime)
            .SumAsync(h => (int?)h.Quantity) ?? 0;

        var consumed = await db.CapacityConsumptions
            .Where(c => c.CapacityDefinitionId == definition.Id
                && c.Status == CapacityConsumptionStatus.Committed
                && c.StartTime < endTime && startTime < c.EndTime)
            .SumAsync(c => (int?)c.Quantity) ?? 0;

        return (effective, held, consumed);
    }

    private void WriteLedger(Guid definitionId, Guid? resourceId, CapacityLedgerEntryType entryType, string? dimensionCode, int quantity)
    {
        db.CapacityLedgerEntries.Add(new CapacityLedgerEntry
        {
            TenantId = tenantContext.RequiredTenantId,
            CapacityDefinitionId = definitionId,
            ResourceId = resourceId,
            EntryType = entryType,
            DimensionCode = dimensionCode,
            Quantity = quantity,
        });
    }
}
