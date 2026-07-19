-- =====================================================================
-- ESWMP - Work Requirement Service - requirement schema
-- Database: eswmp_work   Service: Eswmp.Work (Demand & Intake domain)
-- Sibling of the demand schema; same deployable unit, separate schema.
-- =====================================================================
-- Boundary rules (spec 155):
--   Work Requirement Module owns the requirement schema.
--   Demand Intake code must not touch requirement tables (and vice versa).
--   Scheduling and Assignment cannot modify requirement tables.
--
-- PROVENANCE
--   [SPEC]  stated in the Work Requirement Service Specification (Doc 06)
--   [ALIGN] adopted from the shipped Demand Intake conventions so the two
--           services in this domain stay consistent
--   [INFER] physical type/length chosen here; spec gives logical shape only
-- =====================================================================

CREATE SCHEMA IF NOT EXISTS requirement;                              -- [SPEC 155]

-- ---------------------------------------------------------------------
-- Enums.  Persisted as text for legibility and safe reordering [ALIGN]
-- ---------------------------------------------------------------------
CREATE TYPE requirement.work_requirement_status AS ENUM (             -- [SPEC 10]
  'Draft','Validating','Valid','Invalid','Superseded','Cancelled','Completed'
);
CREATE TYPE requirement.template_status AS ENUM (                     -- [SPEC 13]
  'Draft','Active','Retired'
);
CREATE TYPE requirement.template_version_status AS ENUM (             -- [SPEC 14]
  'Draft','Active','Superseded','Retired'
);
CREATE TYPE requirement.resource_category AS ENUM (                   -- [SPEC 17]
  'Person','Team','Vehicle','Facility','Room','Equipment',
  'VirtualResource','ResourcePool'
);
CREATE TYPE requirement.selection_mode AS ENUM (                      -- [SPEC 19]
  'Single','Multiple','AnyOneOf','AllRequired','Optional'
);
CREATE TYPE requirement.duration_type AS ENUM (                       -- [SPEC 30]
  'Fixed','Estimated','Range','Derived'
);
CREATE TYPE requirement.time_constraint_type AS ENUM (                -- [SPEC 33]
  'Flexible','Window','FixedStart','FixedInterval','Deadline'
);
CREATE TYPE requirement.location_mode AS ENUM (                       -- [SPEC 41]
  'CustomerLocation','ProviderLocation','FacilityLocation',
  'SpecificLocation','Remote'
);
CREATE TYPE requirement.execution_mode AS ENUM (                      -- [SPEC 43]
  'OnSite','Mobile','Remote','FacilityBased','Hybrid'
);
CREATE TYPE requirement.buffer_type AS ENUM (                         -- [SPEC 46]
  'BeforeWork','AfterWork','Travel','Cleanup','Setup'
);
CREATE TYPE requirement.priority AS ENUM (                            -- [ALIGN] mirrors demand.Demands
  'Low','Normal','High','Urgent','Critical'
);

-- =====================================================================
-- requirement.RequirementTemplates                            [SPEC 13]
-- Reusable operational defaults, per tenant.
-- =====================================================================
CREATE TABLE requirement."RequirementTemplates" (
  "Id"             uuid          NOT NULL,
  "TenantId"       uuid          NOT NULL,
  "Code"           varchar(100)  NOT NULL,                            -- e.g. DOG_WALK_STANDARD
  "Name"           varchar(200)  NOT NULL,
  "Description"    text          NULL,
  "WorkType"       varchar(100)  NOT NULL,                            -- e.g. DOG_WALKING
  "Status"         requirement.template_status NOT NULL DEFAULT 'Draft',
  "CurrentVersion" integer       NOT NULL DEFAULT 1,
  "CreatedAt"      timestamptz   NOT NULL DEFAULT now(),
  "CreatedBy"      varchar(200)  NULL,
  "UpdatedAt"      timestamptz   NULL,
  "UpdatedBy"      varchar(200)  NULL,
  CONSTRAINT "PK_RequirementTemplates" PRIMARY KEY ("Id"),
  -- [SPEC 117] cross-tenant template use prohibited; code unique per tenant
  CONSTRAINT "UQ_Template_Tenant_Code" UNIQUE ("TenantId","Code")
);
CREATE INDEX "IX_Templates_Search"                                    -- [SPEC 98] filters
  ON requirement."RequirementTemplates" ("TenantId","WorkType","Status");

