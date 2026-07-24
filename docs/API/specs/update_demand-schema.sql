-- =====================================================================
-- ESWMP - Demand Intake Service - demand schema
-- Database: eswmp_work   Service: Eswmp.Work (port 6004)
-- =====================================================================
-- PROVENANCE
--   [SPEC]  stated in source specification (01-demand-intake-service.txt)
--   [REC]   added per the design recommendations - NOT as-built; confirm
--           against EF migrations / WorkDbContext.cs before treating as fact
--   [INFER] physical type/length/nullability chosen here; the spec gives
--           logical shape only. Reconcile with actual migrations.
-- =====================================================================

CREATE SCHEMA IF NOT EXISTS demand;                                   -- [SPEC]

-- ---------------------------------------------------------------------
-- Enums.  [REC/INFER] persisted as text for legibility + safe reordering
-- (recommendation R4). If the codebase maps these as int via HasConversion,
-- drop these types and use smallint columns instead.
-- ---------------------------------------------------------------------
-- [v2] NeedsAttention added. Non-terminal. Entered from Received (validation
-- errors) and from Accepted (downstream resolution failed). Without it a failed
-- resolution is silent: the demand reads Accepted while no requirement exists.
CREATE TYPE demand.demand_status AS ENUM (                            -- [SPEC] + [v2]
  'Received','Validating','Ready','NeedsAttention','Accepted',
  'Rejected','Cancelled','Expired'
);
-- [v2] Who owns fixing a demand that needs attention (UX-10).
CREATE TYPE demand.attention_owner AS ENUM (
  'Customer','Dispatcher','Administrator'
);
CREATE TYPE demand.demand_priority AS ENUM (                          -- [SPEC] values
  'Low','Normal','High','Urgent','Critical'
);
CREATE TYPE demand.validation_status AS ENUM (                        -- [SPEC] values
  'Invalid','ValidWithWarnings','Valid'
);
-- [REC] Timing axis, platform-owned and closed - kept OUT of DemandType so
-- adding a fulfillment model never multiplies the caller's type vocabulary.
-- OnDemand routes to real-time matching; Scheduled to availability+booking.
CREATE TYPE demand.fulfillment_mode AS ENUM (
  'OnDemand','Scheduled','Recurring','Standby'
);

