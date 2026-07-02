using Eswmp.Rules.Models;
using Eswmp.Shared.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Rules.Data;

public class RulesDbContext(DbContextOptions<RulesDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();
    public DbSet<WorkflowTransitionLog> WorkflowTransitionLogs => Set<WorkflowTransitionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BusinessRule>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<WorkflowTransitionLog>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<BusinessRule>().Property(r => r.DefinitionJson).HasColumnType("jsonb");
    }
}