-- =====================================================================
-- requirement.RequirementTemplateVersions                     [SPEC 14]
-- Immutable after activation. Work generated under v1 must not silently
-- become v2 (SPEC 15).
-- =====================================================================
CREATE TABLE requirement."RequirementTemplateVersions" (
  "Id"            uuid          NOT NULL,
  "TenantId"      uuid          NOT NULL,
  "TemplateId"    uuid          NOT NULL,
  "Version"       integer       NOT NULL,
  "EffectiveFrom" timestamptz   NULL,
  "EffectiveTo"   timestamptz   NULL,
  "Status"        requirement.template_version_status NOT NULL DEFAULT 'Draft',
  "ChangeReason"  varchar(500)  NULL,
  -- The version's requirement definitions, frozen on activation. Held as
  -- jsonb so a template version is one immutable document. [INFER]
  "DefinitionJson" jsonb        NOT NULL DEFAULT '{}',
  "CreatedAt"     timestamptz   NOT NULL DEFAULT now(),
  "CreatedBy"     varchar(200)  NULL,
  CONSTRAINT "PK_RequirementTemplateVersions" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_TemplateVersion" UNIQUE ("TemplateId","Version"),
  CONSTRAINT "FK_TV_Template"
    FOREIGN KEY ("TemplateId") REFERENCES requirement."RequirementTemplates" ("Id")
    ON DELETE RESTRICT
);

-- =====================================================================
-- requirement.WorkRequirements  - aggregate root              [SPEC 9]
-- =====================================================================
CREATE TABLE requirement."WorkRequirements" (
  "Id"                 uuid        NOT NULL,
  "TenantId"           uuid        NOT NULL,
  -- Source reference [SPEC 11]. Points back to the originating demand.
  -- The service must NOT copy the source object. sourceType is open text
  -- so a future Service Request upstream needs no schema change. [ALIGN]
  "SourceType"         varchar(100) NOT NULL,                         -- e.g. 'Demand'
  "SourceId"           varchar(200) NOT NULL,                         -- the Demand's id
  "SourceVersion"      integer      NULL,                             -- Demand.Version at resolution
  -- Template provenance [SPEC 15] - preserved so work does not drift
  "TemplateId"         uuid         NULL,
  "TemplateVersion"    integer      NULL,
  -- Work classification [SPEC 12]
  "WorkType"           varchar(100) NOT NULL,
  "WorkCategory"       varchar(100) NULL,
  "ServiceMode"        varchar(100) NULL,
  "ComplexityLevel"    varchar(50)  NULL,
  "Status"             requirement.work_requirement_status NOT NULL DEFAULT 'Draft',
  "Priority"           requirement.priority NOT NULL DEFAULT 'Normal',
  "EffectiveFrom"      timestamptz  NULL,
  "EffectiveTo"        timestamptz  NULL,
  "RequirementVersion" integer      NOT NULL DEFAULT 1,               -- [SPEC 122] optimistic concurrency
  "CreatedAt"          timestamptz  NOT NULL DEFAULT now(),
  "CreatedBy"          varchar(200) NULL,
  "UpdatedAt"          timestamptz  NULL,
  "UpdatedBy"          varchar(200) NULL,
  CONSTRAINT "PK_WorkRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_WR_TemplateVersion"
    FOREIGN KEY ("TemplateId") REFERENCES requirement."RequirementTemplates" ("Id")
    ON DELETE RESTRICT
);
-- Resolve/lookup by originating demand - the Demand&Intake domain join. [ALIGN]
CREATE INDEX "IX_WR_Source"
  ON requirement."WorkRequirements" ("TenantId","SourceType","SourceId");
CREATE INDEX "IX_WR_Search"
  ON requirement."WorkRequirements" ("TenantId","Status","CreatedAt" DESC)
  INCLUDE ("WorkType","Priority");

