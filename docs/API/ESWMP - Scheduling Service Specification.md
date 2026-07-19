ESWMP — Enterprise Scheduling & Workforce Management Platform

Document 06 — Scheduling Service Specification

**Document Type:** Product, Domain, Architecture, API, UX, and Implementation Requirements Specification
**Service Number:** 06 of ~19
**Version:** 1.0
**Status:** Proposed for Workshop Review — **drafted 2026-07-07 by reconciling
the existing `Eswmp.Core` implementation (`Reservation`/`Appointment`/
`SlotOptimizer`/`ReservationDurationEstimator`, already built and running)
against the format and rigor established by Documents 01–05 and 08.** This
is a first-pass draft for human review, not a ratified spec — see §12 for
where it diverges from or extends what's already running, and §13 for open
questions a workshop review should settle. Per the `[[eswmp-dev-box-by-box]]`
working agreement, this is the spec-first deliverable for `Arch.jpeg` box 6;
no new code changes accompany it.
**Primary Domain:** Bookable Calendar, Reservation Hold/Confirm/Cancel Lifecycle, Slot Search, Duration Estimation
**Initial Deployment Recommendation:** Independent domain module within the "Resource & Availability" deployable unit (alongside Resource, Availability, Capacity — see Document 03 §134, Document 04 §131, Document 05 §147)
**Future Deployment Option:** Independent service when reservation volume, multi-region calendar requirements, or independent scaling needs justify extraction

---

## 1. Executive Summary

