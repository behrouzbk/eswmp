**ESWMP --- Work Requirement Service**

Database Model --- Diagram, Schema & Domain Boundary

**Domain:** Demand & Intake Service: Eswmp.Work Database: eswmp_work
Schema: requirement

This service is the sibling of Demand Intake within the Demand & Intake
domain. Both live in the same deployable unit and the same database, in
separate schemas: Demand Intake owns demand, Work Requirement owns
requirement. Neither may touch the other\'s tables; the only coupling is
the source reference and the event contract.

*Provenance: the entities, fields, enums, statuses, and boundary rules
are drawn from the Work Requirement Service Specification (Document 06).
Physical types, lengths, indexes, and constraints are specified here.
Where this contract adopts a convention from the shipped Demand Intake
service rather than the spec\'s own wording, it is noted in §7.*

# **1. Purpose and boundary**

The Work Requirement Service is the authoritative owner of what must be
true for a unit of work to be performed. It answers one question: what
is operationally required? It deliberately does not answer who performs
the work, when it is scheduled, whether a resource is available, whether
capacity exists, or which candidate is best.

A customer request is not a Work Requirement. "Dog grooming, tomorrow
afternoon, at my home" is demand. The Work Requirement it resolves to is
operational: work type Mobile Dog Grooming, 90 minutes, one Groomer plus
one Mobile Grooming Vehicle, capabilities DOG_GROOMING and
MOBILE_SERVICE, certification ANIMAL_FIRST_AID, capacity PET_COUNT = 1.
That translation is this service\'s entire job.

## **1.1 Owns**

Work Requirement, Requirement Template, Template Version, Resource Role
Requirement, Capability Requirement, Certification Requirement, Capacity
Requirement, Time Requirement, Duration Requirement, Location
Requirement, Execution Requirement, Travel Requirement, Buffer
Requirement, Dependency Requirement, Constraint, Preference, Requirement
Version, Requirement Resolution.

## **1.2 References but does not own**

Source Demand, Product, Service Offering, Resource Type, Capability
Definition, Certification Type, Capacity Dimension, Location,
Organization. All are held as opaque codes or reference ids --- never
copied into this schema.

## **1.3 Must never own**

Resource Availability, Resource Capacity State, Candidate Eligibility,
Candidate Score, Schedule, Reservation, Appointment, Assignment, Task
Status. These belong to the Scheduling & Availability and Workforce &
Execution domains. The service must not become the scheduling engine,
the product engine, or the assignment engine.

**The distinction that matters most:** a Capacity Requirement says the
work consumes PET_COUNT = 2. It does not know how much capacity remains
--- that is the Capacity Service. A Capability Requirement says
DOG_GROOMING is needed. It does not know who has it --- that is
Eligibility.

# **2. Core aggregate**

Templates generate Work Requirements. A Work Requirement carries its
template provenance (TemplateId + TemplateVersion) so work created under
version 1 never silently becomes version 2. Capability, certification,
and capacity requirements attach to a Resource Role --- not to the work
as a whole --- which is what makes multi-resource work expressible.

![](media/image1.png){width="5.895833333333333in" height="5.65625in"}

*Role-scoping is the structural key: a Mobile Grooming requirement has
two roles (Groomer, Vehicle), and DOG_GROOMING attaches to the Groomer
while a driving licence attaches to the Vehicle operator. A flat
capability list on the work could not express that.*

# **3. Requirement detail**

## **3.1 Single-cardinality requirements**

Duration, Time, Location, Execution, and Travel are
one-per-Work-Requirement, each enforced by a unique constraint on
WorkRequirementId.

![](media/image2.png){width="6.197916666666667in"
height="1.5104166666666667in"}

## **3.2 Multi-cardinality requirements**

Buffers, dependencies, constraints, and preferences are
many-per-Work-Requirement. Constraints gate (hard) or influence (soft);
Preferences only weight --- they are never a gate.

