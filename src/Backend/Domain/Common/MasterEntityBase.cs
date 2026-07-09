namespace Akebono.Domain.Common;

/// <summary>17 マスタ共通基底。Phase 5 §3 共通基底 (id / code / name / deleted_at / 監査列 / legacy_id)。第二段階規約: 論理削除は deleted_at (TIMESTAMPTZ NULL) に統一。</summary>
public abstract class MasterEntityBase : IMasterEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }
}
