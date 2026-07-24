using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Consumers;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using Eswmp.Work.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Eswmp.Work.Tests;

/// <summary>v2 delta — the failure path that didn't exist: every early-return branch in
/// DemandAcceptedConsumer must now flag the demand NeedsAttention instead of only logging.</summary>
public class DemandAcceptedConsumerTests
{
    private static WorkDbContext NewDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkDbContext(options, new TenantContext { TenantId = tenantId });
    }

    private static Mock<ConsumeContext<DemandAcceptedEvent>> NewContext(DemandAcceptedEvent evt)
    {
        var mock = new Mock<ConsumeContext<DemandAcceptedEvent>>();
        mock.SetupGet(c => c.Message).Returns(evt);
        mock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return mock;
    }

    [Fact]
    public async Task Consume_NoActiveTemplate_FlagsDemandNeedsAttention()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "NO_SUCH_TEMPLATE",
            SourceSystem = "test",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.Accepted,
        };
        db.Demands.Add(demand);
        await db.SaveChangesAsync();

        var tenantContext = new TenantContext { TenantId = tenantId };
        var linkService = new DemandRequirementLinkService(db, Mock.Of<IPublishEndpoint>());
        var consumer = new DemandAcceptedConsumer(db, tenantContext, Mock.Of<IOutboxPublisher>(), linkService, Mock.Of<ILogger<DemandAcceptedConsumer>>());
        var evt = new DemandAcceptedEvent(demand.Id, tenantId, Guid.NewGuid());

        await consumer.Consume(NewContext(evt).Object);

        var reloaded = await db.Demands.FindAsync(demand.Id);
        Assert.Equal(DemandStatus.NeedsAttention, reloaded!.Status);
        Assert.Equal("TEMPLATE_NOT_ACTIVE", reloaded.AttentionReason);
        Assert.Equal(1, reloaded.ResolutionAttempts);
    }

    [Fact]
    public async Task Consume_SuccessfulRetryAfterFailure_ClearsNeedsAttention()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(tenantId);
        var demand = new Demand
        {
            TenantId = tenantId,
            DemandType = "DOG_WALK_STANDARD",
            SourceSystem = "test",
            ExternalReferenceType = "test",
            ExternalReferenceId = "1",
            Status = DemandStatus.NeedsAttention,
            AttentionReason = "TEMPLATE_NOT_ACTIVE",
            ResolutionAttempts = 1,
        };
        db.Demands.Add(demand);

        var template = new RequirementTemplate { TenantId = tenantId, Code = "DOG_WALK_STANDARD", Name = "n", WorkType = "DOG_WALKING", Status = TemplateStatus.Active, CurrentVersion = 1 };
        var version = new RequirementTemplateVersion
        {
            TenantId = tenantId,
            TemplateId = template.Id,
            Version = 1,
            Status = TemplateVersionStatus.Active,
            DefinitionJson = System.Text.Json.JsonSerializer.Serialize(new RequirementSetDto(
                ResourceRequirements: [new ResourceRoleRequirementDto("DRIVER", ResourceCategory.Person, MinimumQuantity: 1)],
                DurationRequirement: new DurationRequirementDto(DurationType.Fixed, EstimatedDurationMinutes: 30)),
                RequirementResolutionService.JsonOptions),
        };
        db.RequirementTemplates.Add(template);
        db.RequirementTemplateVersions.Add(version);
        await db.SaveChangesAsync();

        var tenantContext = new TenantContext { TenantId = tenantId };
        var linkService = new DemandRequirementLinkService(db, Mock.Of<IPublishEndpoint>());
        var consumer = new DemandAcceptedConsumer(db, tenantContext, Mock.Of<IOutboxPublisher>(), linkService, Mock.Of<ILogger<DemandAcceptedConsumer>>());
        var evt = new DemandAcceptedEvent(demand.Id, tenantId, Guid.NewGuid());

        await consumer.Consume(NewContext(evt).Object);

        var reloaded = await db.Demands.FindAsync(demand.Id);
        Assert.Equal(DemandStatus.Accepted, reloaded!.Status);
        Assert.Null(reloaded.AttentionReason);
        Assert.NotNull(reloaded.RequirementReferenceId);
    }
}
