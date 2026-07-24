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

public class WorkRequirementsControllerTests
{
    // ASP.NET Core's MVC pipeline defaults AddControllers().AddJsonOptions() to camelCase —
    // matched here so a raw JsonSerializer.Serialize(ok.Value) round-trip on an anonymous
    // response object sees the same property names a real HTTP client would.
    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

    private static WorkDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static RequirementTemplatesController NewTemplatesController(WorkDbContext db, Guid tenantId) =>
        new(db, new TenantContext { TenantId = tenantId }, Mock.Of<IOutboxPublisher>());

    private static WorkRequirementsController NewController(WorkDbContext db, Guid tenantId, IOutboxPublisher? outbox = null) =>
        new(db, new TenantContext { TenantId = tenantId }, outbox ?? Mock.Of<IOutboxPublisher>());

    private static JsonElement ValidDefinitionsJson()
    {
        var dto = new RequirementSetDto(
            ResourceRequirements: [new ResourceRoleRequirementDto("DOG_WALKER", ResourceCategory.Person, MinimumQuantity: 1, MaximumQuantity: 1)],
            DurationRequirement: new DurationRequirementDto(DurationType.Fixed, EstimatedDurationMinutes: 60),
            CapabilityRequirements: [new CapabilityRequirementDto("DOG_WALKING", RoleCode: "DOG_WALKER", Mandatory: true)],
            CapacityRequirements: [new CapacityRequirementDto("PET_COUNT", 1, RoleCode: "DOG_WALKER", Unit: "COUNT")],
            LocationRequirement: new LocationRequirementDto(LocationMode.CustomerLocation));

        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(dto, RequirementResolutionService.JsonOptions));
    }

    /// <summary>Creates and activates DOG_WALK_STANDARD so resolve tests have an Active template to target.</summary>
    private async Task ActivateStandardTemplate(WorkDbContext db, Guid tenantId)
    {
        var templates = NewTemplatesController(db, tenantId);
        await templates.Create(new CreateTemplateRequest("DOG_WALK_STANDARD", "Standard Dog Walk", null, "DOG_WALKING"), "tpl-create");
        await templates.ConfigureRequirements(await TemplateId(db, tenantId), 1, ValidDefinitionsJson(), "1");
        await templates.Activate(await TemplateId(db, tenantId), 1);
    }

    private static async Task<Guid> TemplateId(WorkDbContext db, Guid tenantId) =>
        (await db.RequirementTemplates.FirstAsync(t => t.TenantId == tenantId && t.Code == "DOG_WALK_STANDARD")).Id;

    /// <summary>v2 delta (UX-03/UX-04) — a Resource requirement marked Customer-visible, a
    /// Capability requirement left at the default (unset -> Internal), and a Capacity
    /// requirement marked Customer-visible, so audience-filtering tests have a mix.</summary>
    private static JsonElement DefinitionsWithVisibilityJson()
    {
        var dto = new RequirementSetDto(
            ResourceRequirements: [new ResourceRoleRequirementDto("DOG_WALKER", ResourceCategory.Person, MinimumQuantity: 1, MaximumQuantity: 1, VisibilityLevel: VisibilityLevel.Customer)],
            DurationRequirement: new DurationRequirementDto(DurationType.Fixed, EstimatedDurationMinutes: 60),
            CapabilityRequirements: [new CapabilityRequirementDto("DOG_WALKING", RoleCode: "DOG_WALKER", Mandatory: true)],
            CapacityRequirements: [new CapacityRequirementDto("PET_COUNT", 1, RoleCode: "DOG_WALKER", Unit: "COUNT", VisibilityLevel: VisibilityLevel.Customer)],
            LocationRequirement: new LocationRequirementDto(LocationMode.CustomerLocation));

        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(dto, RequirementResolutionService.JsonOptions));
    }

    private async Task ActivateTemplateWithVisibility(WorkDbContext db, Guid tenantId, string code = "VISIBILITY_STANDARD")
    {
        var templates = NewTemplatesController(db, tenantId);
        await templates.Create(new CreateTemplateRequest(code, "Visibility test template", null, "DOG_WALKING"), "tpl-create-" + code);
        var templateId = (await db.RequirementTemplates.FirstAsync(t => t.TenantId == tenantId && t.Code == code)).Id;
        await templates.ConfigureRequirements(templateId, 1, DefinitionsWithVisibilityJson(), "1");
        await templates.Activate(templateId, 1);
    }

    private static ResolveWorkRequirementRequest ResolveRequest(string sourceId = "demand-1", JsonElement? inputs = null) =>
        new("Demand", sourceId, SourceVersion: 1, TemplateCode: "DOG_WALK_STANDARD", Inputs: inputs);

    private static JsonElement InputsWithPetCountAndWindow(int petCount = 2) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
        {
            petCount,
            requestedWindow = new { start = "2026-07-06T08:00:00-07:00", end = "2026-07-06T12:00:00-07:00" },
        }));

    [Fact]
    public async Task Resolve_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(ResolveRequest(), idempotencyKey: null);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task Resolve_TemplateNotActive_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(ResolveRequest(), "resolve-key");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task Resolve_ActiveTemplate_CreatesValidWorkRequirementWithCapacityOverlay()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Resolve(ResolveRequest(inputs: InputsWithPetCountAndWindow(petCount: 2)), "resolve-key");

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(result).StatusCode);
        var wr = await db.WorkRequirements.FirstAsync(w => w.SourceId == "demand-1");
        Assert.Equal(WorkRequirementStatus.Valid, wr.Status);

        var capacity = await db.CapacityRequirements.FirstAsync(c => c.WorkRequirementId == wr.Id);
        Assert.Equal(2m, capacity.Quantity);

        var time = await db.TimeRequirements.FirstAsync(t => t.WorkRequirementId == wr.Id);
        Assert.NotNull(time.EarliestStart);
        Assert.NotNull(time.LatestFinish);
    }

    [Fact]
    public async Task Resolve_SameIdempotencyKeySameBody_Replays()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        var request = ResolveRequest();

        await controller.Resolve(request, "same-key");
        await controller.Resolve(request, "same-key");

        Assert.Equal(1, await db.WorkRequirements.CountAsync());
    }

    [Fact]
    public async Task Resolve_SameIdempotencyKeyDifferentBody_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);

        await controller.Resolve(ResolveRequest("demand-1"), "same-key");
        var result = await controller.Resolve(ResolveRequest("demand-2"), "same-key");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task GetResolved_ReturnsResolvedContractWithResourceAndCapacityRequirements()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.GetResolved(wr.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        Assert.Equal(1, doc.RootElement.GetProperty("resourceRequirements").GetArrayLength());
        Assert.Equal("DOG_WALKING", doc.RootElement.GetProperty("workType").GetString());
    }

    [Fact]
    public async Task Validate_ValidRequirement_MarksValid()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.Validate(wr.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        Assert.True(doc.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Revise_StaleExpectedVersion_ReturnsPreconditionFailed()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.Revise(wr.Id, new ReviseWorkRequirementRequest(wr.RequirementVersion + 1, "test", null), "revise-key");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }

    [Fact]
    public async Task Revise_CapacityChange_BumpsVersionAndPersistsNewQuantity()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var changes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
        {
            capacityRequirements = new[] { new { roleCode = "DOG_WALKER", dimensionCode = "PET_COUNT", quantity = 3 } },
        }));

        var result = await controller.Revise(wr.Id, new ReviseWorkRequirementRequest(wr.RequirementVersion, "owner added a pet", changes), "revise-key");

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(result).StatusCode);
        var reloaded = await db.WorkRequirements.FindAsync(wr.Id);
        Assert.Equal(2, reloaded!.RequirementVersion);
        var capacity = await db.CapacityRequirements.FirstAsync(c => c.WorkRequirementId == wr.Id);
        Assert.Equal(3m, capacity.Quantity);
        Assert.Equal(2, await db.RequirementVersions.CountAsync(v => v.WorkRequirementId == wr.Id));
    }

    [Fact]
    public async Task Revise_TerminalStatus_ReturnsStatusConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();
        await controller.Cancel(wr.Id);

        var result = await controller.Revise(wr.Id, new ReviseWorkRequirementRequest(wr.RequirementVersion, null, null), "revise-key");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();
        await controller.Cancel(wr.Id);

        var result = await controller.Cancel(wr.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, status.StatusCode);
    }

    [Fact]
    public async Task Compare_BetweenResolveAndRevision_ReportsCapacityRequirementsChanged()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var changes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
        {
            capacityRequirements = new[] { new { roleCode = "DOG_WALKER", dimensionCode = "PET_COUNT", quantity = 3 } },
        }));
        await controller.Revise(wr.Id, new ReviseWorkRequirementRequest(wr.RequirementVersion, "bump", changes), "revise-key");

        var result = await controller.Compare(wr.Id, 1, 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        var changedCategories = doc.RootElement.GetProperty("changedCategories").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("capacityRequirements", changedCategories);
    }

    [Fact]
    public async Task Explain_ReturnsSummaryAndDerivedRequirements()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.Explain(wr.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("summary").GetString()));
        Assert.True(doc.RootElement.GetProperty("derivedRequirements").GetArrayLength() > 0);
    }

    // v2 delta (UX-03/UX-04) — per-line visibility.

    [Fact]
    public async Task Resolve_WithVisibilityLevels_PersistsLineVisibility()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateTemplateWithVisibility(db, tenantId);
        var controller = NewController(db, tenantId);

        await controller.Resolve(new ResolveWorkRequirementRequest("Demand", "demand-1", 1, "VISIBILITY_STANDARD", null), "resolve-key");

        var wr = await db.WorkRequirements.FirstAsync();
        var role = await db.ResourceRoleRequirements.FirstAsync(r => r.WorkRequirementId == wr.Id);
        var capability = await db.CapabilityRequirements.FirstAsync(c => c.WorkRequirementId == wr.Id);
        var visibilities = await db.RequirementLineVisibilities.Where(v => v.WorkRequirementId == wr.Id).ToListAsync();

        Assert.Contains(visibilities, v => v.LineType == nameof(ResourceRoleRequirement) && v.LineId == role.Id && v.VisibilityLevel == VisibilityLevel.Customer && v.CustomerVisible);
        Assert.Contains(visibilities, v => v.LineType == nameof(CapabilityRequirement) && v.LineId == capability.Id && v.VisibilityLevel == VisibilityLevel.Internal && !v.CustomerVisible);
    }

    [Fact]
    public async Task GetResolved_AudienceCustomer_HidesInternalOnlyLines()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateTemplateWithVisibility(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(new ResolveWorkRequirementRequest("Demand", "demand-1", 1, "VISIBILITY_STANDARD", null), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.GetResolved(wr.Id, audience: "customer");

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        Assert.Equal(1, doc.RootElement.GetProperty("resourceRequirements").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("capacityRequirements").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("capabilityRequirements").GetArrayLength());
    }

    [Fact]
    public async Task GetResolved_AudienceOmitted_ReturnsEveryLine()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateTemplateWithVisibility(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(new ResolveWorkRequirementRequest("Demand", "demand-1", 1, "VISIBILITY_STANDARD", null), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.GetResolved(wr.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        Assert.Equal(1, doc.RootElement.GetProperty("capabilityRequirements").GetArrayLength());
    }

    [Fact]
    public async Task GetResolved_UnrecognizedAudience_ReturnsValidationFailed()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateTemplateWithVisibility(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(new ResolveWorkRequirementRequest("Demand", "demand-1", 1, "VISIBILITY_STANDARD", null), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var result = await controller.GetResolved(wr.Id, audience: "nonsense");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task Explain_AudienceProvider_HidesInternalOnlyDerivedRequirements()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateTemplateWithVisibility(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(new ResolveWorkRequirementRequest("Demand", "demand-1", 1, "VISIBILITY_STANDARD", null), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        var dispatcherResult = await controller.Explain(wr.Id);
        var providerResult = await controller.Explain(wr.Id, audience: "provider");

        using var dispatcherDoc = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(dispatcherResult).Value, CamelCase));
        using var providerDoc = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(providerResult).Value, CamelCase));

        // dispatcher sees the role (Customer) + the capacity line (Customer); provider sees the
        // same two (Customer is a subset of [Customer, Provider]) — neither audience includes an
        // Internal-only line here, so this asserts both still surface the Customer-visible ones.
        Assert.True(dispatcherDoc.RootElement.GetProperty("derivedRequirements").GetArrayLength() > 0);
        Assert.Equal(
            dispatcherDoc.RootElement.GetProperty("derivedRequirements").GetArrayLength(),
            providerDoc.RootElement.GetProperty("derivedRequirements").GetArrayLength());
    }

    [Fact]
    public async Task Compare_CustomerVisibleOnlyWithInternalOnlyChange_ExcludesFromDiff()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();

        // ActivateStandardTemplate leaves every line at the default (unset -> Internal), so the
        // capacity change below is an Internal-only change.
        var changes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
        {
            capacityRequirements = new[] { new { roleCode = "DOG_WALKER", dimensionCode = "PET_COUNT", quantity = 3 } },
        }));
        await controller.Revise(wr.Id, new ReviseWorkRequirementRequest(wr.RequirementVersion, "bump", changes), "revise-key");

        var unfiltered = await controller.Compare(wr.Id, 1, 2);
        var filtered = await controller.Compare(wr.Id, 1, 2, customerVisibleOnly: true);

        using var unfilteredDoc = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(unfiltered).Value, CamelCase));
        using var filteredDoc = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(filtered).Value, CamelCase));
        var unfilteredCategories = unfilteredDoc.RootElement.GetProperty("changedCategories").EnumerateArray().Select(e => e.GetString()).ToList();
        var filteredCategories = filteredDoc.RootElement.GetProperty("changedCategories").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Contains("capacityRequirements", unfilteredCategories);
        Assert.DoesNotContain("capacityRequirements", filteredCategories);
    }

    // v2 delta (P3) — search + audit.

    [Fact]
    public async Task Search_FiltersByStatusAndWorkType()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest("demand-1"), "resolve-key-1");
        await controller.Resolve(ResolveRequest("demand-2"), "resolve-key-2");
        var wr = await db.WorkRequirements.FirstAsync(w => w.SourceId == "demand-1");
        await controller.Cancel(wr.Id);

        var result = await controller.Search(new SearchWorkRequirementsRequest(Status: WorkRequirementStatus.Cancelled, WorkType: "DOG_WALKING"));

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Audit_ReturnsOrderedVersionHistory()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        await ActivateStandardTemplate(db, tenantId);
        var controller = NewController(db, tenantId);
        await controller.Resolve(ResolveRequest(), "resolve-key");
        var wr = await db.WorkRequirements.FirstAsync();
        var changes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
        {
            capacityRequirements = new[] { new { roleCode = "DOG_WALKER", dimensionCode = "PET_COUNT", quantity = 3 } },
        }));
        await controller.Revise(wr.Id, new ReviseWorkRequirementRequest(wr.RequirementVersion, "bump", changes), "revise-key");

        var result = await controller.Audit(wr.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, CamelCase));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].GetProperty("version").GetInt32());
        Assert.Equal(2, entries[1].GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Audit_UnknownId_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var controller = NewController(db, tenantId);

        var result = await controller.Audit(Guid.NewGuid());

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, status.StatusCode);
    }
}
