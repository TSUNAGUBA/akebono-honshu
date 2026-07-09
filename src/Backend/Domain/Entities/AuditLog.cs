namespace Akebono.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>テナント。認証拒否イベント等テナント確定前の記録は NULL (RLS 適用除外テーブル)。</summary>
    public Guid? TenantId { get; set; }

    public DateTime OccurredAt { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public short Result { get; set; }
    public string? Note { get; set; }
}

public enum AuditResult : short
{
    Success = 0,
    Failure = 1,
}
