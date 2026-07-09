namespace Akebono.Application.Common;

public interface IAuditLogger
{
    Task LogAsync(
        Guid? actorUserId,
        string action,
        string? entityType = null,
        Guid? entityId = null,
        bool success = true,
        string? note = null,
        CancellationToken cancellationToken = default);
}
