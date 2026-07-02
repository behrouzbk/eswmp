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
        modelBuilder.Entity<Reservation>().Property(r => r.ExternalReferenceType).HasMaxLength(100);
        modelBuilder.Entity<Reservation>().Property(r => r.ExternalReferenceId).HasMaxLength(200);
    }
}
