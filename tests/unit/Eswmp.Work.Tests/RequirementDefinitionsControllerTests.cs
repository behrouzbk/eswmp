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

public class RequirementDefinitionsControllerTests
{
    private static WorkDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static RequirementDefinitionsController NewController(WorkDbContext db, Guid tenantId, IPublishEndpoint? publishEndpoint = null) =>
        new(db, new TenantContext { TenantId = tenantId }, publishEndpoint ?? Mock.Of<IPublishEndpoint>());

    private static RequirementDefinitionVersionRequest ValidVersionRequest() => new(
        ChangeSummary: "initial",
        EffectiveFrom: null,
        EffectiveTo: null,
        DurationType: DefinitionDurationType.Fixed,
        FixedDurationMinutes: 60,
        MinimumDurationMinutes: null,
        ExpectedDurationMinutes: null,
        MaximumDurationMinutes: null,
        PreWorkBufferMinutes: 5,
        PostWorkBufferMinutes: 5,
        ResourceRequirements:
        [
            new DefinitionResourceRequirementDto(
                ResourceTypeCode: "technician",
                Role: "lead",
                MinimumQuantity: 1,
                PreferredQuantity: 1,
                MaximumQuantity: 2,
                Mandatory: true,
                Capabilities: [new DefinitionCapabilityRequirementDto("welding", 3, CapabilityImportance.Mandatory)],
                Skills: [new DefinitionSkillRequirementDto("forklift", 1, true)],
                Certifications: [new DefinitionCertificationRequirementDto("osha-10", true)])
        ],
        LocationConstraints:
        [
            new LocationConstraintDto(LocationConstraintMode.CustomerLocation, MaximumTravelDistanceKm: 50, MaximumTravelTimeMinutes: 60)
        ]);

    private async Task<RequirementDefinition> CreateDefinition(WorkDbContext db, Guid tenantId, string code = "WR-1")
    {
        var controller = NewController(db, tenantId);
        var created = await controller.Create(new CreateRequirementDefinitionRequest(code, "Test Requirement", null, null));
        return Assert.IsType<RequirementDefinition>(Assert.IsType<CreatedAtActionResult>(created).Value);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);
        await controller.Create(new CreateRequirementDefinitionRequest("DUP", "First", null, null));

        var result = await controller.Create(new CreateRequirementDefinitionRequest("DUP", "Second", null, null));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateVersion_StartsInDraft_AndBumpsCurrentVersionNumber()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.CreateVersion(definition.Id, ValidVersionRequest());

        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(RequirementDefinitionVersionStatus.Draft, version.Status);
        Assert.Equal(1, version.VersionNumber);

        var reloaded = await db.RequirementDefinitions.FindAsync(definition.Id);
        Assert.Equal(1, reloaded!.CurrentVersionNumber);
    }

    [Fact]
    public async Task ValidateVersion_MissingResourceRequirements_ReturnsInvalid()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        var badRequest = ValidVersionRequest() with { ResourceRequirements = [] };
        var created = await controller.CreateVersion(definition.Id, badRequest);
        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.ValidateVersion(definition.Id, version.VersionNumber);

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
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        await controller.ValidateVersion(definition.Id, version.VersionNumber);

        var reloaded = await db.RequirementDefinitionVersions.FindAsync(version.Id);
        Assert.Equal(RequirementDefinitionVersionStatus.Validated, reloaded!.Status);
    }

    [Fact]
    public async Task ActivateVersion_StaleExpectedVersion_ReturnsPreconditionFailed()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.ActivateVersion(definition.Id, version.VersionNumber, new ActivateDefinitionVersionRequest(definition.ConcurrencyVersion + 1));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }

    [Fact]
    public async Task ActivateVersion_ThenActivateNewVersion_SupersedesThePriorActiveVersion()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId, Mock.Of<IPublishEndpoint>());

        var createdV1 = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var v1 = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(createdV1).Value);
        await controller.ActivateVersion(definition.Id, v1.VersionNumber, new ActivateDefinitionVersionRequest(definition.ConcurrencyVersion));

        var definitionAfterFirstActivate = await db.RequirementDefinitions.FindAsync(definition.Id);

        var createdV2 = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var v2 = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(createdV2).Value);
        var activateV2Result = await controller.ActivateVersion(
            definition.Id, v2.VersionNumber, new ActivateDefinitionVersionRequest(definitionAfterFirstActivate!.ConcurrencyVersion));

        Assert.IsType<OkObjectResult>(activateV2Result);

        var reloadedV1 = await db.RequirementDefinitionVersions.FirstAsync(v => v.RequirementDefinitionId == definition.Id && v.VersionNumber == v1.VersionNumber);
        var reloadedV2 = await db.RequirementDefinitionVersions.FirstAsync(v => v.RequirementDefinitionId == definition.Id && v.VersionNumber == v2.VersionNumber);
        var reloadedDefinition = await db.RequirementDefinitions.FindAsync(definition.Id);

        Assert.Equal(RequirementDefinitionVersionStatus.Superseded, reloadedV1.Status);
        Assert.Equal(RequirementDefinitionVersionStatus.Active, reloadedV2.Status);
        Assert.Equal(v2.VersionNumber, reloadedDefinition!.ActiveVersionNumber);
    }

    [Fact]
    public async Task PatchVersion_OnceActive_IsRejectedAsImmutable()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);
        await controller.ActivateVersion(definition.Id, version.VersionNumber, new ActivateDefinitionVersionRequest(definition.ConcurrencyVersion));

        var result = await controller.UpdateVersion(definition.Id, version.VersionNumber, ValidVersionRequest() with { ChangeSummary = "edited after active" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task PatchVersion_WhileDraft_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.UpdateVersion(definition.Id, version.VersionNumber, ValidVersionRequest() with { ChangeSummary = "edited while draft" });

        var updated = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("edited while draft", updated.ChangeSummary);
    }

    [Fact]
    public async Task Retire_AlreadyRetired_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Retire(definition.Id);

        var result = await controller.Retire(definition.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateSnapshot_FreezesVersionAndChildrenIntoDefinitionJson()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var definition = await CreateDefinition(db, tenantId);
        var controller = NewController(db, tenantId);
        var created = await controller.CreateVersion(definition.Id, ValidVersionRequest());
        var version = Assert.IsType<RequirementDefinitionVersion>(Assert.IsType<CreatedAtActionResult>(created).Value);

        var result = await controller.CreateSnapshot(definition.Id, new CreateDefinitionSnapshotRequest(version.VersionNumber, "pre-acceptance freeze"));

        var snapshot = Assert.IsType<RequirementDefinitionSnapshot>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(version.VersionNumber, snapshot.SourceVersionNumber);
        Assert.Equal(definition.Id, snapshot.SourceRequirementId);
        Assert.Contains("welding", snapshot.DefinitionJson);

        // Snapshot has no update endpoint — it is immutable once created by construction
        // (there's simply no code path that can mutate DefinitionJson after this point).
        var getResult = await controller.GetSnapshot(snapshot.Id);
        var fetched = Assert.IsType<RequirementDefinitionSnapshot>(Assert.IsType<OkObjectResult>(getResult).Value);
        Assert.Equal(snapshot.DefinitionJson, fetched.DefinitionJson);
    }
}
