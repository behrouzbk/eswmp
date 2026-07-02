# ESWMP — Product Vision

> Source: originally proposed by Hassan (co-founder) as `docs/hassan.txt` in the
> PetZiv repository, in response to a review of PetZiv's scheduling architecture.
> Reproduced here as the founding product document for this repository. Text is
> lightly reformatted for Markdown; content is unchanged from the original proposal.

## The core idea

Make the Scheduling Service a generic enterprise scheduling platform that could be
reused outside of any single vertical. It would manage only calendars, resources,
availability, appointments, and scheduling rules. Everything specific to a
particular business — owners, providers, payments, approvals, tasks, medical
records, reviews — lives in separate services (or separate products entirely)
that communicate through APIs and events.

This approach gives:

- **Loose coupling**: each service evolves independently.
- **Scalability**: each service can scale based on its own workload.
- **Reusability**: the scheduler can support grooming, walking, boarding,
  veterinary visits, daycare, transportation, and non-pet businesses.
- **Maintainability**: business rules remain in the appropriate domain services.
- **Future extensibility**: AI scheduling, route optimization, dynamic pricing,
  and workforce optimization can be added without redesigning the scheduler.

## 1. Layered architecture

```
                 Presentation Layer
                        |
          Web / Mobile / Admin Portal
                        |
────────────────────────────────────────────────
              Scheduling API Gateway
────────────────────────────────────────────────
                        |
        Scheduling Domain Microservices
────────────────────────────────────────────────
                        |
      Rules Engine / Optimization Engine
────────────────────────────────────────────────
                        |
             Infrastructure Services
```

## 2. Core microservices (target domain map)

The original proposal sketched ~20 independent services: Scheduling, Availability,
Calendar, Reservation, Appointment, Assignment, Resource, Capacity, Conflict
Detection, Optimization Engine, Routing, Geofencing, Travel Time, Task, Approval,
Workflow, Notification Adapter, Audit, Reporting, Integration Gateway.

**This repository does not implement that as 20 deployables.** It's treated as a
target *domain map* — a list of responsibilities to eventually own — consolidated
into 3 services + a gateway for the initial build, for the same reason PetZiv's
own architecture consolidated 35 business domains into 5 clusters: a small team
building 20 separate deployables creates constant cross-service overhead with no
corresponding benefit until there's real scale or multiple paying tenants to
justify it. See `docs/ARCHITECTURE.md` for the actual service boundaries and the
mapping from this domain list to `Eswmp.Core` / `Eswmp.Assignment` / `Eswmp.Rules`.

## 3. Resource

Everything starts with Resources. **Never schedule employees. Schedule resources.**

```
Resource
- id
- type
- name
- status
- location
- capacity
- skills
- calendar
- timezone
```

Examples: Provider, Mobile Grooming Van, Kennel, Room, Dog Walker, Trainer, Vet,
Play Area, Equipment.

## 4. Scheduling Engine

The brain. Responsibilities: find availability, create reservations, prevent
conflicts, move appointments, recurring appointments, buffer time, split
appointments, travel time, capacity planning, optimization, auto assignment.
**No business logic.**

## 5. Availability Engine

Availability is rule based: working hours, recurring schedule, vacation, holiday,
sick leave, lunch, emergency, blocked time, maximum jobs, maximum hours, travel
buffer, preferred area, preferred customer. **Never hardcode availability.**

## 6. Rules Engine

Rules should be configurable, not compiled. Examples: maximum 6 jobs/day, maximum
8 hours, need 30 minutes travel, cannot handle resources above a size threshold,
need certification, no appointments after 8 PM, no high-risk cases without
review, need assistant for two large jobs. **No code changes to adjust a rule.**

## 7. Optimization Engine

Eventually AI-driven. Inputs: location, traffic, distance, weather, priority,
revenue, skills, provider rating, travel time, customer preference. Output: best
provider, best route, best time, lowest cost, highest revenue.

## 8. Assignment Engine

Separate scheduling from assignment.

```
Customer books
    |
Scheduler reserves
    |
Assignment Engine
    |
Finds best resource
    |
Approval
    |
Confirm
```

This enables: automatic assignment, manual assignment, AI assignment, round
robin, least busy, nearest, highest rated, premium resource.

## 9. Workflow Engine

Every appointment has a workflow: Draft, Requested, Reserved, PendingApproval,
Assigned, Confirmed, Travelling, Arrived, CheckedIn, InProgress, Completed,
Cancelled, NoShow, Rejected, Expired. **Workflow should be configurable.**

Events to publish: SlotReserved, ReservationConfirmed, ReservationCancelled,
ReservationExpired, AvailabilityChanged, ResourceUnavailable.

## 10. Workforce Module

What commercial schedulers provide, for the resource-as-employee case:
Employee, Certification, License, Shift, Payroll Hours, Overtime, Vehicle,
Uniform, Inventory, GPS, Attendance, Performance.

## 11. Calendar Engine

Support multiple calendar views over the same APIs: Daily, Weekly, Monthly,
Timeline, Agenda, Resource, Map, Gantt, Kanban.

## 12. Event driven

```
Appointment Reserved -> Event Bus -> Approval -> Notification -> Task Manager
    -> CRM -> Billing -> Analytics
```

