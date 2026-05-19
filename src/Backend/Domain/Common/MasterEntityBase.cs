namespace Akebono.Domain.Common;

/// <summary>17 マスタ共通基底。Phase 5 §3 共通基底 (id / code / name / delete_flag / 監査列 / legacy_id)。</summary>
public abstract class MasterEntityBase : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool DeleteFlag { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }
}
