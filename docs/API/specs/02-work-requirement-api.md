**ESWMP --- Work Requirement Service**

API Reference --- DTOs, Security & Idempotency

**Base paths:** /api/v1/work-requirements and
/api/v1/work-requirement-templates Domain: Demand & Intake

Aligned with the shipped Demand Intake API: same path prefix, same error
envelope, same idempotency and concurrency mechanics, same two-tier
security. The two services present one coherent surface within the
domain.

*Provenance: endpoints, permissions, statuses, validation categories,
and events come from the Work Requirement Service Specification
(Document 06). The specification supplies concrete request and response
examples, which are reproduced faithfully below with the domain\'s
shared conventions applied. Section 12 lists what to confirm against
work.v1.yaml and a UAT capture before implementation.*

# **1. Conventions**

- **Casing:** camelCase on the wire, PascalCase in C#.

- **Auth:** two-tier --- APIM validates JWT + subscription key; the
  gateway enforces the per-operation permission (§10).

- **Tenancy:** TenantId from the token, never the body. Cross-tenant
  access returns 404, never 403.

- **Concurrency:** revisions and template changes require
  expectedVersion; mismatch → 412.

- **Idempotency:** required on template creation, version creation,
  resolution, revision, override, and cancellation (§11).

- **Templates are immutable after activation.** A change is a new
  version, never an edit in place.

# **2. Error model**

The domain\'s shared envelope. error is the human-readable summary; code
is the stable machine discriminator; issues\[\] carries field-level
detail on any validation failure.

  -----------------------------------------------------------------------
  {

  \"error\": \"The work requirement contains incompatible constraints.\",

  \"code\": \"INVALID_WORK_REQUIREMENT\",

  \"traceId\": \"00-4bf92f\...-01\",

  \"issues\": \[

  { \"path\": \"resourceRequirements\",

  \"code\": \"MISSING_REQUIRED_RESOURCE_ROLE\",

  \"severity\": \"Error\",

  \"message\": \"Mobile grooming requires a vehicle resource role.\" }

  \]

  }
  -----------------------------------------------------------------------

**Error codes**

  ------------------------------------------------------------------------
  **code**                   **Meaning / status**
  -------------------------- ---------------------------------------------
  INVALID_WORK_REQUIREMENT   Requirement failed validation --- 422.
                             issues\[\] populated.

  VALIDATION_FAILED          Request body malformed --- 400.

  TEMPLATE_NOT_ACTIVE        Resolution referenced a Draft or Retired
                             template version --- 409.

  TEMPLATE_IMMUTABLE         Attempt to edit an activated template version
                             --- 409.

  IDEMPOTENCY_CONFLICT       Same key, different body --- 409.

  VERSION_CONFLICT           expectedVersion mismatch --- 412.

  STATUS_CONFLICT            Operation not allowed in current status ---
                             409.

  NOT_FOUND                  No such resource in the caller\'s tenant ---
                             404.

  UNAUTHENTICATED /          401 / 403.
  FORBIDDEN                  

  RATE_LIMITED               429\.
  ------------------------------------------------------------------------

# **3. Shared objects**

## **3.1 WorkRequirement**

  -------------------------------------------------------------------------------
  **Field**            **Type**     **Req**   **Notes**
  -------------------- ------------ --------- -----------------------------------
  id                   uuid         **yes**   Server-assigned

  tenantId             uuid         **yes**   From token

  sourceType           string       **yes**   Origin kind --- \'Demand\' today

  sourceId             string       **yes**   The Demand id; source object never
                                              copied

  sourceVersion        int?         no        Demand version at resolution

  templateId           uuid?        no        Template it resolved from

  templateCode         string?      no        e.g. DOG_WALK_STANDARD

  templateVersion      int?         no        Frozen --- prevents silent drift

  workType             string       **yes**   e.g. DOG_WALKING

  workCategory         string?      no        

  serviceMode          string?      no        e.g. MOBILE

  status               enum         **yes**   Draft \| Validating \| Valid \|
                                              Invalid \| Superseded \| Cancelled
                                              \| Completed

  priority             enum         **yes**   Low ... Critical

  effectiveFrom / To   datetime?    no        Validity window

  requirementVersion   int          **yes**   Concurrency counter

  createdAt /          datetime     **yes**   Audit
  updatedAt                                   
  -------------------------------------------------------------------------------

## **3.2 ResolvedRequirements**

