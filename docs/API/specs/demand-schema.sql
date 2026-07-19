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
CREATE TYPE demand.demand_status AS ENUM (                            -- [SPEC] values
  'Received','Validating','Ready','Accepted','Rejected','Cancelled','Expired'
);
CREATE TYPE demand.demand_priority AS ENUM (                          -- [SPEC] values
  'Low','Normal','High','Urgent','Critical'
);
CREATE TYPE demand.validation_status AS ENUM (                        -- [SPEC] values
  'Invalid','ValidWithWarnings','Valid'
);

-- =====================================================================
-- demand.Demands  - aggregate root
-- =====================================================================
CREATE TABLE demand."Demands" (
  "Id"                    uuid           NOT NULL,                    -- [SPEC] PK, TenantScopedEntity
  "TenantId"              uuid           NOT NULL,                    -- [SPEC]
  "OrganizationId"        uuid           NULL,                        -- [SPEC] opaque, no Organization service
  "DemandType"            varchar(100)   NOT NULL,                    -- [SPEC] required   [INFER] length
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
        OR "RequestedStartAtUtc" < "RequestedEndAtUtc")
);

-- [REC] R3: covering index for POST /search (tenant + status + date range,
-- with priority/demandType as secondary filters).
CREATE INDEX "IX_Demands_Search"
  ON demand."Demands" ("TenantId","Status","CreatedAt" DESC)
  INCLUDE ("Priority","DemandType");

-- [REC] optional: lookups by the caller-domain pointer (e.g. reconciliation).
CREATE INDEX "IX_Demands_ExternalRef"
  ON demand."Demands" ("TenantId","ExternalReferenceType","ExternalReferenceId");

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
--   DemandType    : ScheduledService | EmergencyWork | RecurringService |
--                   Consultation | FollowUp | HomeVisit | Assessment | Transport
--   SourceSystem  : PetZivApp | PetZivWeb | PetZivAdmin | PartnerAPI |
--                   CallCenterCRM | Migration | SystemScheduler
--   SourceChannel : MobileApp | WebPortal | CallCenter | Email | Chat |
--                   WalkIn | PartnerIntegration | AutomatedRecurrence  (nullable)
--
-- Status is PLATFORM-OWNED and closed - already enforced by the enum type
-- demand.demand_status above. Not tenant-extensible.
-- =====================================================================

-- =====================================================================
-- [REC] R5: optimistic-concurrency note (no DDL).
-- Version is application-managed: PATCH supplies expectedVersion, mismatch
-- returns 412. Ensure the read-check-increment happens in ONE transaction,
-- e.g.  UPDATE ... SET "Version" = "Version" + 1, ...
--       WHERE "Id" = @id AND "Version" = @expectedVersion;  -- 0 rows => 412
-- so two concurrent PATCHes cannot both write n+1.
-- =====================================================================
