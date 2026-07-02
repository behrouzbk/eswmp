# ESWMP — Task Board

> **Living document.** Update the Status column after every task completes, starts, or fails.
> Last updated: 2026-07-02

## Status Legend

| Symbol | Meaning |
|---|---|
| ✅ | Completed Successfully |
| 🟡 | In Development |
| 🔴 | Not Started |
| 🔵 | Future Phase — deliberately deferred |

---

## Eswmp.Core

| # | Task | Status |
|---|---|---|
| CO-01 | Project scaffold + `CoreDbContext` | ✅ |
| CO-02 | OpenAPI contract — `core.v1.yaml` | ✅ |
| CO-03 | Resource CRUD | ✅ |
| CO-04 | Availability rules + windows | ✅ |
| CO-05 | `SlotOptimizer` (gap-elimination, adapted from PetZiv) | ✅ |
| CO-06 | `ReservationDurationEstimator` (generalized duration/buffer engine) | ✅ |
| CO-07 | Reservation hold/confirm/cancel + domain events | ✅ |
| CO-08 | Per-resource calendar endpoint | ✅ |
| CO-09 | xUnit tests — SlotOptimizer, ReservationDurationEstimator | ✅ |
| CO-10 | EF Core migration — InitialCreate | 🔴 |
| CO-11 | Redis-backed slot search caching | 🔴 |
| CO-12 | Conflict detection on overlapping reservations | 🔴 |

## Eswmp.Assignment

| # | Task | Status |
|---|---|---|
| AS-01 | Project scaffold + `AssignmentDbContext` | ✅ |
| AS-02 | OpenAPI contract — `assignment.v1.yaml` | ✅ |
| AS-03 | `AssignmentScorer` (proximity/skill/workload/speed-fit) | ✅ |
| AS-04 | `AssignmentController` (score + log) | ✅ |
| AS-05 | xUnit tests — AssignmentScorer | ✅ |
| AS-06 | EF Core migration — InitialCreate | 🔴 |
| AS-07 | Typed `Eswmp.Core` client for live Resource/Reservation lookups (currently caller supplies candidates directly) | 🔴 |

## Eswmp.Rules

| # | Task | Status |
|---|---|---|
| RU-01 | Project scaffold + `RulesDbContext` | ✅ |
| RU-02 | OpenAPI contract — `rules.v1.yaml` | ✅ |
| RU-03 | `WorkflowState` enum + `WorkflowTransitionValidator` | ✅ |
| RU-04 | `BusinessRule` CRUD (jsonb definitions) | ✅ |
| RU-05 | EF Core migration — InitialCreate | 🔴 |
| RU-06 | Real rules-engine integration (Drools/Microsoft RulesEngine) | 🔵 Future — see ARCHITECTURE.md §7 |
| RU-07 | Durable workflow engine (Temporal/Dapr) for multi-step approvals | 🔵 Future |

## Eswmp.Gateway

| # | Task | Status |
|---|---|---|
| GW-01 | YARP routing to Core/Assignment/Rules | ✅ |
| GW-02 | JWT validation + rate limiting | ✅ |
| GW-03 | Health aggregation | ✅ |
| GW-04 | Verified against live services end-to-end | 🔴 |

## Cross-Cutting

| # | Task | Status |
|---|---|---|
| CC-01 | `Eswmp.sln` wired with all 5 projects + 2 test projects | ✅ |
| CC-02 | `docker-compose.yml` + override | ✅ Written, not yet run |
| CC-03 | Terraform (resource group, ACR, Postgres, Redis, Container Apps, Key Vault) | ✅ Written, not yet applied |
| CC-04 | GitHub Actions CI (`build.yml`) | ✅ Written, not yet run |
| CC-05 | NSwag C# client generation script | ✅ Written, not yet run (no cross-service calls exist yet) |
| CC-06 | openapi-typescript generation script | ✅ Written, no frontend consumer yet |
| CC-07 | Git repository initialized, pushed to `github.com/behrouzbk/eswmp` | 🟡 In progress |
| CC-08 | First real tenant integration — PetZiv's Bookings/Operations call this platform instead of hosting scheduling logic internally | 🔴 — this is the actual point of the project |

---

## Summary Scoreboard

| Area | Total | ✅ Done | 🔴 Not Started | 🔵 Future |
|---|---|---|---|---|
| Eswmp.Core | 12 | 9 | 3 | 0 |
| Eswmp.Assignment | 7 | 5 | 2 | 0 |
| Eswmp.Rules | 7 | 5 | 0 | 2 |
| Eswmp.Gateway | 4 | 3 | 1 | 0 |
| Cross-Cutting | 8 | 5 | 2 | 0 |
| **Total** | **38** | **27** | **8** | **2** |

> Phase 0 scaffold is functionally complete: builds clean, 19/19 tests pass. Next
> up: EF Core migrations, a live `docker compose up` run, then the first real
> integration — PetZiv calling this platform's API instead of hosting
> `SlotOptimizer`/`DurationCalculator` internally.
