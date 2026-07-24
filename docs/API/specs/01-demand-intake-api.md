**ESWMP --- Demand Intake Service**

API Reference --- DTOs, Security & Idempotency

**Base path:** /api/v1/demands Contract: contracts/openapi/work.v1.yaml
Service: Eswmp.Work :6004

This is the working contract for the Demand Intake API: DTOs, the error
model, security, and idempotency & concurrency. Section 11 is the
pre-implementation verification checklist the team runs against
work.v1.yaml and a live UAT capture before coding.

*Provenance: the endpoint list, permissions, status/mutability rules,
validation codes, and events are established by the source
specification. The concrete JSON schemas, error envelope, and the
fulfillmentMode field describe this contract and are the values to
confirm against work.v1.yaml and a captured UAT response, since response
JSON cannot be derived from static artifacts.*

**1. Conventions**

- **JSON casing:** camelCase on the wire, PascalCase in C#. Matches the
  spec examples (expectedVersion, reasonCode).

- **Auth:** two-tier --- APIM validates JWT + subscription key; the
  gateway pipeline enforces per-operation permission. Full detail in §9.

- **Tenancy:** TenantId derives from the token, never the body.
  Cross-tenant access returns 404, never 403 (no existence disclosure).

- **Timestamps:** ISO-8601 UTC; fields carry the Utc suffix.

- **Idempotency & concurrency:** POST requires Idempotency-Key; PATCH
  requires expectedVersion. Full semantics in §10.

**2. Error model**

All 4xx responses share one shape. The top-level error string is a
human-readable summary; code is a stable machine-readable discriminator;
issues\[\] is present whenever field-level problems exist --- on create,
patch, and validate alike, so callers have one validation contract
everywhere.

**Error envelope**

  -----------------------------------------------------------------------
  {

  \"error\": \"Human-readable summary (backward-compatible).\",

  \"code\": \"VALIDATION_FAILED\", // stable, machine-branchable

  \"traceId\": \"00-4bf92f\...-01\", // correlation for support

  \"issues\": \[ // present when field-level

  { \"field\": \"requestedEndAtUtc\",

  \"code\": \"INVALID_TIME_WINDOW\",

  \"severity\": \"Error\",

  \"message\": \"End must be after start.\" }

  \]

  }
  -----------------------------------------------------------------------

**Error codes**

  -----------------------------------------------------------------------
  **code**                 **Meaning / typical status**
  ------------------------ ----------------------------------------------
  VALIDATION_FAILED        Body failed field validation --- 400.
                           issues\[\] populated.

  IDEMPOTENCY_CONFLICT     Same key, different body --- 409.

  VERSION_CONFLICT         expectedVersion mismatch --- 412.

  STATUS_CONFLICT          Operation not allowed in current status ---
                           409.

  NOT_FOUND                No such demand in caller\'s tenant --- 404.

  UNAUTHENTICATED          Missing/invalid JWT or subscription key ---
                           401.

  FORBIDDEN                Token lacks the required permission --- 403.

  RATE_LIMITED             Too many requests --- 429 (see §9).

  PAYLOAD_TOO_LARGE        Body exceeds size cap --- 413 (see §9).
  -----------------------------------------------------------------------

*A consumer may read only error and ignore the rest; code and issues
carry the structured detail. traceId is the correlation handle to quote
in support requests.*

**3. Shared object --- Demand**

Returned by GET, search rows, and action endpoints.

  ----------------------------------------------------------------------------------
  **Field**                **Type**     **Req**   **Notes**
  ------------------------ ------------ --------- ----------------------------------
  id                       uuid         **yes**   Server-assigned

  tenantId                 uuid         **yes**   From token

  organizationId           uuid?        no        Opaque

  demandType               string       **yes**   Caller-defined nature of work

  fulfillmentMode          enum         **yes**   OnDemand \| Scheduled \| Recurring
                                                  \| Standby

  sourceSystem             string       **yes**   Submitting caller system

  sourceChannel            string?      no        e.g. MobileApp

  status                   enum         **yes**   Received...Expired

  priority                 enum         **yes**   Low...Critical

  summary                  string?      no        

  description              string?      no        

  requestedStartAtUtc      datetime?    no        Window start

  requestedEndAtUtc        datetime?    no        Window end

  requestedTimezone        string?      no        IANA tz

  locationReference        object?      no        Opaque JSON

  requirementReferenceId   uuid?        no        Set on match

  externalReferenceType    string       **yes**   Opaque pointer

  externalReferenceId      string       **yes**   Opaque pointer

  version                  int          **yes**   Concurrency counter

  createdAt                datetime     **yes**   Audit

  updatedAt                datetime?    no        Audit
  ----------------------------------------------------------------------------------

