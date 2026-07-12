using Xunit;

namespace Eswmp.Core.IntegrationTests;

/// <summary>
/// All Core integration test classes share a single CoreApiFactory (one Postgres
/// container, one app host) instead of one-per-class — spinning up a second
/// Testcontainers container in the same process was observed to hang indefinitely
/// on this machine's Docker Desktop (Windows npipe) setup. Sharing is safe because
/// every test uses a fresh random TenantId, and CoreDbContext's tenant query filter
/// isolates them from each other in the same database.
/// </summary>
[CollectionDefinition(Name)]
public class CoreApiCollection : ICollectionFixture<CoreApiFactory>
{
    public const string Name = "Core API";
}