The canonical operational contract consumed by Eligibility, Matching,
Capacity, Scheduling, and Assignment. Every downstream service reads
this same document, so none of them re-interpret business rules.

  ---------------------------------------------------------------------------------------
  **Field**                       **Type**    **Req**   **Notes**
  ------------------------------- ----------- --------- ---------------------------------
  resourceRequirements\[\]        array       **yes**   roleCode, resourceCategory,
                                                        minimumQuantity, maximumQuantity,
                                                        selectionMode

  capabilityRequirements\[\]      array       no        roleCode, capabilityCode, level,
                                                        mandatory

  certificationRequirements\[\]   array       no        roleCode, certificationTypeCode,
                                                        mandatory, mustBeValidThrough

  capacityRequirements\[\]        array       no        roleCode, dimensionCode,
                                                        quantity, unit

  durationRequirement             object      **yes**   durationType, durationMinutes,
                                                        setup, cleanup

  timeRequirement                 object      no        timeConstraintType,
                                                        earliestStart, latestFinish,
                                                        deadline, timezone

  locationRequirement             object      no        locationMode,
                                                        locationReferenceId, lat, lng,
                                                        serviceRadius

  executionRequirement            object      no        executionMode

  travelRequirement               object      no        travelRequired,
                                                        maximumTravelTimeMinutes

  bufferRequirements\[\]          array       no        bufferType, durationMinutes,
                                                        appliesToRole

  constraints\[\]                 array       no        constraintType, operator, value,
                                                        hardConstraint

  preferences\[\]                 array       no        preferenceType, value, weight ---
                                                        never a gate
  ---------------------------------------------------------------------------------------

# **4. Template endpoints**

Templates hold reusable operational defaults. A template version is
immutable once activated --- a change is a new version. This is what
stops work created under version 1 from silently becoming version 2.

**POST /api/v1/work-requirement-templates
workrequirement.template.create**

**Request**

  -----------------------------------------------------------------------
  {

  \"code\": \"DOG_WALK_STANDARD\",

  \"name\": \"Standard Dog Walk\",

  \"workType\": \"DOG_WALKING\"

  }
  -----------------------------------------------------------------------

**Response --- 201**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| { \"id\": \"\...\", \"code\": \"DOG_WALK_STANDARD\", \"status\": \"Draft\",            |
| \"currentVersion\": 1 }                                                                |
+-------------------------+--------------------------+-----------------------------------+
| **201**                 | Created (or replayed)    | Template object                   |
+-------------------------+--------------------------+-----------------------------------+
| **409**                 | Code already exists in   | error + code                      |
|                         | tenant                   |                                   |
+-------------------------+--------------------------+-----------------------------------+
| **400**                 | Missing field            | error + code=VALIDATION_FAILED +  |
|                         |                          | issues\[\]                        |
+-------------------------+--------------------------+-----------------------------------+

**POST /api/v1/work-requirement-templates/{templateId}/versions
workrequirement.template.update**

Creates a new Draft version. Requires Idempotency-Key.

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **201**    Draft version created       TemplateVersion object

  **404**    No such template            error + code=NOT_FOUND
  ---------------------------------------------------------------------------

**PUT
/api/v1/work-requirement-templates/{templateId}/versions/{version}/requirements
workrequirement.template.update**

Configures the requirement definitions on a Draft version. Rejected with
409 TEMPLATE_IMMUTABLE if the version is already Active.

**Request**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| {                                                                                      |
+----------------------------------------------------------------------------------------+
| \"resourceRequirements\": \[                                                           |
+----------------------------------------------------------------------------------------+
| { \"roleCode\": \"DOG_WALKER\", \"resourceCategory\": \"Person\",                      |
+----------------------------------------------------------------------------------------+
| \"minimumQuantity\": 1, \"maximumQuantity\": 1 }                                       |
+----------------------------------------------------------------------------------------+
| \],                                                                                    |
+----------------------------------------------------------------------------------------+
| \"capabilityRequirements\": \[                                                         |
+----------------------------------------------------------------------------------------+
| { \"roleCode\": \"DOG_WALKER\", \"capabilityCode\": \"DOG_WALKING\",                   |
+----------------------------------------------------------------------------------------+
| \"mandatory\": true }                                                                  |
+----------------------------------------------------------------------------------------+
| \],                                                                                    |
+----------------------------------------------------------------------------------------+
| \"durationRequirement\": {                                                             |
+----------------------------------------------------------------------------------------+
| \"durationType\": \"Fixed\", \"estimatedDurationMinutes\": 60                          |
+----------------------------------------------------------------------------------------+
| },                                                                                     |
+----------------------------------------------------------------------------------------+
| \"locationRequirement\": { \"locationMode\": \"CustomerLocation\" }                    |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+-------------------------+--------------------------+-----------------------------------+
| **200**                 | Definitions saved        | TemplateVersion object            |
+-------------------------+--------------------------+-----------------------------------+
| **409**                 | Version already          | error + code=TEMPLATE_IMMUTABLE   |
|                         | activated                |                                   |
+-------------------------+--------------------------+-----------------------------------+
| **422**                 | Definitions fail         | error +                           |
|                         | validation               | code=INVALID_WORK_REQUIREMENT +   |
|                         |                          | issues\[\]                        |
+-------------------------+--------------------------+-----------------------------------+

**POST
/api/v1/work-requirement-templates/{templateId}/versions/{version}/activate
workrequirement.template.activate**

Activation validates the complete template before it goes live. After
activation the version is frozen.

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Activated; version frozen   TemplateVersion (status=Active)

  **422**    Template incomplete or      error +
             invalid                     code=INVALID_WORK_REQUIREMENT +
                                         issues\[\]
  ---------------------------------------------------------------------------

**POST /api/v1/work-requirement-templates/{templateId}/retire
workrequirement.template.retire**

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Retired                     Template (status=Retired)

  **409**    Already retired             error + code=STATUS_CONFLICT
  ---------------------------------------------------------------------------

**GET /api/v1/work-requirement-templates workrequirement.template.read**

Filters: workType, status, effectiveDate, organizationContext. Paged.

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Executed                    PagedResult\<Template\>

  ---------------------------------------------------------------------------

# **5. Work Requirement endpoints**

**POST /api/v1/work-requirements/resolve workrequirement.resolve**

The core operation: translate a demand plus a template into an
operational requirement. Requires Idempotency-Key --- a retried resolve
must not create a second requirement.

**Request**

  -----------------------------------------------------------------------
  {

  \"sourceType\": \"Demand\",

  \"sourceId\": \"9f3a-\...\", // the Demand id

  \"sourceVersion\": 3,

  \"templateCode\": \"DOG_WALK_STANDARD\",

  \"inputs\": {

  \"petCount\": 2,

  \"requestedWindow\": {

  \"start\": \"2026-07-06T08:00:00-07:00\",

  \"end\": \"2026-07-06T12:00:00-07:00\"

  },

  \"locationReferenceId\": \"LOC-50001\"

  }

  }
  -----------------------------------------------------------------------