-- =====================================================================
-- requirement.RequirementVersions                             [SPEC 65]
-- One row per revision. Change history + audit of what moved and why.
-- =====================================================================
CREATE TABLE requirement."RequirementVersions" (
  "Id"                uuid          NOT NULL,
  "TenantId"          uuid          NOT NULL,
  "WorkRequirementId" uuid          NOT NULL,
  "Version"           integer       NOT NULL,
  "ChangeType"        varchar(100)  NULL,                             -- e.g. Material, Minor
  "ChangeReason"      varchar(500)  NULL,
  "SourceVersion"     integer       NULL,
  "TemplateVersion"   integer       NULL,
  -- Immutable operational snapshot of the resolved requirement [SPEC 63]
  "SnapshotJson"      jsonb         NOT NULL DEFAULT '{}',
  "CreatedAt"         timestamptz   NOT NULL DEFAULT now(),
  "CreatedBy"         varchar(200)  NULL,
  CONSTRAINT "PK_RequirementVersions" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_RV_WR_Version" UNIQUE ("WorkRequirementId","Version"),
  CONSTRAINT "FK_RV_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);

-- =====================================================================
-- requirement.ResourceRoleRequirements                        [SPEC 16]
-- What operational role must be filled. Parent of capability/cert/capacity.
-- =====================================================================
CREATE TABLE requirement."ResourceRoleRequirements" (
  "Id"                   uuid        NOT NULL,
  "TenantId"             uuid        NOT NULL,
  "WorkRequirementId"    uuid        NOT NULL,
  "RoleCode"             varchar(100) NOT NULL,                       -- e.g. DOG_WALKER
  "ResourceCategory"     requirement.resource_category NOT NULL,
  "MinimumQuantity"      integer     NOT NULL DEFAULT 1,
  "MaximumQuantity"      integer     NULL,
  "Required"             boolean     NOT NULL DEFAULT true,
  "SelectionMode"        requirement.selection_mode NOT NULL DEFAULT 'Single',
  "SameResourceRequired" boolean     NOT NULL DEFAULT false,
  "Sequence"             integer     NOT NULL DEFAULT 0,
  CONSTRAINT "PK_ResourceRoleRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_RRR_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  -- [SPEC 73] semantic validation, enforced at the database too
  CONSTRAINT "CK_RRR_Quantity"
    CHECK ("MinimumQuantity" > 0
       AND ("MaximumQuantity" IS NULL OR "MaximumQuantity" >= "MinimumQuantity"))
);
CREATE INDEX "IX_RRR_WR" ON requirement."ResourceRoleRequirements" ("WorkRequirementId");

-- =====================================================================
-- requirement.CapabilityRequirements                          [SPEC 21]
-- =====================================================================
CREATE TABLE requirement."CapabilityRequirements" (
  "Id"                       uuid        NOT NULL,
  "TenantId"                 uuid        NOT NULL,
  "WorkRequirementId"        uuid        NOT NULL,
  "ResourceRoleRequirementId" uuid       NULL,                        -- scoped to a role, or work-wide
  "CapabilityCode"           varchar(100) NOT NULL,                   -- e.g. DOG_GROOMING
  "Level"                    varchar(50)  NULL,
  "MinimumExperience"        integer      NULL,
  "Mandatory"                boolean      NOT NULL DEFAULT true,
  "Scope"                    varchar(50)  NULL,
  CONSTRAINT "PK_CapabilityRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_CapR_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  CONSTRAINT "FK_CapR_Role"
    FOREIGN KEY ("ResourceRoleRequirementId") REFERENCES requirement."ResourceRoleRequirements" ("Id")
    ON DELETE CASCADE
);
CREATE INDEX "IX_CapR_WR" ON requirement."CapabilityRequirements" ("WorkRequirementId");

-- =====================================================================
-- requirement.CertificationRequirements                       [SPEC 24]
-- =====================================================================
CREATE TABLE requirement."CertificationRequirements" (
  "Id"                       uuid        NOT NULL,
  "TenantId"                 uuid        NOT NULL,
  "WorkRequirementId"        uuid        NOT NULL,
  "ResourceRoleRequirementId" uuid       NULL,
  "CertificationTypeCode"    varchar(100) NOT NULL,                   -- e.g. ANIMAL_FIRST_AID
  "Mandatory"                boolean      NOT NULL DEFAULT true,
  "MustBeValidThrough"       timestamptz  NULL,                       -- [SPEC 74] temporal validation
  "VerificationLevel"        varchar(50)  NULL,
  CONSTRAINT "PK_CertificationRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_CertR_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  CONSTRAINT "FK_CertR_Role"
    FOREIGN KEY ("ResourceRoleRequirementId") REFERENCES requirement."ResourceRoleRequirements" ("Id")
    ON DELETE CASCADE
);
CREATE INDEX "IX_CertR_WR" ON requirement."CertificationRequirements" ("WorkRequirementId");

-- =====================================================================
-- requirement.CapacityRequirements                            [SPEC 26]
-- How much of a dimension the work consumes. NOT current remaining
-- capacity - that belongs to the Capacity Service.
-- =====================================================================
CREATE TABLE requirement."CapacityRequirements" (
  "Id"                       uuid        NOT NULL,
  "TenantId"                 uuid        NOT NULL,
  "WorkRequirementId"        uuid        NOT NULL,
  "ResourceRoleRequirementId" uuid       NULL,
  "DimensionCode"            varchar(100) NOT NULL,                   -- e.g. PET_COUNT
  "Quantity"                 numeric(18,4) NOT NULL,
  "Unit"                     varchar(50)  NULL,                       -- e.g. COUNT
  "AggregationScope"         varchar(50)  NULL,
  "Mandatory"                boolean      NOT NULL DEFAULT true,
  CONSTRAINT "PK_CapacityRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_CapaR_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  CONSTRAINT "FK_CapaR_Role"
    FOREIGN KEY ("ResourceRoleRequirementId") REFERENCES requirement."ResourceRoleRequirements" ("Id")
    ON DELETE CASCADE,
  CONSTRAINT "CK_CapaR_Quantity" CHECK ("Quantity" > 0)               -- [SPEC 73]
);
CREATE INDEX "IX_CapaR_WR" ON requirement."CapacityRequirements" ("WorkRequirementId");

-- =====================================================================
-- Single-cardinality requirements. One row per WorkRequirement.
-- Duration [SPEC 29] / Time [SPEC 32] / Location [SPEC 40] /
-- Execution [SPEC 43] / Travel [SPEC 44]
-- =====================================================================
CREATE TABLE requirement."DurationRequirements" (
  "Id"                       uuid        NOT NULL,
  "TenantId"                 uuid        NOT NULL,
  "WorkRequirementId"        uuid        NOT NULL,
  "DurationType"             requirement.duration_type NOT NULL,
  "EstimatedDurationMinutes" integer     NULL,
  "MinimumDurationMinutes"   integer     NULL,
  "MaximumDurationMinutes"   integer     NULL,
  "SetupDurationMinutes"     integer     NULL,
  "CleanupDurationMinutes"   integer     NULL,
  CONSTRAINT "PK_DurationRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_Duration_WR" UNIQUE ("WorkRequirementId"),
  CONSTRAINT "FK_Dur_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  CONSTRAINT "CK_Dur_Positive"                                        -- [SPEC 73]
    CHECK (("EstimatedDurationMinutes" IS NULL OR "EstimatedDurationMinutes" > 0)
       AND ("MinimumDurationMinutes"   IS NULL OR "MinimumDurationMinutes"   > 0)
       AND ("MaximumDurationMinutes"   IS NULL OR "MaximumDurationMinutes"   > 0)
       AND ("MinimumDurationMinutes" IS NULL OR "MaximumDurationMinutes" IS NULL
            OR "MinimumDurationMinutes" <= "MaximumDurationMinutes"))
);

CREATE TABLE requirement."TimeRequirements" (
  "Id"                 uuid        NOT NULL,
  "TenantId"           uuid        NOT NULL,
  "WorkRequirementId"  uuid        NOT NULL,
  "TimeConstraintType" requirement.time_constraint_type NOT NULL,
  "EarliestStart"      timestamptz NULL,
  "LatestStart"        timestamptz NULL,
  "EarliestFinish"     timestamptz NULL,
  "LatestFinish"       timestamptz NULL,
  "FixedStart"         timestamptz NULL,
  "FixedEnd"           timestamptz NULL,
  "Deadline"           timestamptz NULL,
  "Timezone"           varchar(64) NULL,                              -- IANA id
  CONSTRAINT "PK_TimeRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_Time_WR" UNIQUE ("WorkRequirementId"),
  CONSTRAINT "FK_Time_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  -- [SPEC 74] temporal validation, enforced at the database too
  CONSTRAINT "CK_Time_Ordering"
    CHECK (("EarliestStart" IS NULL OR "LatestStart"  IS NULL OR "EarliestStart" <= "LatestStart")
       AND ("FixedStart"    IS NULL OR "FixedEnd"     IS NULL OR "FixedStart"    <  "FixedEnd")
       AND ("EarliestStart" IS NULL OR "Deadline"     IS NULL OR "EarliestStart" <= "Deadline")
       AND ("EarliestStart" IS NULL OR "LatestFinish" IS NULL OR "EarliestStart" <  "LatestFinish"))
);

CREATE TABLE requirement."LocationRequirements" (
  "Id"                    uuid        NOT NULL,
  "TenantId"              uuid        NOT NULL,
  "WorkRequirementId"     uuid        NOT NULL,
  "LocationMode"          requirement.location_mode NOT NULL,
  "LocationReferenceType" varchar(100) NULL,                          -- opaque pointer
  "LocationReferenceId"   varchar(200) NULL,                          -- opaque pointer
  "Latitude"              numeric(9,6) NULL,
  "Longitude"             numeric(9,6) NULL,
  "ServiceRadius"         numeric(10,2) NULL,
  "LocationFlexibility"   varchar(50)  NULL,
  CONSTRAINT "PK_LocationRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_Loc_WR" UNIQUE ("WorkRequirementId"),
  CONSTRAINT "FK_Loc_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);

CREATE TABLE requirement."ExecutionRequirements" (
  "Id"                uuid        NOT NULL,
  "TenantId"          uuid        NOT NULL,
  "WorkRequirementId" uuid        NOT NULL,
  "ExecutionMode"     requirement.execution_mode NOT NULL,
  CONSTRAINT "PK_ExecutionRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_Exec_WR" UNIQUE ("WorkRequirementId"),
  CONSTRAINT "FK_Exec_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);

CREATE TABLE requirement."TravelRequirements" (
  "Id"                       uuid        NOT NULL,
  "TenantId"                 uuid        NOT NULL,
  "WorkRequirementId"        uuid        NOT NULL,
  "TravelRequired"           boolean     NOT NULL DEFAULT false,
  "OriginMode"               varchar(50) NULL,
  "DestinationMode"          varchar(50) NULL,
  "MaximumTravelTimeMinutes" integer     NULL,
  "MaximumTravelDistance"    numeric(10,2) NULL,
  "TravelTimeIncludedInWork" boolean     NOT NULL DEFAULT false,
  CONSTRAINT "PK_TravelRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_Travel_WR" UNIQUE ("WorkRequirementId"),
  CONSTRAINT "FK_Travel_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);

-- =====================================================================
-- requirement.BufferRequirements                              [SPEC 46]
-- =====================================================================
CREATE TABLE requirement."BufferRequirements" (
  "Id"                uuid        NOT NULL,
  "TenantId"          uuid        NOT NULL,
  "WorkRequirementId" uuid        NOT NULL,
  "BufferType"        requirement.buffer_type NOT NULL,
  "DurationMinutes"   integer     NOT NULL,
  "AppliesToRole"     varchar(100) NULL,
  "HardConstraint"    boolean     NOT NULL DEFAULT false,
  CONSTRAINT "PK_BufferRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_Buf_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE,
  CONSTRAINT "CK_Buf_Positive" CHECK ("DurationMinutes" > 0)
);
CREATE INDEX "IX_Buf_WR" ON requirement."BufferRequirements" ("WorkRequirementId");

-- =====================================================================
-- requirement.DependencyRequirements                          [SPEC 51]
-- =====================================================================
CREATE TABLE requirement."DependencyRequirements" (
  "Id"                    uuid        NOT NULL,
  "TenantId"              uuid        NOT NULL,
  "WorkRequirementId"     uuid        NOT NULL,
  "DependencyType"        varchar(50) NOT NULL,
  "DependsOnReferenceType" varchar(100) NULL,                         -- opaque
  "DependsOnReferenceId"  varchar(200) NULL,                          -- opaque
  "LagMinutes"            integer     NULL,
  "HardConstraint"        boolean     NOT NULL DEFAULT true,
  CONSTRAINT "PK_DependencyRequirements" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_Dep_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);
CREATE INDEX "IX_Dep_WR" ON requirement."DependencyRequirements" ("WorkRequirementId");

-- =====================================================================
-- requirement.Constraints  [SPEC 54]   /   requirement.Preferences [SPEC 57]
-- Constraint = must hold (hard) or should hold (soft).
-- Preference = weighted desirability, never a gate.
-- =====================================================================
CREATE TABLE requirement."Constraints" (
  "Id"                uuid          NOT NULL,
  "TenantId"          uuid          NOT NULL,
  "WorkRequirementId" uuid          NOT NULL,
  "ConstraintType"    varchar(100)  NOT NULL,
  "Scope"             varchar(50)   NULL,
  "Operator"          varchar(50)   NULL,
  "Value"             varchar(500)  NULL,
  "HardConstraint"    boolean       NOT NULL DEFAULT true,            -- [SPEC 55/56]
  "Reason"            varchar(500)  NULL,
  CONSTRAINT "PK_Constraints" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_Con_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);
CREATE INDEX "IX_Con_WR" ON requirement."Constraints" ("WorkRequirementId");

CREATE TABLE requirement."Preferences" (
  "Id"                uuid          NOT NULL,
  "TenantId"          uuid          NOT NULL,
  "WorkRequirementId" uuid          NOT NULL,
  "PreferenceType"    varchar(100)  NOT NULL,
  "Value"             varchar(500)  NULL,
  "Weight"            numeric(5,2)  NULL,                             -- relative weight
  "Source"            varchar(100)  NULL,                             -- e.g. Customer, Template
  CONSTRAINT "PK_Preferences" PRIMARY KEY ("Id"),
  CONSTRAINT "FK_Pref_WR"
    FOREIGN KEY ("WorkRequirementId") REFERENCES requirement."WorkRequirements" ("Id")
    ON DELETE CASCADE
);
CREATE INDEX "IX_Pref_WR" ON requirement."Preferences" ("WorkRequirementId");

-- =====================================================================
-- requirement.IdempotencyRecords                       [SPEC 123][ALIGN]
-- Mirrors demand.DemandIdempotencyRecords exactly, so both services in
-- the domain behave identically. Required for: template creation, version
-- creation, resolution, revision, override, cancellation.
-- =====================================================================
CREATE TABLE requirement."IdempotencyRecords" (
  "Id"               uuid          NOT NULL,
  "TenantId"         uuid          NOT NULL,
  "IdempotencyKey"   varchar(255)  NOT NULL,
  "RequestHash"      char(64)      NOT NULL,                          -- SHA-256 of canonical body
  "Operation"        varchar(100)  NOT NULL,                          -- which op the key was used for
  "ResourceId"       uuid          NULL,                              -- the created/affected aggregate
  "ResponseBodyJson" jsonb         NOT NULL,
  "CreatedAt"        timestamptz   NOT NULL DEFAULT now(),
  CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Id"),
  CONSTRAINT "UQ_Idem_Tenant_Key" UNIQUE ("TenantId","IdempotencyKey")
);

-- =====================================================================
-- requirement.OutboxMessages                           [SPEC 124][ALIGN]
-- Transactional outbox. State change + outbox row commit together; a
-- relay publishes afterward. Same pattern as the demand schema.
-- =====================================================================
CREATE TABLE requirement."OutboxMessages" (
  "Id"            uuid          NOT NULL,
  "TenantId"      uuid          NOT NULL,
  "EventType"     varchar(200)  NOT NULL,                             -- e.g. WorkRequirementResolved
  "AggregateType" varchar(100)  NOT NULL,
  "AggregateId"   uuid          NOT NULL,
  "PayloadJson"   jsonb         NOT NULL,
  "CorrelationId" varchar(100)  NULL,
  "OccurredAt"    timestamptz   NOT NULL DEFAULT now(),
  "ProcessedAt"   timestamptz   NULL,
  CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id")
);
-- Hot path: unpublished messages only.
CREATE INDEX "IX_Outbox_Pending"
  ON requirement."OutboxMessages" ("OccurredAt")
  WHERE "ProcessedAt" IS NULL;

-- =====================================================================
-- Tenant isolation [SPEC 117][ALIGN]
-- Two layers, matching the demand schema: EF query filters plus database
-- RLS, so isolation holds even on a path that bypasses the ORM.
-- Enable when the app sets app.current_tenant per connection.
-- =====================================================================
-- ALTER TABLE requirement."RequirementTemplates"        ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."RequirementTemplateVersions" ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."WorkRequirements"            ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."RequirementVersions"         ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."ResourceRoleRequirements"    ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."CapabilityRequirements"      ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."CertificationRequirements"   ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."CapacityRequirements"        ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."DurationRequirements"        ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."TimeRequirements"            ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."LocationRequirements"        ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."ExecutionRequirements"       ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."TravelRequirements"          ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."BufferRequirements"          ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."DependencyRequirements"      ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."Constraints"                 ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."Preferences"                 ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE requirement."IdempotencyRecords"          ENABLE ROW LEVEL SECURITY;
--
-- CREATE POLICY tenant_isolation ON requirement."WorkRequirements"
--   USING ("TenantId" = current_setting('app.current_tenant')::uuid);
-- (repeat per table)

-- =====================================================================
-- Optimistic concurrency [SPEC 122][ALIGN]
-- RequirementVersion is application-managed. The read-check-increment
-- must be one atomic statement:
--   UPDATE requirement."WorkRequirements"
--      SET "RequirementVersion" = "RequirementVersion" + 1, ...
--    WHERE "Id" = @id AND "RequirementVersion" = @expectedVersion;
--   -- 0 rows affected => 412 Precondition Failed
-- =====================================================================
