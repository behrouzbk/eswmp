using Eswmp.Core.Controllers;
using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Eswmp.Core.Tests;

public class CapacityControllerTests
{
    private static CoreDbContext NewDb(Guid tenantId)
    {
        // CapacityController.CreateHold wraps its check-then-insert in a real DB
        // transaction (required for Postgres correctness); the InMemory provider
        // doesn't support transactions and warns-as-error by default, so silence
        // that specific warning for these tests — production behavior is unaffected.
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CoreDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static CapacityController NewController(CoreDbContext db, Guid tenantId) =>
        new(db, new TenantContext { TenantId = tenantId });

    private static async Task<CapacityDefinition> SeedDefinitionAsync(CoreDbContext db, Guid tenantId, Guid resourceId, int maxQuantity)
    {
        var profile = new CapacityProfile
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            Name = "Default",
            Timezone = "UTC",
            Status = CapacityStatus.Active,
        };
        db.CapacityProfiles.Add(profile);

        var definition = new CapacityDefinition
        {
            TenantId = tenantId,
            CapacityProfileId = profile.Id,
            Name = "Concurrent jobs",
            CapacityModel = CapacityModel.Concurrent,
            DimensionCode = "CONCURRENT_WORK",
            MaximumQuantity = maxQuantity,
            Unit = CapacityUnit.Count,
            TimeBasis = CapacityTimeBasis.Concurrent,
            Status = CapacityStatus.Active,
        };
        db.CapacityDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition;
    }

    [Fact]
    public async Task Resolve_NoHoldsOrConsumption_ReturnsFullRemainingCapacity()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await SeedDefinitionAsync(db, tenantId, resourceId, maxQuantity: 2);
        var controller = NewController(db, tenantId);
        var start = DateTimeOffset.UtcNow;

        var result = await controller.Resolve(new ResolveCapacityRequest(resourceId, "CONCURRENT_WORK", start, start.AddHours(1), 1));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResolveCapacityResponse>(ok.Value);
        Assert.Equal(2, response.RemainingCapacity);
        Assert.True(response.CanFulfil);
    }

    [Fact]
    public async Task CreateHold_WithinRemainingCapacity_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await SeedDefinitionAsync(db, tenantId, resourceId, maxQuantity: 2);
        var controller = NewController(db, tenantId);
        var start = DateTimeOffset.UtcNow;

        var result = await controller.CreateHold(new CreateCapacityHoldRequest(
            resourceId, "CONCURRENT_WORK", Quantity: 1, start, start.AddHours(1), HoldDurationSeconds: 60,
            SourceType: "test", SourceId: "1", IdempotencyKey: Guid.NewGuid().ToString()));

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task CreateHold_ExceedingRemainingCapacity_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await SeedDefinitionAsync(db, tenantId, resourceId, maxQuantity: 1);
        var start = DateTimeOffset.UtcNow;
        db.CapacityHolds.Add(new CapacityHold
        {
            TenantId = tenantId,
            CapacityDefinitionId = definition.Id,
            ResourceId = resourceId,
            DimensionCode = "CONCURRENT_WORK",
            Quantity = 1,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = CapacityHoldStatus.Active,
            ExpiresAt = start.AddMinutes(1),
            IdempotencyKey = "existing-hold",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.CreateHold(new CreateCapacityHoldRequest(
            resourceId, "CONCURRENT_WORK", Quantity: 1, start.AddMinutes(30), start.AddMinutes(90), HoldDurationSeconds: 60,
            SourceType: "test", SourceId: "2", IdempotencyKey: Guid.NewGuid().ToString()));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateHold_DuplicateIdempotencyKey_ReturnsOriginalHoldWithoutDoubleCounting()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await SeedDefinitionAsync(db, tenantId, resourceId, maxQuantity: 1);
        var controller = NewController(db, tenantId);
        var start = DateTimeOffset.UtcNow;
        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new CreateCapacityHoldRequest(
            resourceId, "CONCURRENT_WORK", Quantity: 1, start, start.AddHours(1), HoldDurationSeconds: 60,
            SourceType: "test", SourceId: "1", idempotencyKey);

        var first = await controller.CreateHold(request);
        var second = await controller.CreateHold(request);

        Assert.IsType<CreatedAtActionResult>(first);
        Assert.IsType<OkObjectResult>(second);
        Assert.Single(db.CapacityHolds);
    }

    [Fact]
    public async Task CommitHold_ActiveHold_CreatesConsumptionAndMarksHoldCommitted()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await SeedDefinitionAsync(db, tenantId, resourceId, maxQuantity: 2);
        var start = DateTimeOffset.UtcNow;
        var hold = new CapacityHold
        {
            TenantId = tenantId,
            CapacityDefinitionId = definition.Id,
            ResourceId = resourceId,
            DimensionCode = "CONCURRENT_WORK",
            Quantity = 1,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = CapacityHoldStatus.Active,
            ExpiresAt = start.AddMinutes(1),
            IdempotencyKey = "hold-to-commit",
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.CommitHold(hold.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var consumption = Assert.IsType<CapacityConsumption>(ok.Value);
        Assert.Equal(CapacityConsumptionStatus.Committed, consumption.Status);
        Assert.Equal(CapacityHoldStatus.Committed, hold.Status);
    }

    [Fact]
    public async Task CommitHold_AlreadyCommitted_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await SeedDefinitionAsync(db, tenantId, resourceId, maxQuantity: 2);
        var hold = new CapacityHold
        {
            TenantId = tenantId,
            CapacityDefinitionId = definition.Id,
            ResourceId = resourceId,
            DimensionCode = "CONCURRENT_WORK",
            Quantity = 1,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1),
            Status = CapacityHoldStatus.Committed,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            IdempotencyKey = "already-committed",
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.CommitHold(hold.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }
}
