using System.Text.Json;
using Eswmp.Work.Data;
using Eswmp.Work.Models;

namespace Eswmp.Work.Services;

/// <summary>
/// Stages a requirement.OutboxMessages row on the current unit of work (api spec §11.3:
/// "state change + outbox row commit together"). Callers still own calling SaveChangesAsync —
/// this only adds the row to the same DbContext instance so it lands in the same transaction.
/// </summary>
public interface IOutboxPublisher
{
    void Enqueue(WorkDbContext db, Guid tenantId, string eventType, string aggregateType, Guid aggregateId, object payload, string? correlationId = null);
}

public class OutboxPublisher : IOutboxPublisher
{
    public void Enqueue(WorkDbContext db, Guid tenantId, string eventType, string aggregateType, Guid aggregateId, object payload, string? correlationId = null)
    {
        db.WorkRequirementOutboxMessages.Add(new WorkRequirementOutboxMessage
        {
            TenantId = tenantId,
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            PayloadJson = JsonSerializer.Serialize(payload),
            CorrelationId = correlationId,
        });
    }
}
