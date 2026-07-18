using System.Text.Json;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Controllers;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using Eswmp.Work.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Eswmp.Work.Tests;

public class RequirementTemplatesControllerTests
{
    private static WorkDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static RequirementTemplatesController NewController(WorkDbContext db, Guid tenantId) =>
        new(db, new TenantContext { TenantId = tenantId }, Mock.Of<IOutboxPublisher>());

    private static CreateTemplateRequest ValidTemplateRequest(string code = "DOG_WALK_STANDARD") =>
        new(code, "Standard Dog Walk", null, "DOG_WALKING");

    private static JsonElement ValidDefinitionsJson()
    {
        var dto = new RequirementSetDto(
            ResourceRequirements: [new ResourceRoleRequirementDto("DOG_WALKER", ResourceCategory.Person, MinimumQuantity: 1, MaximumQuantity: 1)],
            DurationRequirement: new DurationRequirementDto(DurationType.Fixed, EstimatedDurationMinutes: 60),
            CapabilityRequirements: [new CapabilityRequirementDto("DOG_WALKING", RoleCode: "DOG_WALKER", Mandatory: true)],
            CapacityRequirements: [new CapacityRequirementDto("PET_COUNT", 1, RoleCode: "DOG_WALKER", Unit: "COUNT")],
            LocationRequirement: new LocationRequirementDto(LocationMode.CustomerLocation));

        var json = JsonSerializer.Serialize(dto, RequirementResolutionService.JsonOptions);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private async Task<RequirementTemplate> CreateTemplate(WorkDbContext db, Guid tenantId, string code = "DOG_WALK_STANDARD")
    {
        var controller = NewController(db, tenantId);
        var result = await controller.Create(ValidTemplateRequest(code), "create-key-" + code);
        return Assert.IsType<RequirementTemplate>(Assert.IsType<CreatedAtActionResult>(result).Value);
    }

    [Fact]
    public async Task Create_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Create(ValidTemplateRequest(), idempotencyKey: null);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task Create_FirstCall_CreatesTemplateWithDraftVersion1()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Create(ValidTemplateRequest(), "key-1");

        var template = Assert.IsType<RequirementTemplate>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(TemplateStatus.Draft, template.Status);
        Assert.Equal(1, template.CurrentVersion);
        Assert.Equal(1, await db.RequirementTemplateVersions.CountAsync(v => v.TemplateId == template.Id));
    }

    [Fact]
    public async Task Create_SameIdempotencyKeySameBody_Replays()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);
        var request = ValidTemplateRequest();

        var first = await controller.Create(request, "same-key");
        var second = await controller.Create(request, "same-key");

        var firstTemplate = Assert.IsType<RequirementTemplate>(Assert.IsType<CreatedAtActionResult>(first).Value);
        var secondTemplate = Assert.IsType<RequirementTemplate>(Assert.IsType<CreatedAtActionResult>(second).Value);
        Assert.Equal(firstTemplate.Id, secondTemplate.Id);
        Assert.Equal(1, await db.RequirementTemplates.CountAsync());
    }

    [Fact]
    public async Task Create_SameIdempotencyKeyDifferentBody_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        await controller.Create(ValidTemplateRequest("CODE_A"), "same-key");
        var result = await controller.Create(ValidTemplateRequest("CODE_B"), "same-key");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task ConfigureRequirements_OnActivatedVersion_ReturnsTemplateImmutable()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var template = await CreateTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.ConfigureRequirements(template.Id, 1, ValidDefinitionsJson());
        await controller.Activate(template.Id, 1);

        var result = await controller.ConfigureRequirements(template.Id, 1, ValidDefinitionsJson());

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task ConfigureRequirements_MissingResourceRequirements_ReturnsUnprocessable()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var template = await CreateTemplate(db, tenantId);
        var controller = NewController(db, tenantId);

        var invalidDto = new RequirementSetDto(
            ResourceRequirements: [],
            DurationRequirement: new DurationRequirementDto(DurationType.Fixed, EstimatedDurationMinutes: 60));
        var invalidJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(invalidDto, RequirementResolutionService.JsonOptions));

        var result = await controller.ConfigureRequirements(template.Id, 1, invalidJson);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status.StatusCode);
    }

    [Fact]
    public async Task Activate_ValidatesAndFreezesVersion()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var template = await CreateTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.ConfigureRequirements(template.Id, 1, ValidDefinitionsJson());

        var result = await controller.Activate(template.Id, 1);

        var version = Assert.IsType<RequirementTemplateVersion>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(TemplateVersionStatus.Active, version.Status);
        var reloadedTemplate = await db.RequirementTemplates.FindAsync(template.Id);
        Assert.Equal(TemplateStatus.Active, reloadedTemplate!.Status);
    }

    [Fact]
    public async Task Activate_ThenActivateNewVersion_SupersedesPriorVersion()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var template = await CreateTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.ConfigureRequirements(template.Id, 1, ValidDefinitionsJson());
        await controller.Activate(template.Id, 1);

        await controller.CreateVersion(template.Id, new CreateTemplateVersionRequest("bump"), "version-key");
        await controller.ConfigureRequirements(template.Id, 2, ValidDefinitionsJson());
        await controller.Activate(template.Id, 2);

        var v1 = await db.RequirementTemplateVersions.FirstAsync(v => v.TemplateId == template.Id && v.Version == 1);
        var v2 = await db.RequirementTemplateVersions.FirstAsync(v => v.TemplateId == template.Id && v.Version == 2);
        Assert.Equal(TemplateVersionStatus.Superseded, v1.Status);
        Assert.Equal(TemplateVersionStatus.Active, v2.Status);
    }

    [Fact]
    public async Task Retire_AlreadyRetired_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var template = await CreateTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Retire(template.Id);

        var result = await controller.Retire(template.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }
}