-- =====================================================================
-- demand.Demands  - aggregate root
-- =====================================================================
CREATE TABLE demand."Demands" (
  "Id"                    uuid           NOT NULL,                    -- [SPEC] PK, TenantScopedEntity
  "TenantId"              uuid           NOT NULL,                    -- [SPEC]
  "OrganizationId"        uuid           NULL,                        -- [SPEC] opaque, no Organization service
  "DemandType"            varchar(100)   NOT NULL,                    -- [SPEC] required   [INFER] length
  "FulfillmentMode"       demand.fulfillment_mode NOT NULL DEFAULT 'Scheduled', -- [REC] timing axis, platform-owned
  "SourceSystem"          varchar(100)   NOT NULL,                    -- [SPEC] required   [INFER] length
  "SourceChannel"         varchar(100)   NULL,                        -- [SPEC]            [INFER] length
  "Status"                demand.demand_status   NOT NULL DEFAULT 'Received', -- [SPEC] default
  "Priority"              demand.demand_priority NOT NULL DEFAULT 'Normal',   -- [SPEC] default
  "Summary"               varchar(500)   NULL,                        -- [SPEC]            [INFER] length
  "Description"           text           NULL,                        -- [SPEC]
  "RequestedStartAtUtc"   timestamptz    NULL,                        -- [SPEC]
  "RequestedEndAtUtc"     timestamptz    NULL,                        -- [SPEC]
  "RequestedTimezone"     varchar(64)    NULL,                        -- [SPEC]            [INFER] length
  "LocationReference"     jsonb          NULL,                        -- [SPEC] opaque
  "RequirementReferenceId" uuid          NULL,                        -- [SPEC] set on match
  "ExternalReferenceType" varchar(100)   NOT NULL,                    -- [SPEC] required, opaque
  "ExternalReferenceId"   varchar(200)   NOT NULL,                    -- [SPEC] required, opaque   [INFER] length
  "Version"               integer        NOT NULL DEFAULT 1,          -- [SPEC] optimistic concurrency
  -- [v2] Triage ownership (UX-10)
  "AssignedTo"            varchar(200)   NULL,                        -- actor id or queue name
  "AssignedRole"          demand.attention_owner NULL,
  -- [v2] Why this demand needs attention (UX-09, UX-14)
  "AttentionReason"       varchar(100)   NULL,                        -- e.g. RESOLUTION_FAILED
  "AttentionIssuesJson"   jsonb          NULL,                        -- snapshot of issues[] at the time
  -- [v2] Resolution retry accounting (UX-14)
  "ResolutionAttempts"    integer        NOT NULL DEFAULT 0,
  "LastResolutionError"   varchar(500)   NULL,
  -- [v2] Recurrence made real, not just a FulfillmentMode label
  "RecurrenceRule"        varchar(500)   NULL,                        -- RFC 5545 RRULE
  "SeriesId"              uuid           NULL,                        -- groups instances of one series
  -- audit columns from TenantScopedEntity                              [SPEC]
  "CreatedAt"             timestamptz    NOT NULL DEFAULT now(),
  "CreatedBy"             varchar(200)   NULL,
  "UpdatedAt"             timestamptz    NULL,
  "UpdatedBy"             varchar(200)   NULL,
  CONSTRAINT "PK_Demands" PRIMARY KEY ("Id"),
  -- [REC] R2: model-level guard for the validate-time rule INVALID_TIME_WINDOW.
  -- Application still enforces it; this is defence in depth, not a replacement.
  CONSTRAINT "CK_Demands_TimeWindow"
    CHECK ("RequestedStartAtUtc" IS NULL
        OR "RequestedEndAtUtc"   IS NULL
        OR "RequestedStartAtUtc" < "RequestedEndAtUtc"),
  -- [v2] A demand in NeedsAttention must say why. Prevents a dead-end state
  -- that no operator can action.
  CONSTRAINT "CK_Demands_AttentionReason"
    CHECK ("Status" <> 'NeedsAttention' OR "AttentionReason" IS NOT NULL),
  -- [v2] Recurrence is only meaningful for the Recurring fulfillment mode.
  CONSTRAINT "CK_Demands_Recurrence"
    CHECK ("RecurrenceRule" IS NULL OR "FulfillmentMode" = 'Recurring'),
  CONSTRAINT "CK_Demands_Attempts" CHECK ("ResolutionAttempts" >= 0)
);

-- [REC] R3: covering index for POST /search (tenant + status + date range,
-- with priority/demandType as secondary filters).
CREATE INDEX "IX_Demands_Search"
  ON demand."Demands" ("TenantId","Status","CreatedAt" DESC)
  INCLUDE ("Priority","DemandType");

-- [REC] optional: lookups by the caller-domain pointer (e.g. reconciliation).
CREATE INDEX "IX_Demands_ExternalRef"
  ON demand."Demands" ("TenantId","ExternalReferenceType","ExternalReferenceId");

-- [REC] partial index for the real-time OnDemand queue - keeps the hot path
-- (unassigned on-demand demands awaiting dispatch) small and fast.
CREATE INDEX "IX_Demands_OnDemandQueue"
  ON demand."Demands" ("TenantId","CreatedAt")
  WHERE "FulfillmentMode" = 'OnDemand' AND "Status" IN ('Received','Ready');

-- [v2] Triage queue: the dispatcher's primary working set.
CREATE INDEX "IX_Demands_Attention"
  ON demand."Demands" ("TenantId","AssignedRole","CreatedAt")
  WHERE "Status" = 'NeedsAttention';

-- [v2] Recurring series lookup.
CREATE INDEX "IX_Demands_Series"
  ON demand."Demands" ("TenantId","SeriesId")
  WHERE "SeriesId" IS NOT NULL;

