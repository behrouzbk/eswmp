using Eswmp.Shared.DTOs;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Controllers;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Eswmp.Work.Tests;

public class DemandsControllerTests
{
    private static WorkDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    /// <summary>Opens a new WorkDbContext against an existing shared InMemory database name,
    /// so multiple contexts can see the same underlying data — used to simulate two concurrent
    /// requests each holding their own tracked copy of the same row.</summary>
    private static WorkDbContext OpenDb(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static DemandsController NewController(WorkDbContext db, Guid tenantId, IPublishEndpoint? publishEndpoint = null) =>
        new(db, new TenantContext { TenantId = tenantId }, publishEndpoint ?? Mock.Of<IPublishEndpoint>());

    private static CreateDemandRequest ValidRequest() => new(
        OrganizationId: null,
        DemandType: "field-service-visit",
        SourceSystem: "test-suite",
        SourceChannel: "api",
        Priority: DemandPriority.Normal,
        Summary: "Test demand",
        Description: "A demand created for a unit test",
        RequestedStartAtUtc: DateTimeOffset.UtcNow.AddDays(1),
        RequestedEndAtUtc: DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
        RequestedTimezone: "UTC",
        LocationReference: null,
        ExternalReferenceType: "test",
        ExternalReferenceId: "1");

    [Fact]
    public async Task Create_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Create(ValidRequest(), idempotencyKey: null);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task Create_FirstCall_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Create(ValidRequest(), "key-1");

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1, await db.Demands.CountAsync());
    }

    [Fact]
    public async Task Create_RepeatedKeySameBody_ReplaysOriginalResponse_WithoutCreatingASecondDemand()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);
        var request = ValidRequest();

        var first = await controller.Create(request, "key-1");
        var second = await controller.Create(request, "key-1");

        var firstDemand = Assert.IsType<Demand>(Assert.IsType<CreatedAtActionResult>(first).Value);
        var secondDemand = Assert.IsType<Demand>(Assert.IsType<CreatedAtActionResult>(second).Value);

        Assert.Equal(firstDemand.Id, secondDemand.Id);
        Assert.Equal(1, await db.Demands.CountAsync());
    }

    [Fact]
    public async Task Create_RepeatedKeyDifferentBody_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        await controller.Create(ValidRequest(), "key-1");
        var differentRequest = ValidRequest() with { Summary = "A different summary" };
        var result = await controller.Create(differentRequest, "key-1");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
        Assert.Equal(1, await db.Demands.CountAsync());
    }

    [Fact]
    public async Task Validate_WithMissingDescriptionAndSummary_ReturnsInvalidWhenTimeWindowBroken()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            RequestedStartAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            RequestedEndAtUtc = DateTimeOffset.UtcNow, // end before start -> Error-level issue
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Validate(demand.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(DemandStatus.Received, demand.Status); // stays Received on Invalid
    }

    [Fact]
    public async Task Validate_ValidDemand_TransitionsToReady()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Summary = "Has a summary",
            RequestedStartAtUtc = DateTimeOffset.UtcNow,
            RequestedEndAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        await controller.Validate(demand.Id);

        Assert.Equal(DemandStatus.Ready, demand.Status);
    }

    [Fact]
    public async Task Accept_WhenNotReady_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.Received,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Accept(demand.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task Accept_WhenReady_TransitionsToAccepted_AndPublishesEvent()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.Ready,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var publishMock = new Mock<IPublishEndpoint>();
        var controller = NewController(db, tenantId, publishMock.Object);

        var result = await controller.Accept(demand.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(DemandStatus.Accepted, demand.Status);
        publishMock.Verify(p => p.Publish(It.IsAny<DemandAcceptedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_WithoutReasonCode_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Reject(demand.Id, new RejectDemandRequest(ReasonCode: "", Comment: null));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task Cancel_WhenAlreadyAccepted_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.Accepted,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Cancel(demand.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task Update_WithStaleExpectedVersion_ReturnsPreconditionFailed()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Update(demand.Id, new UpdateDemandRequest(
            ExpectedVersion: demand.Version + 1, Priority: null, Summary: null, Description: null,
            RequestedStartAtUtc: null, RequestedEndAtUtc: null, RequestedTimezone: null, LocationReference: null));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }

    [Fact]
    public async Task Update_WhenReady_RejectsRestrictedFieldChange()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.Ready,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Update(demand.Id, new UpdateDemandRequest(
            ExpectedVersion: demand.Version, Priority: null, Summary: "New summary", Description: null,
            RequestedStartAtUtc: null, RequestedEndAtUtc: null, RequestedTimezone: null, LocationReference: null));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task Update_WhenReady_AllowsPriorityChange()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.Ready,
            Priority = DemandPriority.Normal,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Update(demand.Id, new UpdateDemandRequest(
            ExpectedVersion: demand.Version, Priority: DemandPriority.Urgent, Summary: null, Description: null,
            RequestedStartAtUtc: null, RequestedEndAtUtc: null, RequestedTimezone: null, LocationReference: null));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(DemandPriority.Urgent, demand.Priority);
    }

    [Fact]
    public async Task Create_WithFulfillmentModeOmitted_DefaultsToScheduled()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Create(ValidRequest(), "key-fulfillment-default");

        var created = Assert.IsType<Demand>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(DemandFulfillmentMode.Scheduled, created.FulfillmentMode);
    }

    [Fact]
    public async Task Create_WithInvalidTimeWindow_ReturnsBadRequestWithIssues_AndDoesNotPersist()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);
        var request = ValidRequest() with
        {
            RequestedStartAtUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
            RequestedEndAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        };

        var result = await controller.Create(request, "key-bad-window");

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var envelope = Assert.IsType<ErrorEnvelope>(badRequest.Value);
        Assert.Contains(envelope.Issues!, i => i.Code == "INVALID_TIME_WINDOW");
        Assert.Equal(0, await db.Demands.CountAsync());
    }

    [Fact]
    public async Task Validate_ScheduledWithNoWindow_ReturnsInvalid_WithModeWindowRequiredIssue()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Summary = "Has a summary",
            FulfillmentMode = DemandFulfillmentMode.Scheduled,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        await controller.Validate(demand.Id);

        Assert.Equal(DemandStatus.Received, demand.Status); // Error-level issue -> stays Received
        var result = await db.DemandValidationResults.SingleAsync(r => r.DemandId == demand.Id);
        Assert.Contains("MODE_WINDOW_REQUIRED", result.IssuesJson);
    }

    [Fact]
    public async Task Validate_OnDemandWithFutureWindow_ReturnsValidWithWarnings_WithModeWindowUnexpectedIssue()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Summary = "Has a summary",
            FulfillmentMode = DemandFulfillmentMode.OnDemand,
            RequestedStartAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        await controller.Validate(demand.Id);

        Assert.Equal(DemandStatus.Ready, demand.Status); // Warning-only -> Ready
        var result = await db.DemandValidationResults.SingleAsync(r => r.DemandId == demand.Id);
        Assert.Contains("MODE_WINDOW_UNEXPECTED", result.IssuesJson);
    }

    [Fact]
    public async Task Search_FiltersByFulfillmentMode()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        db.Demands.AddRange(
            new Demand { TenantId = tenantId, DemandType = "x", SourceSystem = "x", ExternalReferenceType = "t", ExternalReferenceId = "1", FulfillmentMode = DemandFulfillmentMode.OnDemand },
            new Demand { TenantId = tenantId, DemandType = "x", SourceSystem = "x", ExternalReferenceType = "t", ExternalReferenceId = "2", FulfillmentMode = DemandFulfillmentMode.Scheduled });
        await db.SaveChangesAsync();
        var controller = NewController(db, tenantId);

        var result = await controller.Search(new DemandSearchRequest(
            Status: null, Priority: null, DemandType: null, FulfillmentMode: DemandFulfillmentMode.OnDemand,
            FromUtc: null, ToUtc: null));

        var ok = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Demand>>(ok.Value);
        Assert.Single(paged.Items);
        Assert.Equal(DemandFulfillmentMode.OnDemand, paged.Items[0].FulfillmentMode);
    }

    [Fact]
    public async Task DemandValidationResults_AreTenantIsolated()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var dbA = OpenDb(dbName, tenantA))
        {
            var demand = new Demand
            {
                TenantId = tenantA,
                DemandType = "x",
                SourceSystem = "x",
                ExternalReferenceType = "test",
                ExternalReferenceId = "1",
                Summary = "Has a summary",
            };
            dbA.Demands.Add(demand);
            await dbA.SaveChangesAsync();

            var controllerA = NewController(dbA, tenantA);
            await controllerA.Validate(demand.Id);
        }

        // A regression guard for R1: without HasQueryFilter on DemandValidationResult, this
        // would incorrectly return tenant A's row.
        await using var dbB = OpenDb(dbName, tenantB);
        var resultsVisibleToTenantB = await dbB.DemandValidationResults.ToListAsync();

        Assert.Empty(resultsVisibleToTenantB);
    }

    [Fact]
    public async Task Update_ConcurrentModification_ReturnsPreconditionFailed()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();

        await using var seedDb = OpenDb(dbName, tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "x",
            SourceSystem = "x",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
        };
        seedDb.Demands.Add(demand);
        await seedDb.SaveChangesAsync();

        // Two independent contexts each load their own tracked copy of the same row before
        // either one saves — the TOCTOU gap the concurrency token exists to close.
        await using var dbA = OpenDb(dbName, tenantId);
        var demandA = await dbA.Demands.FindAsync(demand.Id);
        await using var dbB = OpenDb(dbName, tenantId);
        var demandB = await dbB.Demands.FindAsync(demand.Id);
        Assert.NotNull(demandB);

        var updateRequest = new UpdateDemandRequest(
            ExpectedVersion: demandA!.Version, Priority: DemandPriority.High, Summary: null, Description: null,
            RequestedStartAtUtc: null, RequestedEndAtUtc: null, RequestedTimezone: null, LocationReference: null);

        var firstResult = await NewController(dbA, tenantId).Update(demand.Id, updateRequest);
        Assert.IsType<OkObjectResult>(firstResult);

        // dbB is still holding the pre-commit version — its save now loses the race that the
        // in-memory expectedVersion check alone (pre-IsConcurrencyToken) would have missed.
        var secondResult = await NewController(dbB, tenantId).Update(demand.Id, updateRequest);

        var status = Assert.IsType<ObjectResult>(secondResult);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }
}