**Response --- 201**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| {                                                                                      |
+----------------------------------------------------------------------------------------+
| \"workRequirementId\": \"\...\",                                                       |
+----------------------------------------------------------------------------------------+
| \"requirementVersion\": 1,                                                             |
+----------------------------------------------------------------------------------------+
| \"templateCode\": \"DOG_WALK_STANDARD\",                                               |
+----------------------------------------------------------------------------------------+
| \"templateVersion\": 4,                                                                |
+----------------------------------------------------------------------------------------+
| \"status\": \"Valid\",                                                                 |
+----------------------------------------------------------------------------------------+
| \"warnings\": \[\]                                                                     |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+-------------------------+--------------------------+-----------------------------------+
| **201**                 | Resolved (or replayed)   | Resolution result                 |
+-------------------------+--------------------------+-----------------------------------+
| **409**                 | Template version not     | error + code=TEMPLATE_NOT_ACTIVE  |
|                         | Active                   |                                   |
+-------------------------+--------------------------+-----------------------------------+
| **422**                 | Resolved requirement     | error +                           |
|                         | invalid                  | code=INVALID_WORK_REQUIREMENT +   |
|                         |                          | issues\[\]                        |
+-------------------------+--------------------------+-----------------------------------+
| **409**                 | Same key, different body | error + code=IDEMPOTENCY_CONFLICT |
+-------------------------+--------------------------+-----------------------------------+

**GET /api/v1/work-requirements/{id} workrequirement.read**

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Found                       WorkRequirement object

  **404**    Not found / other tenant    error + code=NOT_FOUND
  ---------------------------------------------------------------------------

**GET /api/v1/work-requirements/{id}/versions/{version}
workrequirement.read**

A specific historical version, from its immutable snapshot.

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Found                       WorkRequirement at that version

  **404**    No such version             error + code=NOT_FOUND
  ---------------------------------------------------------------------------

**GET /api/v1/work-requirements/{id}/resolved workrequirement.read**

The canonical resolved contract --- what Eligibility, Capacity, and
Scheduling actually consume.

**Response**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| {                                                                                      |
+----------------------------------------------------------------------------------------+
| \"workRequirementId\": \"\...\", \"requirementVersion\": 3,                            |
+----------------------------------------------------------------------------------------+
| \"workType\": \"DOG_WALKING\",                                                         |
+----------------------------------------------------------------------------------------+
| \"resourceRequirements\": \[ { \"roleCode\": \"DOG_WALKER\",                           |
+----------------------------------------------------------------------------------------+
| \"resourceCategory\": \"Person\", \"minimumQuantity\": 1,                              |
+----------------------------------------------------------------------------------------+
| \"maximumQuantity\": 1 } \],                                                           |
+----------------------------------------------------------------------------------------+
| \"capabilityRequirements\":\[ { \"roleCode\": \"DOG_WALKER\",                          |
+----------------------------------------------------------------------------------------+
| \"capabilityCode\": \"DOG_WALKING\", \"mandatory\": true } \],                         |
+----------------------------------------------------------------------------------------+
| \"capacityRequirements\": \[ { \"roleCode\": \"DOG_WALKER\",                           |
+----------------------------------------------------------------------------------------+
| \"dimensionCode\": \"PET_COUNT\", \"quantity\": 2,                                     |
+----------------------------------------------------------------------------------------+
| \"unit\": \"COUNT\" } \],                                                              |
+----------------------------------------------------------------------------------------+
| \"durationRequirement\": { \"durationType\": \"Fixed\",                                |
+----------------------------------------------------------------------------------------+
| \"durationMinutes\": 60 },                                                             |
+----------------------------------------------------------------------------------------+
| \"timeRequirement\": { \"timeConstraintType\": \"Window\",                             |
+----------------------------------------------------------------------------------------+
| \"earliestStart\": \"2026-07-06T08:00:00-07:00\",                                      |
+----------------------------------------------------------------------------------------+
| \"latestFinish\": \"2026-07-06T12:00:00-07:00\",                                       |
+----------------------------------------------------------------------------------------+
| \"timezone\": \"America/Vancouver\" },                                                 |
+----------------------------------------------------------------------------------------+
| \"locationRequirement\": { \"locationMode\": \"CustomerLocation\",                     |
+----------------------------------------------------------------------------------------+
| \"locationReferenceId\": \"LOC-50001\" }                                               |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+-------------------------+--------------------------+-----------------------------------+
| **200**                 | Resolved contract        | ResolvedRequirements (§3.2)       |
+-------------------------+--------------------------+-----------------------------------+
| **409**                 | Requirement is Invalid   | error + code=STATUS_CONFLICT      |
+-------------------------+--------------------------+-----------------------------------+

**POST /api/v1/work-requirements/{id}/validate
workrequirement.validate**

Runs the seven validation categories (§9). Records the outcome; moves
the requirement to Valid or Invalid.

**Response**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| {                                                                                      |
+----------------------------------------------------------------------------------------+
| \"valid\": false,                                                                      |
+----------------------------------------------------------------------------------------+
| \"errors\": \[                                                                         |
+----------------------------------------------------------------------------------------+
| { \"code\": \"MISSING_REQUIRED_RESOURCE_ROLE\",                                        |
+----------------------------------------------------------------------------------------+
| \"path\": \"resourceRequirements\",                                                    |
+----------------------------------------------------------------------------------------+
| \"message\": \"Mobile grooming requires a vehicle resource role.\" }                   |
+----------------------------------------------------------------------------------------+
| \],                                                                                    |
+----------------------------------------------------------------------------------------+
| \"warnings\": \[\]                                                                     |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+-------------------------+--------------------------+-----------------------------------+
| **200**                 | Validation ran (pass or  | ValidationResult                  |
|                         | fail)                    |                                   |
+-------------------------+--------------------------+-----------------------------------+
| **404**                 | Not found                | error + code=NOT_FOUND            |
+-------------------------+--------------------------+-----------------------------------+

**POST /api/v1/work-requirements/{id}/revisions workrequirement.revise**

Creates a new requirement version. Requires expectedVersion and
Idempotency-Key. A material change publishes WorkRequirementChanged so
downstream services can recalculate.

**Request**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| {                                                                                      |
+----------------------------------------------------------------------------------------+
| \"expectedVersion\": 2,                                                                |
+----------------------------------------------------------------------------------------+
| \"reason\": \"Owner added a second pet\",                                              |
+----------------------------------------------------------------------------------------+
| \"changes\": {                                                                         |
+----------------------------------------------------------------------------------------+
| \"capacityRequirements\": \[                                                           |
+----------------------------------------------------------------------------------------+
| { \"roleCode\": \"DOG_WALKER\",                                                        |
+----------------------------------------------------------------------------------------+
| \"dimensionCode\": \"PET_COUNT\", \"quantity\": 2 }                                    |
+----------------------------------------------------------------------------------------+
| \]                                                                                     |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+-------------------------+--------------------------+-----------------------------------+
| **201**                 | New version created      | WorkRequirement                   |
|                         |                          | (requirementVersion+1)            |
+-------------------------+--------------------------+-----------------------------------+
| **412**                 | expectedVersion mismatch | error + code=VERSION_CONFLICT     |
+-------------------------+--------------------------+-----------------------------------+
| **409**                 | Requirement is terminal  | error + code=STATUS_CONFLICT      |
|                         | (Cancelled/Completed)    |                                   |
+-------------------------+--------------------------+-----------------------------------+
| **422**                 | Revised requirement      | error +                           |
|                         | invalid                  | code=INVALID_WORK_REQUIREMENT +   |
|                         |                          | issues\[\]                        |
+-------------------------+--------------------------+-----------------------------------+

**GET /api/v1/work-requirements/{id}/compare?fromVersion=2&toVersion=3
workrequirement.read**

Structured diff between two versions --- what changed, and which
downstream areas it affects.

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Diff                        Comparison object

  **404**    Version not found           error + code=NOT_FOUND
  ---------------------------------------------------------------------------

**GET /api/v1/work-requirements/{id}/explain workrequirement.explain**

Explainability: why each requirement exists and where it came from ---
template, source input, or override.

**Response**

+-------------------------+--------------------------+-----------------------------------+
| **Code**                | **When**                 | **Body**                          |
+=========================+==========================+===================================+
| {                                                                                      |
+----------------------------------------------------------------------------------------+
| \"summary\": \"One qualified dog walker is required for 60 minutes                     |
+----------------------------------------------------------------------------------------+
| at the customer location.\",                                                           |
+----------------------------------------------------------------------------------------+
| \"derivedRequirements\": \[                                                            |
+----------------------------------------------------------------------------------------+
| { \"requirement\": \"PET_COUNT = 2\", \"source\": \"Demand.petCount\" },               |
+----------------------------------------------------------------------------------------+
| { \"requirement\": \"DOG_WALKING\",                                                    |
+----------------------------------------------------------------------------------------+
| \"source\": \"Template DOG_WALK_STANDARD version 4\" }                                 |
+----------------------------------------------------------------------------------------+
| \]                                                                                     |
+----------------------------------------------------------------------------------------+
| }                                                                                      |
+-------------------------+--------------------------+-----------------------------------+
| **200**                 | Explanation              | Explanation object                |
+-------------------------+--------------------------+-----------------------------------+

**POST /api/v1/work-requirements/{id}/cancel workrequirement.revise**

Cancels the requirement. Idempotent by status --- a repeat call on an
already-Cancelled requirement returns 409 rather than re-publishing.

  ---------------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    Cancelled; event published  WorkRequirement (status=Cancelled)

  **409**    Already terminal            error + code=STATUS_CONFLICT
  ---------------------------------------------------------------------------

# **6. Status and transitions**

  -----------------------------------------------------------------------
  **Status**         **Meaning and exits**
  ------------------ ----------------------------------------------------
  Draft              Created, not validated. → Validating, Cancelled

  Validating         Validation in progress. → Valid, Invalid

  Valid              Consumable downstream. → Superseded (on revision),
                     Cancelled, Completed

  Invalid            Must not be consumed. → Valid (after fix), Cancelled

  Superseded         Replaced by a newer version. Terminal for that
                     version

  Cancelled          Withdrawn. Terminal

  Completed          Work finished. Set from a downstream lifecycle event
                     --- never inferred here. Terminal
  -----------------------------------------------------------------------

# **7. Events**

## **7.1 Published**

All published through the transactional outbox --- state change and
outbox row commit together, so an event can never diverge from the state
that caused it.

  ---------------------------------------------------------------------------------
  **Event**                             **Fired when**
  ------------------------------------- -------------------------------------------
  RequirementTemplateCreated            A template is created

  RequirementTemplateVersionCreated     A draft version is created

  RequirementTemplateVersionActivated   A version is activated and frozen

  RequirementTemplateRetired            A template is retired

  WorkRequirementCreated                A requirement is first created

  WorkRequirementResolved               Resolution completes --- the key downstream
                                        trigger

  WorkRequirementChanged                A material revision lands

  WorkRequirementValidated              Validation passes

  WorkRequirementInvalidated            Validation fails

  WorkRequirementSuperseded             A newer version replaces this one

  WorkRequirementCancelled              The requirement is cancelled
  ---------------------------------------------------------------------------------

**WorkRequirementResolved**

  -----------------------------------------------------------------------
  {

  \"eventId\": \"\...\", \"eventType\": \"WorkRequirementResolved\",

  \"eventVersion\": 1, \"tenantId\": \"\...\",

  \"aggregateType\": \"WorkRequirement\", \"aggregateId\": \"\...\",

  \"occurredAt\": \"2026-07-04T21:00:00Z\", \"correlationId\": \"\...\",

  \"payload\": {

  \"workRequirementId\": \"\...\",

  \"sourceType\": \"Demand\", \"sourceId\": \"\...\",

  \"requirementVersion\": 3, \"workType\": \"DOG_WALKING\",

  \"affectedAreas\": \[\"Eligibility\",\"Capacity\",\"Scheduling\"\]

  }

  }
  -----------------------------------------------------------------------

WorkRequirementChanged additionally names changedCategories,
affectedResourceRoles, affectedCapacityDimensions, and the affected time
range --- so a consumer knows what to recalculate rather than
recomputing everything.

## **7.2 Consumed --- via adapter**

The service never subscribes to a caller\'s business events directly. An
adapter normalizes the upstream into the resolution contract, so the
core stays free of caller coupling.

**DemandAcceptedEvent → Demand Adapter → Resolution Contract → Work
Requirement**

The adapter reads the accepted Demand, maps its demandType to a template
code and its fields to resolution inputs, and calls resolve. Because the
adapter absorbs the mapping, an additional upstream (should a Service
Request service ever exist) needs only a second adapter --- no change to
this service. sourceType records which upstream produced the work.

Also consumed, to invalidate affected requirements:
CapabilityDefinitionRetired, CertificationTypeRetired,
CapacityDimensionRetired, LocationChanged.

# **8. Domain flow --- Demand & Intake**

The two services in this domain connect at exactly two points: an event,
and a pair of reverse pointers. There is no foreign key across the
schemas and neither service reads the other\'s tables.

  -----------------------------------------------------------------------
  Demand Intake Work Requirement

  \-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\--
  \-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\-\--

  POST /api/v1/demands

  Demand (Received)

  POST /demands/{id}/validate

  Demand (Ready)

  POST /demands/{id}/accept

  Demand (Accepted) \-\-\-\-\-\-\-\-\-\-\-\--\> DemandAcceptedEvent

  \|

  v Demand Adapter

  POST /work-requirements/resolve

  sourceType = \'Demand\'

  sourceId = \<demand id\>

  \|

  v

  WorkRequirement (Valid)

  Demand.requirementReferenceId \<\-\-- WorkRequirementResolved

  \|

  v consumed by

  Eligibility / Capacity / Scheduling
  -----------------------------------------------------------------------

Demand Intake\'s requirementReferenceId and Work Requirement\'s
sourceType/sourceId are the two halves of the link. Demand answers what
was requested; Work Requirement answers what is operationally required
to fulfil it.

# **9. Validation categories**

  -----------------------------------------------------------------------
  **Category**        **Examples**
  ------------------- ---------------------------------------------------
  Structural          Work type required; at least one resource role;
                      duration required; source reference required

  Reference           Capability code, certification type, capacity
                      dimension, resource type, location all exist

  Semantic            Quantity positive; duration positive; minimum
                      quantity ≤ maximum

  Temporal            Latest start not before earliest start; deadline
                      not before earliest start; certification valid
                      through the work period

  Composition         Mobile execution requires a vehicle role

  Cross-requirement   A PET_COUNT capacity requirement must reference a
                      role that can consume PET_COUNT

  Policy              Tenant or organization policy gates
  -----------------------------------------------------------------------

Structural, semantic, and temporal rules are also enforced as database
CHECK constraints, so an invalid requirement cannot be persisted even by
a path that bypasses the validator.

# **10. Security**

## **10.1 Authentication**

  -----------------------------------------------------------------------
  **Layer**          **Control**
  ------------------ ----------------------------------------------------
  APIM edge          validate-jwt requires a signed JWT and a
                     subscription key. Both required.

  Gateway            Re-derives identity and enforces the per-operation
                     permission independently --- defence in depth.
  -----------------------------------------------------------------------

## **10.2 Permissions**

Finer-grained than Demand Intake, because template authoring,
resolution, and revision are genuinely different authorities.

  -------------------------------------------------------------------------------
  **Permission**                      **Gates**
  ----------------------------------- -------------------------------------------
  workrequirement.template.create     Create a template

  workrequirement.template.read       Read / search templates

  workrequirement.template.update     Create versions; configure definitions

  workrequirement.template.activate   Activate a version (freezes it)

  workrequirement.template.retire     Retire a template

  workrequirement.read                Read requirements, versions, resolved
                                      contract, compare

  workrequirement.resolve             Resolve a demand into a requirement

  workrequirement.revise              Create revisions; cancel

  workrequirement.validate            Run validation

  workrequirement.explain             Read the explanation

  workrequirement.override.restrict   Add a more restrictive override

  workrequirement.override.relax      Relax a requirement --- held separately, as
                                      it can weaken safety-relevant constraints

  workrequirement.admin               Administrative operations
  -------------------------------------------------------------------------------

Separating override.relax from override.restrict is deliberate: relaxing
a mandatory certification is not the same authority as tightening one.
Providers must never be able to remove mandatory requirements.

## **10.3 Tenant isolation**

- Every template and requirement belongs to exactly one tenant.
  Cross-tenant template use is prohibited, enforced by UQ (TenantId,
  Code).

- Two layers: EF query filters plus database row-level security, so
  isolation holds on any path that bypasses the ORM.

- Cross-tenant reads return 404, never 403 --- no existence disclosure.

## **10.4 Expression safety**

Derived durations and conditional requirements must not become an
arbitrary expression engine. No executable expressions are evaluated
from tenant input; derivation is limited to a fixed, safe set of
operations. This is a hard boundary --- an arbitrary expression
evaluator in a multi-tenant service is a remote-code-execution surface.

## **10.5 Input hardening**

  -----------------------------------------------------------------------
  **Control**        **Detail**
  ------------------ ----------------------------------------------------
  Body size cap      413 before parsing. inputs and DefinitionJson are
                     free-form --- the natural abuse vector.

  jsonb bounds       Bound nesting depth and byte size on DefinitionJson,
                     SnapshotJson, and inputs.

  Strict enums       Reject unknown resourceCategory, selectionMode,
                     durationType, etc. at the edge.

  Rate limits        Per subscription key; tighter on resolve and revise
                     than on reads. 429 with Retry-After.
  -----------------------------------------------------------------------

# **11. Idempotency and concurrency**

## **11.1 Idempotency**

Required on template creation, template version creation, resolution,
revision, override, and cancellation. Mechanics are identical to Demand
Intake, so the domain behaves consistently.

  -----------------------------------------------------------------------
  **Aspect**          **Behaviour**
  ------------------- ---------------------------------------------------
  Scope               Unique on (TenantId, IdempotencyKey). Keys never
                      collide across tenants.

  Match               Key unseen → execute. Key seen + same canonical
                      body hash → replay the stored response. Key seen +
                      different body → 409 IDEMPOTENCY_CONFLICT.

  Canonicalization    The hash is over a canonical form --- stable key
                      order, whitespace-insensitive --- so a reordered
                      but identical retry replays rather than
                      conflicting.

  Race                Two concurrent first-writes: the unique index
                      arbitrates. First insert wins; the second converts
                      to the replay path, not an error.

  Retention           24 hours, then purged.
  -----------------------------------------------------------------------

Resolution idempotency matters more here than anywhere else in the
domain: a retried resolve that created a second Work Requirement would
produce two operational contracts for one demand, and downstream
scheduling would act on both.

## **11.2 Optimistic concurrency**

Revisions and template configuration carry expectedVersion. The update
is a single guarded statement --- UPDATE ... WHERE Id = \@id AND
RequirementVersion = \@expected --- so two concurrent revisions cannot
both write version n+1. Zero rows affected returns 412 VERSION_CONFLICT.

## **11.3 Transactional outbox**

Every event is written to the outbox in the same transaction as the
state change it describes; a relay publishes afterward. A crash between
commit and publish leaves the event pending in the outbox rather than
lost, which is what makes the once-per-transition guarantee hold under
at-least-once delivery.

# **12. Verification checklist**

Confirm against work.v1.yaml and one live UAT capture per endpoint
before coding, since response wire format cannot be derived from static
artifacts.

  -----------------------------------------------------------------------
  **Item**            **Confirm**
  ------------------- ---------------------------------------------------
  Base path           /api/v1/work-requirements --- the spec\'s own text
                      says /v1/work-requirements.

  Error envelope      The domain hybrid (error/code/traceId/issues) ---
                      the spec\'s own text specifies RFC-7807.

  Status codes        422 for invalid requirement vs 400 for malformed
                      body; 413 and 429 emission.

  Resolved contract   Field names and nesting in GET /resolved --- this
                      is the document every downstream service consumes.

  Event schemas       WorkRequirementResolved and WorkRequirementChanged
                      payloads, against the event contract registry.

  Upstream event      Whether the adapter consumes DemandAcceptedEvent
                      --- see the open Service Request question in the
                      model document.

  Permissions         The thirteen permission strings exist in the
                      identity provider.
  -----------------------------------------------------------------------

*Established by the Work Requirement Service Specification (Document
06): the endpoints and their request/response examples, permissions,
statuses, validation categories, published events, and the idempotency,
concurrency, and outbox requirements. Specified by this contract: the
shared error envelope, the /api/v1 prefix, the concrete status-code
mapping, the Demand adapter, and the security hardening detail --- each
adopted so that Work Requirement and Demand Intake present one coherent
domain surface. Section 12 lists what to confirm before implementation.*

# **13. v2 Delta (2026-07-23) --- concurrency, per-line visibility, search & audit**

*Source: docs/API/specs/update_requirement-schema.sql and
docs/API/specs/v2-delta-summary.docx §3 "Work Requirement", driven by the
Demand & Intake Domain UX architecture review. Three data-model changes,
six endpoint changes (four revised, two new).*

## **13.1 Template concurrency --- rowVersion**

Every RequirementTemplateVersion now carries a **rowVersion** (integer
counter, starts at 1). **PUT
/api/v1/work-requirement-templates/{templateId}/versions/{version}/requirements**
now **requires** an **If-Match** header carrying the version\'s current
rowVersion --- two authors editing the same Draft version can no longer
silently overwrite each other (UX-08).

  -----------------------------------------------------------------------
  **Code**   **When**                    **Body**
  ---------- --------------------------- ------------------------------------
  **200**    If-Match matched; saved     TemplateVersion (rowVersion + 1)

  **400**    If-Match header missing/    error + code=VALIDATION_FAILED
             malformed

  **412**    If-Match does not match     error + code=VERSION_CONFLICT
             current rowVersion
  -----------------------------------------------------------------------

The response\'s rowVersion is what the caller passes as If-Match on the
*next* PUT.

## **13.2 Per-line visibility --- visibilityLevel**

Every item in a RequirementSetDto (§3.2) --- every resourceRequirement,
capabilityRequirement, certificationRequirement, capacityRequirement,
durationRequirement, timeRequirement, locationRequirement,
executionRequirement, travelRequirement, bufferRequirement,
dependencyRequirement, constraint, and preference --- now carries two
additional fields:

  -----------------------------------------------------------------------
  **Field**          **Type**    **Notes**
  ------------------ ----------- ----------------------------------------
  id                 uuid        Set on read (GET .../resolved); ignored
                                  on write (PUT .../requirements)

  visibilityLevel    enum?       Customer \| Provider \| Internal.
                                  Settable on write; defaults to Internal
                                  when omitted --- the safe choice, since
                                  "no explicit disclosure grant" means
                                  "don\'t disclose."
  -----------------------------------------------------------------------

Persisted as one row per line in requirement.RequirementLineVisibility,
keyed by (lineType, lineId) rather than as a column repeated across
every requirement-line table --- explain/resolved/compare all assemble
their output from many sources, and this keeps the disclosure filter in
one place. customerVisible is derived (true only when visibilityLevel =
Customer) and enforced consistent with visibilityLevel by a CHECK
constraint.

## **13.3 Audience-filtered reads**

Three existing endpoints gain an audience filter --- **server-side, not
client-side**: a customer surface that receives the full requirement and
hides fields in the browser has still transmitted internal operational
data.

  -----------------------------------------------------------------------
  **Endpoint**                           **New parameter**
  --------------------------------------- -----------------------------------
  GET /work-requirements/{id}/resolved   ?audience=customer\|provider\|dispatcher
                                          (default)

  GET /work-requirements/{id}/explain    ?audience=customer\|provider\|dispatcher
                                          (default)

  GET /work-requirements/{id}/compare    ?customerVisibleOnly=true\|false
                                          (default false)
  -----------------------------------------------------------------------

Audience → allowed visibilityLevel set: customer → \[Customer\];
provider → \[Customer, Provider\]; dispatcher (or omitted) →
unfiltered, today\'s behaviour. An unrecognized audience value returns
400 VALIDATION_FAILED. durationRequirement is exempt from filtering ---
it is structurally required on every resolved contract and is always
returned.

## **13.4 New endpoints**

**GET /api/v1/work-requirements/search workrequirement.read**

Paged search over the WorkRequirement aggregate: status, workType,
sourceType, sourceId, templateId, page, pageSize.

**GET /api/v1/work-requirements/{id}/audit workrequirement.read**

Unified audit trail --- the same RequirementVersion history
GetVersion/Compare already read (no separate "override" entity exists in
this module), returned as a flat, version-ordered list of {version,
changeType, changeReason, createdAt, createdBy}.

## **13.5 Contract**

contracts/openapi/work.v1.yaml documents these six endpoints as of this
delta. The rest of the reconciled WorkRequirement/RequirementTemplate
surface (resolve, GetById, validate, revise, cancel, template
create/version/activate/retire) predates this delta and remains
undocumented in OpenAPI --- a pre-existing gap this delta does not close.
