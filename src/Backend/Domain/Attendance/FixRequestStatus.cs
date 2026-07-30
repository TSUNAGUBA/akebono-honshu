namespace Akebono.Domain.Attendance;

/// <summary>打刻修正申請のステータス (attendance_fix_requests.status)。</summary>
public enum FixRequestStatus : short
{
    /// <summary>承認待ち</summary>
    Pending = 0,
    /// <summary>承認済み</summary>
    Approved = 1,
    /// <summary>却下</summary>
    Rejected = 2,

    /// <summary>
    /// 承認中 (経路の途中ステップ / Iteration 33 で追加)。
    /// 既存値 (0..2) を変えないよう末尾に採番する (下位互換 = 原則7)。
    /// 直行/直帰申請 (<see cref="DirectRequestStatus"/>) と異なり打刻修正申請に取下げ (withdrawn) は無い。
    /// </summary>
    InReview = 3,
}