-- =====================================================================
-- [v2] demand.DemandLineage - split and merge provenance
-- A split creates N children from one parent; a merge points losers at a
-- surviving demand. Recorded rather than inferred so the audit trail can
-- answer "where did this come from".
-- =====================================================================
CREATE TABLE demand."DemandLineage" (
  "Id"            uuid          NOT NULL,
  "TenantId"      uuid          NOT NULL,
  "DemandId"      uuid          NOT NULL,                             -- the child / merged-away demand
  "RelatedId"     uuid          NOT NULL,                             -- the parent / surviving demand
  "Relation"      varchar(20)   NOT NULL,                             -- 'SplitFrom' | 'MergedInto'
  "ActorId"       varchar(200)  NULL,
  "Reason"        varchar(500)  NULL,
  "CreatedAt"     timestamptz   NOT NULL DEFAULT now(),
  CONSTRAINT "PK_DemandLineage" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_Lineage_Demand"
    FOREIGN KEY ("DemandId") REFERENCES demand."Demands" ("Id") ON DELETE CASCADE,
  CONSTRAINT "FK_Lineage_Related"
    FOREIGN KEY ("RelatedId") REFERENCES demand."Demands" ("Id") ON DELETE RESTRICT,
  CONSTRAINT "CK_Lineage_Relation" CHECK ("Relation" IN ('SplitFrom','MergedInto')),
  CONSTRAINT "CK_Lineage_NotSelf" CHECK ("DemandId" <> "RelatedId")
);
CREATE INDEX "IX_Lineage_Demand" ON demand."DemandLineage" ("TenantId","DemandId");
CREATE INDEX "IX_Lineage_Related" ON demand."DemandLineage" ("TenantId","RelatedId");

-- =====================================================================
-- [v2] demand.DemandAuditEntries - real audit trail
-- Replaces the known-empty GET /{id}/history. One row per state change or
-- material mutation.
-- =====================================================================
CREATE TABLE demand."DemandAuditEntries" (
  "Id"            uuid          NOT NULL,
  "TenantId"      uuid          NOT NULL,
  "DemandId"      uuid          NOT NULL,
  "ChangeType"    varchar(50)   NOT NULL,                             -- Created/Validated/Accepted/...
  "FromStatus"    demand.demand_status NULL,
  "ToStatus"      demand.demand_status NULL,
  "ActorId"       varchar(200)  NULL,
  "ActorRole"     varchar(100)  NULL,
  "CorrelationId" varchar(100)  NULL,
  "Reason"        varchar(500)  NULL,
  "BeforeSummary" jsonb         NULL,
  "AfterSummary"  jsonb         NULL,
  "OccurredAt"    timestamptz   NOT NULL DEFAULT now(),
  CONSTRAINT "PK_DemandAuditEntries" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_Audit_Demand"
    FOREIGN KEY ("DemandId") REFERENCES demand."Demands" ("Id") ON DELETE CASCADE
);
CREATE INDEX "IX_Audit_Demand" ON demand."DemandAuditEntries" ("TenantId","DemandId","OccurredAt" DESC);

-- =====================================================================
-- demand.DemandValidationResults  - one row per validate call
-- =====================================================================
CREATE TABLE demand."DemandValidationResults" (
  "Id"           uuid                     NOT NULL,                   -- [SPEC] PK
  "TenantId"     uuid                     NOT NULL,                   -- [SPEC]
  "DemandId"     uuid                     NOT NULL,                   -- [SPEC]
  "Status"       demand.validation_status NOT NULL,                  -- [SPEC]
  "ValidatedAt"  timestamptz              NOT NULL,                   -- [SPEC]
  "IssuesJson"   jsonb                    NOT NULL DEFAULT '[]',      -- [SPEC] [{code,severity,message}]
  "CreatedAt"    timestamptz              NOT NULL DEFAULT now(),     -- [SPEC] TenantScopedEntity
  "CreatedBy"    varchar(200)             NULL,
  "UpdatedAt"    timestamptz              NULL,
  "UpdatedBy"    varchar(200)             NULL,
  CONSTRAINT "PK_DemandValidationResults" PRIMARY KEY ("Id"),
  -- [REC] R2: enforce the parent link with an explicit delete rule rather
  -- than leaving it implicit. CASCADE chosen so a removed Demand cannot leave
  -- orphaned results; change to RESTRICT if Demands are never hard-deleted.
  CONSTRAINT "FK_DVR_Demand"
    FOREIGN KEY ("DemandId") REFERENCES demand."Demands" ("Id") ON DELETE CASCADE
);

-- [REC] latest-result-per-demand and history reads.
CREATE INDEX "IX_DVR_Demand"
  ON demand."DemandValidationResults" ("TenantId","DemandId","ValidatedAt" DESC);

