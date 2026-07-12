using Xunit;

namespace Eswmp.Work.IntegrationTests;

/// <summary>
/// All Work integration test classes share a single WorkApiFactory (one Postgres
/// container, one app host) instead of one-per-class — see CoreApiCollection for why
/// (a second Testcontainers container in the same process was observed to hang on this
/// machine's Docker Desktop setup). Sharing is safe because every test uses a fresh
/// random TenantId, and WorkDbContext's tenant query filter isolates them from each
/// other in the same database.
/// </summary>
[CollectionDefinition(Name)]
public class WorkApiCollection : ICollectionFixture<WorkApiFactory>
{
    public const string Name = "Work API";
}
