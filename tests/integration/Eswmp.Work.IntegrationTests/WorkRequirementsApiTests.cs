using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Eswmp.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Eswmp.Work.IntegrationTests;

/// <summary>
/// Exercises the reconciled Work Requirement model (docs/api/specs/02-work-requirement-api.md)
/// against a real Postgres via WorkApiFactory/Testcontainers — mirrors DemandsApiTests.cs and
/// RequirementDefinitionsApiTests.cs conventions. Requires Docker; see WK-07 in TASK_BOARD.md
/// for this environment's known Testcontainers fragility.
/// </summary>
[Collection(WorkApiCollection.Name)]
public class WorkRequirementsApiTests(WorkApiFactory factory)
{
    private static readonly string[] AllTemplatePermissions =
    [
        EswmpPermissions.WorkRequirementTemplateCreate,
        EswmpPermissions.WorkRequirementTemplateRead,
        EswmpPermissions.WorkRequirementTemplateUpdate,
        EswmpPermissions.WorkRequirementTemplateActivate,
        EswmpPermissions.WorkRequirementRead,
        EswmpPermissions.WorkRequirementResolve,
        EswmpPermissions.WorkRequirementRevise,
        EswmpPermissions.WorkRequirementValidate,
        EswmpPermissions.WorkRequirementExplain,
    ];

    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(tenantId, permissions));
        return client;
    }

    private static HttpRequestMessage PostWithIdempotencyKey(string url, object body, string idempotencyKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private static object ValidDefinitions() => new
    {
        resourceRequirements = new[] { new { roleCode = "DOG_WALKER", resourceCategory = "Person", minimumQuantity = 1, maximumQuantity = 1 } },
        durationRequirement = new { durationType = "Fixed", estimatedDurationMinutes = 60 },
        capabilityRequirements = new[] { new { roleCode = "DOG_WALKER", capabilityCode = "DOG_WALKING", mandatory = true } },
        capacityRequirements = new[] { new { roleCode = "DOG_WALKER", dimensionCode = "PET_COUNT", quantity = 1, unit = "COUNT" } },
        locationRequirement = new { locationMode = "CustomerLocation" },
    };

    private async Task<(Guid TemplateId, string Code)> CreateAndActivateTemplate(HttpClient client, string code)
    {
        var createResponse = await client.SendAsync(PostWithIdempotencyKey(
            "/api/v1/work-requirement-templates",
            new { code, name = "Standard Dog Walk", workType = "DOG_WALKING" },
            Guid.NewGuid().ToString()));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = created.GetProperty("id").GetGuid();

        var configureResponse = await client.PutAsJsonAsync(
            $"/api/v1/work-requirement-templates/{templateId}/versions/1/requirements", ValidDefinitions());
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activateResponse = await client.PostAsync($"/api/v1/work-requirement-templates/{templateId}/versions/1/activate", null);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return (templateId, code);
    }

    [Fact]
    public async Task Resolve_AgainstActiveTemplate_CreatesValidWorkRequirement()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, AllTemplatePermissions);
        await CreateAndActivateTemplate(client, "DOG_WALK_STANDARD_1");

        var resolveResponse = await client.SendAsync(PostWithIdempotencyKey(
            "/api/v1/work-requirements/resolve",
            new { sourceType = "Demand", sourceId = "demand-1", sourceVersion = 1, templateCode = "DOG_WALK_STANDARD_1" },
            Guid.NewGuid().ToString()));

        resolveResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<JsonElement>();
        resolved.GetProperty("status").GetString().Should().Be("Valid");

        var workRequirementId = resolved.GetProperty("workRequirementId").GetGuid();
        var getResponse = await client.GetAsync($"/api/v1/work-requirements/{workRequirementId}/resolved");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var wire = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        wire.GetProperty("resourceRequirements").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Resolve_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.WorkRequirementResolve);

        var response = await client.PostAsJsonAsync("/api/v1/work-requirements/resolve",
            new { sourceType = "Demand", sourceId = "demand-1", templateCode = "NO_SUCH_TEMPLATE" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resolve_TemplateNotActive_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.WorkRequirementResolve);

        var response = await client.SendAsync(PostWithIdempotencyKey(
            "/api/v1/work-requirements/resolve",
            new { sourceType = "Demand", sourceId = "demand-1", templateCode = "NO_SUCH_TEMPLATE" },
            Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("TEMPLATE_NOT_ACTIVE");
    }

    [Fact]
    public async Task GetById_ForAnotherTenantsWorkRequirement_ReturnsNotFound()
    {
        var ownerClient = AuthenticatedClient(Guid.NewGuid(), AllTemplatePermissions);
        await CreateAndActivateTemplate(ownerClient, "DOG_WALK_STANDARD_2");
        var resolveResponse = await ownerClient.SendAsync(PostWithIdempotencyKey(
            "/api/v1/work-requirements/resolve",
            new { sourceType = "Demand", sourceId = "demand-x", templateCode = "DOG_WALK_STANDARD_2" },
            Guid.NewGuid().ToString()));
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workRequirementId = resolved.GetProperty("workRequirementId").GetGuid();

        var otherTenantClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.WorkRequirementRead);
        var response = await otherTenantClient.GetAsync($"/api/v1/work-requirements/{workRequirementId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancel_ThenCancelAgain_ReturnsStatusConflict()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, AllTemplatePermissions.Append(EswmpPermissions.WorkRequirementRevise).ToArray());
        await CreateAndActivateTemplate(client, "DOG_WALK_STANDARD_3");
        var resolveResponse = await client.SendAsync(PostWithIdempotencyKey(
            "/api/v1/work-requirements/resolve",
            new { sourceType = "Demand", sourceId = "demand-cancel", templateCode = "DOG_WALK_STANDARD_3" },
            Guid.NewGuid().ToString()));
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workRequirementId = resolved.GetProperty("workRequirementId").GetGuid();

        var first = await client.PostAsync($"/api/v1/work-requirements/{workRequirementId}/cancel", null);
        var second = await client.PostAsync($"/api/v1/work-requirements/{workRequirementId}/cancel", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
