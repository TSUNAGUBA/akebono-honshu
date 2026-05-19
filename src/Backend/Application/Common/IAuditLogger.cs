namespace Akebono.Application.Common;

public interface IAuditLogger
{
    Task LogAsync(
        long? actorUserId,
        string action,
        string? entityType = null,
        long? entityId = null,
        bool success = true,
        string? note = null,
        CancellationToken cancellationToken = default);
}
