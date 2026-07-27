using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 休暇申請 (leave_requests)。1 行 = 1 日分の取得 (全日 or 半日)。
/// 残数の消化に数えるのは <see cref="LeaveRequestStatus.Approved"/> のみ
/// (引当は <see cref="LeaveCalc.Balance"/> の FIFO)。
/// </summary>
public class LeaveRequest : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>申請者 (users.id)。申請は常に本人。</summary>
    public Guid UserId { get; set; }

    /// <summary>休暇種別 (leave_types.id)。</summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>取得日 (JST の業務日付)。</summary>
    public DateOnly Date { get; set; }

    public LeaveUnit Unit { get; set; }

    public LeaveRequestStatus Status { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>承認/却下したオーナー (users.id)。未処理なら NULL。</summary>
    public Guid? DecidedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