## 13. API design

REST plus events:

```
GET /resources
GET /availability
GET /slots
POST /reservations
POST /appointments
PATCH /appointments
DELETE /appointments
POST /assignments
GET /calendar
GET /capacity
POST /optimize
POST /rules/evaluate
```

## 14. Database separation

Each service owns its data: Scheduling DB, Availability DB, Assignment DB, Task
DB, Reporting DB, Audit DB. **Never share tables.**

## 15. Plugin framework

An area for differentiation:

```
Scheduling Engine
    |
 ┌──┼────────┐
Travel Plugin, Pricing Plugin, Rule Plugin, Optimization Plugin,
Calendar Plugin, Notification Plugin, AI Plugin, Integration Plugin
```

Customers can replace any plugin.

## 16. Multi-tenant

Build multi-tenant from day one: Tenant, Timezone, Business Hours, Holiday
Calendar, Branding, Custom Rules, Languages, Currency, Integrations.
PetZiv becomes Tenant #1.

## 17. AI layer

Eventually every decision becomes AI assisted: predict no-show, suggest time,
predict cancellation, optimize route, recommend provider, estimate duration,
estimate delay, demand forecast, revenue forecast.

## 18. Technology stack (as originally proposed)

| Layer | Technology |
|---|---|
| API | ASP.NET Core (.NET 10) |
| Messaging | Azure Service Bus or Apache Kafka |
| Cache | Redis |
| Database | PostgreSQL |
| Search | OpenSearch |
| Workflow | Temporal or Dapr Workflows |
| Rules Engine | Microsoft RulesEngine or Drools |
| Scheduler (background jobs) | Quartz.NET |
| Authentication | OAuth2/OIDC with JWT |
| Observability | OpenTelemetry + Prometheus + Grafana |
| File Storage | Azure Blob Storage |
| Maps | Google Maps or Azure Maps |
| AI | Azure OpenAI + Semantic Kernel |

**Note on deviations in this repo**: .NET 9 (not 10, for parity with the proven
PetZiv toolchain at time of writing), Azure Monitor/Jaeger (not
Prometheus/Grafana, again for toolchain parity), MassTransit as the messaging
abstraction over RabbitMQ (local) / Azure Service Bus (prod). Temporal/Dapr,
Drools/RulesEngine, OpenSearch, and Quartz.NET are deferred — see
`docs/ARCHITECTURE.md` for why and what's built instead.

## 19. Biggest recommendation

Don't start by building the UI. First design the platform using Domain-Driven
Design and define: bounded contexts, domain model, event model, API contracts,
state machines, permissions, extension/plugin points. Only then build the user
interfaces.

## 20. Scheduling Service owns

Resources, Calendars, Availability, Time Slots, Reservations, Appointments,
Conflicts, Exceptions.

## 21. Scheduling Service does not own

Pet profile, Owner profile, Provider profile details, Payment, Pricing,
Approval, Task execution, Notification, Reviews, Medical records.

## 22. Core objects

```
Resource
- id
- tenantId
- type
- name
- timezone
- status

Calendar
- id
- resourceId
- timezone

AvailabilityRule
- resourceId
- dayOfWeek
- startTime
- endTime
- recurrence
- effectiveFrom
- effectiveTo

TimeOff / Exception
- resourceId
- startTime
- endTime
- reason

Reservation
- id
- resourceId
- startTime
- endTime
- status: held | confirmed | expired | cancelled
- expiresAt

Appointment
- id
- reservationId
- resourceId
- startTime
- endTime
- status
```

## 23. Minimum APIs

```
POST /resources
GET /resources/{id}

POST /availability-rules
GET /resources/{id}/availability

POST /slots/search

POST /reservations
POST /reservations/{id}/confirm
POST /reservations/{id}/cancel

GET /resources/{id}/calendar
```

## 24. Main workflow

```
Booking Service asks for slots
        |
Scheduling Service returns available slots
        |
Booking Service asks to reserve one slot
        |
Scheduling Service creates temporary hold
        |
Approval / Payment happens outside scheduler
        |
Booking Service confirms or cancels reservation
        |
Scheduling Service publishes event
```

## 25. Key rule

The scheduler should only expose generic metadata:

```json
{
  "externalReferenceType": "booking",
  "externalReferenceId": "BK-12345"
}
```

**Not this:**

```json
{
  "petName": "Lucky",
  "ownerName": "John",
  "groomingPackage": "Premium"
}
```

This is the single most load-bearing rule in the whole platform — see CLAUDE.md
rule 1, which enforces it for all future code generation.

## 26. First implementation scope (MVP)

1. Resource calendar
2. Availability rules
3. Slot search
4. Temporary reservation/hold
5. Confirm reservation
6. Cancel reservation
7. Conflict prevention
8. Events

## 27. Events to publish

`SlotReserved`, `ReservationConfirmed`, `ReservationCancelled`,
`ReservationExpired`, `AvailabilityChanged`, `ResourceUnavailable`.

## 28. Recommended first database tables

`resources`, `calendars`, `availability_rules`, `availability_exceptions`,
`reservations`, `appointments`, `outbox_events`.