![](media/image3.png){width="6.197916666666667in"
height="2.0833333333333335in"}

# **4. Schema reference**

## **4.1 requirement.WorkRequirements --- aggregate root**

  -------------------------------------------------------------------------
  **Column**           **Type**        **Notes**
  -------------------- --------------- ------------------------------------
  Id                   uuid (PK)       

  TenantId             uuid            Query filter + RLS

  SourceType           varchar,        Origin kind --- \'Demand\' today
                       required        

  SourceId             varchar,        The Demand\'s id; source object
                       required        never copied

  SourceVersion        int, null       Demand.Version at resolution time

  TemplateId           uuid, null      FK → RequirementTemplates

  TemplateVersion      int, null       Frozen; prevents silent drift

  WorkType             varchar,        e.g. DOG_WALKING
                       required        

  WorkCategory         varchar, null   Classification

  ServiceMode          varchar, null   e.g. MOBILE

  ComplexityLevel      varchar, null   

  Status               enum, def.      Draft → Completed (§5)
                       Draft           

  Priority             enum, def.      Low ... Critical
                       Normal          

  EffectiveFrom / To   timestamptz,    Validity window
                       null            

  RequirementVersion   int, def. 1     Optimistic concurrency

  IX_WR_Source         index           TenantId, SourceType, SourceId ---
                                       the domain join

  IX_WR_Search         index           TenantId, Status, CreatedAt
  -------------------------------------------------------------------------

## **4.2 requirement.RequirementTemplates / TemplateVersions**

Templates hold reusable operational defaults per tenant; versions freeze
them. Cross-tenant template use is prohibited, enforced by UQ (TenantId,
Code).

  -----------------------------------------------------------------------
  **Column**         **Type**        **Notes**
  ------------------ --------------- ------------------------------------
  Code               varchar,        e.g. DOG_WALK_STANDARD; UQ per
                     required        tenant

  WorkType           varchar,        e.g. DOG_WALKING
                     required        

  Status             enum            Draft \| Active \| Retired

  CurrentVersion     int             Pointer to the live version

  --- versions ---                   

  Version            int             UQ with TemplateId

  Status             enum            Draft \| Active \| Superseded \|
                                     Retired

  DefinitionJson     jsonb           Requirement definitions, immutable
                                     after activation

  EffectiveFrom / To timestamptz     Version validity

  ChangeReason       varchar         Why this version exists
  -----------------------------------------------------------------------

## **4.3 requirement.ResourceRoleRequirements**

The pivot of the model. Capability, certification, and capacity
requirements all hang off a role.

  ---------------------------------------------------------------------------
  **Column**             **Type**        **Notes**
  ---------------------- --------------- ------------------------------------
  RoleCode               varchar,        e.g. DOG_WALKER, GROOMER, VEHICLE
                         required        

  ResourceCategory       enum, required  Person \| Team \| Vehicle \|
                                         Facility \| Room \| Equipment \|
                                         VirtualResource \| ResourcePool

  MinimumQuantity        int, def. 1     CHECK \> 0

  MaximumQuantity        int, null       CHECK ≥ minimum

  Required               bool, def. true 

  SelectionMode          enum, def.      Single \| Multiple \| AnyOneOf \|
                         Single          AllRequired \| Optional

  SameResourceRequired   bool, def.      Same physical resource across the
                         false           work

  Sequence               int, def. 0     Ordering hint
  ---------------------------------------------------------------------------

*AnyOneOf is what allows "one of: Standard Van or Large Van" without
demanding two vehicles.*

## **4.4 Role-scoped requirements**

  ----------------------------------------------------------------------------
  **Table**                   **Key fields**           **Notes**
  --------------------------- ------------------------ -----------------------
  CapabilityRequirements      CapabilityCode, Level,   e.g. DOG_GROOMING
                              MinimumExperience,       
                              Mandatory                

  CertificationRequirements   CertificationTypeCode,   e.g. ANIMAL_FIRST_AID;
                              Mandatory,               validity must cover the
                              MustBeValidThrough       work period

  CapacityRequirements        DimensionCode, Quantity, e.g. PET_COUNT = 2,
                              Unit, AggregationScope   COUNT. CHECK Quantity
                                                       \> 0
  ----------------------------------------------------------------------------

