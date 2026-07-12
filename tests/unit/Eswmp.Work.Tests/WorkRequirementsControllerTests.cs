using System.Text.Json;
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

public class WorkRequirementsControllerTests
{
    private static WorkDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static WorkRequirementsController NewController(WorkDbContext db, Guid tenantId, IPublishEndpoint? publishEndpoint = null) =>
        new(db, new TenantContext { TenantId = tenantId }, publishEndpoint ?? Mock.Of<IPublishEndpoint>());

    private static RequirementVersionRequest ValidVersionRequest() => new(
        ChangeSummary: "initial",
        EffectiveFrom: null,
        EffectiveTo: null,
        DurationType: DurationType.Fixed,
        FixedDurationMinutes: 60,
        MinimumDurationMinutes: null,
        ExpectedDurationMinutes: null,
        MaximumDurationMinutes: null,
        PreWorkBufferMinutes: 5,
        PostWorkBufferMinutes: 5,
        ResourceRequirements:
        [
            new ResourceRequirementDto(
                ResourceTypeCode: "technician",
                Role: "lead",
                MinimumQuantity: 1,
                PreferredQuantity: 1,
                MaximumQuantity: 2,
                Mandatory: true,
                Capabilities: [new CapabilityRequirementDto("welding", 3, CapabilityImportance.Mandatory)],
                Skills: [new SkillRequirementDto("forklift", 1, true)],
                Certifications: [new CertificationRequirementDto("osha-10", true)])
        ],
        LocationConstraints:
        [
            new LocationConstraintDto(LocationConstraintMode.CustomerLocation, MaximumTravelDistanceKm: 50, MaximumTravelTimeMinutes: 60)
        ]);

    private async Task<WorkRequirement> CreateWorkRequirement(WorkDbContext db, Guid tenantId, string code = "WR-1")
    {
        var controller = NewController(db, tenantId);
        var created = await controller.Create(new CreateWorkRequirementRequest(code, "Test Requirement", null, null));
        return Assert.IsType<WorkRequirement>(Assert.IsType<CreatedAtActionResult>(created).Value);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);
        await controller.Create(new CreateWorkRequirementRequest("DUP", "First", null, null));

        var result = await controller.Create(new CreateWorkRequirementRequest("DUP", "Second", null, null));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateVersion_StartsInDraft_AndBumpsCurrentVersionNumber()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.CreateVersion(wr.Id, ValidVersionRequest());

        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(RequirementVersionStatus.Draft, version.Status);
        Assert.Equal(1, version.VersionNumber);

        var reloaded = await db.WorkRequirements.FindAsync(wr.Id);
        Assert.Equal(1, reloaded!.CurrentVersionNumber);
    }

    [Fact]
    public async Task ValidateVersion_MissingResourceRequirements_ReturnsInvalid()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        var badRequest = ValidVersionRequest() with { ResourceRequirements = [] };
        var created = await controller.CreateVersion(wr.Id, badRequest);
        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.ValidateVersion(wr.Id, version.VersionNumber);

        var ok = Assert.IsType<OkObjectResult>(result);
        // The controller returns an internal anonymous type — round-trip through JSON
        // instead of `dynamic`, which can't bind to an anonymous type across assemblies.
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("Invalid", doc.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task ValidateVersion_Valid_MarksVersionValidated()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        await controller.ValidateVersion(wr.Id, version.VersionNumber);

        var reloaded = await db.RequirementVersions.FindAsync(version.Id);
        Assert.Equal(RequirementVersionStatus.Validated, reloaded!.Status);
    }

    [Fact]
    public async Task ActivateVersion_StaleExpectedVersion_ReturnsPreconditionFailed()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.ActivateVersion(wr.Id, version.VersionNumber, new ActivateVersionRequest(wr.ConcurrencyVersion + 1));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }

    [Fact]
    public async Task ActivateVersion_ThenActivateNewVersion_SupersedesThePriorActiveVersion()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId, Mock.Of<IPublishEndpoint>());

        var createdV1 = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var v1 = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(createdV1).Value);
        await controller.ActivateVersion(wr.Id, v1.VersionNumber, new ActivateVersionRequest(wr.ConcurrencyVersion));

        var wrAfterFirstActivate = await db.WorkRequirements.FindAsync(wr.Id);

        var createdV2 = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var v2 = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(createdV2).Value);
        var activateV2Result = await controller.ActivateVersion(
            wr.Id, v2.VersionNumber, new ActivateVersionRequest(wrAfterFirstActivate!.ConcurrencyVersion));

        Assert.IsType<OkObjectResult>(activateV2Result);

        var reloadedV1 = await db.RequirementVersions.FirstAsync(v => v.WorkRequirementId == wr.Id && v.VersionNumber == v1.VersionNumber);
        var reloadedV2 = await db.RequirementVersions.FirstAsync(v => v.WorkRequirementId == wr.Id && v.VersionNumber == v2.VersionNumber);
        var reloadedWr = await db.WorkRequirements.FindAsync(wr.Id);

        Assert.Equal(RequirementVersionStatus.Superseded, reloadedV1.Status);
        Assert.Equal(RequirementVersionStatus.Active, reloadedV2.Status);
        Assert.Equal(v2.VersionNumber, reloadedWr!.ActiveVersionNumber);
    }

    [Fact]
    public async Task PatchVersion_OnceActive_IsRejectedAsImmutable()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);
        await controller.ActivateVersion(wr.Id, version.VersionNumber, new ActivateVersionRequest(wr.ConcurrencyVersion));

        var result = await controller.UpdateVersion(wr.Id, version.VersionNumber, ValidVersionRequest() with { ChangeSummary = "edited after active" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task PatchVersion_WhileDraft_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.UpdateVersion(wr.Id, version.VersionNumber, ValidVersionRequest() with { ChangeSummary = "edited while draft" });

        var updated = Assert.IsType<RequirementVersion>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("edited while draft", updated.ChangeSummary);
    }

    [Fact]
    public async Task Retire_AlreadyRetired_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Retire(wr.Id);

        var result = await controller.Retire(wr.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateSnapshot_FreezesVersionAndChildrenIntoDefinitionJson()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var wr = await CreateWorkRequirement(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(wr.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.CreateSnapshot(wr.Id, new CreateSnapshotRequest(version.VersionNumber, "pre-acceptance freeze"));

        var snapshot = Assert.IsType<RequirementSnapshot>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(version.VersionNumber, snapshot.SourceVersionNumber);
        Assert.Equal(wr.Id, snapshot.SourceRequirementId);
        Assert.Contains("welding", snapshot.DefinitionJson);

        // Snapshot has no update endpoint — it is immutable once created by construction
        // (there's simply no code path that can mutate DefinitionJson after this point).
        var getResult = await controller.GetSnapshot(snapshot.Id);
        var fetched = Assert.IsType<RequirementSnapshot>(Assert.IsType<OkObjectResult>(getResult).Value);
        Assert.Equal(snapshot.DefinitionJson, fetched.DefinitionJson);
    }
}
