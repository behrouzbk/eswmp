# ESWMP — Enterprise Scheduling & Workforce Management Platform

A generic, industry-agnostic scheduling, availability, reservation, assignment, and
workflow platform. Any business that schedules **resources** — people, vehicles,
rooms, equipment — against **reservations** can run on it. It knows nothing about
pets, grooming, or any specific vertical: the only thing a caller passes in beyond
scheduling data is `externalReferenceType` / `externalReferenceId`, opaque pointers
back into the caller's own domain (see `docs/ESWMP_VISION.md`, "Key rule").

PetZiv (a pet-care marketplace) is the first tenant/consumer of this platform, not
part of its codebase.

## Why this exists

See `docs/ESWMP_VISION.md` for the full product rationale (originally proposed by
Hassan, a co-founder). Short version: scheduling and workforce assignment is a
horizontal problem. Building it once, generically, and licensing it — rather than
burying it inside a single vertical product — is the business bet this repo makes.

## Architecture at a glance

Three ASP.NET Core Web API services + a gateway, C# / .NET 9, database-per-service
PostgreSQL, contract-first OpenAPI, event-driven via MassTransit. Full detail in
`docs/ARCHITECTURE.md`.

| Service | Port (local) | Owns |
|---|---|---|
| `Eswmp.Gateway` | 6000 | YARP reverse proxy, JWT validation, rate limiting |
| `Eswmp.Core` | 6001 | Resource, Calendar, AvailabilityRule, Reservation, Appointment — the scheduling primitives (Hassan's "Scheduling Service") |
| `Eswmp.Assignment` | 6002 | Auto-assignment scoring — which Resource should fulfill a Reservation |
| `Eswmp.Rules` | 6003 | Workflow state machine + tenant-configurable business rules |

`Eswmp.Shared` is a class library referenced by all four services (auth, tenant
context, resilience, observability, messaging, common DTOs — no business logic).

This is deliberately **not** the ~20-microservice decomposition originally sketched
— see `docs/ESWMP_VISION.md` for the source list and `docs/ARCHITECTURE.md` for why
it was consolidated to 3 services + gateway for an initial two-person team, mirroring
the same lesson PetZiv's own architecture already learned (35 domains → 5 clusters).
Split further only when real scale or licensing needs force it.

## Local development

Prerequisites: .NET 9 SDK, Docker Desktop, PowerShell 7+.

```powershell
# 1. Copy environment template and fill in secrets
Copy-Item .env.example .env

# 2. Start infrastructure (Postgres, Redis, RabbitMQ, Jaeger)
docker compose up -d postgres redis rabbitmq jaeger

# 3. Run each service (separate terminals), or use the Eswmp.sln multi-startup in VS
dotnet run --project src\Eswmp.Core          # http://localhost:6001/swagger
dotnet run --project src\Eswmp.Assignment    # http://localhost:6002/swagger
dotnet run --project src\Eswmp.Rules         # http://localhost:6003/swagger
dotnet run --project src\Eswmp.Gateway       # http://localhost:6000

# 4. Run tests
dotnet test Eswmp.sln

# 5. Regenerate typed clients after any contracts/openapi/*.yaml change
.\scripts\generate-csharp-clients.ps1
```

Or run everything in Docker:

```powershell
docker compose up -d --build
```

### PostgreSQL connection (local dev)

- Docker host port: **6432** (maps to container 5432) — chosen to avoid colliding
  with PetZiv's local Postgres on 5433 in case both stacks run side by side.
- Connection string: `Host=localhost;Port=6432;Database=eswmp_core;Username=eswmp;Password=<see .env>`

## Status

Phase 0 scaffold. See `docs/DEVELOPMENT_STATUS.md` for what's built and
`docs/TASK_BOARD.md` for what's next.
