using Eswmp.Core.Controllers;
using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Eswmp.Core.Tests;

public class ResourceLifecycleTests
{
    private static CoreDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CoreDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static ResourcesController NewController(CoreDbContext db, Guid tenantId) =>
        new(db, new TenantContext { TenantId = tenantId });

    private static async Task<Resource> SeedResourceAsync(CoreDbContext db, Guid tenantId, ResourceStatus status)
    {
        var resource = new Resource
        {
            TenantId = tenantId,
            ResourceType = "Van",
            Name = "Test Resource",
            Timezone = "UTC",
            Status = status,
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync();
        return resource;
    }

    [Fact]
    public async Task Activate_FromDraft_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var resource = await SeedResourceAsync(db, tenantId, ResourceStatus.Draft);
        var controller = NewController(db, tenantId);

        var result = await controller.Activate(resource.Id, new ResourceLifecycleRequest(null, null, null));

        var ok = Assert.IsType<OkObjectResult>(result);
        var updated = Assert.IsType<Resource>(ok.Value);
        Assert.Equal(ResourceStatus.Active, updated.Status);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task Activate_FromActive_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var resource = await SeedResourceAsync(db, tenantId, ResourceStatus.Active);
        var controller = NewController(db, tenantId);

        var result = await controller.Activate(resource.Id, new ResourceLifecycleRequest(null, null, null));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Suspend_FromActive_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var resource = await SeedResourceAsync(db, tenantId, ResourceStatus.Active);
        var controller = NewController(db, tenantId);

        var result = await controller.Suspend(resource.Id, new ResourceLifecycleRequest("test", null, null));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(ResourceStatus.Suspended, Assert.IsType<Resource>(ok.Value).Status);
    }

    [Fact]
    public async Task Retire_IsTerminal_CannotBeReversedByActivate()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var resource = await SeedResourceAsync(db, tenantId, ResourceStatus.Active);
        var controller = NewController(db, tenantId);

        await controller.Retire(resource.Id, new ResourceLifecycleRequest(null, null, null));
        var reactivateResult = await controller.Activate(resource.Id, new ResourceLifecycleRequest(null, null, null));

        Assert.Equal(ResourceStatus.Retired, resource.Status);
        Assert.IsType<ConflictObjectResult>(reactivateResult);
    }

    [Fact]
    public async Task Transition_WithStaleExpectedVersion_ReturnsPreconditionFailed()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var resource = await SeedResourceAsync(db, tenantId, ResourceStatus.Active);
        var controller = NewController(db, tenantId);

        var result = await controller.Suspend(resource.Id, new ResourceLifecycleRequest(null, null, ExpectedVersion: 99));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }

    [Fact]
    public async Task AddCapability_PersistsAgainstResource()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var resource = await SeedResourceAsync(db, tenantId, ResourceStatus.Active);
        var controller = NewController(db, tenantId);

        var result = await controller.AddCapability(resource.Id, new AddResourceCapabilityRequest("HEAVY_LIFTING", 3, null, null));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var capability = Assert.IsType<ResourceCapability>(created.Value);
        Assert.Equal(resource.Id, capability.ResourceId);
        Assert.Equal(3, capability.Level);
    }
}