-- =====================================================================
-- demand.DemandIdempotencyRecords  - create-request idempotency
-- =====================================================================
CREATE TABLE demand."DemandIdempotencyRecords" (
  "Id"               uuid          NOT NULL,                          -- [SPEC] PK
  "TenantId"         uuid          NOT NULL,                          -- [SPEC]
  "IdempotencyKey"   varchar(255)  NOT NULL,                          -- [SPEC] from header   [INFER] length
  "RequestHash"      char(64)      NOT NULL,                          -- [SPEC] SHA-256 hex    [INFER] fixed len
  "DemandId"         uuid          NOT NULL,                          -- [SPEC]
  "ResponseBodyJson" jsonb         NOT NULL,                          -- [SPEC] replayed 201
  "CreatedAt"        timestamptz   NOT NULL DEFAULT now(),            -- [SPEC] TenantScopedEntity
  "CreatedBy"        varchar(200)  NULL,
  "UpdatedAt"        timestamptz   NULL,
  "UpdatedBy"        varchar(200)  NULL,
  CONSTRAINT "PK_DemandIdempotencyRecords" PRIMARY KEY ("Id"),
  -- [SPEC] the one constraint the spec states explicitly
  CONSTRAINT "UQ_Idem_Tenant_Key" UNIQUE ("TenantId","IdempotencyKey"),
  -- [REC] R2: parent link. RESTRICT (not CASCADE) - an idempotency record is
  -- the receipt of a create; it should outlive casual deletes so replays stay
  -- correct. Revisit alongside any retention/purge policy.
  CONSTRAINT "FK_Idem_Demand"
    FOREIGN KEY ("DemandId") REFERENCES demand."Demands" ("Id") ON DELETE RESTRICT
);

-- =====================================================================
-- [REC] R1: tenant isolation.
-- The spec confirms EF HasQueryFilter on Demand and DemandIdempotencyRecord
-- but NOT on DemandValidationResult. If isolation is enforced only in EF,
-- these RLS policies make it hold at the database level too - belt and
-- braces, and they cover the result table the EF filter may miss.
-- Enable only if the app sets app.current_tenant per connection/transaction.
-- =====================================================================
-- ALTER TABLE demand."Demands"                   ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE demand."DemandValidationResults"   ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE demand."DemandIdempotencyRecords"  ENABLE ROW LEVEL SECURITY;
--
-- CREATE POLICY tenant_isolation ON demand."Demands"
--   USING ("TenantId" = current_setting('app.current_tenant')::uuid);
-- CREATE POLICY tenant_isolation ON demand."DemandValidationResults"
--   USING ("TenantId" = current_setting('app.current_tenant')::uuid);
-- CREATE POLICY tenant_isolation ON demand."DemandIdempotencyRecords"
--   USING ("TenantId" = current_setting('app.current_tenant')::uuid);

-- =====================================================================
-- [REC] Reference values for the categorical columns.
-- DemandType / SourceSystem / SourceChannel are CALLER-DEFINED - the values
-- below are an illustrative PetZiv (tenant #1) starter set, not a platform
-- constraint. If you constrain them at all, do it per-tenant (lookup table
-- owned by caller config), NOT as a global CHECK - a hard-coded list here
-- would make the platform "know" the caller's vocabulary, which the spec
-- forbids. Shown as comments, not enforced:
--
--   DemandType    : GroomingVisit | VetConsult | DogWalk | PetTransport |
--                   Boarding | Training | NailTrim | Assessment  (illustrative;
--                   NATURE of work only - timing lives in FulfillmentMode)
--   SourceSystem  : PetZivApp | PetZivWeb | PetZivAdmin | PartnerAPI |
--                   CallCenterCRM | Migration | SystemScheduler
--   SourceChannel : MobileApp | WebPortal | CallCenter | Email | Chat |
--                   WalkIn | PartnerIntegration | AutomatedRecurrence  (nullable)
--
-- Status and FulfillmentMode are PLATFORM-OWNED and closed - enforced by the
-- enum types demand.demand_status / demand.fulfillment_mode above. Neither is
-- tenant-extensible. FulfillmentMode is the TIMING axis (OnDemand | Scheduled |
-- Recurring | Standby); keeping it separate from DemandType is what lets a new
-- service type OR a new fulfillment model be added without multiplying the other.
-- =====================================================================

-- =====================================================================
-- [REC] R5: optimistic-concurrency note (no DDL).
-- Version is application-managed: PATCH supplies expectedVersion, mismatch
-- returns 412. Ensure the read-check-increment happens in ONE transaction,
-- e.g.  UPDATE ... SET "Version" = "Version" + 1, ...
--       WHERE "Id" = @id AND "Version" = @expectedVersion;  -- 0 rows => 412
-- so two concurrent PATCHes cannot both write n+1.
-- =====================================================================