**4. Endpoints**

**POST /api/v1/demands demand.create**

Creates a Demand in Received status. Requires Idempotency-Key (§10).
Same key + identical body replays the original 201; same key + different
body → 409 IDEMPOTENCY_CONFLICT.

**Headers**

  -----------------------------------------------------------------------
  Idempotency-Key: \<caller-supplied string\> (required)

  Authorization: Bearer \<jwt\>

  Ocp-Apim-Subscription-Key: \<key\>
  -----------------------------------------------------------------------

**Request body**

  ---------------------------------------------------------------------------------
  **Field**               **Type**     **Req**   **Notes**
  ----------------------- ------------ --------- ----------------------------------
  demandType              string       **yes**   Caller-defined

  fulfillmentMode         enum         no        Defaults to Scheduled

  sourceSystem            string       **yes**   

  sourceChannel           string?      no        

  priority                enum         no        Defaults to Normal

  summary                 string?      no        

  description             string?      no        

  requestedStartAtUtc     datetime?    no        

  requestedEndAtUtc       datetime?    no        Must be \> start

  requestedTimezone       string?      no        

  locationReference       object?      no        Opaque

  externalReferenceType   string       **yes**   Opaque pointer

  externalReferenceId     string       **yes**   Opaque pointer
  ---------------------------------------------------------------------------------

**Responses**

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **201**    Created, or replayed on     Demand object
             matching retry              

  **400**    Field validation failed     error + code=VALIDATION_FAILED +
                                         issues\[\]

  **409**    Same key, different body    error + code=IDEMPOTENCY_CONFLICT

  **413**    Body exceeds size cap       error + code=PAYLOAD_TOO_LARGE

  **429**    Rate limit exceeded         error + code=RATE_LIMITED

  **401 /    Auth failure / missing      error + code
  403**      permission                  
  --------------------------------------------------------------------------

**GET /api/v1/demands/{id} demand.read**

Single Demand, tenant-scoped.

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Found                       Demand object

  **404**    Not found or other tenant   error + code=NOT_FOUND

  **401 /    Auth / permission           error + code
  403**                                  
  --------------------------------------------------------------------------

**POST /api/v1/demands/search demand.read**

Filtered, paged search within the tenant.

**Filters (all optional)**

  ---------------------------------------------------------------------------
  **Field**         **Type**     **Req**   **Notes**
  ----------------- ------------ --------- ----------------------------------
  status            enum?        no        

  priority          enum?        no        

  demandType        string?      no        

  fulfillmentMode   enum?        no        Exact mode match

  createdFromUtc    datetime?    no        Range start

  createdToUtc      datetime?    no        Range end

  page              int          no        1-based; default 1

  pageSize          int          no        Default 50; bounded server-side
  ---------------------------------------------------------------------------

