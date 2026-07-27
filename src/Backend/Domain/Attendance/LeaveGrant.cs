using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 休暇の付与 (leave_grants)。個別付与 / 一括付与 / 周期自動付与のいずれも 1 行として追記する。
///
/// 冪等性 (CLAUDE.md 原則 2): DB 側の
/// UNIQUE (tenant_id, user_id, leave_type_id, grant_date) が二重付与を防ぐ。
/// 付与処理は**挿入のみ**で、既存付与を更新・削除しない (重複は skipped として件数だけ返す)。
/// </summary>
public class LeaveGrant : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>付与対象 (users.id)。</summary>
    public Guid UserId { get; set; }

    /// <summary>休暇種別 (leave_types.id)。</summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>付与日 (JST の業務日付)。冪等キーの一部。</summary>
    public DateOnly GrantDate { get; set; }

    /// <summary>付与日数 (0.5 刻み)。CHECK (days &gt; 0)。</summary>
    public decimal Days { get; set; }

    public LeaveGrantKind Kind { get; set; } = LeaveGrantKind.Special;

    /// <summary>失効日 (この日以降は引当不可)。無期限は 9999-12-31。</summary>
    public DateOnly ExpireDate { get; set; }

    /// <summary>付与操作をしたオーナー (users.id)。NULL = 周期自動付与。</summary>
    public Guid? GrantedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