## **4.5 Single-cardinality requirements**

  -----------------------------------------------------------------------------
  **Table**               **Key fields**              **Notes**
  ----------------------- --------------------------- -------------------------
  DurationRequirements    DurationType,               Fixed \| Estimated \|
                          Estimated/Min/Max, Setup,   Range \| Derived. CHECK
                          Cleanup                     positive, min ≤ max

  TimeRequirements        TimeConstraintType,         Flexible \| Window \|
                          EarliestStart, LatestStart, FixedStart \|
                          LatestFinish,               FixedInterval \|
                          FixedStart/End, Deadline,   Deadline. CHECK temporal
                          Timezone                    ordering

  LocationRequirements    LocationMode,               CustomerLocation \|
                          LocationReferenceType/Id,   ProviderLocation \|
                          Lat, Lng, ServiceRadius     FacilityLocation \|
                                                      SpecificLocation \|
                                                      Remote

  ExecutionRequirements   ExecutionMode               OnSite \| Mobile \|
                                                      Remote \| FacilityBased
                                                      \| Hybrid

  TravelRequirements      TravelRequired,             Describes travel
                          Origin/DestinationMode,     constraints; does not
                          MaxTravelTime, MaxDistance  calculate routes
  -----------------------------------------------------------------------------

## **4.6 Multi-cardinality requirements**

  -------------------------------------------------------------------------------
  **Table**                **Key fields**               **Notes**
  ------------------------ ---------------------------- -------------------------
  BufferRequirements       BufferType, DurationMinutes, BeforeWork \| AfterWork
                           AppliesToRole,               \| Travel \| Cleanup \|
                           HardConstraint               Setup

  DependencyRequirements   DependencyType,              Opaque pointer --- no
                           DependsOnReferenceType/Id,   cross-service FK
                           LagMinutes                   

  Constraints              ConstraintType, Operator,    Hard = must hold (gate).
                           Value, HardConstraint,       Soft = should hold
                           Reason                       

  Preferences              PreferenceType, Value,       Weighted desirability.
                           Weight, Source               Never a gate
  -------------------------------------------------------------------------------

## **4.7 Versioning, idempotency, outbox**

  -------------------------------------------------------------------------
  **Table**             **Purpose**         **Notes**
  --------------------- ------------------- -------------------------------
  RequirementVersions   One row per         SnapshotJson holds the
                        revision            immutable resolved snapshot; UQ
                                            (WorkRequirementId, Version)

  IdempotencyRecords    Replay protection   UQ (TenantId, IdempotencyKey) +
                                            SHA-256 request hash ---
                                            mirrors the demand schema
                                            exactly

  OutboxMessages        Transactional event State change + outbox row
                        publish             commit together; partial index
                                            on unprocessed
  -------------------------------------------------------------------------

# **5. Work Requirement status**

  -----------------------------------------------------------------------
  **Status**         **Meaning**
  ------------------ ----------------------------------------------------
  Draft              Created, not yet validated.

  Validating         Validation in progress.

  Valid              Passed validation; usable by Eligibility, Capacity,
                     and Scheduling.

  Invalid            Failed validation; must not be consumed downstream.

  Superseded         Replaced by a newer requirement version.

  Cancelled          Withdrawn.

  Completed          Work finished. Set from a downstream lifecycle event
                     --- this service never infers completion itself.
  -----------------------------------------------------------------------

# **6. Validation categories**

