namespace Akebono.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public short Result { get; set; }
    public string? Note { get; set; }
}

public enum AuditResult : short
{
    Success = 0,
    Failure = 1,
}
