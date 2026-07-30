using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 勤怠承認経路のステップ定義 (attendance_route_steps)。承認者を 役職 / ロール / 個人 で指定する。
/// <see cref="ApproverType"/> により有効なフィールドが変わる:
///   - Title:  <see cref="ApproverTitle"/> (users.title に一致する在籍者)
///   - Role:   <see cref="ApproverRole"/>  (honshu の権限ロール)
///   - Member: <see cref="ApproverUserId"/> (個人指定の users.id)
/// 承認者の解決は <see cref="ApproverResolver.Pick"/> に一本化する。
/// </summary>
public class AttendanceRouteStep : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>所属する経路 (attendance_routes.id)。</summary>
    public Guid RouteId { get; set; }

    /// <summary>ステップ順 (1..n)。経路内で一意。</summary>
    public int StepOrder { get; set; }

    public ApproverType ApproverType { get; set; }

    /// <summary>ロール指定 (ApproverType=Role のときのみ有効)。それ以外は null。</summary>
    public ApproverRole? ApproverRole { get; set; }

    /// <summary>役職ラベル (ApproverType=Title のときのみ有効)。それ以外は null。</summary>
    public string? ApproverTitle { get; set; }

    /// <summary>個人指定の承認者 (ApproverType=Member のときのみ有効。users.id)。それ以外は null。</summary>
    public Guid? ApproverUserId { get; set; }

    public ApprovalMode Mode { get; set; }

    public DateTime CreatedAt { get; set; }
}
