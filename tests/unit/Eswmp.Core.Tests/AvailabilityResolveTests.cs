using Eswmp.Core.Controllers;
using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Eswmp.Core.Tests;

public class AvailabilityResolveTests
{
    private static CoreDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CoreDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static AvailabilityController NewController(CoreDbContext db, Guid tenantId) =>
        new(db, new TenantContext { TenantId = tenantId });

    // A Monday used as a stable anchor so DayOfWeek-based rules are deterministic regardless of "today".
    private static DateTimeOffset Monday9Am()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 14) % 7;
        var nextMonday = today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday);
        return new DateTimeOffset(nextMonday.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
    }

    [Fact]
    public async Task Resolve_WithinRegularRuleWindow_ReturnsFreeInterval()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mondayStart = Monday9Am();
        await using var db = NewDb(tenantId);
        db.AvailabilityRules.Add(new AvailabilityRule
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(new ResolveAvailabilityRequest(resourceId, mondayStart, mondayStart.AddHours(1)));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResolveAvailabilityResponse>(ok.Value);
        Assert.Single(response.FreeIntervals);
    }

    [Fact]
    public async Task Resolve_ForceUnavailableOverride_BlocksOtherwiseFreeWindow()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mondayStart = Monday9Am();
        await using var db = NewDb(tenantId);
        db.AvailabilityRules.Add(new AvailabilityRule
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
        });
        db.AvailabilityOverrides.Add(new AvailabilityOverride
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            Effect = AvailabilityOverrideEffect.ForceUnavailable,
            StartTime = mondayStart,
            EndTime = mondayStart.AddHours(1),
            Status = AvailabilityOverrideStatus.Active,
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(new ResolveAvailabilityRequest(resourceId, mondayStart, mondayStart.AddHours(1)));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResolveAvailabilityResponse>(ok.Value);
        Assert.Empty(response.FreeIntervals);
    }

    [Fact]
    public async Task Resolve_ApprovedTimeOff_BlocksOtherwiseFreeWindow()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mondayStart = Monday9Am();
        await using var db = NewDb(tenantId);
        db.AvailabilityRules.Add(new AvailabilityRule
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
        });
        db.TimeOffs.Add(new TimeOff
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            Type = "Vacation",
            StartTime = mondayStart,
            EndTime = mondayStart.AddHours(1),
            ApprovalStatus = TimeOffApprovalStatus.Approved,
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(new ResolveAvailabilityRequest(resourceId, mondayStart, mondayStart.AddHours(1)));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResolveAvailabilityResponse>(ok.Value);
        Assert.Empty(response.FreeIntervals);
    }

    [Fact]
    public async Task Resolve_PendingTimeOff_DoesNotBlock()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mondayStart = Monday9Am();
        await using var db = NewDb(tenantId);
        db.AvailabilityRules.Add(new AvailabilityRule
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
        });
        db.TimeOffs.Add(new TimeOff
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            Type = "Vacation",
            StartTime = mondayStart,
            EndTime = mondayStart.AddHours(1),
            ApprovalStatus = TimeOffApprovalStatus.PendingApproval,
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(new ResolveAvailabilityRequest(resourceId, mondayStart, mondayStart.AddHours(1)));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResolveAvailabilityResponse>(ok.Value);
        Assert.Single(response.FreeIntervals);
    }

    [Fact]
    public async Task BatchResolve_ReturnsOneResultPerResource()
    {
        var tenantId = Guid.NewGuid();
        var resourceA = Guid.NewGuid();
        var resourceB = Guid.NewGuid();
        var mondayStart = Monday9Am();
        await using var db = NewDb(tenantId);
        db.AvailabilityRules.Add(new AvailabilityRule
        {
            TenantId = tenantId,
            ResourceId = resourceA,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.BatchResolve(new BatchResolveAvailabilityRequest([resourceA, resourceB], mondayStart, mondayStart.AddHours(1)));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var responses = Assert.IsAssignableFrom<IReadOnlyList<ResolveAvailabilityResponse>>(ok.Value);
        Assert.Equal(2, responses.Count);
        Assert.Single(responses.Single(r => r.ResourceId == resourceA).FreeIntervals);
        Assert.Empty(responses.Single(r => r.ResourceId == resourceB).FreeIntervals);
    }
}