**Response --- PagedResult\<Demand\>**

  -----------------------------------------------------------------------
  { \"items\": \[ /\* Demand \*/ \], \"page\": 1, \"pageSize\": 50,
  \"totalCount\": 128 }

  -----------------------------------------------------------------------

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Executed (may be empty)     PagedResult\<Demand\>

  **400**    Invalid filter              error + code=VALIDATION_FAILED +
                                         issues\[\]

  **429**    Rate limit                  error + code=RATE_LIMITED
  --------------------------------------------------------------------------

**PATCH /api/v1/demands/{id} demand.create**

Partial update guarded by optimistic concurrency (§10) and
status-dependent mutability. Requires expectedVersion; server increments
version on success. PATCH is NOT idempotent by key --- the version guard
is its safety mechanism instead.

**Request body**

  -------------------------------------------------------------------------------
  **Field**             **Type**     **Req**   **Notes**
  --------------------- ------------ --------- ----------------------------------
  expectedVersion       int          **yes**   412 VERSION_CONFLICT on mismatch

  priority              enum?        no        Mutable in Received and Ready

  summary               string?      no        Received only

  description           string?      no        Received only

  requestedStartAtUtc   datetime?    no        Received only

  requestedEndAtUtc     datetime?    no        Received only

  fulfillmentMode       enum?        no        Received only
  -------------------------------------------------------------------------------

**Mutability by status**

- Received --- broadly mutable.

- Ready --- priority only; else 409 STATUS_CONFLICT.

- Accepted / Rejected / Cancelled / Expired --- immutable; 409
  STATUS_CONFLICT.

**Responses**

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Updated; version            Demand object
             incremented                 

  **400**    Malformed / missing         error + code=VALIDATION_FAILED +
             expectedVersion             issues\[\]

  **409**    Field not mutable in status error + code=STATUS_CONFLICT

  **412**    expectedVersion mismatch    error + code=VERSION_CONFLICT

  **404**    Not found / other tenant    error + code=NOT_FOUND
  --------------------------------------------------------------------------

**5. Action endpoints**

Lifecycle state changes. accept/reject/cancel require demand.transition;
validate requires demand.create. All are guarded against terminal
statuses, and accept/reject/cancel publish an event (§7). Actions are
not key-idempotent but are idempotent by status (§10.4).

**POST /api/v1/demands/{id}/validate demand.create**

Runs the rule set (§6); Received → Ready when clean, stays Received on
error; records a DemandValidationResult either way. No body.

**Response --- DemandValidationResult**

  -----------------------------------------------------------------------
  {

  \"demandId\": \"9f3a\...\",

  \"status\": \"ValidWithWarnings\",

  \"validatedAt\": \"2026-07-11T18:22:04Z\",

  \"issues\": \[ { \"field\": \"description\", \"code\":
  \"MISSING_DESCRIPTION\",

  \"severity\": \"Warning\", \"message\": \"\...\" } \]

  }
  -----------------------------------------------------------------------

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Ran (pass or fail)          DemandValidationResult

  **409**    Terminal status             error + code=STATUS_CONFLICT
  --------------------------------------------------------------------------

**POST /api/v1/demands/{id}/accept demand.transition**

Only from Ready → Accepted (terminal). Publishes DemandAcceptedEvent. No
body.

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Accepted; event published   Demand (status=Accepted)

  **409**    Not in Ready                error + code=STATUS_CONFLICT
  --------------------------------------------------------------------------

**POST /api/v1/demands/{id}/reject demand.transition**

Not from terminal. reasonCode required. Publishes DemandRejectedEvent.

**Request body**

  --------------------------------------------------------------------------
  **Field**        **Type**     **Req**   **Notes**
  ---------------- ------------ --------- ----------------------------------
  reasonCode       string       **yes**   400 VALIDATION_FAILED if blank

  comment          string?      no        
  --------------------------------------------------------------------------

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Rejected; event published   Demand (status=Rejected)

  **400**    reasonCode blank            error + code=VALIDATION_FAILED +
                                         issues\[\]

  **409**    Terminal status             error + code=STATUS_CONFLICT
  --------------------------------------------------------------------------

**POST /api/v1/demands/{id}/cancel demand.transition**

Not from terminal. Publishes DemandCancelledEvent. Optional comment
body.

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Cancelled; event published  Demand (status=Cancelled)

  **409**    Terminal status             error + code=STATUS_CONFLICT
  --------------------------------------------------------------------------

**GET /api/v1/demands/{id}/history demand.read**

Known-empty collection: returns \[\] until an audit table exists, and is
declared in OpenAPI as intentionally empty so integrators do not build
against a populated shape.

  --------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- -----------------------------------
  **200**    Always \[\] today           \[\] (documented empty)

  **404 /    Not found / auth            error + code
  401 /                                  
  403**                                  
  --------------------------------------------------------------------------

**6. Validation rules**

Run by validate, and also on create/patch, returning the same issues\[\]
contract. Any Error keeps Received; warnings-only → Ready with
ValidWithWarnings.

  --------------------------------------------------------------------------------------
  **Field**                    **Type**     **Req**   **Notes**
  ---------------------------- ------------ --------- ----------------------------------
  MISSING_EXTERNAL_REFERENCE   Error                  type or id blank

  INVALID_TIME_WINDOW          Error                  start \>= end

  MISSING_DESCRIPTION          Warning                no summary and no description

  MODE_WINDOW_REQUIRED         Error                  Scheduled with no requested window

  MODE_WINDOW_UNEXPECTED       Warning                OnDemand with a future window
  --------------------------------------------------------------------------------------

The two MODE\_ rules make the fulfillmentMode axis enforceable: a
Scheduled demand must carry a requested window, an OnDemand demand
should not. Warning-severity on the OnDemand case keeps it permissive;
Error on the Scheduled case keeps the downstream scheduling handoff
well-formed.

**7. Emitted events**

Published via MassTransit; carry only opaque platform identifiers ---
never externalReference or caller-domain fields. No consumer exists yet.

  --------------------------------------------------------------------------------
  **Field**              **Type**     **Req**   **Notes**
  ---------------------- ------------ --------- ----------------------------------
  DemandAcceptedEvent    on accept              demandId, tenantId, correlationId

  DemandRejectedEvent    on reject              demandId, tenantId, reasonCode,
                                                comment?, correlationId

  DemandCancelledEvent   on cancel              demandId, tenantId, correlationId
  --------------------------------------------------------------------------------

**8. Status & transitions**

- Received → Ready validate (no errors)

- Ready → Accepted accept (terminal)

- Received / Validating / Ready → Rejected reject

- Received / Validating / Ready → Cancelled cancel

- Expired defined, unreachable (no expiry job)

**9. Security**

Intake is an internet-facing write surface, so it carries the
platform\'s strongest edge controls. The model is two-tier: APIM at the
edge, the gateway pipeline in-cluster.

**9.1 Authentication**

  -----------------------------------------------------------------------
  **Layer**           **Control**
  ------------------- ---------------------------------------------------
  APIM edge           validate-jwt inbound policy requires a valid signed
                      JWT AND a subscription key. Both must be present;
                      neither alone suffices.

  JWT                 Signature, issuer, audience, and expiry verified at
                      the edge. Claims carry tenant and permissions.

  Subscription key    Ocp-Apim-Subscription-Key identifies the calling
                      partner/app and drives rate-limit buckets.

  Gateway pipeline    Re-derives identity and enforces the per-operation
                      permission independently of APIM --- defence in
                      depth, not trust-the-edge.
  -----------------------------------------------------------------------

Three permissions separate the distinct authorities, so a partner that
may submit work need not also be able to accept or cancel it:

  -----------------------------------------------------------------------
  **Permission**        **Gates**
  --------------------- -------------------------------------------------
  demand.read           get, search, history

  demand.create         create (POST /) and validate

  demand.transition     accept, reject, cancel --- the lifecycle state
                        changes
  -----------------------------------------------------------------------

Splitting transition from create at the outset avoids a breaking
widening later: an external caller can be granted create without being
handed the authority to accept or cancel demands.

**9.3 Tenant isolation**

- TenantId is taken from the token, never the request body --- a caller
  cannot assert another tenant.

- Cross-tenant reads return 404, not 403, so existence is not disclosed.

- Enforced in two layers: EF query filters on every entity, plus a
  database-level row-level-security policy keyed on tenant --- so
  isolation holds even for a code path that bypasses the ORM. Both cover
  DemandValidationResult, not only Demand.

**9.4 Input hardening**

  -----------------------------------------------------------------------
  **Control**         **Detail**
  ------------------- ---------------------------------------------------
  Body size cap       Reject oversized bodies with 413 PAYLOAD_TOO_LARGE
                      before parsing (locationReference is free-form
                      jsonb --- a natural abuse vector).

  Field bounds        Enforce max lengths on all strings; the DDL lengths
                      (varchar(100/200/500)) are the contract.

  jsonb depth/size    Bound locationReference nesting depth and byte
                      size; reject rather than store unbounded JSON.

  Strict types        Reject unknown enum values (status, priority,
                      fulfillmentMode) at the edge, not deep in the
                      handler.

  No HTML/script echo summary/description are stored opaque and never
                      rendered by the platform; downstream consumers must
                      encode on output.
  -----------------------------------------------------------------------

**9.5 Rate limiting & abuse**

- Per-subscription-key rate limits at APIM; exceed → 429 RATE_LIMITED
  with a Retry-After header.

- Tighter limits on POST / than on reads, since create is the expensive,
  state-changing path.

- Idempotency (§10) blunts retry storms --- a client hammering the same
  key gets replays, not duplicate demands.

**9.6 Transport & audit**

- TLS only; no plaintext. Reject non-HTTPS at the edge.

- traceId (§2) correlates a request across APIM, gateway, and logs
  without exposing internals to the caller.

- Do not log full request bodies containing locationReference or
  external references at info level --- treat as potentially sensitive.

**10. Idempotency & concurrency**

**10.1 Key scope & lifetime**

  -----------------------------------------------------------------------
  **Aspect**         **Behaviour**
  ------------------ ----------------------------------------------------
  Scope              Uniqueness is (TenantId, Idempotency-Key) --- keys
                     never collide across tenants.

  Applies to         POST / only. Reads are naturally idempotent;
                     PATCH/actions use the version + status guards
                     instead.

  Retention          Records are kept 24h (covering the client retry
                     window), then purged. After expiry the same key is
                     treated as new.

  Client duty        Callers must generate a fresh key per logical create
                     and reuse it only for retries of that same create.
  -----------------------------------------------------------------------

**10.2 Match semantics**

On POST, the server hashes the canonical request body (SHA-256) and
looks up the key within the tenant:

  -----------------------------------------------------------------------
  **Situation**         **Result**
  --------------------- -------------------------------------------------
  Key unseen            Create normally; store key + request hash + the
                        201 body.

  Key seen, hash        Replay the stored 201 verbatim --- no second
  matches               demand, no second event.

  Key seen, hash        409 IDEMPOTENCY_CONFLICT --- the same key was
  differs               reused for a different body.
  -----------------------------------------------------------------------

*The hash is computed over a canonical form of the body --- stable key
order, whitespace-insensitive --- so a semantically identical retry with
reordered JSON keys replays rather than false-positiving as a conflict.*

**10.3 Concurrent first-writes (the race)**

Two requests with the same new key can arrive simultaneously before
either has committed. The unique (TenantId, IdempotencyKey) index is the
arbiter: the first insert wins; the second fails the constraint and is
transparently converted into the replay path (return the winner\'s
stored 201), not surfaced as an error. Do not rely on a read-then-insert
check alone --- it has a gap between the read and the insert.

**10.4 Concurrency on mutations**

  -----------------------------------------------------------------------
  **Endpoint**       **Guard**
  ------------------ ----------------------------------------------------
  PATCH              Optimistic concurrency: expectedVersion must equal
                     current; the update is a single atomic UPDATE \...
                     WHERE Id=@id AND Version=@expected --- 0 rows → 412.
                     This closes the lost-update race between two
                     concurrent PATCHes.

  accept / reject /  Guarded by status, not version. Naturally idempotent
  cancel             in effect: a second accept on an already-Accepted
                     demand hits the terminal-status guard → 409
                     STATUS_CONFLICT rather than double-publishing an
                     event.

  Event publication  One transition = one event. The status guard ensures
                     a retried action cannot emit a duplicate
                     DemandAcceptedEvent / etc.
  -----------------------------------------------------------------------

Event publication uses the transactional outbox pattern: the state
change and the outbox row commit in one transaction, and a relay
publishes from the outbox afterward. This guarantees a state change and
its event cannot diverge --- a crash between commit and publish leaves
the event pending in the outbox, not lost. It is what makes the
once-per-transition guarantee hold under at-least-once delivery when
Work Management begins consuming these.

**11. Verification checklist**

Response JSON cannot be derived from static artifacts, so before coding
to this contract the team confirms each item below against work.v1.yaml
and one live UAT capture per endpoint.

  -----------------------------------------------------------------------
  **Item**              **Confirm**
  --------------------- -------------------------------------------------
  **Casing**            camelCase vs PascalCase on every field, against a
                        real response.

  **Error envelope**    error present; code / traceId / issues field
                        names and when each appears.

  **PagedResult**       items / page / pageSize / totalCount names and
                        whether more fields exist.

  **Validation result** DemandValidationResult shape and the issue object
                        (field/code/severity/message).

  **Nullability**       which response fields are omitted vs null when
                        empty.

  **fulfillmentMode**   the column, enum values, default, and search
                        filter exist as designed.

  **Headers**           Idempotency-Key, Retry-After (429), and any
                        others APIM injects.

  **Status codes**      413 / 429 are actually emitted by the current
                        edge config.
  -----------------------------------------------------------------------

*Established by the source specification: the eight endpoints, the
permission model, status and mutability rules, the base validation
codes, and the three events. This contract additionally specifies the
concrete JSON schemas, the error envelope, fulfillmentMode and its
validation rules, the three-way permission split, tenant RLS, input and
rate limits, and the idempotency and outbox mechanics. Section 11 lists
what to confirm against work.v1.yaml and a UAT capture before
implementation, since response wire format cannot be derived from static
artifacts.*

**12. v2 Delta (2026-07-24) --- NeedsAttention lifecycle, triage/ownership, recurrence, lineage, audit**

*Source: docs/API/specs/update_demand-schema.sql,
docs/API/specs/v2-delta-summary.docx §2. Closes the highest-priority gap
the UX review found: a failed resolution was silent --- the demand read
Accepted while no WorkRequirement was ever created, and the customer had
already been told their request was received.*

**12.1 Data model**

  -----------------------------------------------------------------------
  **Change**                       **Detail**
  --------------------------------- -------------------------------------
  demand_status --- NeedsAttention  Appended last (ordinal 7 in the
                                    as-built int-column enum, never
                                    inserted mid-list --- see §12.6).
                                    Non-terminal; entered from Received
                                    (validation error) and from Accepted
                                    (resolution failure).

  attention_owner (new)            Customer \| Dispatcher \|
                                    Administrator.

  AssignedTo / AssignedRole (new)  Triage ownership.

  AttentionReason /                Why; a snapshot of issues\[\] or the
  AttentionIssuesJson (new)        resolution failure detail.

  ResolutionAttempts /             Counted, not capped --- see the open
  LastResolutionError (new)        D-02 product decision.

  RecurrenceRule / SeriesId (new)  RFC 5545 RRULE + series identity.

  DemandLineage (new table)        Split/merge provenance --- SplitFrom /
                                    MergedInto edges.

  DemandAuditEntries (new table)   Real audit trail; GET /{id}/history no
                                    longer always returns \[\].
  -----------------------------------------------------------------------

**12.2 New and revised endpoints**

  -----------------------------------------------------------------------------
  **Method / path**                          **Change**            **Detail**
  ------------------------------------------- --------------------- -----------
  POST /demands/{id}/flag-attention           New (P1)              Requires demand.transition.

  POST /demands/{id}/retry-resolution         New (P1)              Re-emits DemandAcceptedEvent. Requires demand.transition.

  GET /demands/{id}, POST /demands/search     Revised               Both now return externalStatus (§12.3).

  GET /demands/{id}/history                   Revised               Was always \[\]; now the real DemandAuditEntries trail.

  POST /demands/{id}/assign                   New (P3)              Requires the new demand.assign.

  POST /demands/{id}/escalate                 New (P3)              Must raise priority. Requires the new demand.escalate.

  POST /demands/bulk/accept, bulk/reject,     New (P3)              Per-item results; one failure doesn't abort the batch.
  bulk/cancel                                                       Requires demand.transition.

  GET /demands/metrics                        New (P3)              Counts by status, mode, priority, age band.

  POST /demands/{id}/split                    New (P3)              Requires the new demand.split. Parent is not
                                                                     auto-cancelled.

  POST /demands/merge                         New (P3)              Requires the new demand.merge.

  GET /demands/{id}/audit                     New (P3)              Same shape as the revised {id}/history.

  GET /demands/history                        New (P5)              Customer-scoped by externalReferenceType/Id;
                                                                     customer-safe fields only, no internal detail.
  -----------------------------------------------------------------------------

**12.3 externalStatus**

A derived, customer-safe projection over the 8-value internal
demand_status (UX-17: the internal enum must not leak to customer
surfaces). The reviewed UX doc's own example states only NeedsAttention
-\> NeedsAttention; the rest of this mapping is an inferred, documented
choice made when implementing this contract:

  -----------------------------------------------------------------------
  **Internal status**              **externalStatus**
  --------------------------------- -------------------------------------
  Received, Validating             Submitted

  Ready                             Received

  NeedsAttention                    NeedsAttention

  Accepted                          Confirmed

  Rejected, Cancelled, Expired       Cancelled
  -----------------------------------------------------------------------

**12.4 Permissions**

Only demand.create/demand.read/demand.transition existed before this
delta. flag-attention, retry-resolution, and all three bulk/\* actions
reuse demand.transition (same authority, just a different trigger or
batched); metrics, {id}/audit, and the new history reuse demand.read.
Four genuinely different authorities got new permissions, matching the
Work Requirement module's precedent of splitting authorities that are
"genuinely different" rather than reusing broad ones: **demand.assign**,
**demand.escalate**, **demand.split**, **demand.merge**.

**12.5 Split and merge semantics**

Split: children are fresh Demand rows (new id, Status = Received)
cloning every parent field not overridden by the request, each getting a
DemandLineage row (Relation = SplitFrom). The parent is deliberately
**not** auto-cancelled --- no product decision says it should be; a
dispatcher cancels it separately if desired. Merge: each mergedId -\>
Cancelled (reuses the existing terminal state) plus a
DemandLineage(MergedInto) row; rejects a mergedId that's the survivor
itself or already terminal.

**12.6 Enum-ordinal safety**

The as-built demand_status is a plain integer column (EF's default
enum-to-int convention, confirmed against WorkDbContext.cs), not the
native Postgres enum this update's DDL literally declares.
**NeedsAttention had to be appended last (ordinal 7)**, not inserted at
its spec-narrative position (4th, between Ready and Accepted) ---
inserting it mid-list would have silently reshuffled the stored ordinals
of every status after it in the live database, corrupting every existing
row's meaning. The same class of issue this codebase already fixed once
for DemandFulfillmentMode's ordinal-0 sentinel.

**12.7 Resolution-failure wiring**

DemandAcceptedConsumer's four early-return branches (demand not found,
no Active template, no definitions, validation failed) previously only
logged a warning. Three of the four (all but "demand not found", which
has no demand to flag) now call
DemandRequirementLinkService.FlagResolutionFailedAsync --- the
Demand-module-owned service the consumer already called for the success
path (CLAUDE.md rule 11) --- which sets NeedsAttention, increments
ResolutionAttempts, and publishes DemandNeedsAttentionEvent. A successful
retry-resolution call clears NeedsAttention via the same
LinkRequirementAsync method the success path always used, once
resolution actually succeeds.

*Established by this delta: the 8 data-model items and 12 endpoint
changes above, each traced to a specific reviewed UX capability
(UX-09/UX-10/UX-14/UX-17) or an explicitly open product decision (D-02).
New events (DemandNeedsAttentionEvent, DemandAssignedEvent,
DemandEscalatedEvent, DemandSplitEvent, DemandMergedEvent) publish the
same way Demand's three existing events already do --- a direct
IPublishEndpoint.Publish after SaveChangesAsync, not the Work Requirement
module's transactional outbox, per an explicit scope decision to match
the existing pattern rather than introduce new infrastructure.*
