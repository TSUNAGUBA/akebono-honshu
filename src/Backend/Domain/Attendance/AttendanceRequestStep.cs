using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 申請時に凍結する承認経路スナップショット (attendance_request_steps)。直行/直帰申請・打刻修正申請で共通。
/// 申請作成時に有効な経路のステップをコピーして凍結し、以後の経路編集が進行中の申請に影響しないようにする
/// (稟議の routeSnapshot と同設計)。
///
/// <see cref="RequestKind"/> と <see cref="RequestId"/> の組で親申請を特定する
/// (direct_requests / attendance_fix_requests の 2 テーブルを跨ぐため FK は張らず soft reference とする。
///  申請と同一トランザクションで挿入する)。列の意味は <see cref="AttendanceRouteStep"/> と同一。
/// </summary>
public class AttendanceRequestStep : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>親申請の種別 (Direct=direct_requests / Fix=attendance_fix_requests)。</summary>
    public AttendanceRequestCategory RequestKind { get; set; }

    /// <summary>親申請の id (soft reference)。</summary>
    public Guid RequestId { get; set; }

    public int StepOrder { get; set; }

    public ApproverType ApproverType { get; set; }
    public ApproverRole? ApproverRole { get; set; }
    public string? ApproverTitle { get; set; }
    public Guid? ApproverUserId { get; set; }
    public ApprovalMode Mode { get; set; }

    public DateTime CreatedAt { get; set; }
}
