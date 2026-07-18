using Eswmp.Shared.Middleware;
using Eswmp.Work.Models;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Data;

public class WorkDbContext(DbContextOptions<WorkDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    // Demand Intake module
    public DbSet<Demand> Demands => Set<Demand>();
    public DbSet<DemandValidationResult> DemandValidationResults => Set<DemandValidationResult>();
    public DbSet<DemandIdempotencyRecord> DemandIdempotencyRecords => Set<DemandIdempotencyRecord>();

    // Requirement Definition module (first-generation Work Requirement model — see the
    // provenance note on Eswmp.Work.Models.RequirementDefinition)
    public DbSet<RequirementDefinition> RequirementDefinitions => Set<RequirementDefinition>();
    public DbSet<RequirementDefinitionVersion> RequirementDefinitionVersions => Set<RequirementDefinitionVersion>();
    public DbSet<RequirementDefinitionSnapshot> RequirementDefinitionSnapshots => Set<RequirementDefinitionSnapshot>();
    public DbSet<DefinitionResourceRequirement> DefinitionResourceRequirements => Set<DefinitionResourceRequirement>();
    public DbSet<DefinitionCapabilityRequirement> DefinitionCapabilityRequirements => Set<DefinitionCapabilityRequirement>();
    public DbSet<DefinitionSkillRequirement> DefinitionSkillRequirements => Set<DefinitionSkillRequirement>();
    public DbSet<DefinitionCertificationRequirement> DefinitionCertificationRequirements => Set<DefinitionCertificationRequirement>();
    public DbSet<LocationConstraint> LocationConstraints => Set<LocationConstraint>();

    // Work Requirement module (reconciled model — schema "requirement")
    public DbSet<RequirementTemplate> RequirementTemplates => Set<RequirementTemplate>();
    public DbSet<RequirementTemplateVersion> RequirementTemplateVersions => Set<RequirementTemplateVersion>();
    public DbSet<WorkRequirement> WorkRequirements => Set<WorkRequirement>();
    public DbSet<RequirementVersion> RequirementVersions => Set<RequirementVersion>();
    public DbSet<ResourceRoleRequirement> ResourceRoleRequirements => Set<ResourceRoleRequirement>();
    public DbSet<CapabilityRequirement> CapabilityRequirements => Set<CapabilityRequirement>();
    public DbSet<CertificationRequirement> CertificationRequirements => Set<CertificationRequirement>();
    public DbSet<CapacityRequirement> CapacityRequirements => Set<CapacityRequirement>();
    public DbSet<DurationRequirement> DurationRequirements => Set<DurationRequirement>();
    public DbSet<TimeRequirement> TimeRequirements => Set<TimeRequirement>();
    public DbSet<LocationRequirement> LocationRequirements => Set<LocationRequirement>();
    public DbSet<ExecutionRequirement> ExecutionRequirements => Set<ExecutionRequirement>();
    public DbSet<TravelRequirement> TravelRequirements => Set<TravelRequirement>();
    public DbSet<BufferRequirement> BufferRequirements => Set<BufferRequirement>();
    public DbSet<DependencyRequirement> DependencyRequirements => Set<DependencyRequirement>();
    public DbSet<RequirementConstraint> Constraints => Set<RequirementConstraint>();
    public DbSet<RequirementPreference> Preferences => Set<RequirementPreference>();
    public DbSet<WorkRequirementIdempotencyRecord> WorkRequirementIdempotencyRecords => Set<WorkRequirementIdempotencyRecord>();
    public DbSet<WorkRequirementOutboxMessage> WorkRequirementOutboxMessages => Set<WorkRequirementOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Demand Intake module — schema "demand" ──────────────────────
        modelBuilder.Entity<Demand>(e =>
        {
            e.ToTable("Demands", schema: "demand", t => t.HasCheckConstraint(
                "CK_Demands_TimeWindow",
                "\"RequestedStartAtUtc\" IS NULL OR \"RequestedEndAtUtc\" IS NULL OR \"RequestedStartAtUtc\" < \"RequestedEndAtUtc\""));
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.LocationReference).HasColumnType("jsonb");
            e.Property(x => x.FulfillmentMode).HasDefaultValue(DemandFulfillmentMode.Scheduled);
            // Optimistic concurrency: PATCH/actions supply expectedVersion, and this token
            // makes SaveChangesAsync itself the atomic guard (DbUpdateConcurrencyException on
            // a lost race), not just the in-memory check that runs before it.
            e.Property(x => x.Version).IsConcurrencyToken();
            // Covers POST /search (tenant + status + created-date range, priority/demandType secondary).
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            // Lookups by the caller-domain pointer (e.g. reconciliation).
            e.HasIndex(x => new { x.TenantId, x.ExternalReferenceType, x.ExternalReferenceId });
        });

        modelBuilder.Entity<DemandValidationResult>(e =>
        {
            e.ToTable("DemandValidationResults", schema: "demand");
            // R1: the source spec confirmed this filter on Demand/DemandIdempotencyRecord but
            // not here — without it, IssuesJson (which may echo caller-supplied content) is
            // reachable across tenants for any read that doesn't join through a filtered Demand.
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.IssuesJson).HasColumnType("jsonb");
            e.HasOne<Demand>().WithMany().HasForeignKey(x => x.DemandId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.DemandId, x.ValidatedAt });
        });

        modelBuilder.Entity<DemandIdempotencyRecord>(e =>
        {
            e.ToTable("DemandIdempotencyRecords", schema: "demand");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.ResponseBodyJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            // RESTRICT (not CASCADE): an idempotency record is the receipt of a create — it
            // should outlive casual deletes so replays stay correct.
            e.HasOne<Demand>().WithMany().HasForeignKey(x => x.DemandId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Requirement Definition module — schema "requirements" ───────
        // First-generation Work Requirement model; see the provenance note on
        // Eswmp.Work.Models.RequirementDefinition. Physical schema unchanged (still
        // "requirements", plural) — only the C# vocabulary and routes moved, to free
        // "WorkRequirement"/"requirement" (singular) for the reconciled model below.
        modelBuilder.Entity<RequirementDefinition>(e =>
        {
            e.ToTable("RequirementDefinitions", schema: "requirements");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            e.HasMany(x => x.Versions)
                .WithOne()
                .HasForeignKey(x => x.RequirementDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementDefinitionVersion>(e =>
        {
            e.ToTable("RequirementDefinitionVersions", schema: "requirements");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => new { x.TenantId, x.VersionNumber, x.RequirementDefinitionId }).IsUnique();

            e.HasMany(x => x.ResourceRequirements)
                .WithOne()
                .HasForeignKey(x => x.RequirementDefinitionVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.LocationConstraints)
                .WithOne()
                .HasForeignKey(x => x.RequirementDefinitionVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementDefinitionSnapshot>(e =>
        {
            e.ToTable("RequirementDefinitionSnapshots", schema: "requirements");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<DefinitionResourceRequirement>(e =>
        {
            e.ToTable("DefinitionResourceRequirements", schema: "requirements");

            e.HasMany(x => x.CapabilityRequirements)
                .WithOne()
                .HasForeignKey(x => x.DefinitionResourceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.SkillRequirements)
                .WithOne()
                .HasForeignKey(x => x.DefinitionResourceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.CertificationRequirements)
                .WithOne()
                .HasForeignKey(x => x.DefinitionResourceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DefinitionCapabilityRequirement>(e =>
        {
            e.ToTable("DefinitionCapabilityRequirements", schema: "requirements");
        });

        modelBuilder.Entity<DefinitionSkillRequirement>(e =>
        {
            e.ToTable("DefinitionSkillRequirements", schema: "requirements");
        });

        modelBuilder.Entity<DefinitionCertificationRequirement>(e =>
        {
            e.ToTable("DefinitionCertificationRequirements", schema: "requirements");
        });

        modelBuilder.Entity<LocationConstraint>(e =>
        {
            e.ToTable("LocationConstraints", schema: "requirements");
        });

        // ── Work Requirement module — schema "requirement" ──────────────
        // The reconciled model (docs/api/specs/02-work-requirement-model.md,
        // requirement-schema.sql). Sibling of the "demand" schema in the same deployable
        // unit; neither module's code may touch the other's tables (CLAUDE.md rule 11).
        modelBuilder.Entity<RequirementTemplate>(e =>
        {
            e.ToTable("RequirementTemplates", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            // [SPEC] cross-tenant template use prohibited; code unique per tenant
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.WorkType, x.Status });

            e.HasMany(x => x.Versions)
                .WithOne()
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementTemplateVersion>(e =>
        {
            e.ToTable("RequirementTemplateVersions", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TemplateId, x.Version }).IsUnique();
        });

        modelBuilder.Entity<WorkRequirement>(e =>
        {
            e.ToTable("WorkRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            // The domain join back to Demand Intake — resolve/lookup by originating source.
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId });
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasOne<RequirementTemplate>().WithMany()
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.ResourceRequirements).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CapabilityRequirements).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CertificationRequirements).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CapacityRequirements).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DurationRequirement).WithOne().HasForeignKey<DurationRequirement>(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TimeRequirement).WithOne().HasForeignKey<TimeRequirement>(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LocationRequirement).WithOne().HasForeignKey<LocationRequirement>(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ExecutionRequirement).WithOne().HasForeignKey<ExecutionRequirement>(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TravelRequirement).WithOne().HasForeignKey<TravelRequirement>(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.BufferRequirements).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.DependencyRequirements).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Constraints).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Preferences).WithOne().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementVersion>(e =>
        {
            e.ToTable("RequirementVersions", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.WorkRequirementId, x.Version }).IsUnique();
            e.HasOne<WorkRequirement>().WithMany().HasForeignKey(x => x.WorkRequirementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResourceRoleRequirement>(e =>
        {
            e.ToTable("ResourceRoleRequirements", schema: "requirement", t => t.HasCheckConstraint(
                "CK_RRR_Quantity",
                "\"MinimumQuantity\" > 0 AND (\"MaximumQuantity\" IS NULL OR \"MaximumQuantity\" >= \"MinimumQuantity\")"));
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId);
        });

        modelBuilder.Entity<CapabilityRequirement>(e =>
        {
            e.ToTable("CapabilityRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId);
            e.HasOne<ResourceRoleRequirement>().WithMany().HasForeignKey(x => x.ResourceRoleRequirementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CertificationRequirement>(e =>
        {
            e.ToTable("CertificationRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId);
            e.HasOne<ResourceRoleRequirement>().WithMany().HasForeignKey(x => x.ResourceRoleRequirementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CapacityRequirement>(e =>
        {
            e.ToTable("CapacityRequirements", schema: "requirement", t => t.HasCheckConstraint(
                "CK_CapaR_Quantity", "\"Quantity\" > 0"));
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.Quantity).HasColumnType("numeric(18,4)");
            e.HasIndex(x => x.WorkRequirementId);
            e.HasOne<ResourceRoleRequirement>().WithMany().HasForeignKey(x => x.ResourceRoleRequirementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DurationRequirement>(e =>
        {
            e.ToTable("DurationRequirements", schema: "requirement", t => t.HasCheckConstraint(
                "CK_Dur_Positive",
                "(\"EstimatedDurationMinutes\" IS NULL OR \"EstimatedDurationMinutes\" > 0) " +
                "AND (\"MinimumDurationMinutes\" IS NULL OR \"MinimumDurationMinutes\" > 0) " +
                "AND (\"MaximumDurationMinutes\" IS NULL OR \"MaximumDurationMinutes\" > 0) " +
                "AND (\"MinimumDurationMinutes\" IS NULL OR \"MaximumDurationMinutes\" IS NULL OR \"MinimumDurationMinutes\" <= \"MaximumDurationMinutes\")"));
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId).IsUnique();
        });

        modelBuilder.Entity<TimeRequirement>(e =>
        {
            e.ToTable("TimeRequirements", schema: "requirement", t => t.HasCheckConstraint(
                "CK_Time_Ordering",
                "(\"EarliestStart\" IS NULL OR \"LatestStart\" IS NULL OR \"EarliestStart\" <= \"LatestStart\") " +
                "AND (\"FixedStart\" IS NULL OR \"FixedEnd\" IS NULL OR \"FixedStart\" < \"FixedEnd\") " +
                "AND (\"EarliestStart\" IS NULL OR \"Deadline\" IS NULL OR \"EarliestStart\" <= \"Deadline\") " +
                "AND (\"EarliestStart\" IS NULL OR \"LatestFinish\" IS NULL OR \"EarliestStart\" < \"LatestFinish\")"));
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId).IsUnique();
        });

        modelBuilder.Entity<LocationRequirement>(e =>
        {
            e.ToTable("LocationRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.Latitude).HasColumnType("numeric(9,6)");
            e.Property(x => x.Longitude).HasColumnType("numeric(9,6)");
            e.Property(x => x.ServiceRadius).HasColumnType("numeric(10,2)");
            e.HasIndex(x => x.WorkRequirementId).IsUnique();
        });

        modelBuilder.Entity<ExecutionRequirement>(e =>
        {
            e.ToTable("ExecutionRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId).IsUnique();
        });

        modelBuilder.Entity<TravelRequirement>(e =>
        {
            e.ToTable("TravelRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.MaximumTravelDistance).HasColumnType("numeric(10,2)");
            e.HasIndex(x => x.WorkRequirementId).IsUnique();
        });

        modelBuilder.Entity<BufferRequirement>(e =>
        {
            e.ToTable("BufferRequirements", schema: "requirement", t => t.HasCheckConstraint(
                "CK_Buf_Positive", "\"DurationMinutes\" > 0"));
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId);
        });

        modelBuilder.Entity<DependencyRequirement>(e =>
        {
            e.ToTable("DependencyRequirements", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId);
        });

        modelBuilder.Entity<RequirementConstraint>(e =>
        {
            e.ToTable("Constraints", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => x.WorkRequirementId);
        });

        modelBuilder.Entity<RequirementPreference>(e =>
        {
            e.ToTable("Preferences", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.Weight).HasColumnType("numeric(5,2)");
            e.HasIndex(x => x.WorkRequirementId);
        });

        modelBuilder.Entity<WorkRequirementIdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords", schema: "requirement");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.ResponseBodyJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<WorkRequirementOutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages", schema: "requirement");
            // No tenant query filter: the outbox relay runs outside any tenant HTTP request
            // and must see every tenant's pending messages (mirrors requirement-schema.sql,
            // which deliberately excludes OutboxMessages from the RLS table list).
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAt).HasFilter("\"ProcessedAt\" IS NULL");
        });
    }
}
