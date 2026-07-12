using Eswmp.Core.Models;
using Eswmp.Shared.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Data;

public class CoreDbContext(DbContextOptions<CoreDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<DurationSizeBracket> DurationSizeBrackets => Set<DurationSizeBracket>();
    public DbSet<DurationTagRule> DurationTagRules => Set<DurationTagRule>();

    // Resource module (schema: resource)
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();
    public DbSet<ResourceCapability> ResourceCapabilities => Set<ResourceCapability>();
    public DbSet<ResourceSkill> ResourceSkills => Set<ResourceSkill>();
    public DbSet<ResourceCertification> ResourceCertifications => Set<ResourceCertification>();

    // Availability module (schema: availability)
    public DbSet<AvailabilityProfile> AvailabilityProfiles => Set<AvailabilityProfile>();
    public DbSet<TimeOff> TimeOffs => Set<TimeOff>();
    public DbSet<AvailabilityOverride> AvailabilityOverrides => Set<AvailabilityOverride>();

    // Capacity module (schema: capacity)
    public DbSet<CapacityProfile> CapacityProfiles => Set<CapacityProfile>();
    public DbSet<CapacityDefinition> CapacityDefinitions => Set<CapacityDefinition>();
    public DbSet<CapacityHold> CapacityHolds => Set<CapacityHold>();
    public DbSet<CapacityConsumption> CapacityConsumptions => Set<CapacityConsumption>();
    public DbSet<CapacityOverride> CapacityOverrides => Set<CapacityOverride>();
    public DbSet<CapacityLedgerEntry> CapacityLedgerEntries => Set<CapacityLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Multi-tenant isolation — see CLAUDE.md rule 7.
        // No manual .Where(e => e.TenantId == ...) filters anywhere else in the codebase.
        modelBuilder.Entity<Resource>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<AvailabilityRule>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<AvailabilityException>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Reservation>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Appointment>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<DurationSizeBracket>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<DurationTagRule>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Resource>().Property(r => r.ResourceType).HasMaxLength(100);
        modelBuilder.Entity<Resource>().Property(r => r.Version).IsConcurrencyToken();
        modelBuilder.Entity<Reservation>().Property(r => r.ExternalReferenceType).HasMaxLength(100);
        modelBuilder.Entity<Reservation>().Property(r => r.ExternalReferenceId).HasMaxLength(200);

        // Supports the overlap-conflict lookup in ReservationsController.Create (CO-12).
        modelBuilder.Entity<Reservation>().HasIndex(r => new { r.ResourceId, r.StartTime, r.EndTime });

        ConfigureResourceModule(modelBuilder);
        ConfigureAvailabilityModule(modelBuilder);
        ConfigureCapacityModule(modelBuilder);
    }

    private void ConfigureResourceModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResourceType>(b =>
        {
            b.ToTable("ResourceTypes", schema: "resource");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.Code).HasMaxLength(100);
            b.Property(e => e.Name).HasMaxLength(200);
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<ResourceCapability>(b =>
        {
            b.ToTable("ResourceCapabilities", schema: "resource");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.CapabilityCode).HasMaxLength(100);
            b.HasIndex(e => e.ResourceId);
        });

        modelBuilder.Entity<ResourceSkill>(b =>
        {
            b.ToTable("ResourceSkills", schema: "resource");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.SkillCode).HasMaxLength(100);
            b.Property(e => e.YearsOfExperience).HasColumnType("numeric(6,2)");
            b.HasIndex(e => e.ResourceId);
        });

        modelBuilder.Entity<ResourceCertification>(b =>
        {
            b.ToTable("ResourceCertifications", schema: "resource");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.CertificationTypeCode).HasMaxLength(100);
            b.Property(e => e.CredentialReference).HasMaxLength(200);
            b.HasIndex(e => e.ResourceId);
        });
    }

    private void ConfigureAvailabilityModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AvailabilityProfile>(b =>
        {
            b.ToTable("AvailabilityProfiles", schema: "availability");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.Name).HasMaxLength(200);
            b.Property(e => e.Timezone).HasMaxLength(100);
            b.HasIndex(e => e.ResourceId).IsUnique();
        });

        modelBuilder.Entity<AvailabilityRule>(b =>
        {
            b.Property(e => e.RecurrencePattern).HasColumnType("jsonb");
            b.Property(e => e.Source).HasMaxLength(100);
            b.HasIndex(e => e.AvailabilityProfileId);
        });

        modelBuilder.Entity<TimeOff>(b =>
        {
            b.ToTable("TimeOffs", schema: "availability");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.Type).HasMaxLength(100);
            b.Property(e => e.ReasonCode).HasMaxLength(100);
            b.HasIndex(e => new { e.ResourceId, e.StartTime, e.EndTime });
        });

        modelBuilder.Entity<AvailabilityOverride>(b =>
        {
            b.ToTable("AvailabilityOverrides", schema: "availability");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.ReasonCode).HasMaxLength(100);
            b.HasIndex(e => new { e.ResourceId, e.StartTime, e.EndTime });
        });
    }

    private void ConfigureCapacityModule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CapacityProfile>(b =>
        {
            b.ToTable("CapacityProfiles", schema: "capacity");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.Name).HasMaxLength(200);
            b.Property(e => e.Timezone).HasMaxLength(100);
            b.Property(e => e.Version).IsConcurrencyToken();
            b.HasIndex(e => e.ResourceId);
        });

        modelBuilder.Entity<CapacityDefinition>(b =>
        {
            b.ToTable("CapacityDefinitions", schema: "capacity");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.Name).HasMaxLength(200);
            b.Property(e => e.DimensionCode).HasMaxLength(100);
            b.Property(e => e.Version).IsConcurrencyToken();
            b.HasIndex(e => e.CapacityProfileId);
            b.HasIndex(e => new { e.CapacityProfileId, e.DimensionCode });
        });

        modelBuilder.Entity<CapacityHold>(b =>
        {
            b.ToTable("CapacityHolds", schema: "capacity");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.DimensionCode).HasMaxLength(100);
            b.Property(e => e.IdempotencyKey).HasMaxLength(200);
            b.Property(e => e.SourceType).HasMaxLength(100);
            b.Property(e => e.SourceId).HasMaxLength(200);
            b.Property(e => e.Version).IsConcurrencyToken();
            // Supports the "sum active holds/consumptions overlapping the window" resolve query.
            b.HasIndex(e => new { e.CapacityDefinitionId, e.StartTime, e.EndTime });
            b.HasIndex(e => new { e.TenantId, e.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<CapacityConsumption>(b =>
        {
            b.ToTable("CapacityConsumptions", schema: "capacity");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.DimensionCode).HasMaxLength(100);
            b.Property(e => e.SourceType).HasMaxLength(100);
            b.Property(e => e.SourceId).HasMaxLength(200);
            b.Property(e => e.Version).IsConcurrencyToken();
            b.HasIndex(e => new { e.CapacityDefinitionId, e.StartTime, e.EndTime });
        });

        modelBuilder.Entity<CapacityOverride>(b =>
        {
            b.ToTable("CapacityOverrides", schema: "capacity");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.OverrideType).HasMaxLength(100);
            b.Property(e => e.ReasonCode).HasMaxLength(100);
            b.HasIndex(e => new { e.CapacityDefinitionId, e.StartTime, e.EndTime });
        });

        modelBuilder.Entity<CapacityLedgerEntry>(b =>
        {
            b.ToTable("CapacityLedgerEntries", schema: "capacity");
            b.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            b.Property(e => e.DimensionCode).HasMaxLength(100);
            b.HasIndex(e => e.CapacityDefinitionId);
            b.HasIndex(e => e.CorrelationId);
        });
    }
}
