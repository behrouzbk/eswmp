# ESWMP — Codex Context

> Read this file at the start of every session before generating any code.

## Project Identity

**ESWMP** — Enterprise Scheduling & Workforce Management Platform. A generic,
industry-agnostic backend for scheduling **resources** (people, vehicles, rooms,
equipment) against **reservations**, with pluggable assignment scoring and
tenant-configurable workflow/business rules.

This is **not** PetZiv. PetZiv is one tenant that will consume this platform's API;
this codebase must never contain PetZiv-specific concepts (pets, grooming, clients,
owners, breeds, etc.). If a requirement can't be expressed in terms of `Resource`,
`Reservation`, `Calendar`, `AvailabilityRule`, `Assignment`, and an opaque
`externalReferenceType`/`externalReferenceId` pair pointing back into the caller's
own domain, it does not belong in this repo — it belongs in the consuming product.

Source docs: `docs/ESWMP_VISION.md` (original product rationale),
`docs/ARCHITECTURE.md` (technical architecture), and — as of 2026-07-07 —
`docs/api/Arch.jpeg` and `docs/api/INDEX.md` (the current, richer target
domain map: a "Proposed for Workshop Review" diagram plus a series of
per-domain specs, only some of which exist yet; see `docs/ARCHITECTURE.md`
§1.1 for how it reconciles with what's actually built).

## Architecture in One Paragraph

Four ASP.NET Core Web API services in a C# / .NET 9 monorepo, plus a YARP gateway.
`Eswmp.Core` owns Resource/Calendar/AvailabilityRule/Reservation/Appointment,
and — as of the 2026-07-07 target-architecture reconciliation — a Capacity
module. `Eswmp.Assignment` owns auto-assignment scoring — which Resource
should fulfill a given Reservation — plus a newer Matching module (richer,
versioned, explainable candidate ranking). `Eswmp.Rules` owns the workflow
state machine and tenant-configurable business rules. `Eswmp.Work` (new)
owns Demand Intake and Work Requirement — converting a caller's
industry-specific request into ESWMP's generic `Demand`, and defining what a
piece of work requires before it can be scheduled/assigned. Each service
owns its own PostgreSQL database; no service reads another's database
directly, and within a service, modules from the target architecture are
further isolated by Postgres schema — see rule 11. Cross-service calls use
NSwag-generated typed clients sourced from `contracts/openapi/`.
Multi-tenant from day one via a `TenantId` query filter on every entity, resolved
from a `tenant_id` JWT claim — **this platform does not issue its own JWTs or run
its own user/login system**; it validates tokens issued by whichever product
embeds it (PetZiv today, others later).

## Critical Rules for All Code Generation

1. **No vertical-specific concepts.** No `Pet`, `Client`, `Groomer`, `Booking`, or
   any other single-industry noun. Use `Resource`, `Reservation`, `Appointment`,
   `Calendar`, `AvailabilityRule`, `Capacity`, `Demand`, `WorkRequirement`,
   `Matching`. If a consumer needs to attach domain meaning, it goes in
   `ExternalReferenceType` / `ExternalReferenceId` — an opaque pointer the
   platform never inspects. The workshop specs in `docs/api/` illustrate this
   generality using PetZiv examples (Groomer, Dog Walker, etc.) — that's fine
   in spec prose explaining applicability, but none of that vocabulary may
   appear in actual code, consistent with "This is not PetZiv" above.
2. **Contract-first always**: new cross-service endpoints get their OpenAPI YAML
   written in `contracts/openapi/{service}.v1.yaml` first, then implemented.
3. **One database per service**: `CoreDbContext`, `AssignmentDbContext`,
   `RulesDbContext`, `WorkDbContext`. Never add entities to the wrong DbContext.
4. **No cross-service DB reads.** Cross-service data access is HTTP (typed client)
   or the event bus, never a direct query into another service's database.
5. **Use generated clients for cross-service calls.** Never hand-write `HttpClient`
   calls between `Eswmp.*` services — generate from `contracts/openapi/` via NSwag
   into `Eswmp.Shared/Generated/`.
6. **Shared code in `Eswmp.Shared` only**: auth/tenant middleware, resilience,
   observability, messaging, common DTOs, domain events. No business logic.
7. **Multi-tenant isolation**: every `DbContext` registers a global
   `HasQueryFilter(e => e.TenantId == _currentTenantId)` for every tenant-scoped
   entity. Never query without this filter active.
8. **Rules over hardcoding.** Business logic that varies by tenant or resource type
   (duration buffers, safety-alert triggers, size brackets) belongs in a
   `DurationTagRule` / `DurationSizeBracket` / `BusinessRule` database row, not a
   hardcoded `switch` statement. This is the one correction to how the original
   PetZiv scheduling code worked — see `docs/ESWMP_VISION.md`.
9. **Tests alongside every implementation.** Every prompt that generates a service
   or controller also generates the xUnit test class. No exceptions.
10. **Keep the three tracking documents synchronized.** `docs/DEVELOPMENT_STATUS.md`,
    `docs/TASK_BOARD.md`, and `docs/ESWMP_Project_Tracker.xlsx` together form the
    single source of truth for development status — see "Tracking Document
    Synchronization" below. Update all three whenever a task starts, progresses,
    or completes. No exceptions.
11. **Schema-per-module within a service.** Where a service hosts more than one
    module from the target architecture (`docs/api/Arch.jpeg` /
    `docs/api/INDEX.md`) — e.g. `Eswmp.Core`'s Resource/Availability/Capacity
    modules, `Eswmp.Assignment`'s scoring/Matching modules, `Eswmp.Work`'s
    Demand Intake/Work Requirement modules — isolate each module's tables in
    its own Postgres schema, and never let one module's code read or write
    another module's tables directly. Cross-module data needs go through the
    owning module's own service/repository class, never a raw query into its
    tables. This is rule 4 applied one level down, between modules in one
    service instead of between services. See `docs/ARCHITECTURE.md` §1.2.

## Tracking Document Synchronization

| Document                          | Role                                                                                                                                                         | Granularity                                                                                                                       |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| `docs/DEVELOPMENT_STATUS.md`      | Narrative status + dated changelog, read by humans for "what's the state of things"                                                                          | Component-level (per service/area), plus a chronological changelog                                                                |
| `docs/TASK_BOARD.md`              | Per-service task list with status symbols (✅ 🟡 🔴 🔵) and a summary scoreboard                                                                                 | Task-code level (`CO-*`, `AS-*`, `RU-*`, `GW-*`, `CC-*`, `WK-*`)                                                                          |
| `docs/ESWMP_Project_Tracker.xlsx` | Full PM tracker — `Task Tracker` sheet (dates, % complete, effort, risk), `Dashboard` sheet (live formulas over Task Tracker), `Legend & Instructions` sheet | Task-code level, superset of `TASK_BOARD.md` — also covers `ENV-*`, `SCF-*`, `GH-*` setup tasks not broken out in `TASK_BOARD.md` |

Rule: when a task's status changes (started / in progress / blocked / completed),
update all three in the same turn:

1. `docs/TASK_BOARD.md` — flip the task's status symbol; update the Summary
   Scoreboard counts if a task moved in/out of a category; bump "Last updated".
2. `docs/DEVELOPMENT_STATUS.md` — update the relevant component status line
   under "What has been built" / "What is NOT yet built", and add a dated
   Changelog entry describing what changed; bump "Last updated".
3. `docs/ESWMP_Project_Tracker.xlsx` — update the matching `Task Tracker` row's
   `Status`, `% Complete`, and (if completed) `Actual Completion Date`; add a
   note in `Notes / Comments` if there's a blocker or deviation. The `Dashboard`
   sheet's counts are live formulas over `Task Tracker` and need no manual edit.

If a task exists in one document but not another (e.g. a new `CO-*` item not
yet in the xlsx), add it rather than skipping the update — the three documents
must stay a consistent view of the same task set, not drift into separate
lists.

## Service Quick Reference

| Service        | Port | DB Name            | Csproj                                         |
| -------------- | ---- | ------------------- | ----------------------------------------------- |
| Gateway        | 6100 | N/A                 | `src/Eswmp.Gateway/Eswmp.Gateway.csproj`        |
| Core           | 6001 | `eswmp_core`        | `src/Eswmp.Core/Eswmp.Core.csproj`              |
| Assignment     | 6002 | `eswmp_assignment`  | `src/Eswmp.Assignment/Eswmp.Assignment.csproj`  |
| Rules          | 6003 | `eswmp_rules`       | `src/Eswmp.Rules/Eswmp.Rules.csproj`            |
| Work           | 6004 | `eswmp_work`        | `src/Eswmp.Work/Eswmp.Work.csproj`              |
| Shared library | N/A  | N/A                 | `src/Eswmp.Shared/Eswmp.Shared.csproj`          |

## Key Entity Ownership

| Entity                                                                                                                                                                                                                                                                                    | Service    | DbContext             | Schema (rule 11) |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------- | ---------------------- | ------------------ |
| Tenant, Reservation, Appointment, DurationSizeBracket, DurationTagRule                                                                                                                                                                                                                     | Core       | `CoreDbContext`        | default             |
| Resource, ResourceType, ResourceCapability, ResourceSkill, ResourceCertification                                                                                                                                                                                                           | Core       | `CoreDbContext`        | `resource`          |
| AvailabilityProfile, AvailabilityRule, AvailabilityException, TimeOff, AvailabilityOverride                                                                                                                                                                                                | Core       | `CoreDbContext`        | `availability`      |
| CapacityProfile, CapacityDefinition, CapacityHold, CapacityConsumption, CapacityOverride, CapacityLedgerEntry                                                                                                                                                                              | Core       | `CoreDbContext`        | `capacity`          |
| AssignmentLog                                                                                                                                                                                                                                                                               | Assignment | `AssignmentDbContext`  | default             |
| MatchEvaluation, CandidateMatchResult, MatchFactorEvaluation, MatchingPolicy, MatchingPolicyVersion                                                                                                                                                                                        | Assignment | `AssignmentDbContext`  | `matching`          |
| BusinessRule, WorkflowTransitionLog                                                                                                                                                                                                                                                         | Rules      | `RulesDbContext`       | default             |
| Demand, DemandValidationResult                                                                                                                                                                                                                                                              | Work       | `WorkDbContext`        | `demand`            |
| WorkRequirement, RequirementVersion, RequirementSnapshot, ResourceRequirement, CapabilityRequirement, SkillRequirement, CertificationRequirement, LocationConstraint                                                                                                                       | Work       | `WorkDbContext`        | `requirements`      |

## Naming & Style Conventions

- **Namespaces**: `Eswmp.{ServiceName}.{Layer}` (e.g., `Eswmp.Core.Services`)
- **Controllers**: `[ApiController] [Route("api/v1/[controller]")]`
- **DTOs**: suffix `Dto` for input/output shapes
- **EF migrations**: descriptive names — `AddResourceSkillsColumn`, not `Migration001`
- **Tests**: `{ClassUnderTest}_{Method}_{Scenario}`
- **C# style**: nullable enabled, implicit usings enabled, `record` types for immutable DTOs

## Relationship to PetZiv

PetZiv (`C:\workspace\petziv`) is a separate repository and a separate product.
This platform was seeded from patterns proven there (contract-first workflow,
resilience/observability/messaging plumbing, the Tetris gap-elimination slot
algorithm, multi-tenant query-filter pattern) but the code was generalized and
carries no PetZiv naming. Do not assume familiarity with PetZiv's business domain
when working in this repo, and do not import from or reference the PetZiv
codebase. Once this platform's MVP is stable, PetZiv's Bookings/Operations
clusters are expected to migrate to calling this platform's API instead of
hosting their own scheduling logic — but that migration happens in the PetZiv
repo, not here.

## Phase Status

| Phase   | Focus                                                                                                                                                                     | Status        |
| ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------- |
| Phase 0 | Scaffold: Core/Assignment/Rules/Work/Gateway skeletons, Resource/Reservation model, generalized Slot Optimizer + Duration Estimator, Assignment Scorer, Workflow state machine, Capacity/Matching/Demand Intake/Work Requirement modules per `docs/api/INDEX.md` | 🟡 In Progress |
| Phase 1 | Full Core CRUD + confirm/cancel reservation flow, first real tenant integration (PetZiv)                                                                                  | ⬜ Not Started |
| Phase 2 | Rules engine hardening, workflow engine (approval steps), routing/geofencing                                                                                              | ⬜ Not Started |
| Phase 3 | Multi-tenant licensing surface (API keys, usage metering, billing hooks)                                                                                                  | ⬜ Not Started |

Whenever creating or updating Markdown tables:

- Produce valid GitHub Flavored Markdown tables.
- Align all columns with consistent spacing.
- Ensure separator rows use at least three dashes.
- Do not use tabs.
- After modifying a table, reformat it so it is easy to read in Visual Studio Code.