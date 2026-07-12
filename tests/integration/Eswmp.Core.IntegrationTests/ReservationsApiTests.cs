using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eswmp.Core.Controllers;
using Eswmp.Core.Models;
using Eswmp.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Eswmp.Core.IntegrationTests;

[Collection(CoreApiCollection.Name)]
public class ReservationsApiTests(CoreApiFactory factory)
{
    private HttpClient AuthenticatedClient(Guid tenantId, params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(tenantId, permissions));
        return client;
    }

    private static CreateReservationRequest NewRequest(Guid resourceId, DateTimeOffset start, DateTimeOffset end) =>
        new(resourceId, start, end, HoldDurationMinutes: 15, ExternalReferenceType: "test", ExternalReferenceId: Guid.NewGuid().ToString());

    [Fact]
    public async Task Create_ThenGetById_RoundTripsThroughRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.ReservationCreate, EswmpPermissions.ReservationRead);
        var start = DateTimeOffset.UtcNow.AddHours(1);

        var createResponse = await client.PostAsJsonAsync("/api/v1/reservations", NewRequest(Guid.NewGuid(), start, start.AddHours(1)));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<Reservation>(TestJson.Options);

        var getResponse = await client.GetAsync($"/api/v1/reservations/{created!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<Reservation>(TestJson.Options);
        fetched!.Id.Should().Be(created.Id);
        fetched.Status.Should().Be(ReservationStatus.Held);
    }

    [Fact]
    public async Task Create_WithoutReservationCreatePermission_ReturnsForbidden()
    {
        var client = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.ReservationRead);
        var start = DateTimeOffset.UtcNow.AddHours(1);

        var response = await client.PostAsJsonAsync("/api/v1/reservations", NewRequest(Guid.NewGuid(), start, start.AddHours(1)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithoutJwt_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var start = DateTimeOffset.UtcNow.AddHours(1);

        var response = await client.PostAsJsonAsync("/api/v1/reservations", NewRequest(Guid.NewGuid(), start, start.AddHours(1)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_OverlappingExistingHold_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var client = AuthenticatedClient(tenantId, EswmpPermissions.ReservationCreate);
        var start = DateTimeOffset.UtcNow.AddHours(2);

        var first = await client.PostAsJsonAsync("/api/v1/reservations", NewRequest(resourceId, start, start.AddHours(1)));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var overlapping = await client.PostAsJsonAsync(
            "/api/v1/reservations", NewRequest(resourceId, start.AddMinutes(30), start.AddMinutes(90)));

        overlapping.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetById_ForAnotherTenantsReservation_ReturnsNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        var ownerClient = AuthenticatedClient(ownerTenantId, EswmpPermissions.ReservationCreate);
        var start = DateTimeOffset.UtcNow.AddHours(3);

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/reservations", NewRequest(Guid.NewGuid(), start, start.AddHours(1)));
        var created = await createResponse.Content.ReadFromJsonAsync<Reservation>(TestJson.Options);

        var otherTenantClient = AuthenticatedClient(Guid.NewGuid(), EswmpPermissions.ReservationRead);
        var response = await otherTenantClient.GetAsync($"/api/v1/reservations/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
