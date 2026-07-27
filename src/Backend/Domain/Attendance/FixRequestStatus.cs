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
}
