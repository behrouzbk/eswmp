# ESWMP — Remote QA Testing Guide

> **Audience:** a remote tester or developer who needs to call ESWMP's APIs
> against the QA environment in Azure, from their own machine, without VPN
> access or local checkout of every service.
> **Companion docs:** `docs/Azure/DEPLOYMENT-WORKFLOW-GUIDE.md` (deploying
> code/infra changes to QA), `docs/api/INDEX.md` (the target domain map this
> platform is being built against).
> **Last updated:** 2026-07-19.

---

## Contents

1. [How this works, in one paragraph](#1-how-this-works-in-one-paragraph)
2. [Getting a token](#2-getting-a-token)
3. [Calling conventions every service shares](#3-calling-conventions-every-service-shares)
4. [Core service — Resources](#4-core-service--resources)
5. [Core service — Availability, Time Off, Overrides](#5-core-service--availability-time-off-overrides)
6. [Core service — Calendar](#6-core-service--calendar)
7. [Core service — Capacity](#7-core-service--capacity)
8. [Core service — Duration Estimation](#8-core-service--duration-estimation)
9. [Core service — Reservations](#9-core-service--reservations)
10. [Core service — Slots](#10-core-service--slots)
11. [Assignment service — Assignment scoring/log](#11-assignment-service--assignment-scoringlog)
12. [Assignment service — Matching](#12-assignment-service--matching)
13. [Rules service — Business Rules](#13-rules-service--business-rules)
14. [Rules service — Workflow](#14-rules-service--workflow)
15. [Work service — Demand Intake](#15-work-service--demand-intake)
16. [Work service — Requirement Definitions (legacy)](#16-work-service--requirement-definitions-legacy)
17. [Work service — Requirement Templates](#17-work-service--requirement-templates)
18. [Work service — Work Requirements](#18-work-service--work-requirements)
19. [End-to-end walkthrough](#19-end-to-end-walkthrough)
20. [Troubleshooting](#20-troubleshooting)

---

## 1. How this works, in one paragraph

There is one publicly reachable address, the **Gateway**. It is a YARP
reverse proxy that validates your JWT and forwards the request to whichever
of the four backend services (Core/Assignment/Rules/Work) owns that route —
the backends themselves are not publicly reachable, so you always call the
Gateway, never a backend directly, no matter which service you're testing.
No VPN, no Azure login, no network setup on your end — it's a normal HTTPS
API.

```
QA Gateway base URL: https://eswmp-gateway-staging.bravehill-5af5160d.canadacentral.azurecontainerapps.io
```

Get the current URL yourself rather than trusting this being still accurate:
`terraform output -raw gateway_url` from `infrastructure/terraform/`, or ask
whoever manages the QA environment.

---

## 2. Getting a token

Every request needs `Authorization: Bearer <token>`. This platform never
issues its own tokens — QA tokens are minted with `scripts/generate-qa-jwt.ps1`,
which pulls the real signing key live from Azure Key Vault (only someone with
Key Vault access — normally whoever runs `terraform apply` for QA — can run
this script). If you're a remote tester, ask that person for a token rather
than trying to mint your own.

```powershell
# Run by whoever has Key Vault access, not by the remote tester:
.\scripts\generate-qa-jwt.ps1 -Permissions "resource.read,resource.write,reservation.create,reservation.read" -ExpiryHours 48
```

Two things every token bakes in that shape what you'll see:

- **`tenant_id`** — every entity in every service is filtered by this claim
  (multi-tenant isolation, `CLAUDE.md` rule 7). QA has **no seed data**, so a
  fresh `tenant_id` starts empty. `POST` something first, then `GET` it back
  with the same token, before assuming an endpoint is broken.
- **`permissions`** — a comma-separated list checked per-endpoint (see the
  "Permission" column in every table below). A request with a valid token but
  the wrong permission gets `403 Forbidden`, not `401` — that's a sign the
  token itself is fine, just under-scoped. Full permission list:
  `src/Eswmp.Shared/Auth/EswmpClaimTypes.cs` (`EswmpPermissions`).

---

## 3. Calling conventions every service shares

| Convention | Detail |
| --- | --- |
| Header | `Authorization: Bearer <token>` on every request |
| Content type | `Content-Type: application/json` on every request with a body |
| Success codes | `200 OK` (read), `201 Created` (create — usually with a `Location` header), `204/200` on state transitions |
| Auth failure | `401 Unauthorized` — token missing/expired/bad signature |
| Authorization failure | `403 Forbidden` — token valid, missing the required `permissions` entry |
| Not found | `404 Not Found` |
| Validation failure | `400 Bad Request` |
| Business-rule conflict | `409 Conflict` (e.g. wrong status for a transition) |
| Optimistic-concurrency conflict | `412 Precondition Failed` (Core/legacy Work endpoints) |
| List/search endpoints | Wrapped in a `PagedResult`: `{ items: [...], page, pageSize, totalCount, totalPages, hasNextPage, hasPreviousPage }` |
| Cold start | First request after idle may `503` — `min_replicas = 0` in QA. Retry 2-3 times, a few seconds apart, before assuming something's broken (see [§20](#20-troubleshooting)). |

Two different error-body shapes exist depending which service answers:

**Core / Assignment / Rules** (simple, ad hoc):
```json
{ "error": "Cannot activate a Resource in status Active." }
```

**Work** (`Demand`/`WorkRequirement`/`RequirementTemplate` endpoints — a
structured envelope, since these flows need machine-readable error codes):
```json
{
  "error": "The request failed validation.",
  "code": "VALIDATION_FAILED",
  "traceId": "00-abc123...-01",
  "issues": [
    { "field": "requestedEndAtUtc", "code": "INVALID_TIME_WINDOW", "severity": "Error", "message": "RequestedStartAtUtc must be before RequestedEndAtUtc." }
  ]
}
```

All PowerShell examples below assume:

```powershell
$GW = "https://eswmp-gateway-staging.bravehill-5af5160d.canadacentral.azurecontainerapps.io"
$TOKEN = "<paste the token here>"
$Headers = @{ Authorization = "Bearer $TOKEN" }
```

---

## 4. Core service — Resources

Gateway route: `/api/v1/resources/{**catch-all}` → Core.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/resources` | `resource.write` | Create a Resource |
| `GET /api/v1/resources` | `resource.read` | List (filters: `resourceType`, `status`, `page`, `pageSize`) |
| `GET /api/v1/resources/{id}` | `resource.read` | Get one |
| `POST /api/v1/resources/{id}/activate` | `resource.write` | Draft/PendingVerification/Inactive → Active |
| `POST /api/v1/resources/{id}/suspend` | `resource.write` | Active → Suspended |
| `POST /api/v1/resources/{id}/reactivate` | `resource.write` | Suspended → Active |
| `POST /api/v1/resources/{id}/deactivate` | `resource.write` | Active/Suspended → Inactive |
| `POST /api/v1/resources/{id}/retire` | `resource.write` | Any non-Retired → Retired |
| `POST /api/v1/resources/{id}/capabilities` | `resource.write` | Attach a capability |
| `POST /api/v1/resources/{id}/skills` | `resource.write` | Attach a skill |
| `POST /api/v1/resources/{id}/certifications` | `resource.write` | Attach a certification |

Lifecycle transitions accept an optional `expectedVersion` for optimistic
concurrency (`412` if it doesn't match the current version) and return
`409 Conflict` if the current status doesn't allow that transition.

**Create a resource:**

```powershell
$body = @{
    resourceType = "Equipment"
    name         = "Van 12"
    timezone     = "America/Toronto"
    capacity     = 2
    skills       = @("refrigerated", "liftgate")
} | ConvertTo-Json

$resource = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/resources" -Headers $Headers -ContentType "application/json" -Body $body
$resource
```

Expected response (`201 Created`) — verified live against QA:

```json
{
  "id": "fa2b7e2d-e759-4e59-b779-4665b7adbdcb",
  "tenantId": "...",
  "resourceType": "Equipment",
  "name": "Van 12",
  "status": "Active",
  "verificationStatus": "NotRequired",
  "timezone": "America/Toronto",
  "capacity": 2,
  "skills": "refrigerated,liftgate",
  "version": 1,
  "createdAt": "2026-07-19T..."
}
```

**A Resource is created `Active` already** — there is no Draft step to walk
through first. Calling `.../activate` on it immediately returns `409
Conflict` (`"Cannot activate a Resource in status Active."`), since
`activate` is only legal from `Draft`, `PendingVerification`, or `Inactive`.
Use `suspend`/`deactivate` if you want to exercise a lifecycle transition
against a freshly created resource:

```powershell
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/resources/$($resource.id)/suspend" -Headers $Headers -ContentType "application/json" -Body '{}'
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/resources/$($resource.id)/reactivate" -Headers $Headers -ContentType "application/json" -Body '{}'
```

`resourceType` and status names are free-text/enum, not tenant-configurable
lookups — valid `ResourceStatus` values: `Draft`, `PendingVerification`,
`Active`, `Suspended`, `Inactive`, `Retired`.

---

## 5. Core service — Availability, Time Off, Overrides

Gateway routes: `/api/v1/availability-rules/{**catch-all}`,
`/api/v1/availability/{**catch-all}`, `/api/v1/time-off-requests/{**catch-all}`,
plus `/api/v1/resources/{id}/availability*` (covered by the `core-resources`
catch-all) — all → Core.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/availability-rules` | `availability.write` | Create a recurring weekly rule (`dayOfWeek`, `startTime`, `endTime`) |
| `GET /api/v1/resources/{id}/availability?date=YYYY-MM-DD` | `availability.read` | Free windows for one resource on one date |
| `POST /api/v1/availability/resolve` | `availability.read` | Free windows for one resource over an arbitrary time range |
| `POST /api/v1/availability/batch-resolve` | `availability.read` | Same, for multiple resources at once |
| `POST /api/v1/resources/{id}/time-off-requests` | `availability.write` | Request time off (starts `PendingApproval`) |
| `POST /api/v1/time-off-requests/{id}/approve` | `availability.write` | Approve |
| `POST /api/v1/time-off-requests/{id}/reject` | `availability.write` | Reject |
| `POST /api/v1/time-off-requests/{id}/cancel` | `availability.write` | Cancel (from Pending or Approved) |
| `POST /api/v1/resources/{id}/availability-overrides` | `availability.write` | One-off force-available/force-unavailable window |

**Create a Monday 9am–5pm rule, then resolve availability:**

```powershell
$rule = @{
    resourceId = $resource.id
    dayOfWeek  = "Monday"
    startTime  = "09:00:00"
    endTime    = "17:00:00"
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/availability-rules" -Headers $Headers -ContentType "application/json" -Body $rule

$resolve = @{
    resourceId = $resource.id
    startTime  = "2026-07-20T00:00:00Z"
    endTime    = "2026-07-27T00:00:00Z"
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/availability/resolve" -Headers $Headers -ContentType "application/json" -Body $resolve
```

Expected response — free intervals after applying the priority stack (hard
unavailability > approved time off > force-available override > exception >
recurring rule):

```json
{
  "resourceId": "3fa85f64-...",
  "freeIntervals": [
    { "start": "2026-07-20T09:00:00+00:00", "end": "2026-07-20T17:00:00+00:00" }
  ]
}
```

---

## 6. Core service — Calendar

Covered by the `core-resources` gateway route (`/api/v1/resources/{**catch-all}`).

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `GET /api/v1/resources/{id}/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD` | `reservation.read` | Reservations overlapping the range for one resource |

```powershell
Invoke-RestMethod -Uri "$GW/api/v1/resources/$($resource.id)/calendar?from=2026-07-20&to=2026-07-27" -Headers $Headers
```

```json
{ "resourceId": "3fa85f64-...", "entries": [
    { "reservationId": "...", "start": "2026-07-21T14:00:00+00:00", "end": "2026-07-21T15:00:00+00:00", "status": "Confirmed" }
] }
```

---

## 7. Core service — Capacity

Gateway route: `/api/v1/capacity/{**catch-all}` → Core.

A generic ledger for a Resource's *non-calendar* capacity — e.g. a van with
two cargo bays, a technician who can carry 5 concurrent tickets — separate
from whether the Resource's calendar is booked.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/capacity/profiles` | `capacity.write` | Create a CapacityProfile for a resource |
| `GET /api/v1/capacity/profiles/{id}` | `capacity.read` | Get one |
| `POST /api/v1/capacity/profiles/{id}/definitions` | `capacity.write` | Define a dimension (e.g. `cargo_bays`, max quantity) |
| `PATCH /api/v1/capacity/definitions/{id}` | `capacity.write` | Update a definition |
| `POST /api/v1/capacity/resolve` | `capacity.read` | `{ effectiveCapacity, consumedCapacity, heldCapacity, remainingCapacity, canFulfil }` for a window |
| `POST /api/v1/capacity/explain` | `capacity.read` | Same computation, itemized (`definedCapacity`, `activeHolds`, `confirmedConsumption`) |
| `POST /api/v1/capacity/holds` | `capacity.write` | Acquire a hold (requires `idempotencyKey`) |
| `GET /api/v1/capacity/holds/{id}` | `capacity.read` | Get a hold |
| `POST /api/v1/capacity/holds/{id}/commit` | `capacity.write` | Hold → committed Consumption |
| `POST /api/v1/capacity/holds/{id}/release` | `capacity.write` | Release an active hold |
| `POST /api/v1/capacity/consumptions/{id}/release` | `capacity.write` | Release committed consumption |

**Define capacity, then check remaining:**

```powershell
$profile = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/capacity/profiles" -Headers $Headers -ContentType "application/json" `
    -Body (@{ resourceId = $resource.id; name = "Van 12 cargo"; timezone = "America/Toronto" } | ConvertTo-Json)

$definition = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/capacity/profiles/$($profile.id)/definitions" -Headers $Headers -ContentType "application/json" `
    -Body (@{ name = "Cargo bays"; capacityModel = "Concurrent"; dimensionCode = "cargo_bays"; maximumQuantity = 2; unit = "Count"; timeBasis = "Concurrent" } | ConvertTo-Json)

Invoke-RestMethod -Method Post -Uri "$GW/api/v1/capacity/resolve" -Headers $Headers -ContentType "application/json" `
    -Body (@{ resourceId = $resource.id; dimensionCode = "cargo_bays"; startTime = "2026-07-21T14:00:00Z"; endTime = "2026-07-21T15:00:00Z"; requiredQuantity = 1 } | ConvertTo-Json)
```

```json
{ "effectiveCapacity": 2, "consumedCapacity": 0, "heldCapacity": 0, "remainingCapacity": 2, "canFulfil": true }
```

`CapacityModel`, `CapacityUnit`, `CapacityTimeBasis` are enums defined in
`src/Eswmp.Core/Models/` — check there for the full valid-value set if a
`400` complains about an unrecognized string.

---

## 8. Core service — Duration Estimation

Gateway route: `/api/v1/duration/{**catch-all}` → Core.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/duration/estimate` | `availability.read` | Estimate a job's duration from tenant-configured `DurationSizeBracket`/`DurationTagRule` rows |

```powershell
$body = @{ resourceType = "Equipment"; defaultBaseMinutes = 30; sizeValue = 12.5; attributeTags = @("fragile") } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/duration/estimate" -Headers $Headers -ContentType "application/json" -Body $body
```

```json
{ "estimatedMinutes": 30, "baseMinutes": 30, "bufferMinutes": 0, "safetyAlertReason": null, "requiresSafetyAlert": false }
```

QA has no seeded `DurationSizeBracket`/`DurationTagRule` rows, so a fresh
tenant just gets `defaultBaseMinutes` back unchanged — that's expected, not a
bug.

---

## 9. Core service — Reservations

Gateway route: `/api/v1/reservations/{**catch-all}` → Core.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/reservations` | `reservation.create` | Hold a time window on a resource (conflict-checked) |
| `GET /api/v1/reservations/{id}` | `reservation.read` | Get one |
| `POST /api/v1/reservations/{id}/confirm` | `reservation.confirm` | Held → Confirmed (also creates an Appointment) |
| `POST /api/v1/reservations/{id}/cancel` | `reservation.cancel` | Any → Cancelled |

`externalReferenceType`/`externalReferenceId` are the opaque pointer back
into your own domain (`CLAUDE.md` — ESWMP never inspects them). Any
non-empty strings work for testing.

```powershell
$body = @{
    resourceId          = $resource.id
    startTime            = "2026-07-21T14:00:00Z"
    endTime              = "2026-07-21T15:00:00Z"
    holdDurationMinutes  = 15
    externalReferenceType = "test-booking"
    externalReferenceId   = "TEST-001"
} | ConvertTo-Json

$reservation = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/reservations" -Headers $Headers -ContentType "application/json" -Body $body
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/reservations/$($reservation.id)/confirm" -Headers $Headers -ContentType "application/json" -Body '{}'
```

`201` on create, reservation starts `Held` with `expiresAt` = now +
`holdDurationMinutes`. A second overlapping reservation on the same resource
returns `409 Conflict`.

---

## 10. Core service — Slots

Gateway route: `/api/v1/slots/{**catch-all}` → Core.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/slots/search` | `availability.read` | Bookable start times for a resource/date/duration, existing reservations excluded |

```powershell
$body = @{ resourceId = $resource.id; date = "2026-07-21"; requestedDurationMinutes = 60; minimumBookableDurationMinutes = 30 } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/slots/search" -Headers $Headers -ContentType "application/json" -Body $body
```

```json
{ "slots": ["2026-07-21T00:00:00+00:00", "2026-07-21T01:00:00+00:00", "..."] }
```

With no `AvailabilityRule` configured for that resource/day, expect an empty
or all-day slot list depending on what the optimizer treats as the default
window — pair this with §5 first if you want slots constrained to business
hours.

---

## 11. Assignment service — Assignment scoring/log

Gateway route: `/api/v1/assignments/{**catch-all}` → Assignment.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/assignments/score` | `assignment.read` | Stateless: rank candidate resources for a reservation (proximity/skills/workload/speed-fit) |
| `POST /api/v1/assignments` | `assignment.execute` | Persist an `AssignmentLog` record for a chosen resource |

**Score candidates:**

```powershell
$body = @{
    reservationId              = $reservation.id
    targetLatitude              = 43.65
    targetLongitude             = -79.38
    requiredSkills               = @("refrigerated")
    estimatedDurationMinutes     = 60
    candidates = @(
        @{ resourceId = $resource.id; currentLatitude = 43.66; currentLongitude = -79.39; skills = @("refrigerated"); activeReservationCount = 1 }
    )
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post -Uri "$GW/api/v1/assignments/score" -Headers $Headers -ContentType "application/json" -Body $body
```

```json
{
  "reservationId": "...",
  "rankedCandidates": [
    { "resourceId": "3fa85f64-...", "score": 0.87, "distanceMetres": 1450.2, "skillMatch": true, "rationale": "..." }
  ]
}
```

This endpoint is pure computation — it never reads/writes any database, so
it works fine against any `reservationId` (even one that doesn't exist),
since the reservation ID is just echoed back, not looked up.

**Log the chosen assignment:**

```powershell
$logBody = @{ reservationId = $reservation.id; resourceId = $resource.id; method = "Auto"; score = 0.87 } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/assignments" -Headers $Headers -ContentType "application/json" -Body $logBody
```

`AssignmentMethod` enum: check `src/Eswmp.Assignment/Models/AssignmentLog.cs`
for valid values (`Auto`, `Manual`, etc.) if `400` complains.

---

## 12. Assignment service — Matching

Gateway routes: `/api/v1/matching/{**catch-all}` and
`/api/v1/matching-policies/{**catch-all}` → Assignment. The richer, versioned,
explainable successor to the plain scorer in §11 — every evaluation is
persisted and can be re-explained per candidate later.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/matching/evaluations` | `matching.execute` | Score + persist a ranked candidate list |
| `GET /api/v1/matching/evaluations/{id}` | `matching.read` | Get a persisted evaluation + its results |
| `GET /api/v1/matching/evaluations/{id}/candidates/{candidateId}/explanation` | `matching.read` | Per-factor breakdown for one candidate |
| `POST /api/v1/matching/evaluations/{id}/recalculate` | `matching.execute` | Re-score with new candidates/weights, invalidates the old evaluation |
| `POST /api/v1/matching-policies` | `matching.execute` | Create a named policy (a container for weight-versions) |
| `POST /api/v1/matching-policies/{id}/versions` | `matching.execute` | Add a Draft weight configuration |
| `POST /api/v1/matching-policies/{id}/versions/{version}/activate` | `matching.execute` | Draft/Validated → Active (weights must sum to ~1.0) |

**Evaluate without a custom policy (uses the built-in "Balanced" strategy):**

```powershell
$body = @{
    requiredSkills   = @("refrigerated")
    targetLatitude    = 43.65
    targetLongitude   = -79.38
    candidates = @(
        @{ type = "Resource"; id = $resource.id; skills = @("refrigerated"); latitude = 43.66; longitude = -79.39; currentWorkload = 1 }
    )
    limit = 5
} | ConvertTo-Json -Depth 5

$eval = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/matching/evaluations" -Headers $Headers -ContentType "application/json" -Body $body
$eval
Invoke-RestMethod -Uri "$GW/api/v1/matching/evaluations/$($eval.matchEvaluationId)" -Headers $Headers
```

```json
{
  "matchEvaluationId": "...",
  "evaluatedAt": "2026-07-19T...",
  "expiresAt": "2026-07-19T...",
  "results": [
    { "candidateId": "3fa85f64-...", "candidateType": "Resource", "rank": 1, "score": 0.91, "recommendationLevel": "Strong", "primaryReasonCode": null }
  ]
}
```

An evaluation expires 15 minutes after creation (`expiresAt`) — that's a
data-lifetime marker on the record, not an enforced read cutoff; `GET` still
works after expiry.

---

## 13. Rules service — Business Rules

Gateway route: `/api/v1/rules/{**catch-all}` → Rules.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/rules` | `rule.write` | Create a tenant-configurable `BusinessRule` (opaque JSON `definition`) |
| `GET /api/v1/rules?ruleType=...` | `rule.read` | List active rules, optional type filter |

```powershell
$body = @{
    name         = "Weekend safety alert"
    ruleType     = "SafetyAlert"
    resourceType = "Equipment"
    definition   = @{ trigger = "weekend"; alert = "Confirm gate access before dispatch" }
    isActive     = $true
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post -Uri "$GW/api/v1/rules" -Headers $Headers -ContentType "application/json" -Body $body
Invoke-RestMethod -Uri "$GW/api/v1/rules?ruleType=SafetyAlert" -Headers $Headers
```

`definition` is stored as raw JSON and never interpreted by ESWMP itself
(`CLAUDE.md` rule 8) — any well-formed JSON object is valid input; what it
means is entirely up to whichever tenant/consumer reads it back.

---

## 14. Rules service — Workflow

Gateway route: `/api/v1/workflow/{**catch-all}` → Rules.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/workflow/transitions/validate` | `workflow.transition` | Check if a `fromState → toState` transition is legal |

```powershell
$body = @{ fromState = "Confirmed"; toState = "InProgress" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/workflow/transitions/validate" -Headers $Headers -ContentType "application/json" -Body $body
```

```json
{ "isValid": true, "reason": null }
```

Valid `WorkflowState` values: `Draft`, `Requested`, `Reserved`,
`PendingApproval`, `Assigned`, `Confirmed`, `Travelling`, `Arrived`,
`CheckedIn`, `InProgress`, `Completed`, `Cancelled`, `NoShow`, `Rejected`.
This endpoint is pure computation (no persistence) — safe to call
repeatedly with any combination while exploring which transitions are legal.

---

## 15. Work service — Demand Intake

Gateway route: `/api/v1/demands/{**catch-all}` → Work.

Converts a caller's industry-specific ask into ESWMP's generic `Demand` —
the front door before a `WorkRequirement` exists (§18).

| Method & Path | Permission | Purpose | Idempotency-Key required? |
| --- | --- | --- | --- |
| `POST /api/v1/demands` | `demand.create` | Create + auto-validate a demand | **Yes** |
| `GET /api/v1/demands/{id}` | `demand.read` | Get one | — |
| `POST /api/v1/demands/search` | `demand.read` | Paged search (filters: `status`, `priority`, `demandType`, `fulfillmentMode`, `fromUtc`, `toUtc`) | — |
| `PATCH /api/v1/demands/{id}` | `demand.create` | Update mutable fields (optimistic concurrency via `expectedVersion`) | — |
| `POST /api/v1/demands/{id}/validate` | `demand.create` | Re-run validation | — |
| `POST /api/v1/demands/{id}/accept` | `demand.transition` | Ready → Accepted | — |
| `POST /api/v1/demands/{id}/reject` | `demand.transition` | → Rejected (requires `reasonCode`) | — |
| `POST /api/v1/demands/{id}/cancel` | `demand.transition` | → Cancelled | — |
| `GET /api/v1/demands/{id}/history` | `demand.read` | Always `[]` today — no audit trail table exists yet | — |

`Idempotency-Key` is a required **header** (not body field) on `POST
/api/v1/demands` — a missing header is a `400 VALIDATION_FAILED`, a repeat of
the same key with a *different* body is `409 IDEMPOTENCY_CONFLICT`, a repeat
with the *same* body replays the original `201` response.

```powershell
$headers2 = $Headers + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$body = @{
    demandType             = "ServiceVisit"
    sourceSystem            = "remote-test"
    priority                 = "Normal"
    summary                  = "Test demand from remote QA session"
    requestedStartAtUtc      = "2026-07-21T14:00:00Z"
    requestedEndAtUtc        = "2026-07-21T15:00:00Z"
    requestedTimezone        = "America/Toronto"
    externalReferenceType    = "test-booking"
    externalReferenceId      = "TEST-001"
    fulfillmentMode          = "Scheduled"
} | ConvertTo-Json

$demand = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/demands" -Headers $headers2 -ContentType "application/json" -Body $body
$demand.status   # always "Received" right after create — Create only rejects on hard errors, it
                 # does not itself promote to "Ready"; call /validate (below) for that
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/demands/$($demand.id)/validate" -Headers $Headers -ContentType "application/json" -Body '{}'
# -> { demandId, status: "Valid", validatedAt, issues: [] } and the demand's own status is now "Ready"
```

Valid enums: `DemandStatus` = `Received, Validating, Ready, Accepted,
Rejected, Cancelled, Expired`; `DemandPriority` = `Low, Normal, High, Urgent,
Critical`; `DemandFulfillmentMode` = `Scheduled, OnDemand, Recurring,
Standby` (a `Scheduled` demand *requires* both
`requestedStartAtUtc`/`requestedEndAtUtc` or validation flags an `Error`).

**Error envelope example** (missing Idempotency-Key header):

```json
{ "error": "The Idempotency-Key header is required.", "code": "VALIDATION_FAILED", "traceId": "00-...-01" }
```

---

## 16. Work service — Requirement Definitions (legacy)

Gateway routes: `/api/v1/requirement-definitions/{**catch-all}` and
`/api/v1/requirement-definition-snapshots/{**catch-all}` → Work.

> First-generation Work Requirement model, kept alongside §18's reconciled
> surface rather than removed (see the provenance note on
> `Eswmp.Work.Models.RequirementDefinition`). Prefer §17/§18 for new testing
> unless you're specifically exercising this older surface.

| Method & Path | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/requirement-definitions` | `requirementdefinition.write` | Create (unique `code` per tenant) |
| `GET /api/v1/requirement-definitions/{id}` | `requirementdefinition.read` | Get with full version/requirement tree |
| `POST /api/v1/requirement-definitions/search` | `requirementdefinition.read` | Paged search (`status`, `category`) |
| `POST /api/v1/requirement-definitions/{id}/versions` | `requirementdefinition.write` | Add a Draft version (resource/capability/skill/certification/location requirements) |
| `GET /api/v1/requirement-definitions/{id}/versions/{versionNumber}` | `requirementdefinition.read` | Get one version |
| `PATCH /api/v1/requirement-definitions/{id}/versions/{versionNumber}` | `requirementdefinition.write` | Edit a Draft version (rejects if not Draft) |
| `POST /api/v1/requirement-definitions/{id}/versions/{versionNumber}/validate` | `requirementdefinition.write` | Draft → Validated if structurally sound |
| `POST /api/v1/requirement-definitions/{id}/versions/{versionNumber}/activate` | `requirementdefinition.write` | Draft/Validated → Active (supersedes prior active version) |
| `POST /api/v1/requirement-definitions/{id}/retire` | `requirementdefinition.write` | → Retired |
| `POST /api/v1/requirement-definitions/{id}/snapshots` | `requirementdefinition.write` | Freeze a version as an immutable snapshot |
| `GET /api/v1/requirement-definition-snapshots/{id}` | `requirementdefinition.read` | Get a snapshot |

```powershell
$def = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/requirement-definitions" -Headers $Headers -ContentType "application/json" `
    -Body (@{ code = "TEST-DEF-001"; name = "Test requirement definition"; category = "Delivery" } | ConvertTo-Json)

$versionBody = @{
    changeSummary       = "initial"
    durationType         = "Fixed"
    fixedDurationMinutes = 60
    preWorkBufferMinutes  = 10
    postWorkBufferMinutes = 10
    resourceRequirements = @(
        @{
            resourceTypeCode = "Equipment"
            minimumQuantity   = 1
            preferredQuantity = 1
            maximumQuantity   = 1
            mandatory          = $true
            capabilities       = @(@{ capabilityCode = "refrigerated"; importance = "Mandatory" })
        }
    )
} | ConvertTo-Json -Depth 6

$version = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/requirement-definitions/$($def.id)/versions" -Headers $Headers -ContentType "application/json" -Body $versionBody
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/requirement-definitions/$($def.id)/versions/1/activate" -Headers $Headers -ContentType "application/json" -Body (@{ expectedVersion = 1 } | ConvertTo-Json)
```

`CapabilityImportance` = `Mandatory, Preferred, Optional`.
`DefinitionDurationType` = `Fixed` (needs `fixedDurationMinutes`) or `Range`
(needs `minimumDurationMinutes`/`expectedDurationMinutes`/`maximumDurationMinutes`,
Minimum ≤ Expected ≤ Maximum). Activation uses `expectedVersion` against the
*definition's* `ConcurrencyVersion` (starts at `1` on creation, not `0`), not
the version number — verified live against QA.

---

## 17. Work service — Requirement Templates

Gateway route: `/api/v1/work-requirement-templates/{**catch-all}` → Work.
Reusable operational defaults a `WorkRequirement` (§18) resolves against.
**Templates are immutable after activation** — a change is always a new
version, never an edit in place.

| Method & Path | Permission | Purpose | Idempotency-Key required? |
| --- | --- | --- | --- |
| `POST /api/v1/work-requirement-templates` | `workrequirement.template.create` | Create (Draft, version 1 auto-created) | **Yes** |
| `GET /api/v1/work-requirement-templates/{id}` | `workrequirement.template.read` | Get with versions | — |
| `POST /api/v1/work-requirement-templates/search` | `workrequirement.template.read` | Paged search (`workType`, `status`) | — |
| `POST /api/v1/work-requirement-templates/{id}/versions` | `workrequirement.template.update` | New Draft version | **Yes** |
| `GET /api/v1/work-requirement-templates/{id}/versions/{version}` | `workrequirement.template.read` | Get one version | — |
| `PUT /api/v1/work-requirement-templates/{id}/versions/{version}/requirements` | `workrequirement.template.update` | Set the full `RequirementSetDto` body (only while Draft) | — |
| `POST /api/v1/work-requirement-templates/{id}/versions/{version}/activate` | `workrequirement.template.activate` | Draft → Active (validates first; supersedes prior active version) | — |
| `POST /api/v1/work-requirement-templates/{id}/retire` | `workrequirement.template.retire` | → Retired | — |

```powershell
$headers2 = $Headers + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$template = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirement-templates" -Headers $headers2 -ContentType "application/json" `
    -Body (@{ code = "TEST-TPL-001"; name = "Test delivery template"; workType = "Delivery" } | ConvertTo-Json)

$requirements = @{
    resourceRequirements = @(
        @{ roleCode = "Driver"; resourceCategory = "Person"; minimumQuantity = 1; required = $true }
    )
    durationRequirement = @{ durationType = "Estimated"; estimatedDurationMinutes = 60 }
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Put -Uri "$GW/api/v1/work-requirement-templates/$($template.id)/versions/1/requirements" -Headers $Headers -ContentType "application/json" -Body $requirements
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirement-templates/$($template.id)/versions/1/activate" -Headers $Headers -ContentType "application/json" -Body '{}'
```

`resourceRequirements` and `durationRequirement` are the only two required
fields of the `RequirementSetDto` body — every other category
(`capabilityRequirements`, `certificationRequirements`, `capacityRequirements`,
`timeRequirement`, `locationRequirement`, `executionRequirement`,
`travelRequirement`, `bufferRequirements`, `dependencyRequirements`,
`constraints`, `preferences`) is optional. Full shape:
`src/Eswmp.Work/Services/RequirementSetDto.cs`. `ResourceCategory`: `Person,
Team, Vehicle, Facility, Room, Equipment, VirtualResource`.

---

## 18. Work service — Work Requirements

Gateway route: `/api/v1/work-requirements/{**catch-all}` → Work. This is the
reconciled Work Requirement Service — the canonical contract every
downstream module (Eligibility/Matching/Capacity/Scheduling) reads. Its core
operation is **resolve**: Demand + Template → an operational `WorkRequirement`.

| Method & Path | Permission | Purpose | Idempotency-Key required? |
| --- | --- | --- | --- |
| `POST /api/v1/work-requirements/resolve` | `workrequirement.resolve` | Materialize a template into a `WorkRequirement` (`201`) | **Yes** |
| `GET /api/v1/work-requirements/{id}` | `workrequirement.read` | Summary view | — |
| `GET /api/v1/work-requirements/{id}/versions/{version}` | `workrequirement.read` | One historical snapshot | — |
| `GET /api/v1/work-requirements/{id}/resolved` | `workrequirement.read` | Full resolved contract (every requirement category) | — |
| `POST /api/v1/work-requirements/{id}/validate` | `workrequirement.validate` | Re-run the 7-category validator | — |
| `POST /api/v1/work-requirements/{id}/revisions` | `workrequirement.revise` | Patch named categories (`changes` body), bumps `requirementVersion` | **Yes** |
| `GET /api/v1/work-requirements/{id}/compare?fromVersion=&toVersion=` | `workrequirement.read` | Diff two versions' snapshots | — |
| `GET /api/v1/work-requirements/{id}/explain` | `workrequirement.explain` | Human-readable summary + per-requirement provenance | — |
| `POST /api/v1/work-requirements/{id}/cancel` | `workrequirement.revise` | → Cancelled (terminal) | — |

**Resolve against the template from §17:**

```powershell
$headers2 = $Headers + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$resolveBody = @{
    sourceType   = "Demand"
    sourceId     = $demand.id
    templateCode = "TEST-TPL-001"
} | ConvertTo-Json

$wr = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirements/resolve" -Headers $headers2 -ContentType "application/json" -Body $resolveBody
$wr
Invoke-RestMethod -Uri "$GW/api/v1/work-requirements/$($wr.workRequirementId)/resolved" -Headers $Headers
Invoke-RestMethod -Uri "$GW/api/v1/work-requirements/$($wr.workRequirementId)/explain" -Headers $Headers
```

```json
{ "workRequirementId": "...", "requirementVersion": 1, "templateCode": "TEST-TPL-001", "templateVersion": 1, "status": "Valid", "warnings": [] }
```

`resolve` requires the named `templateCode` to have an **Active** version —
`409 TEMPLATE_NOT_ACTIVE` otherwise, so run §17's activate step first.
`sourceType`/`sourceId` don't have to reference a real `Demand` for testing
purposes — they're recorded, not dereferenced.

**Revise (bump a capacity requirement):**

```powershell
$headers2 = $Headers + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$revision = @{
    expectedVersion = 1
    reason           = "test revision"
    changes = @{ capacityRequirements = @(@{ dimensionCode = "seat_count"; quantity = 2 }) }
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirements/$($wr.workRequirementId)/revisions" -Headers $headers2 -ContentType "application/json" -Body $revision
```

`expectedVersion` must match the requirement's current `requirementVersion`
— `412 VERSION_CONFLICT` otherwise. Valid `WorkRequirementStatus`: `Draft,
Validating, Valid, Invalid, Superseded, Cancelled, Completed`
(`Cancelled`/`Completed` are terminal — further revise/cancel calls `409`).

---

## 19. End-to-end walkthrough

A single PowerShell session exercising all four services in one realistic
flow — create a resource, make it available, take a demand, resolve a work
requirement against it, score/match candidates, reserve the slot:

```powershell
$GW = "https://eswmp-gateway-staging.bravehill-5af5160d.canadacentral.azurecontainerapps.io"
$TOKEN = "<token from generate-qa-jwt.ps1>"
$Headers = @{ Authorization = "Bearer $TOKEN" }
$json = @{ ContentType = "application/json" }

# 1. Core: a resource (created Active already, no separate activation step needed), available Mondays 9-5
$resource = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/resources" -Headers $Headers @json `
    -Body (@{ resourceType = "Equipment"; name = "Van 12"; timezone = "America/Toronto" } | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/availability-rules" -Headers $Headers @json `
    -Body (@{ resourceId = $resource.id; dayOfWeek = "Monday"; startTime = "09:00:00"; endTime = "17:00:00" } | ConvertTo-Json)

# 2. Work: a demand + a template, resolved into a WorkRequirement
$idKey = @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$demand = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/demands" -Headers ($Headers + $idKey) @json `
    -Body (@{ demandType = "Delivery"; sourceSystem = "remote-test"; requestedStartAtUtc = "2026-07-20T14:00:00Z"; requestedEndAtUtc = "2026-07-20T15:00:00Z"; externalReferenceType = "test"; externalReferenceId = "E2E-1" } | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/demands/$($demand.id)/validate" -Headers $Headers @json -Body '{}'   # Received -> Ready, required before /accept works

$idKey2 = @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$template = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirement-templates" -Headers ($Headers + $idKey2) @json `
    -Body (@{ code = "E2E-TPL"; name = "E2E template"; workType = "Delivery" } | ConvertTo-Json)
Invoke-RestMethod -Method Put -Uri "$GW/api/v1/work-requirement-templates/$($template.id)/versions/1/requirements" -Headers $Headers @json `
    -Body (@{ resourceRequirements = @(@{ roleCode = "Driver"; resourceCategory = "Person"; minimumQuantity = 1 }); durationRequirement = @{ durationType = "Estimated"; estimatedDurationMinutes = 60 } } | ConvertTo-Json -Depth 6)
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirement-templates/$($template.id)/versions/1/activate" -Headers $Headers @json -Body '{}'

$idKey3 = @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }
$wr = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/work-requirements/resolve" -Headers ($Headers + $idKey3) @json `
    -Body (@{ sourceType = "Demand"; sourceId = $demand.id; templateCode = "E2E-TPL" } | ConvertTo-Json)

# 3. Assignment: score the resource as a candidate
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/assignments/score" -Headers $Headers @json -Body (@{
    reservationId = [guid]::NewGuid(); candidates = @(@{ resourceId = $resource.id }) } | ConvertTo-Json -Depth 5)

# 4. Core: reserve and confirm the slot
$reservation = Invoke-RestMethod -Method Post -Uri "$GW/api/v1/reservations" -Headers $Headers @json `
    -Body (@{ resourceId = $resource.id; startTime = "2026-07-20T14:00:00Z"; endTime = "2026-07-20T15:00:00Z"; holdDurationMinutes = 15; externalReferenceType = "test"; externalReferenceId = "E2E-1" } | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/reservations/$($reservation.id)/confirm" -Headers $Headers @json -Body '{}'

# 5. Work: mark the demand accepted
Invoke-RestMethod -Method Post -Uri "$GW/api/v1/demands/$($demand.id)/accept" -Headers $Headers @json -Body '{}'
```

The token used across this whole session needs every permission touched:
`resource.write, availability.write, demand.create, demand.transition,
workrequirement.template.create, workrequirement.template.update,
workrequirement.template.activate, workrequirement.resolve, assignment.read,
reservation.create, reservation.confirm`.

---

## 20. Troubleshooting

| Symptom | Meaning | Fix |
| --- | --- | --- |
| `401 Unauthorized` | Token missing, expired, or signed with the wrong key | Re-check the `Authorization: Bearer` header; mint a new token |
| `403 Forbidden` | Token is valid but lacks the endpoint's required permission | Check the "Permission" column above; mint a new token with it added |
| `404 Not Found` on a `GET` right after a `POST` | Usually a different `tenant_id` between the two calls | Reuse the same token for the whole session |
| Empty `items: []` on every list/search | QA has no seeded data for a fresh `tenant_id` | Create data first, or ask for a token scoped to an existing tenant |
| `503` on the very first call of a session | `min_replicas = 0` cold start | Retry 2-3 times, a few seconds apart |
| `GET /` (bare Gateway URL) → `404` in a browser | Expected — the Gateway only proxies specific `/api/v1/...` and `/health` routes, nothing is mapped at `/` | Call a real API path, not the bare URL |
| `400 VALIDATION_FAILED` mentioning `Idempotency-Key` | That header is required on Work service create/mutate endpoints (see per-section tables) | Add `"Idempotency-Key" = [guid]::NewGuid().ToString()` to the headers |
| `409 IDEMPOTENCY_CONFLICT` | Same `Idempotency-Key` reused with a different body | Use a fresh GUID per distinct request |
| `412 Precondition Failed` / `VERSION_CONFLICT` | Optimistic-concurrency mismatch (`expectedVersion` stale) | `GET` the current version first, retry with the current value |

For deploying code changes that affect these endpoints, or diagnosing an
actual outage rather than one of the above, see
`docs/Azure/DEPLOYMENT-WORKFLOW-GUIDE.md`.
