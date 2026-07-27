using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 休暇種別マスタ (leave_types)。有給休暇 (法定) と特別休暇 (任意) を扱う。
///
/// <see cref="IsStatutory"/> = true の種別 (法定有給) は改竄防止のため
/// 作成・編集・論理削除を禁止する (409)。PATCH では <see cref="IsStatutory"/> を受け取らない。
/// 残数の繰越上限 40 日も法定有給のみに適用する (<see cref="LeaveCalc.Balance"/>)。
/// </summary>
public class LeaveType : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>付与方式。Periodic は周期自動付与の対象で手動付与できない。</summary>
    public LeaveGrantMethod GrantMethod { get; set; } = LeaveGrantMethod.Manual;

    /// <summary>失効までの月数 (1〜120)。NULL = 無期限 (失効日は 9999-12-31 とする)。</summary>
    public int? ExpiryMonths { get; set; }

    /// <summary>法定 (労基法 39 条の年次有給休暇) か。true の種別は変更不可。</summary>
    public bool IsStatutory { get; set; }

    public string Description { get; set; } = string.Empty;

    public int DisplayOrder { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    /// <summary>論理削除 (第二段階規約: deleted_at TIMESTAMPTZ NULL に統一)。</summary>
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
