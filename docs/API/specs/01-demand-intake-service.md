# ESWMP — Demand Intake Service

**Diagram reference:** `Arch.jpeg`, Demand & Intake Domain, Box #1
**Workshop source:** `docs/api/ESWMP Demand Intake Service Specification.docx` (Document 01 of ~19, "Proposed for Workshop Review")
**This document's status:** **As-Built** — reflects real, tested code as of 2026-07-07, not a proposal. Where this document and the source `.docx` disagree, this document is authoritative for what's actually running; the `.docx` remains the record of original intent.
**Owning microservice:** `Eswmp.Work` (shared with Box #2, Work Requirement — see §3)

---

## 1. Executive Summary

The Demand Intake Service is the controlled entry point through which a
caller (PetZiv today, other tenants later) submits a request for work into
ESWMP. It exists to separate the caller's own business vocabulary — "a
grooming request," "a plumbing dispatch," "a home nursing visit" — from
ESWMP's internal scheduling primitives, which must never understand what any
of that means. A `Demand` is the generic, opaque unit that results: what
kind of work, roughly when, and an unexamined pointer back into the caller's
own domain.

It answers: *"A request for work has arrived — is it well-formed enough to
act on?"* It does **not** answer which resource performs the work, when
exactly it's scheduled, or whether it's been completed — those belong to
Resource, Availability, Scheduling, Assignment, and Work Management (see §2).

## 2. Domain Boundary

**Owns:** `Demand`, `DemandValidationResult`, `DemandIdempotencyRecord`.

**References but does not own:** `Tenant`, `Organization` (as an opaque
`Guid?`, no Organization service exists yet), a `WorkRequirement` (as an
opaque `Guid?` pointer, set once a demand is matched to one — see
`docs/api/specs/02-work-requirement-service.md`).

**Must never own** (per the source spec's explicit boundary list, still
honored by the as-built code): Customer/Pet/Patient records, provider
commercial profiles, commercial service catalog, pricing, payments,
invoicing, resource availability, slot generation, reservations,
appointments, assignments, dispatch, tasks, work execution, notifications,
payroll, HR records. None of these appear anywhere in `Eswmp.Work`.

**Prohibited dependencies, honored as-built:** `DemandsController` never
queries `Eswmp.Core`'s `CoreDbContext`, never creates a `Reservation` or
`Appointment`, never selects or assigns a `Resource`. The only way a Demand
influences scheduling is indirectly, via `RequirementReferenceId` and the
`DemandAcceptedEvent` — a future Work Management module (not yet built,
see `docs/ARCHITECTURE.md` §7.1) is expected to consume that event and drive
the actual scheduling handoff, not this service.

## 3. Microservice / Deployment

| | |
|---|---|
| **Service** | `Eswmp.Work` |
| **Port** | 6004 |
| **Database** | `eswmp_work` |
| **Schema** | `demand` (this module only — the sibling Work Requirement module lives in schema `requirements` in the same database; see `docs/ARCHITECTURE.md` §1.2) |
| **DbContext** | `WorkDbContext` (`src/Eswmp.Work/Data/WorkDbContext.cs`) |
| **Controller** | `DemandsController` (`src/Eswmp.Work/Controllers/DemandsController.cs`) |
| **Deployment recommendation (per source spec)** | Module within the "Work & Workforce" deployable unit, alongside Work Requirement, Work Management, Assignment, Dispatch, Task Execution — only Demand Intake and Work Requirement are built today |
| **Extraction trigger (per source spec, not yet met)** | Very high external intake volume, independent engineering ownership, partner-integration isolation, marketplace demand ingestion |

`Eswmp.Work` cannot read or write `Eswmp.Core`'s, `Eswmp.Assignment`'s, or
`Eswmp.Rules`' databases directly (`CLAUDE.md` rule 4), and within
`Eswmp.Work` itself, Demand Intake code must not touch the `requirements`
schema's tables directly (`CLAUDE.md` rule 11).

## 4. Data Model(s)

All entities inherit `TenantScopedEntity` (`Id`, `TenantId`,
`CreatedAt`/`CreatedBy`/`UpdatedAt`/`UpdatedBy`) or the tenant-less
`BaseEntity`, both from `Eswmp.Shared.DTOs`.

### `Demand` — table `demand.Demands`, aggregate root

| Field | Type | Notes |
|---|---|---|
| `OrganizationId` | `Guid?` | Opaque — no Organization service exists yet |
| `DemandType` | `string` (required) | Caller-defined, e.g. `"ScheduledService"`, `"EmergencyWork"` |
| `SourceSystem` | `string` (required) | Which caller system submitted this |
| `SourceChannel` | `string?` | e.g. `"MobileApp"`, `"CallCenter"` |
| `Status` | `DemandStatus` enum, default `Received` | See lifecycle, §7 |
| `Priority` | `DemandPriority` enum, default `Normal` | `Low, Normal, High, Urgent, Critical` |
| `Summary` / `Description` | `string?` | |
| `RequestedStartAtUtc` / `RequestedEndAtUtc` | `DateTimeOffset?` | |
| `RequestedTimezone` | `string?` | |
| `LocationReference` | `string?`, `jsonb` | Opaque JSON — no Location service exists yet |
| `RequirementReferenceId` | `Guid?` | Set once matched to a `WorkRequirement` |
| `ExternalReferenceType` / `ExternalReferenceId` | `string` (required) | The opaque caller-domain pointer — mirrors `Eswmp.Core`'s `Reservation` pattern, per `CLAUDE.md` rule 1 |
| `Version` | `int`, default `1` | Optimistic concurrency — required as `expectedVersion` on `PATCH` |

`DemandStatus`: `Received, Validating, Ready, Accepted, Rejected, Cancelled, Expired`
`DemandPriority`: `Low, Normal, High, Urgent, Critical`

### `DemandValidationResult` — table `demand.DemandValidationResults`

| Field | Type | Notes |
|---|---|---|
| `DemandId` | `Guid` | |
| `Status` | `DemandValidationStatus` enum | `Invalid, ValidWithWarnings, Valid` |
| `ValidatedAt` | `DateTimeOffset` | |
| `IssuesJson` | `string`, `jsonb` | Serialized `[{code, severity, message}]` |

### `DemandIdempotencyRecord` — table `demand.DemandIdempotencyRecords`

| Field | Type | Notes |
|---|---|---|
| `IdempotencyKey` | `string` (required) | Caller-supplied, from the `Idempotency-Key` header |
| `RequestHash` | `string` (required) | SHA-256 of the create-request JSON — detects same-key/different-body reuse |
| `DemandId` | `Guid` | |
| `ResponseBodyJson` | `string`, `jsonb` | The original 201 response, replayed verbatim on a matching retry |

Unique index: `(TenantId, IdempotencyKey)`.

## 5. API(s)

Base path `/api/v1/demands`. Contract: `contracts/openapi/work.v1.yaml`.

| Method & Path | Permission | Notes |
|---|---|---|
| `POST /` | `demand.create` | Requires `Idempotency-Key` header. Same key + same request body → replays the original 201. Same key + different body → `409`. |
| `GET /{id}` | `demand.read` | |
| `POST /search` | `demand.read` | Filters: status, priority, demandType, created-date range. Paged (`PagedResult<Demand>`). |
| `PATCH /{id}` | `demand.create` | Requires `expectedVersion` (→ `412` on mismatch). Mutability depends on status: `Received` = broadly mutable; `Ready` = **Priority only**, any other field → `409`; `Accepted/Rejected/Cancelled/Expired` = fully immutable → `409`. |
| `POST /{id}/validate` | `demand.create` | Runs the rule set in §6; moves `Received`→`Ready` (or stays `Received` if invalid) and records a `DemandValidationResult`. |
| `POST /{id}/accept` | `demand.create` | Only from `Ready`. Publishes `DemandAcceptedEvent`. |
| `POST /{id}/reject` | `demand.create` | Body: `{reasonCode (required), comment?}`. Not from a terminal status. Publishes `DemandRejectedEvent`. |
| `POST /{id}/cancel` | `demand.create` | Not from a terminal status. Publishes `DemandCancelledEvent`. |
| `GET /{id}/history` | `demand.read` | Returns `[]` today — no dedicated audit table exists yet; deferred until a real audit need is demonstrated (see §9). |

Standard error shape on validation/idempotency failures: `{ error: string }`.

## 6. Policies

- **Idempotency**: every `POST /` requires `Idempotency-Key`; enforced by a
  unique `(TenantId, IdempotencyKey)` index plus a request-body hash check —
  a replay with a different body is a hard `409`, not silently accepted.
- **Optimistic concurrency**: every material mutation after creation carries
  a `Version` counter; `PATCH` requires the caller to supply the
  `expectedVersion` it last saw, `412 Precondition Failed` on mismatch.
- **Status-dependent mutability**: `Received` (broadly mutable) →
  `Validating`/`Ready` (Priority-only) → `Accepted`/`Rejected`/`Cancelled`/
  `Expired` (fully immutable). Enforced identically by `PATCH`, `validate`,
  `accept`, `reject`, and `cancel` all checking against the same
  `ImmutableStatuses` set.
- **Validation rules** (run by `POST /{id}/validate`):
  - `MISSING_EXTERNAL_REFERENCE` (Error) — `ExternalReferenceType`/`Id` blank.
  - `INVALID_TIME_WINDOW` (Error) — `RequestedStartAtUtc >= RequestedEndAtUtc`.
  - `MISSING_DESCRIPTION` (Warning) — neither `Summary` nor `Description` set.
  - Any `Error` → stays `Received`, not `Ready`. Warnings-only → `Ready` with `ValidWithWarnings`.
- **Multi-tenancy**: every entity carries `TenantId`; `WorkDbContext` applies
  `HasQueryFilter` on `Demand` and `DemandIdempotencyRecord` — no manual
  `.Where(TenantId == ...)` anywhere in `DemandsController`.
- **Permissions**: `demand.create` gates every write (including validate/
  accept/reject/cancel — these are state transitions, not pure reads);
  `demand.read` gates get/search/history.
- **Reject reason is mandatory**: `RejectDemandRequest.ReasonCode` is
  required — enforced with an explicit `400` if blank, not just a schema
  hint.

## 7. Lifecycle (state machine)

```
Received --validate(no errors)--> Ready --accept--> Accepted (terminal)
Received --validate(has errors)--> Received (unchanged)
Received/Validating/Ready --reject--> Rejected (terminal)
Received/Validating/Ready --cancel--> Cancelled (terminal)
(Expired is a defined terminal state; nothing transitions a Demand to it yet — no expiry sweep job exists, see §9)
```

## 8. Domain Events

Published via `IPublishEndpoint` (MassTransit — `AddEswmpMessageBus`), defined in
`Eswmp.Shared.Events.WorkEvents`:

| Event | Fields | Fired on |
|---|---|---|
| `DemandAcceptedEvent` | `DemandId, TenantId, CorrelationId` | `POST /{id}/accept` |
| `DemandRejectedEvent` | `DemandId, TenantId, ReasonCode, Comment?, CorrelationId` | `POST /{id}/reject` |
| `DemandCancelledEvent` | `DemandId, TenantId, CorrelationId` | `POST /{id}/cancel` |

All three carry only opaque platform identifiers — never
`ExternalReferenceType`/`Id` or any caller-domain field — per `CLAUDE.md`
rule 1. No consumer exists yet for any of them (Work Management, the
natural consumer of `DemandAcceptedEvent`, is not built — see §10).

## 9. Testing

- **Unit** (`tests/unit/Eswmp.Work.Tests/DemandsControllerTests.cs`):
  idempotency-key replay and conflict, validate lifecycle (both the
  error-stays-`Received` and warnings-go-`Ready` paths), accept/reject/
  cancel guards against terminal statuses, `PATCH` optimistic-concurrency
  (`412`) and the `Ready`-status field-restriction rule.
- **Integration** (`tests/integration/Eswmp.Work.IntegrationTests/DemandsApiTests.cs`,
  Testcontainers Postgres): create→get round trip, idempotency-key replay,
  permission-denied (`403`), unauthenticated (`401`), cross-tenant `404`,
  validate→accept lifecycle. Compiles; not executed in this environment
  (needs Docker).

## 10. Deviations from the source workshop spec (`.docx`, Document #01)

The source spec is a design proposal; building it surfaced a few points
where the as-built code diverges, all intentional simplifications:

- **No `ExternalReference` entity.** The spec models a general-purpose
  `ExternalReference` sub-entity with a `relationshipType` enum
  (`Origin, Parent, Related, RequestedBy, Subject, Contract, Case`). The
  as-built `Demand` instead carries a single flat
  `ExternalReferenceType`/`ExternalReferenceId` pair, matching the simpler
  pattern already proven in `Eswmp.Core`'s `Reservation`. Multiple/typed
  external references were judged speculative until a real multi-reference
  need appears.
- **No `DemandLifecycleEntry` / dedicated history table.** `GET
  /{id}/history` returns `[]` rather than a real audit trail — the spec
  names this entity but gives it no field list either, so nothing was lost
  by deferring it.
- **`DemandTimeConstraint`'s 5 patterns collapsed to 2 fields.** The spec
  describes five time-constraint shapes (Exact/Window/Date-only/Deadline/
  Flexible); the as-built `Demand` just has
  `RequestedStartAtUtc`/`RequestedEndAtUtc`, both nullable — covers Exact
  and Window today; Date-only/Deadline/Flexible would need a `type`
  discriminator added later if a real caller needs them.
- **No consumed events.** The spec lists `TenantSuspended`,
  `OrganizationDeactivated`, `WorkCreated` as (deliberately minimal)
  consumed events; none are wired up, since `Eswmp.Work` doesn't yet react
  to any external state change.
- **Naming inconsistency the spec itself had** (`requestedStartAt` in one
  section vs. `requestedStartAtUtc` in another) was resolved in code as
  `RequestedStartAtUtc`/`RequestedEndAtUtc` — the `Utc` suffix made
  authoritative.

## 11. Not yet built (backlog)

- Real audit/history trail behind `GET /{id}/history`.
- Automatic expiry (nothing currently transitions a `Demand` into
  `Expired` — no sweep job exists).
- `ExternalReference` as its own multi-relationship entity, if a real
  multi-reference need appears.
- Any consumer of `DemandAcceptedEvent`/`DemandRejectedEvent`/
  `DemandCancelledEvent` — depends on Work Management (Box in the "Work &
  Workforce" deployable unit, not yet built — see `docs/ARCHITECTURE.md` §7.1).
- AsyncAPI contract for these three events (mirrors `Eswmp.Core`'s `CO-16`
  backlog item, tracked as `WK-10` in `docs/TASK_BOARD.md`).
