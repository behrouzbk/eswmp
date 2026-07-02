# ESWMP — Development Status

> Living document. Updated after every task. Last updated: 2026-07-02.

---

## Changelog

| Date | Change |
|---|---|
| 2026-07-02 | Initial scaffold — repository created, seeded from PetZiv's proven infrastructure patterns (generalized, no PetZiv naming), `Eswmp.Core`/`Eswmp.Assignment`/`Eswmp.Rules`/`Eswmp.Gateway`/`Eswmp.Shared` all build clean, 19 unit tests passing |

---

## 1. What has been built

### Eswmp.Shared (`src/Eswmp.Shared/`)

| Component | Status |
|---|---|
| `Auth/EswmpClaimTypes.cs`, `RequirePermissionAttribute.cs`, `PermissionAuthorizationHandler.cs` | ✅ |
| `Middleware/CorrelationIdMiddleware.cs`, `TenantResolutionMiddleware.cs` | ✅ |
| `Extensions/HealthCheckExtensions.cs`, `ServiceCollectionExtensions.cs` | ✅ |
| `Infrastructure/MassTransitExtensions.cs`, `OpenTelemetryExtensions.cs`, `ResilienceExtensions.cs` | ✅ |
| `DTOs/CommonDtos.cs` (`PagedResult`, `TenantScopedEntity`, `BaseEntity`) | ✅ |
| `Events/SchedulingEvents.cs` (6 domain events) | ✅ |
| `Generated/` (NSwag typed clients) | 🔴 Not yet generated — run `scripts/generate-csharp-clients.ps1` once services are running |

### Eswmp.Core (`src/Eswmp.Core/`) — port 6001

| Component | Status |
|---|---|
| Models: `Tenant`, `Resource`, `AvailabilityRule`, `AvailabilityException`, `Reservation`, `Appointment`, `DurationSizeBracket`, `DurationTagRule` | ✅ |
| `CoreDbContext` with tenant query filters | ✅ |
| `SlotOptimizer` (gap-elimination algorithm, adapted from PetZiv, tested) | ✅ |
| `ReservationDurationEstimator` (generalized duration/buffer engine) | ✅ |
| `ResourcesController`, `AvailabilityController`, `SlotsController`, `DurationController`, `ReservationsController`, `CalendarController` | ✅ |
| EF Core migration (`InitialCreate`) | 🔴 Not yet generated — run `dotnet ef migrations add InitialCreate --project src\Eswmp.Core` after `docker compose up -d postgres` |

### Eswmp.Assignment (`src/Eswmp.Assignment/`) — port 6002

| Component | Status |
|---|---|
| `AssignmentLog` model + `AssignmentDbContext` | ✅ |
| `AssignmentScorer` (proximity/skill/workload/speed-fit weighted scoring) | ✅ |
| `AssignmentController` (`POST /score`, `POST /`) | ✅ |
| EF Core migration | 🔴 Not yet generated |

### Eswmp.Rules (`src/Eswmp.Rules/`) — port 6003

| Component | Status |
|---|---|
| `WorkflowState` enum (15 states) + `WorkflowTransitionValidator` | ✅ |
| `BusinessRule` (jsonb definition) + `WorkflowTransitionLog` models | ✅ |
| `RulesDbContext` | ✅ |
| `RulesController`, `WorkflowController` | ✅ |
| EF Core migration | 🔴 Not yet generated |

### Eswmp.Gateway (`src/Eswmp.Gateway/`) — port 6000

YARP routing to all 3 services, JWT validation, 200 req/s rate limit, health
aggregation. ✅ Built, not yet run against live services.

### Contracts (`contracts/openapi/`)

`core.v1.yaml`, `assignment.v1.yaml`, `rules.v1.yaml` — all three written
contract-first, matching the controllers implemented. ✅

### Infrastructure

| Item | Status |
|---|---|
| `docker-compose.yml` + `docker-compose.override.yml` | ✅ Written, not yet run end-to-end |
| `infrastructure/postgres/init-databases.sql` | ✅ |
| `infrastructure/terraform/*.tf` (resource group, ACR, Postgres Flexible Server, Redis, Container Apps, Key Vault) | ✅ Written, not yet applied |
| `.github/workflows/build.yml` | ✅ Written, not yet run in CI |

### Tests

| Project | Coverage | Status |
|---|---|---|
| `Eswmp.Core.Tests` | `SlotOptimizerTests` (6 cases), `ReservationDurationEstimatorTests` (7 cases) | ✅ 13/13 passing |
| `Eswmp.Assignment.Tests` | `AssignmentScorerTests` (6 cases) | ✅ 6/6 passing |

**Verified 2026-07-02**: `dotnet restore Eswmp.sln`, `dotnet build Eswmp.sln -c Release` (0 errors), both test projects passing (19/19 total).

---

## 2. What is NOT yet built

### Before first real use

| Item | Priority | Notes |
|---|---|---|
| EF Core migrations for all 3 services | High | Need `docker compose up -d postgres` running first, then `dotnet ef migrations add InitialCreate` per service |
| End-to-end docker-compose run | High | Written but not yet verified with `docker compose up -d --build` |
| NSwag-generated typed clients | Medium | No cross-service HTTP calls exist yet, so not urgent, but needed before `Eswmp.Assignment`/`Eswmp.Rules` can call `Eswmp.Core` |
| First real tenant integration (PetZiv) | High | This is the actual point of the platform — see `docs/ESWMP_VISION.md` §16, "PetZiv becomes Tenant #1" |

### Deferred by design — see `docs/ARCHITECTURE.md` §7

Routing/Geofencing/Travel Time, Task/Approval/Notification/Audit/Reporting/
Integration Gateway services, Temporal/Dapr workflow engine, Drools/RulesEngine
integration, plugin framework, PostGIS spatial types.

---

## 3. Running the project

```powershell
# Start infrastructure
docker compose up -d postgres redis rabbitmq jaeger

# Generate first migration per service (one-time, after postgres is up)
dotnet ef migrations add InitialCreate --project src\Eswmp.Core
dotnet ef migrations add InitialCreate --project src\Eswmp.Assignment
dotnet ef migrations add InitialCreate --project src\Eswmp.Rules

# Run all services (separate terminals)
dotnet run --project src\Eswmp.Core          # http://localhost:6001/swagger
dotnet run --project src\Eswmp.Assignment    # http://localhost:6002/swagger
dotnet run --project src\Eswmp.Rules         # http://localhost:6003/swagger
dotnet run --project src\Eswmp.Gateway       # http://localhost:6000

# Run tests
dotnet test Eswmp.sln
```
