**ESWMP --- Demand Intake Service**

Database Model --- Diagram, Schema & Recommendations

**Service:** Eswmp.Work · Port 6004 · Database: eswmp_work · Schema:
demand

*Scope note: the diagram and tables describe the logical model from the
source specification, with the design recommendations folded in and
marked. Items shown as REC are proposed, not as-built; confirm against
the EF migrations and WorkDbContext.cs before treating them as fact.*

**1. Entity-relationship diagram**

Three tables in the demand schema, with the recommended foreign keys,
indexes, check constraint, and tenant-isolation additions applied. Each
column note carries its provenance.

■ **spec** as stated in the source specification

■ **REC** added per the recommendations in section 3

![](media/2c6753bc6336f3a7ce7878969b7938bcbe17fac5.png){width="5.604166666666667in"
height="7.427083333333333in"}

*Cardinality: one Demand has many validation results (one row per
validate call) and at most one idempotency record (one per create key).
Delete rules differ by intent --- cascade for validation results,
restrict for idempotency records.*

**2. Schema reference**

Columns as stated in the specification. Rows highlighted in purple are
additions from the recommendations.

**2.1 demand.Demands --- aggregate root**

  --------------------------------------------------------------------------------
  **Column**               **Type**       **Notes**
  ------------------------ -------------- ----------------------------------------
  Id                       uuid (PK)      TenantScopedEntity

  TenantId                 uuid           Query-filtered · RLS (rec)

  OrganizationId           uuid, null     Opaque --- no Organization service

  DemandType               varchar,       Caller-defined --- nature of work
                           required       

  **FulfillmentMode**      **enum (rec)** **Timing axis ---
                                          OnDemand/Scheduled/Recurring/Standby**

  SourceSystem             varchar,       Submitting caller system
                           required       

  SourceChannel            varchar, null  e.g. MobileApp, CallCenter

  Status                   enum, def.     Lifecycle state
                           Received       

  Priority                 enum, def.     Low → Critical
                           Normal         

  Summary                  varchar, null  Optional text

  Description              text, null     Optional text

  RequestedStartAtUtc      timestamptz,   Window start
                           null           

  RequestedEndAtUtc        timestamptz,   Window end
                           null           

  RequestedTimezone        varchar, null  Timezone id

  LocationReference        jsonb, null    Opaque

  RequirementReferenceId   uuid, null     Set once matched

  ExternalReferenceType    varchar,       Opaque pointer · indexed (rec)
                           required       

  ExternalReferenceId      varchar,       Opaque pointer · indexed (rec)
                           required       

  Version                  int, def. 1    Optimistic concurrency

  **CK_TimeWindow**        **check        **start \< end --- mirrors validate
                           (rec)**        rule**

  **IX_Search**            **index        **TenantId, Status, CreatedAt**
                           (rec)**        
  --------------------------------------------------------------------------------

Status: Received · Validating · Ready · Accepted · Rejected · Cancelled
· Expired. Priority: Low · Normal · High · Urgent · Critical.

**2.2 demand.DemandValidationResults**

  -----------------------------------------------------------
  **Column**       **Type**       **Notes**
  ---------------- -------------- ---------------------------
  Id               uuid (PK)      TenantScopedEntity

  TenantId         uuid           RLS (rec) --- see R1

  **DemandId**     **uuid, FK     **→ Demands, ON DELETE
                   (rec)**        CASCADE**

  Status           enum           Invalid · ValidWithWarnings
                                  · Valid

  ValidatedAt      timestamptz    Timestamp

  IssuesJson       jsonb          \[{code, severity,
                                  message}\]

  **IX_DVR**       **index        **TenantId, DemandId,
                   (rec)**        ValidatedAt**
  -----------------------------------------------------------

**2.3 demand.DemandIdempotencyRecords**

  -------------------------------------------------------------
  **Column**         **Type**       **Notes**
  ------------------ -------------- ---------------------------
  Id                 uuid (PK)      TenantScopedEntity

  TenantId           uuid           Unique with key · RLS (rec)

  IdempotencyKey     varchar,       From Idempotency-Key header
                     required       

  RequestHash        char(64)       SHA-256 of request JSON

  **DemandId**       **uuid, FK     **→ Demands, ON DELETE
                     (rec)**        RESTRICT**

  ResponseBodyJson   jsonb          Original 201, replayed
                                    verbatim
  -------------------------------------------------------------

**Unique index:** (TenantId, IdempotencyKey) --- the only constraint the
source spec states explicitly.

**3. Reference values**

Suggested value sets for the five categorical columns. DemandType,
SourceSystem, and SourceChannel hold the caller\'s own vocabulary ---
the platform stores them as opaque strings and must never branch on
them; the values shown are an illustrative PetZiv starter set,
configuration owned by the caller. FulfillmentMode and Status are
platform-owned closed sets that downstream logic depends on. Note the
deliberate split of DemandType (nature of work) from FulfillmentMode
(timing) --- see 3.1--3.2.

