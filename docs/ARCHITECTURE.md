# ESWMP — Architecture Reference

> **Scope:** Backend services, container topology, data isolation, communication
> contracts, auth scope, and how this maps to `docs/ESWMP_VISION.md`.

## 1. Architecture philosophy

ESWMP consolidates the vision document's ~20-service domain map into **3 bounded
services + a gateway**, for a small initial team — the same reasoning PetZiv's
own Cluster Engineering Guide used to consolidate 35 business domains into 5
clusters. Split further only when real scale, multiple paying tenants, or a
licensing requirement forces it, not speculatively.

| Vision domain | This repo |
|---|---|
| Scheduling, Availability, Calendar, Reservation, Resource, Capacity, Conflict Detection | `Eswmp.Core` |
| Assignment, Optimization Engine (initial Haversine/skill/workload scoring only — routing/geofencing/travel-time deferred) | `Eswmp.Assignment` |
| Rules Engine, Workflow Engine | `Eswmp.Rules` |
| Task, Approval, Notification Adapter, Audit, Reporting, Integration Gateway | Not yet built — see §7 "Deferred" |

### Core principles

| Principle | Implementation |
|---|---|
| No vertical-specific concepts | `Resource`/`Reservation`/`Appointment` only; caller-domain meaning lives entirely in `ExternalReferenceType`/`ExternalReferenceId` |
| Bounded service isolation | Each service owns its own database; no cross-service DB reads |
| Contract-first API design | OpenAPI 3.1 YAML in `contracts/openapi/` written before controller code |
| Rules over hardcoding | Tenant-variable logic (duration buffers, size brackets) lives in DB rows, not compiled `switch` statements |
| Multi-tenant from day one | `TenantId` query filter on every entity, in every service, from the first migration |

## 2. Service catalog

### 2.1 Eswmp.Gateway

YARP reverse proxy, port 6000. JWT validation before forwarding, 200 req/s rate
limit, health aggregation across the 3 backend services. No database.

### 2.2 Eswmp.Core — port 6001, db `eswmp_core`

Owns: `Tenant`, `Resource`, `AvailabilityRule`, `AvailabilityException`,
`Reservation`, `Appointment`, `DurationSizeBracket`, `DurationTagRule`.

Endpoints: resource CRUD, availability rules, slot search (`SlotOptimizer` —
gap-elimination algorithm), duration estimation (`ReservationDurationEstimator`
— tenant-configurable size/tag rules), reservation hold/confirm/cancel,
per-resource calendar.

Publishes: `SlotReservedEvent`, `ReservationConfirmedEvent`,
`ReservationCancelledEvent`, `ReservationExpiredEvent`, `AvailabilityChangedEvent`,
`ResourceUnavailableEvent`.

### 2.3 Eswmp.Assignment — port 6002, db `eswmp_assignment`

Owns: `AssignmentLog`. Stateless `AssignmentScorer` ranks candidate Resources by
a weighted blend of proximity (Haversine), skill match, workload, and
speed/capacity fit — deliberately separate from Core so "which Resource fulfills
this Reservation" never leaks into the scheduling primitives.

### 2.4 Eswmp.Rules — port 6003, db `eswmp_rules`

Owns: `BusinessRule` (tenant-configurable rule rows, `jsonb` definition),
`WorkflowTransitionLog`. `WorkflowTransitionValidator` enforces the 15-state
workflow from `docs/ESWMP_VISION.md` §9 as a guard-clause state machine.

## 3. Database architecture

Database-per-service, PostgreSQL 16, no PostGIS yet — `Resource.Location*` is
plain lat/lng; add PostGIS when routing/geofencing (deferred, see §7) is built.
Local Docker host port **6432** (chosen to avoid colliding with PetZiv's 5433 if
both stacks run side by side on the same machine).

Every entity inherits `TenantScopedEntity` (`Eswmp.Shared.DTOs`); every
`DbContext` registers `HasQueryFilter(e => e.TenantId == tenantContext.TenantId)`
for each one. No manual `.Where(TenantId == ...)` anywhere else in the codebase.

## 4. Communication architecture

**Synchronous (HTTP)**: cross-service queries use NSwag-generated typed clients
from `contracts/openapi/*.v1.yaml`, placed in `Eswmp.Shared/Generated/` (run
`scripts/generate-csharp-clients.ps1`). Hand-written `HttpClient` calls between
Eswmp services are prohibited — see CLAUDE.md rule 5.

