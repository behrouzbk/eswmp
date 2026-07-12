using Eswmp.Core.Controllers;
using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Middleware;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Eswmp.Core.Tests;

public class ReservationsControllerTests
{
    private static CoreDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CoreDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static ReservationsController NewController(CoreDbContext db, Guid tenantId) =>
        new(db, new TenantContext { TenantId = tenantId }, Mock.Of<IPublishEndpoint>());

    private static CreateReservationRequest Request(Guid resourceId, DateTimeOffset start, DateTimeOffset end) =>
        new(resourceId, start, end, HoldDurationMinutes: 15, ExternalReferenceType: "test", ExternalReferenceId: "1");

    [Fact]
    public async Task Create_NoExistingReservations_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Create(Request(resourceId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Create_OverlapsExistingHeldReservation_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        await using var db = NewDb(tenantId);
        db.Reservations.Add(new Reservation
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = ReservationStatus.Held,
            ExpiresAt = start.AddMinutes(15),
            ExternalReferenceType = "test",
            ExternalReferenceId = "existing",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Create(Request(resourceId, start.AddMinutes(30), start.AddMinutes(90)));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Create_OverlapsExistingConfirmedReservation_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        await using var db = NewDb(tenantId);
        db.Reservations.Add(new Reservation
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = ReservationStatus.Confirmed,
            ExternalReferenceType = "test",
            ExternalReferenceId = "existing",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Create(Request(resourceId, start.AddMinutes(30), start.AddMinutes(90)));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Create_BackToBackWithNoTimeOverlap_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        await using var db = NewDb(tenantId);
        db.Reservations.Add(new Reservation
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = ReservationStatus.Confirmed,
            ExternalReferenceType = "test",
            ExternalReferenceId = "existing",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        // New reservation starts exactly when the existing one ends — not an overlap.
        var result = await controller.Create(Request(resourceId, start.AddHours(1), start.AddHours(2)));

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Create_OverlapsExpiredHeldReservation_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        await using var db = NewDb(tenantId);
        db.Reservations.Add(new Reservation
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = ReservationStatus.Held,
            ExpiresAt = start.AddMinutes(-1), // hold expired before this request
            ExternalReferenceType = "test",
            ExternalReferenceId = "existing",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Create(Request(resourceId, start.AddMinutes(30), start.AddMinutes(90)));

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Create_OverlapsCancelledReservation_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        await using var db = NewDb(tenantId);
        db.Reservations.Add(new Reservation
        {
            TenantId = tenantId,
            ResourceId = resourceId,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = ReservationStatus.Cancelled,
            ExternalReferenceType = "test",
            ExternalReferenceId = "existing",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Create(Request(resourceId, start.AddMinutes(30), start.AddMinutes(90)));

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Create_OverlapsReservationOnDifferentResource_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        await using var db = NewDb(tenantId);
        db.Reservations.Add(new Reservation
        {
            TenantId = tenantId,
            ResourceId = Guid.NewGuid(),
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = ReservationStatus.Confirmed,
            ExternalReferenceType = "test",
            ExternalReferenceId = "existing",
        });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Create(Request(Guid.NewGuid(), start.AddMinutes(30), start.AddMinutes(90)));

        Assert.IsType<CreatedAtActionResult>(result);
    }
}
