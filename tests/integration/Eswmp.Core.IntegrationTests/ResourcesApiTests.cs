using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eswmp.Core.Controllers;
using Eswmp.Core.Models;
using Eswmp.Shared.Auth;
using Eswmp.Shared.DTOs;
using FluentAssertions;
using Xunit;

namespace Eswmp.Core.IntegrationTests;

[Collection(CoreApiCollection.Name)]
public class ResourcesApiTests(CoreApiFactory factory)
{
    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(tenantId, permissions));
        return client;
    }

    [Fact]
    public async Task Create_ThenList_ReturnsTheCreatedResource_FromRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.ResourceWrite, EswmpPermissions.ResourceRead);
        var request = new CreateResourceRequest("Vehicle", "Test Van", "UTC", 1, ["heavy-lifting"], null, null);

        var createResponse = await client.PostAsJsonAsync("/api/v1/resources", request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await client.GetAsync("/api/v1/resources");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<Resource>>(TestJson.Options);

        page!.Items.Should().ContainSingle(r => r.Name == "Test Van" && r.ResourceType == "Vehicle");
    }

    [Fact]
    public async Task List_DoesNotReturnAnotherTenantsResources()
    {
        var tenantAClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.ResourceWrite);
        await tenantAClient.PostAsJsonAsync(
            "/api/v1/resources", new CreateResourceRequest("Vehicle", "Tenant A Van", "UTC", 1, null, null, null));

        var tenantBClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.ResourceRead);
        var listResponse = await tenantBClient.GetAsync("/api/v1/resources");
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<Resource>>(TestJson.Options);

        page!.Items.Should().NotContain(r => r.Name == "Tenant A Van");
    }
}
