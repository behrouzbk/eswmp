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

    // Work Requirement module
    public DbSet<WorkRequirement> WorkRequirements => Set<WorkRequirement>();
    public DbSet<RequirementVersion> RequirementVersions => Set<RequirementVersion>();
    public DbSet<RequirementSnapshot> RequirementSnapshots => Set<RequirementSnapshot>();
    public DbSet<ResourceRequirement> ResourceRequirements => Set<ResourceRequirement>();
    public DbSet<CapabilityRequirement> CapabilityRequirements => Set<CapabilityRequirement>();
    public DbSet<SkillRequirement> SkillRequirements => Set<SkillRequirement>();
    public DbSet<CertificationRequirement> CertificationRequirements => Set<CertificationRequirement>();
    public DbSet<LocationConstraint> LocationConstraints => Set<LocationConstraint>();

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

        // ── Work Requirement module — schema "requirements" ─────────────
        modelBuilder.Entity<WorkRequirement>(e =>
        {
            e.ToTable("WorkRequirements", schema: "requirements");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            e.HasMany(x => x.Versions)
                .WithOne()
                .HasForeignKey(x => x.WorkRequirementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementVersion>(e =>
        {
            e.ToTable("RequirementVersions", schema: "requirements");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.HasIndex(x => new { x.TenantId, x.VersionNumber, x.WorkRequirementId }).IsUnique();

            e.HasMany(x => x.ResourceRequirements)
                .WithOne()
                .HasForeignKey(x => x.RequirementVersionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.LocationConstraints)
                .WithOne()
                .HasForeignKey(x => x.RequirementVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementSnapshot>(e =>
        {
            e.ToTable("RequirementSnapshots", schema: "requirements");
            e.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ResourceRequirement>(e =>
        {
            e.ToTable("ResourceRequirements", schema: "requirements");

            e.HasMany(x => x.CapabilityRequirements)
                .WithOne()
                .HasForeignKey(x => x.ResourceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.SkillRequirements)
                .WithOne()
                .HasForeignKey(x => x.ResourceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.CertificationRequirements)
                .WithOne()
                .HasForeignKey(x => x.ResourceRequirementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CapabilityRequirement>(e =>
        {
            e.ToTable("CapabilityRequirements", schema: "requirements");
        });

        modelBuilder.Entity<SkillRequirement>(e =>
        {
            e.ToTable("SkillRequirements", schema: "requirements");
        });

        modelBuilder.Entity<CertificationRequirement>(e =>
        {
            e.ToTable("CertificationRequirements", schema: "requirements");
        });

        modelBuilder.Entity<LocationConstraint>(e =>
        {
            e.ToTable("LocationConstraints", schema: "requirements");
        });
    }
}