The Scheduling Service is the authoritative owner of **bookable**
availability — the actual calendar of holds, confirmations, and cancellations
against a Resource, as distinct from the Availability Service's **potential**
availability (Document 04 §Exec Summary: "The Availability Service owns
potential availability. The Scheduling Service determines bookable
availability."). It is where a caller's abstract "I need this Resource for
this window" becomes a concrete `Reservation`, and where a confirmed
`Reservation` becomes an `Appointment` — the execution record everything
downstream (Assignment, Work Management, Evidence & Completion) eventually
refers back to.

It answers:

> Given this Resource's potential availability, capacity, and existing
> commitments, can I hold this time window right now — and did I actually
> get it?

It does **not** answer:

- Is this Resource qualified or eligible for the work? *(Resource,
  Eligibility)*
- When can this Resource potentially work, before any booking exists?
  *(Availability)*
- How much capacity does this Resource have left? *(Capacity)*
- Which Resource should fulfill this work? *(Matching, Assignment)*
- Does creating this booking conflict with another? *(Conflict Detection —
  Document 07/08 per `Arch.jpeg` box 8; today's inline overlap check in
  `ReservationsController` is the placeholder for that module — see §12)*

These belong to other domains, several of which are already specified
(Documents 03, 04, 05, 08) or reconciled as-built (`docs/api/specs/
01-demand-intake-service.md`).

## 2. Product Principles

- **Scheduling ≠ Availability.** A Resource can be "available" (per
  Document 04) for a window and still have no `Reservation` against it, and
  vice versa — a `Reservation` records a *commitment*, Availability records
  a *potential*. Document 04 §43–44 explicitly calls this out as "Why
  Appointments Must Not Be Stored [in Availability]" and names a
  "Commitment Projection" that Scheduling — not Availability — maintains.
  As-built, that projection *is* the `Reservation`/`Appointment` pair; no
  separate projection table exists (see §12).
- **Scheduling ≠ Capacity.** A Resource can have remaining Capacity
  (Document 05) for a dimension and still have no free calendar window, and
  vice versa. Document 05 §152 states plainly: "The Capacity Service must
  not become: The Booking Engine" — meaning Scheduling, not Capacity, owns
  the actual booking action, even though a real booking flow should check
  both before committing (see §9, Business Rules).
- **Hold before Confirm.** Never write a confirmed commitment directly —
  every booking passes through a `Held` state first (with an expiry) before
  becoming `Confirmed`, so a caller session that dies mid-flow doesn't
  leave a permanent, un-cancelable lock on a Resource.
- **Never re-derive scheduling logic per tenant type.** Duration estimation
  (base time + buffers) is tenant-configurable data (`DurationSizeBracket`/
  `DurationTagRule`), not compiled per-industry logic — this is the one
  correction to how the originating PetZiv code worked (`CLAUDE.md` rule 8,
  `docs/ESWMP_VISION.md`).

## 3. Domain Boundary

**Owns:** `Reservation`, `Appointment`, `DurationSizeBracket`,
`DurationTagRule`, and the gap-elimination slot-search algorithm.

**References but does not own:** `Resource` (by `ResourceId`, opaque
foreign key — no join into Resource's own tables), `AvailabilityProfile`/
`AvailabilityRule` (Document 04 — Scheduling reads *resolved* availability,
it does not compute the priority stack itself; as-built, this reconciliation
is incomplete, see §12), `CapacityDefinition`/`CapacityHold` (Document 05 —
same relationship), the caller's own domain (via the opaque
`ExternalReferenceType`/`ExternalReferenceId` pointer, exactly as in
`Reservation` today).

**Must never own** (mirrors the "does not answer" list in §1): Resource
identity/capabilities/certifications, Availability rule authoring, Capacity
definitions, Eligibility decisions, Matching/ranking scores, Assignment
decisions, Work execution, Payroll.

**Prohibited dependencies:** must never query `Eswmp.Assignment`'s or
`Eswmp.Work`'s databases directly (`CLAUDE.md` rule 4); must never write to
`resource`, `availability`, or `capacity` schema tables directly, only
through their owning module's application code (`CLAUDE.md` rule 11 — see
§12 for where this isn't fully true yet).

## 4. Data Model(s)

All entities inherit `TenantScopedEntity` (`Id`, `TenantId`, audit fields)
from `Eswmp.Shared.DTOs`.

### `Reservation` — aggregate root

| Field | Type | Notes |
|---|---|---|
| `ResourceId` | `Guid` (required) | Opaque FK into the Resource module |
| `StartTime` / `EndTime` | `DateTimeOffset` (required) | |
| `Status` | `ReservationStatus` enum, default `Held` | `Held, Confirmed, Expired, Cancelled` |
| `ExpiresAt` | `DateTimeOffset?` | Set on hold creation; governs conflict-check exclusion (see §9) |
| `ExternalReferenceType` / `ExternalReferenceId` | `string` (required) | Opaque caller-domain pointer — ESWMP never inspects it |

### `Appointment` — created on confirm

| Field | Type | Notes |
|---|---|---|
| `ReservationId` | `Guid` (required) | |
| `ResourceId` | `Guid` (required) | Denormalized from the Reservation for direct calendar queries |
| `StartTime` / `EndTime` | `DateTimeOffset` (required) | |
| `Status` | `string`, default `"Confirmed"` | Free-text today — see §12 recommendation to enum-ify |

### `DurationSizeBracket` — duration-estimation module

| Field | Type | Notes |
|---|---|---|
| `ResourceType` | `string` (required) | Matched against the caller-supplied resource type label |
| `MaxSizeValue` | `decimal` (required) | Generic size metric (weight, sq ft, party size, ...) |
| `BaseMinutes` | `int` (required) | Matched ascending by `SizeValue <= MaxSizeValue` |

### `DurationTagRule` — duration-estimation module

| Field | Type | Notes |
|---|---|---|
| `ResourceType` | `string?` | Null = applies to all types |
| `Tag` | `string` (required) | Matched against caller-supplied attribute tags |
| `AdditionalMinutes` | `int` | Additive buffer |
| `MultiplierPercent` | `decimal?` | Multiplicative modifier |
| `SafetyAlertMessage` | `string?` | Surfaced to the caller, never blocks booking |

## 5. API(s)

Base path `/api/v1`. Contract: `contracts/openapi/core.v1.yaml`.

| Method & Path | Notes |
|---|---|
| `POST /reservations` | Create a hold. Rejects (`409`) if the Resource has an overlapping `Confirmed` or unexpired `Held` reservation. Publishes `SlotReservedEvent`. |
| `GET /reservations/{id}` | |
| `POST /reservations/{id}/confirm` | `Held → Confirmed`; creates the `Appointment`. Publishes `ReservationConfirmedEvent`. `409` if not `Held`. |
| `POST /reservations/{id}/cancel` | Any non-terminal status → `Cancelled`. Publishes `ReservationCancelledEvent`. |
| `GET /resources/{id}/calendar?from=&to=` | Confirmed + held reservation entries in a date range. |
| `POST /slots/search` | Gap-elimination candidate start times for a requested duration (`SlotOptimizer`). |
| `POST /duration/estimate` | Tenant-configured base + buffer duration estimate (`ReservationDurationEstimator`). |

**Proposed additions** (not yet built — flagged for workshop review, not
assumed):

| Proposed endpoint | Rationale |
|---|---|
| `POST /reservations/search` | List/filter reservations by status/date-range/external-reference, mirroring the search pattern every other received spec has (Documents 01 §Search, 02 §Search, 03 §69) — today only per-Resource calendar lookup exists. |
| `POST /reservations/{id}/extend` | Extend a `Held` reservation's `ExpiresAt` before it lapses — no such endpoint exists; a caller must currently cancel and recreate. |
| `POST /calendar/batch` | Batch calendar lookup across multiple Resources, mirroring Availability's `batch-resolve` and Capacity's `batch-resolve` — today `CalendarController` is single-Resource only. |

## 6. Domain Events

Defined in `Eswmp.Shared.Events.SchedulingEvents`, published via
`IPublishEndpoint` (MassTransit):

| Event | Fired on | As-built? |
|---|---|---|
| `SlotReservedEvent` | `POST /reservations` | ✅ Published |
| `ReservationConfirmedEvent` | `POST /reservations/{id}/confirm` | ✅ Published |
| `ReservationCancelledEvent` | `POST /reservations/{id}/cancel` | ✅ Published |
| `ReservationExpiredEvent` | A `Held` reservation's `ExpiresAt` lapses | 🔴 **Defined but never published** — no expiry sweep job exists (see §12) |

All four carry only opaque identifiers (`ReservationId`, `ResourceId`,
`ExternalReferenceType`/`Id`) — never caller-domain fields, per `CLAUDE.md`
rule 1.

**Consumed (proposed, none wired up yet):** `ResourceSuspended`,
`ResourceRetired` (Document 03) — a suspended/retired Resource should
probably not accept new holds; today `ReservationsController` doesn't check
`Resource.Status` at all before creating a hold. Flagged as a real gap, not
assumed away.

## 7. Deployment Recommendation

Per the pattern every received spec (Documents 01–05, 08) independently
converges on: this module belongs inside the shared "Resource & Availability"
deployable unit, isolated by its own Postgres **schema** —
`docs/ARCHITECTURE.md` §1.2 already names this schema `scheduling`, reserved
but not yet used. As-built today, `Reservation`/`Appointment`/
`DurationSizeBracket`/`DurationTagRule` sit in `eswmp_core`'s **default**
schema, alongside `Tenant` — not yet split out the way Resource/
Availability/Capacity were during the 2026-07-07 reconciliation pass. This
is the primary "not yet reconciled" gap this document identifies (see §12).

Extraction criteria (proposed, mirroring the other specs' pattern): very
high reservation volume, multi-region calendar requirements, independent
release cadence, a dedicated real-time booking/waitlist product need.

## 8. Multi-Tenancy, Versioning, Concurrency

- **Tenant isolation**: every `Reservation`/`Appointment` carries `TenantId`;
  `CoreDbContext` applies `HasQueryFilter` on both. Consistent with every
  other module.
- **No optimistic-concurrency `Version` field.** Every module built or
  expanded during the 2026-07-07 pass (`Resource`, `AvailabilityProfile`,
  `CapacityProfile`/`Definition`, `Demand`, `WorkRequirement`) carries a
  `Version` int for `expectedVersion`/`412` semantics. `Reservation`/
  `Appointment` predate that pattern and don't have one — confirm/cancel
  race conditions rely entirely on the database read-then-write happening
  inside one request, with no explicit stale-write guard. **Recommended**:
  add `Version` to `Reservation` for consistency, unless a workshop review
  decides the hold/confirm/cancel state machine's inherent one-way
  transitions make it unnecessary.
- **No idempotency key** on `POST /reservations`, unlike `Eswmp.Work`'s
  `POST /demands` (which requires one). A retried create request with a
  transient network failure could double-hold a Resource until the
  overlap-conflict check catches the second attempt (it would, correctly,
  409 — but the caller has no way to safely replay and get the *original*
  hold back). **Recommended**: add an `Idempotency-Key` header, mirroring
  `Eswmp.Work`'s `DemandIdempotencyRecord` pattern.

## 9. Business Rules / Invariants

- A `Reservation` may exist without an `Appointment` (a `Held` or
  `Cancelled` reservation never gets one).
- An `Appointment` can never exist without a confirmed `Reservation` — the
  data model enforces this by construction (`Appointment.ReservationId` is
  required, only ever created inside `Confirm`).
- **Conflict Detection today is inline, not a module.** `POST /reservations`
  rejects an overlap against any `Confirmed` reservation or any `Held`
  reservation whose `ExpiresAt` hasn't passed, scoped to the same
  `ResourceId`. This is a genuine invariant Scheduling enforces, but
  `Arch.jpeg` box 8 names Conflict Detection as its own domain — as-built,
  it has no independent existence; extracting it later should not change
  the invariant, only where the code that enforces it lives.
- **Scheduling does not check Capacity or Availability before holding.**
  Neither `POST /reservations` nor `POST /slots/search` calls into the
  Capacity or Availability modules — `SlotOptimizer` only reasons about
  existing `Reservation` rows, and the overlap check only reasons about
  `Reservation` rows. A Resource that is fully consumed on a Capacity
  dimension, or explicitly force-unavailable via an `AvailabilityOverride`,
  can still be successfully booked through Scheduling today. **This is the
  single biggest as-built correctness gap this document surfaces** — see
  §12 and §13.
- Duration estimation never blocks a booking — a missing size bracket or
  tag rule falls back to `defaultBaseMinutes`, and a safety alert is
  informational only (`RequiresSafetyAlert`), never a hard stop.

## 10. Testing (as-built)

`tests/unit/Eswmp.Core.Tests/`: `SlotOptimizerTests` (6 cases — empty day,
gap-equal-to-duration, gap-too-small, past-slot exclusion, fully-booked day,
multiple large-gap slots), `ReservationDurationEstimatorTests` (7 cases —
default base, bracket matching, tag buffers, safety alerts, multiplier,
multi-tag stacking, resource-type mismatch exclusion),
`ReservationsControllerTests` (7 cases — no-conflict success, held/confirmed
conflict, back-to-back success, expired-held/cancelled don't block,
cross-resource independence).
`tests/integration/Eswmp.Core.IntegrationTests/ReservationsApiTests.cs`
(Testcontainers Postgres): create→get, permission-denied, unauthenticated,
overlap conflict, cross-tenant isolation.

## 11. Not Yet Built / Backlog

- `ReservationExpiredEvent` publication and the background sweep job that
  would fire it.
- Capacity/Availability checks before holding (§9 — the priority gap).
- `Resource.Status` check before holding (suspended/retired resources can
  currently be booked).
- Optimistic concurrency (`Version`) on `Reservation`/`Appointment`.
- Idempotency key on `POST /reservations`.
- `POST /reservations/search`, `/extend`, and batch calendar lookup (§5).
- Schema isolation (`scheduling` schema within `eswmp_core`), per
  `CLAUDE.md` rule 11 — currently default-schema.
- Conflict Detection (`Arch.jpeg` box 8) as its own explicit module,
  separated from the inline check in `ReservationsController`.

## 12. Reconciliation Notes — Spec vs. As-Built

This document was written *after* the code, which is the reverse of every
other received spec. Net effect: §§4–6, 9–10 describe real, tested behavior
with high confidence; §§5 (proposed additions), 7 (schema), 8 (versioning/
idempotency), and 9's Capacity/Availability gap are this document's actual
contribution — they're gaps a from-scratch spec review would likely have
caught before implementation, surfaced here after the fact instead. None of
them are implemented as part of authoring this document — per the box-by-box
working agreement, this is spec-only; implementing any of §11 is separate,
future work once this draft is reviewed.

## 13. Open Workshop Questions

1. Should Scheduling call into Capacity/Availability synchronously before
   granting a hold, or should it remain unaware of them and rely on a
   higher-level orchestrator (Work Management? Assignment?) to check all
   three before ever calling `POST /reservations`? The latter keeps
   Scheduling simpler but pushes a real correctness burden onto a caller
   that doesn't exist yet.
2. Is `Appointment.Status` (currently a free string) worth promoting to an
   enum now, or left alone until Work Management/Task Execution define
   what execution-time statuses actually look like?
3. Does `Reservation` need a `Version` field, or does its narrow
   Held→Confirmed/Cancelled state machine make optimistic concurrency
   unnecessary in a way Resource/Availability/Capacity's broader mutation
   surface doesn't share?
