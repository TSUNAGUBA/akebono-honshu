using Akebono.Application.Common;
using Akebono.Domain.Entities;
using Akebono.Infrastructure.Persistence;

namespace Akebono.Infrastructure.Audit;

public class AuditLogger(AkebonoDbContext db) : IAuditLogger
{
    public async Task LogAsync(
        long? actorUserId,
        string action,
        string? entityType = null,
        long? entityId = null,
        bool success = true,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = DateTime.UtcNow,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Result = (short)(success ? AuditResult.Success : AuditResult.Failure),
            Note = note,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