**Asynchronous (events)**: MassTransit, RabbitMQ locally / Azure Service Bus in
production, selected via `MessageBus:Transport` config. Domain events are
`record` types in `Eswmp.Shared.Events`.

## 5. Auth scope — a deliberate cut

**ESWMP does not issue JWTs or run a user/login system.** It validates tokens
issued by whatever product embeds it — `tenant_id` and `permissions` claims are
all it needs. This is different from PetZiv's Platform Core, which *does* own
full identity (registration, MFA, password resets). Building that here would
duplicate what every consuming product already has, and would turn ESWMP into
an identity provider it has no business being. If a standalone admin console is
ever built for ESWMP directly (rather than embedded), token issuance becomes a
new, explicit scope decision at that point — not assumed now.

Multi-tenant isolation policy: `TenantResolutionMiddleware` reads `tenant_id`
from the JWT into `ITenantContext` before any controller runs; `RequirePermission`
attributes (from `Eswmp.Shared.Auth`) gate individual endpoints against the
`permissions` claim.

## 6. What was reused from PetZiv, and what changed

Seeded from `C:\workspace\petziv` (a separate repository — see CLAUDE.md
"Relationship to PetZiv"):

| Reused as-is (generic infra, renamed `PetZiv.*` → `Eswmp.*`) | Generalized (business logic, renamed and reworked) |
|---|---|
| MassTransit/OpenTelemetry/Polly extensions | `SlotOptimizer` (renamed types only — algorithm untouched) |
| CorrelationId + TenantResolution middleware | `DurationCalculator` → `ReservationDurationEstimator` (hardcoded buffers → `DurationSizeBracket`/`DurationTagRule` DB rows) |
| Permission-claim auth handler/attribute | Auto-assignment scoring — designed fresh as `AssignmentScorer`, since PetZiv's own version (CEG §9.6, task OP-08) was never actually built; only its scoring-factor design was reused |
| Health check + common DTO patterns | Job lifecycle state machine → generalized 15-state `WorkflowState` from `docs/ESWMP_VISION.md` §9 |
| Contract-first OpenAPI workflow, NSwag/openapi-typescript scripts | — |
| Docker Compose / Terraform skeletons | — |

Left behind entirely: `Client`/`Pet`/`Appointment` (PetZiv's, not this repo's)
business models, Stripe/SendGrid/Twilio integrations, PetZiv's OpenAPI contracts,
the PetZiv frontend, `JobCardDto` (the *privacy-projection technique* — never
returning caller-domain PII from a service-facing endpoint — is reused in spirit
via the `ExternalReferenceType`/`Id` opacity rule, but no code was copied).

## 7. Deferred (not built yet, and why)

| Vision component | Status | Why deferred |
|---|---|---|
| Temporal/Dapr Workflow engine | Not built | `WorkflowTransitionValidator`'s guard-clause state machine covers the MVP need; a durable workflow engine is justified once approval steps or long-running sagas exist |
| Drools / Microsoft RulesEngine | Not built | `BusinessRule` stores `jsonb` rule definitions today; wiring a real rules engine is deferred until there's an actual catalogue of rule types to evaluate against it |
| Routing / Geofencing / Travel Time services | Not built | No PostGIS yet; add when a consuming tenant needs zone-based or route-sequencing features |
| Task / Approval / Notification Adapter / Audit / Reporting / Integration Gateway | Not built | No consuming tenant has exercised these yet — build against real requirements, not speculatively |
| Plugin framework (§15 of ESWMP_VISION.md) | Not built | Premature without a second real consumer to prove the extension points against |
| Kafka / OpenSearch / Quartz.NET | Not used | MassTransit+RabbitMQ and Hangfire-equivalent needs are already covered by patterns proven in PetZiv; revisit only if a specific requirement can't be met with the current stack |

## 8. Technology stack (as built)

| Layer | Technology |
|---|---|
| Backend | C# / .NET 9, ASP.NET Core Web API |
| ORM | Entity Framework Core 9, Npgsql provider |
| Database | PostgreSQL 16 |
| Messaging | MassTransit 8 (RabbitMQ dev / Azure Service Bus prod) |
| Observability | OpenTelemetry -> Jaeger (dev) / Azure Monitor (prod) |
| Resilience | Polly v8 via `Microsoft.Extensions.Http.Resilience` |
| API Gateway | YARP |
| Contract tooling | NSwag (C# clients), openapi-typescript (future TS clients) |
| Containerization | Docker / Docker Compose locally, Azure Container Apps in prod |
| IaC | Terraform |
| CI/CD | GitHub Actions |