■ **spec** value set fixed by the specification --- closed, not
tenant-extensible

■ **REC** illustrative PetZiv starter set --- confirm the real list
before enforcing

**3.1 Demands.DemandType caller-defined**

DemandType carries one axis only: the nature of the work, in the
caller\'s own terms. It deliberately does NOT encode timing (on-demand
vs scheduled) --- that is a separate, platform-owned axis,
FulfillmentMode (section 3.2). Splitting them is what keeps the type set
extensible: a new service adds one DemandType with no platform change,
and a new fulfillment model never forks every type into an on-demand and
a scheduled variant.

Grounding: ServiceNow Field Service Management classifies work orders by
nature (reactive, preventive, inspection, installation); appointment and
on-demand platforms classify by timing (one-time, recurring, immediate).
On-demand marketplace guidance is explicit that instant-vs-scheduled is
a structural property driving matching, payment, and notification logic
--- building one model on the other\'s stack is cited as a leading cause
of costly rebuilds. Hence timing lives in its own column, not in
DemandType.

  -----------------------------------------------------------
  **Value**          **Meaning**
  ------------------ ----------------------------------------
  GroomingVisit      A grooming service (bath, cut, style)
                     for a pet.

  VetConsult         A veterinary consultation or advisory
                     interaction.

  DogWalk            A walking service, often the recurring
                     or on-demand case.

  PetTransport       A pickup / drop-off or movement request.

  Boarding           An overnight or multi-day stay.

  Training           A behaviour or obedience training
                     session.

  NailTrim           A short grooming add-on; shown to
                     illustrate cheap extensibility.

  Assessment         An evaluation to determine what work, if
                     any, is required.
  -----------------------------------------------------------

*These are illustrative PetZiv values on the nature-of-work axis.
Recommended handling: a per-tenant lookup owned by caller configuration,
not a platform enum --- the platform stores the string and never
branches on it.*

**3.2 Demands.FulfillmentMode platform-owned, closed set (rec)**

The timing axis --- how a demand is fulfilled in time. Unlike DemandType
this is platform-owned and closed, because downstream matching, booking,
and notification logic branches on it. A dedicated column (rather than
inferring from the requested window or overloading Priority) makes the
distinction explicit and queryable at the scheduling handoff.
Recommended NOT NULL default: Scheduled.

  -----------------------------------------------------------
  **Value**          **Meaning**
  ------------------ ----------------------------------------
  OnDemand           Immediate / near-immediate; routes to
                     real-time matching and dispatch.

  Scheduled          Booked for a specific future time or
                     window; routes to availability +
                     booking.

  Recurring          One instance of a repeating arrangement;
                     cadence lives in recurrence fields, not
                     here.

  Standby            Flexible / as-capacity-allows --- the
                     drop-in pattern from scheduling
                     platforms.
  -----------------------------------------------------------

*Closed set, like Status. Any DemandType can pair with any
FulfillmentMode --- the two axes are orthogonal, so a Demand row is
effectively one cell in a type-by-mode grid (e.g. GroomingVisit +
OnDemand, or DogWalk + Recurring).*

**3.3 Demands.SourceSystem caller-defined**

Which caller system submitted the request --- provenance, effectively.
Values should be stable machine identifiers, not display names. A slug
convention (lowercase, hyphenated) keeps them clean.

  -----------------------------------------------------------
  **Value**          **Meaning**
  ------------------ ----------------------------------------
  PetZivApp          The PetZiv consumer mobile application.

  PetZivWeb          The PetZiv customer web experience.

  PetZivAdmin        Internal staff/admin tooling creating
                     demands on a customer\'s behalf.

  PartnerAPI         A third-party partner submitting through
                     the public API.

  CallCenterCRM      The CRM used by call-centre agents.

  Migration          Records loaded in bulk during data
                     migration --- distinguishes backfill
                     from live intake.

  SystemScheduler    An automated internal process (e.g.
                     generating recurring instances).
  -----------------------------------------------------------

*Recommended handling: reserve one value (Migration or backfill) for
bulk-loaded rows so they stay separable from live traffic later.*

**3.4 Demands.SourceChannel caller-defined, nullable**

How the request reached the caller --- the human or interaction channel,
distinct from the software system in SourceSystem. Nullable, so decide
what null means: unknown versus not-applicable.

  --------------------------------------------------------------
  **Value**             **Meaning**
  --------------------- ----------------------------------------
  MobileApp             Submitted by the customer through a
                        mobile app.

  WebPortal             Submitted through a browser-based
                        portal.

  CallCenter            Taken by a human agent over the phone.

  Email                 Originated from an inbound email.

  Chat                  Originated from a chat or messaging
                        interaction.

  WalkIn                Captured in person at a physical
                        location.

  PartnerIntegration    Arrived via a partner\'s integration
                        rather than a direct customer action.

  AutomatedRecurrence   Generated by the system with no human
                        channel --- an explicit value in place
                        of null.
  --------------------------------------------------------------

