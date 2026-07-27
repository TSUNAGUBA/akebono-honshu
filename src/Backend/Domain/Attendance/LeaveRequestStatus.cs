namespace Akebono.Domain.Attendance;

/// <summary>休暇申請のステータス (leave_requests.status)。残数の消化に数えるのは Approved のみ。</summary>
public enum LeaveRequestStatus : short
{
    /// <summary>承認待ち</summary>
    Pending = 0,
    /// <summary>承認済み (残数を消化する)</summary>
    Approved = 1,
    /// <summary>却下</summary>
    Rejected = 2,
}
