using Eswmp.Assignment.Models;
using Eswmp.Shared.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Assignment.Data;

public class AssignmentDbContext(DbContextOptions<AssignmentDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<AssignmentLog> AssignmentLogs => Set<AssignmentLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AssignmentLog>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
    }
}
