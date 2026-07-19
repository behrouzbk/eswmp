# ESWMP — Target Architecture Reference Index

> **Status: DRAFT.** Every document below is stamped "Proposed for Workshop
> Review, Version 1.0" by its authors. None of this has been ratified as final
> architecture — treat it as the current best target-domain-map candidate, not
> a signed-off spec. See `docs/ARCHITECTURE.md` §1 for how this reconciles
> with what's actually built.

## Source diagram

`Arch.jpeg` — "ESWMP – Domain Services Architecture" — depicts 14 numbered
domain services across 4 domains (Demand & Intake; Scheduling & Availability;
Workforce & Execution; plus supporting layers), 9 Decision & Intelligence
engines, 12 Shared Platform Services, and 6 Integration & Event Platform
components, consumed by 7 consumer surfaces and integrating with 9 classes of
external system. It is the richer, more current target map — supersedes the
inline "~20 services" list in `docs/ESWMP_VISION.md` §2.

`Image 2026-07-06 at 7.12.41 PM.jpeg` — companion screenshot/diagram, not yet
individually catalogued.

## Workshop specification series ("Document NN of 19")

Each service spec answers what it does and, explicitly, what it does **not**
own — and gives an "Initial Deployment Recommendation" (module inside a
shared deployable unit) vs. a "Future Deployment Option" (extraction
trigger). None mandate one microservice per domain box.

| # | Document | Domain | Spec status | As-built doc |
|---|----------|--------|--------|--------------|
| 01 | Demand Intake Service Specification | Converts external business intent into generic `Demand` | ✅ Received | ✅ **Implemented** — [`specs/01-demand-intake-service.md`](specs/01-demand-intake-service.md) |
| 02 | Work Requirement Service Specification | Operational requirements needed before work can be scheduled/assigned/dispatched | ✅ Received | 🟡 Implemented, as-built doc not yet written |
| 03 | Resource Service Specification | Resource identity, capabilities, skills, certifications | ✅ Received | 🟡 Implemented, as-built doc not yet written |
| 04 | Availability Service Specification | When a Resource may potentially work | ✅ Received | 🟡 Implemented, as-built doc not yet written |
| 05 | Capacity Service Specification | How much work a Resource/pool can accept | ✅ Received | 🟡 Implemented, as-built doc not yet written |
| 06 | Scheduling Service Specification | Bookable availability, calendar, reservations | 🟡 **Drafted 2026-07-07** — [`ESWMP - Scheduling Service Specification.md`](ESWMP%20-%20Scheduling%20Service%20Specification.md), pending workshop review | 🟡 Partially implemented pre-spec (`Reservation`/`Appointment`/`SlotOptimizer` in `Eswmp.Core`) — spec identifies real gaps against that code (see its §12), none fixed yet |
| 07 | (Eligibility Engine, inferred from Matching spec's references) | Is a candidate allowed/qualified for the work | 🔴 Not yet written | — |
| 08 | Matching Service / Candidate Ranking Engine Specification | Ranks *eligible* candidates by fit | ✅ Received | 🟡 Implemented, as-built doc not yet written |
| 09–19 | Conflict Detection, Workforce, Dispatch, Work Management, Task & Execution, Evidence & Completion, Rules Engine (detailed spec), Scoring/Recommendation/Optimization/Forecasting engines, AI Assistant, others per `Arch.jpeg` | — | 🔴 Not yet written | — |
| — | Catalog Service Specification | Generic service catalog, Work Types, Equipment Types — `Arch.jpeg` box 3, Demand & Intake domain | 🔴 Not part of the numbered 19-document series at all — no workshop `.docx` exists, and it isn't named in the 09–19 catch-all above either; a real diagram box the original series appears to have missed | 🟡 Proposed engineering draft, self-drafted 2026-07-17, unimplemented — [`specs/box3-catalog-service.txt`](specs/box3-catalog-service.txt) |

**As-built docs** (`docs/api/specs/`) are written *after* implementation, one
per `Arch.jpeg` box, in the format: Executive Summary, Domain Boundary,
Microservice/Deployment, Data Model(s), API(s), Policies, Lifecycle, Domain
Events, Testing, Deviations from the source workshop spec, and a
Not-Yet-Built backlog. They supersede the source `.docx` for "what actually
runs" while the `.docx` remains the record of original proposed intent.

**Not part of the numbered 19-document series at all, and entirely
unspecified**: all 12 Shared Platform Services (Identity & Access, Tenant,
Organization, Authorization, Configuration, Feature Management, Audit,
Search, File & Media, Notification Orchestrator, Localization, Time Zone)
and all 6 Integration & Event Platform components beyond the message bus
already built (Integration Gateway, Event Contract Registry, Webhook
Service, Connector Framework, Outbox/Inbox — Event Bus/Message Broker itself
is the one already covered by MassTransit).

## Cross-cutting conventions all six received specs agree on

- **Physical layout**: not 19 deployables — a handful of "deployable units,"
  each containing several modules isolated by **Postgres schema**, with an
  explicit rule that one module must never write another module's tables
  directly (only through its application contract / commands / events).
  - *Resource & Availability* unit: Resource, Availability, Capacity modules.
  - *Work & Workforce* unit: Demand Intake, Work Requirement, Work
    Management, Assignment, Dispatch, Task Execution modules.
  - *Qualification & Matching* unit: Eligibility, Matching modules.
- **Versioning**: every aggregate carries a `version` field for optimistic
  concurrency (`If-Match`/expected-version → `412 Precondition Failed` on
  mismatch); Availability/Capacity/Matching additionally carry a
  higher-level broadcast version (`availabilityVersion`, `capacityVersion`,
  etc.) used purely for cache invalidation and change-notification scoping.
- **Event envelope**: `eventId, eventType, eventVersion, tenantId,
  aggregateType, aggregateId, occurredAt, correlationId, causationId,
  actor, payload` — identical shape across every spec.
- **Transactional outbox is mandatory** in every spec — state change and
  outbox event persist in one DB transaction; consumers are idempotent.
- **Tenant isolation** and **no direct cross-domain DB reads** are asserted
  in every spec, consistent with `CLAUDE.md` rules 4 and 7.

## How this maps to what's actually built

See `docs/ARCHITECTURE.md` §1 for the full reconciliation table (deployable
unit → module → spec status → owning C# project → built/not-built).