Validation runs across seven categories. Structural, semantic, and
temporal rules are additionally enforced as database CHECK constraints,
so an invalid requirement cannot be persisted even by a code path that
skips the validator.

  ------------------------------------------------------------------------
  **Category**        **Examples**
  ------------------- ----------------------------------------------------
  Structural          Work type required; at least one resource role;
                      duration required; source reference required

  Reference           Capability code exists; certification type exists;
                      capacity dimension exists; location exists

  Semantic            Quantity positive; duration positive; minimum
                      quantity ≤ maximum

  Temporal            Latest start not before earliest start; deadline not
                      before earliest start; certification validity covers
                      the work period

  Composition         Mobile execution implies a vehicle role --- warning
                      or error by policy

  Cross-requirement   A PET_COUNT capacity requirement must reference a
                      role able to consume PET_COUNT

  Policy              Tenant or organization policy gates
  ------------------------------------------------------------------------

# **7. Alignment with Demand Intake**

Both services sit in the Demand & Intake domain and share a deployable
unit and database. Four points where this contract adopts the shipped
Demand Intake convention rather than the specification\'s own wording,
so the domain presents one coherent surface:

  ------------------------------------------------------------------------
  **Point**      **Specification says**   **This contract**
  -------------- ------------------------ --------------------------------
  Base path      /v1/work-requirements    /api/v1/work-requirements ---
                                          matches /api/v1/demands

  Error model    RFC-7807 problem+json    The shipped hybrid: error +
                                          code + traceId + issues\[\]

  Upstream event ServiceRequestCreated    DemandAcceptedEvent, via an
                                          adapter (§8)

  Source fields  sourceType / sourceId /  Retained as-is; SourceId holds
                 sourceVersion            the Demand id
  ------------------------------------------------------------------------

## **7.1 Open question --- "Service Request"**

The specification repeatedly names a Service Request as the upstream,
and its recommended deployment unit lists three modules: Demand Intake,
Service Request, and Work Requirement. The architecture diagram shows
only two --- Demand Intake feeding Work Requirement. The specification
also states plainly that this service "references: Source Demand."

Two readings are possible, and the source material does not settle it:

- **Service Request is this specification\'s name for what the platform
  calls a Demand** --- in which case no third service exists and the
  naming is simply inconsistent between documents.

- **Service Request is a genuine third service** sitting between Demand
  Intake and Work Requirement, not yet built and not yet drawn.

This contract does not resolve the question. It is designed so that
either reading works without schema change: SourceType is open text, and
the adapter layer (§8) absorbs whichever upstream event arrives. The
decision should be taken at workshop before Work Requirement is
implemented, because it determines whether a Service Request service
needs specifying at all.

# **8. Upstream adapter**

The specification requires that this service never couple to a caller\'s
business vocabulary, and mandates an adapter between the upstream demand
and the resolution contract. That adapter is what makes the open
question above harmless.

**DemandAcceptedEvent → Demand Adapter → Resolution Contract → Work
Requirement Service**

**ServiceRequestCreated → Service Request Adapter → Resolution Contract
→ Work Requirement Service**

Today only the first path exists: the adapter consumes
DemandAcceptedEvent, reads the Demand\'s opaque externalReference and
its demandType, and normalizes them into a resolution request naming a
template code and inputs. If a Service Request service ever appears, a
second adapter is added and the core service is untouched --- it never
learns which upstream produced the work. SourceType records which one
did.

**Note on the domain join:** Demand Intake already carries a
requirementReferenceId field, set once a demand is matched to a Work
Requirement. This schema\'s SourceType / SourceId is the reverse
pointer. The two together form the only coupling between the schemas ---
there is no foreign key across them, and neither service reads the
other\'s tables.

*Established by the Work Requirement Service Specification (Document
06): all entities, fields, enums, statuses, validation categories,
permissions, and boundary rules. Specified by this contract: physical
types and lengths, foreign keys and delete rules, indexes, CHECK
constraints, the outbox and idempotency tables, and the four alignment
decisions in §7. The "Service Request" question in §7.1 is unresolved in
the source material and is flagged for workshop decision.*