*Recommended handling: use an explicit System/Automated value rather
than overloading null, if \'no human channel\' and \'channel unknown\'
both need to be distinguishable in reporting.*

**3.5 Demands.Status platform-owned, fixed by spec**

ESWMP\'s own lifecycle state. Unlike the three columns above, this set
is closed --- the lifecycle and mutability rules depend on the exact
values, so it is not tenant-extensible.

  -----------------------------------------------------------
  **Value**          **Meaning**
  ------------------ ----------------------------------------
  Received           Initial state on creation. Broadly
                     mutable via PATCH.

  Validating         Present in the mutability rules; a
                     Demand may not rest here if validate is
                     synchronous.

  Ready              Passed validation (Valid or
                     ValidWithWarnings). Only Priority is
                     mutable. Can be accepted.

  Accepted           Terminal. Publishes DemandAcceptedEvent.
                     Fully immutable.

  Rejected           Terminal. Requires a reasonCode.
                     Publishes DemandRejectedEvent.

  Cancelled          Terminal. Publishes
                     DemandCancelledEvent.

  Expired            Terminal and defined, but unreachable
                     today --- no expiry job sets it.
  -----------------------------------------------------------

*Reachable transitions: Received → Ready → Accepted; and
Received/Validating/Ready → Rejected or Cancelled. Nothing transitions
into Expired yet.*

**4. Recommendations applied**

Each item below is realized in the accompanying DDL and marked in the
diagram and tables above.

**R1 Confirm the tenant query filter on DemandValidationResults \[ High
\]**

The spec confirms HasQueryFilter on Demand and DemandIdempotencyRecord
but not on DemandValidationResult. If the filter is absent there,
validation results --- including IssuesJson, which may echo
caller-supplied content --- are reachable across tenants unless every
read joins through a filtered Demand. The DDL adds optional
row-level-security policies on all three tables as database-level
defence in depth.

**Action:** grep -n \"HasQueryFilter\"
src/Eswmp.Work/Data/WorkDbContext.cs

**R2 Enforce the parent links with explicit delete rules \[ Medium \]**

Both child tables now carry an explicit foreign key. Delete rules differ
by intent: CASCADE on validation results (a removed Demand should not
leave orphans) and RESTRICT on idempotency records (the create receipt
should outlive casual deletes so replays stay correct). A time-window
CHECK constraint mirrors the INVALID_TIME_WINDOW validation rule at the
database level.

**Action:** Confirm no code path hard-deletes a Demand in a way RESTRICT
would block; revisit alongside any purge policy.

**R3 Add a covering index for the search endpoint \[ Medium \]**

POST /search filters on status, priority, demandType and a created-date
range, all tenant-scoped. IX_Search on (TenantId, Status, CreatedAt)
with Priority and DemandType included keeps that query flat as intake
volume grows --- the exact scenario the spec names as the extraction
trigger.

**Action:** Validate with EXPLAIN once representative data exists.

**R4 Confirm the enum storage strategy \[ Low \]**

Status and Priority are modelled as native enum types for legibility and
safe reordering. If the code maps them as int via HasConversion, switch
the columns to smallint to match. Status drives the immutability rules,
so a silent remap would be a correctness bug.

**Action:** grep -rn \"HasConversion\" src/Eswmp.Work/Data/

**R5 Keep the Version check atomic \[ Low \]**

Version is application-managed: PATCH supplies expectedVersion, a
mismatch returns 412. The read-check-increment must run in one statement
(UPDATE ... WHERE Id = \@id AND Version = \@expected) so two concurrent
PATCHes cannot both write n+1.

**Action:** Verify the update path is a single guarded statement, not
read-then-write.

**R6 Keep deferred entities on the backlog, not in the schema \[
Informational \]**

Two documented simplifications should stay visible rather than becoming
dead affordances: no history/audit table (GET /history returns \[\]),
and Expired is a defined status with nothing that sets it. Both are fine
as staged delivery.

**Action:** Track the audit trail and expiry sweep as backlog items.

*Confirmed from the source specification: the three tables and their
fields, the (TenantId, IdempotencyKey) unique index, query filters on
Demand and DemandIdempotencyRecord, and the
validate-writes-on-every-call behaviour. Everything marked REC ---
foreign keys, indexes, the check constraint, RLS, enum storage --- is a
recommendation to verify against the codebase before it is stated as
fact.*
