using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eswmp.Shared.Auth;
using Eswmp.Work.Controllers;
using Eswmp.Work.Models;
using FluentAssertions;
using Xunit;

namespace Eswmp.Work.IntegrationTests;

[Collection(WorkApiCollection.Name)]
public class RequirementDefinitionsApiTests(WorkApiFactory factory)
{
    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(tenantId, permissions));
        return client;
    }

    private static CreateRequirementDefinitionRequest NewRequest(string code) =>
        new(code, "Integration Test Requirement", "Created by an integration test", "field-service");

    [Fact]
    public async Task Create_ThenGetById_RoundTripsThroughRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.RequirementDefinitionWrite, EswmpPermissions.RequirementDefinitionRead);

        var createResponse = await client.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-ROUNDTRIP"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<RequirementDefinition>(TestJson.Options);

        var getResponse = await client.GetAsync($"/api/v1/requirement-definitions/{created!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<RequirementDefinition>(TestJson.Options);
        fetched!.Id.Should().Be(created.Id);
        fetched.Code.Should().Be("WR-ROUNDTRIP");
        fetched.Status.Should().Be(RequirementDefinitionStatus.Draft);
    }

    [Fact]
    public async Task Create_WithoutRequirementDefinitionWritePermission_ReturnsForbidden()
    {
        var client = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.RequirementDefinitionRead);

        var response = await client.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-FORBIDDEN"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithoutJwt_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-ANON"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_ForAnotherTenantsDefinition_ReturnsNotFound()
    {
        var ownerClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.RequirementDefinitionWrite);
        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-CROSS-TENANT"));
        var created = await createResponse.Content.ReadFromJsonAsync<RequirementDefinition>(TestJson.Options);

        var otherTenantClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.RequirementDefinitionRead);
        var response = await otherTenantClient.GetAsync($"/api/v1/requirement-definitions/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SameCode_AcrossDifferentTenants_DoesNotConflict()
    {
        var tenantAClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.RequirementDefinitionWrite);
        var tenantBClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.RequirementDefinitionWrite);

        var firstResponse = await tenantAClient.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-SHARED-CODE"));
        var secondResponse = await tenantBClient.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-SHARED-CODE"));

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateVersion_ThenActivate_MakesVersionActive()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.RequirementDefinitionWrite, EswmpPermissions.RequirementDefinitionRead);
        var createResponse = await client.PostAsJsonAsync("/api/v1/requirement-definitions", NewRequest("WR-ACTIVATE"));
        var created = await createResponse.Content.ReadFromJsonAsync<RequirementDefinition>(TestJson.Options);

        var versionRequest = new RequirementDefinitionVersionRequest(
            ChangeSummary: "initial",
            EffectiveFrom: null,
            EffectiveTo: null,
            DurationType: DefinitionDurationType.Fixed,
            FixedDurationMinutes: 60,
            MinimumDurationMinutes: null,
            ExpectedDurationMinutes: null,
            MaximumDurationMinutes: null,
            PreWorkBufferMinutes: 0,
            PostWorkBufferMinutes: 0,
            ResourceRequirements:
            [
                new DefinitionResourceRequirementDto("technician", null, 1, 1, 1, true, null, null, null)
            ],
            LocationConstraints: null);

        var versionResponse = await client.PostAsJsonAsync($"/api/v1/requirement-definitions/{created!.Id}/versions", versionRequest);
        versionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var version = await versionResponse.Content.ReadFromJsonAsync<RequirementDefinitionVersion>(TestJson.Options);

        var activateResponse = await client.PostAsJsonAsync(
            $"/api/v1/requirement-definitions/{created.Id}/versions/{version!.VersionNumber}/activate",
            new ActivateDefinitionVersionRequest(created.ConcurrencyVersion));

        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activated = await activateResponse.Content.ReadFromJsonAsync<RequirementDefinitionVersion>(TestJson.Options);
        activated!.Status.Should().Be(RequirementDefinitionVersionStatus.Active);
    }
}
