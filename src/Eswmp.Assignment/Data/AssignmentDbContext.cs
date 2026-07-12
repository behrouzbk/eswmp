using Eswmp.Assignment.Models;
using Eswmp.Shared.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Assignment.Data;

public class AssignmentDbContext(DbContextOptions<AssignmentDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<AssignmentLog> AssignmentLogs => Set<AssignmentLog>();

    // Matching module (schema "matching") — additive, parallel to AssignmentLog/AssignmentScorer above.
    // See CLAUDE.md rule 11: one module's code must never write another module's tables directly;
    // this DbContext only hosts both because they share the eswmp_assignment database, not because
    // they're the same module.
    public DbSet<MatchEvaluation> MatchEvaluations => Set<MatchEvaluation>();
    public DbSet<CandidateMatchResult> CandidateMatchResults => Set<CandidateMatchResult>();
    public DbSet<MatchFactorEvaluation> MatchFactorEvaluations => Set<MatchFactorEvaluation>();
    public DbSet<MatchingPolicy> MatchingPolicies => Set<MatchingPolicy>();
    public DbSet<MatchingPolicyVersion> MatchingPolicyVersions => Set<MatchingPolicyVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AssignmentLog>().HasQueryFilter(e => e.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<MatchEvaluation>(builder =>
        {
            builder.ToTable("MatchEvaluations", schema: "matching");
            builder.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            builder.HasMany(e => e.Results)
                .WithOne(r => r.MatchEvaluation)
                .HasForeignKey(r => r.MatchEvaluationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CandidateMatchResult>(builder =>
        {
            builder.ToTable("CandidateMatchResults", schema: "matching");
            builder.HasMany(r => r.Factors)
                .WithOne(f => f.CandidateMatchResult)
                .HasForeignKey(f => f.CandidateMatchResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchFactorEvaluation>(builder =>
        {
            builder.ToTable("MatchFactorEvaluations", schema: "matching");
        });

        modelBuilder.Entity<MatchingPolicy>(builder =>
        {
            builder.ToTable("MatchingPolicies", schema: "matching");
            builder.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
            builder.HasMany(p => p.Versions)
                .WithOne(v => v.MatchingPolicy)
                .HasForeignKey(v => v.MatchingPolicyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchingPolicyVersion>(builder =>
        {
            builder.ToTable("MatchingPolicyVersions", schema: "matching");
            builder.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
            builder.HasIndex(e => new { e.MatchingPolicyId, e.VersionNumber }).IsUnique();
        });
    }
}
