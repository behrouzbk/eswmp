using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eswmp.Shared.Auth;
using Eswmp.Shared.DTOs;
using Eswmp.Work.Controllers;
using Eswmp.Work.Models;
using FluentAssertions;
using Xunit;

namespace Eswmp.Work.IntegrationTests;

[Collection(WorkApiCollection.Name)]
public class DemandsApiTests(WorkApiFactory factory)
{
    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(tenantId, permissions));
        return client;
    }

    private static CreateDemandRequest NewRequest(string externalReferenceId) => new(
        OrganizationId: null,
        DemandType: "field-service-visit",
        SourceSystem: "integration-tests",
        SourceChannel: "api",
        Priority: DemandPriority.Normal,
        Summary: "Integration test demand",
        Description: null,
        RequestedStartAtUtc: DateTimeOffset.UtcNow.AddDays(1),
        RequestedEndAtUtc: DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
        RequestedTimezone: "UTC",
        LocationReference: null,
        ExternalReferenceType: "test",
        ExternalReferenceId: externalReferenceId);

    private static HttpRequestMessage CreateDemandHttpRequest(CreateDemandRequest body, string idempotencyKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/demands")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    [Fact]
    public async Task Create_ThenGetById_RoundTripsThroughRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.DemandCreate, EswmpPermissions.DemandRead);

        var createResponse = await client.SendAsync(CreateDemandHttpRequest(NewRequest("ext-1"), Guid.NewGuid().ToString()));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<Demand>(TestJson.Options);

        var getResponse = await client.GetAsync($"/api/v1/demands/{created!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<Demand>(TestJson.Options);
        fetched!.Id.Should().Be(created.Id);
        fetched.Status.Should().Be(DemandStatus.Received);
    }

    [Fact]
    public async Task Create_RepeatedIdempotencyKeySameBody_ReplaysOriginalDemand()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.DemandCreate);
        var request = NewRequest("ext-idempotent");
        var key = Guid.NewGuid().ToString();

        var first = await client.SendAsync(CreateDemandHttpRequest(request, key));
        var second = await client.SendAsync(CreateDemandHttpRequest(request, key));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstDemand = await first.Content.ReadFromJsonAsync<Demand>(TestJson.Options);
        var secondDemand = await second.Content.ReadFromJsonAsync<Demand>(TestJson.Options);
        secondDemand!.Id.Should().Be(firstDemand!.Id);
    }

    [Fact]
    public async Task Create_WithoutDemandCreatePermission_ReturnsForbidden()
    {
        var client = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.DemandRead);

        var response = await client.SendAsync(CreateDemandHttpRequest(NewRequest("ext-forbidden"), Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithoutJwt_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.SendAsync(CreateDemandHttpRequest(NewRequest("ext-anon"), Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_ForAnotherTenantsDemand_ReturnsNotFound()
    {
        var ownerClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.DemandCreate);
        var createResponse = await ownerClient.SendAsync(CreateDemandHttpRequest(NewRequest("ext-cross-tenant"), Guid.NewGuid().ToString()));
        var created = await createResponse.Content.ReadFromJsonAsync<Demand>(TestJson.Options);

        var otherTenantClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.DemandRead);
        var response = await otherTenantClient.GetAsync($"/api/v1/demands/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ValidateThenAccept_TransitionsDemandToAccepted()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.DemandCreate, EswmpPermissions.DemandRead, EswmpPermissions.DemandTransition);
        var createResponse = await client.SendAsync(CreateDemandHttpRequest(NewRequest("ext-lifecycle"), Guid.NewGuid().ToString()));
        var created = await createResponse.Content.ReadFromJsonAsync<Demand>(TestJson.Options);

        var validateResponse = await client.PostAsync($"/api/v1/demands/{created!.Id}/validate", content: null);
        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var acceptResponse = await client.PostAsync($"/api/v1/demands/{created.Id}/accept", content: null);
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<Demand>(TestJson.Options);
        accepted!.Status.Should().Be(DemandStatus.Accepted);
    }

    [Fact]
    public async Task Accept_WithOnlyDemandCreatePermission_ReturnsForbidden()
    {
        // demand.create authorizes submitting/validating work, not accepting/rejecting/
        // cancelling it — that's demand.transition, a deliberately separate authority
        // (docs/api/specs/01-demand-intake-api.md §9.1) so a partner can be granted
        // create-only access.
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.DemandCreate, EswmpPermissions.DemandRead);
        var createResponse = await client.SendAsync(CreateDemandHttpRequest(NewRequest("ext-transition-forbidden"), Guid.NewGuid().ToString()));
        var created = await createResponse.Content.ReadFromJsonAsync<Demand>(TestJson.Options);
        await client.PostAsync($"/api/v1/demands/{created!.Id}/validate", content: null);

        var acceptResponse = await client.PostAsync($"/api/v1/demands/{created.Id}/accept", content: null);

        acceptResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_ConcurrentSameIdempotencyKey_ResultsInExactlyOneDemand()
    {
        // Two requests racing on a brand-new Idempotency-Key can both pass the
        // "does this key exist yet" check before either commits; the unique
        // (TenantId, IdempotencyKey) index is the real arbiter (spec §10.3) — the
        // loser must transparently replay the winner's response, not surface the
        // constraint violation. Only exercisable against a real database — the
        // InMemory provider used by unit tests doesn't enforce the unique index.
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.DemandCreate, EswmpPermissions.DemandRead);
        var request = NewRequest("ext-idempotency-race");
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            client.SendAsync(CreateDemandHttpRequest(request, key)),
            client.SendAsync(CreateDemandHttpRequest(request, key)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var demands = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<Demand>(TestJson.Options)));
        demands[0]!.Id.Should().Be(demands[1]!.Id);

        var searchResponse = await client.PostAsJsonAsync("/api/v1/demands/search", new DemandSearchRequest(
            Status: null, Priority: null, DemandType: null, FulfillmentMode: null, FromUtc: null, ToUtc: null));
        var page = await searchResponse.Content.ReadFromJsonAsync<PagedResult<Demand>>(TestJson.Options);
        page!.Items.Count(d => d.ExternalReferenceId == "ext-idempotency-race").Should().Be(1);
    }
}
