using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 直行/直帰申請 (direct_requests)。承認された日について打刻修正 (出勤/退勤) を申請できるようになる
/// (AttendanceService の AKO-ATT-005 ゲート)。区分 <see cref="AttendanceRequestCategory.Direct"/> の経路で
/// 多段承認する。経路未設定時は管理者単段 (凍結スナップショット 0 件)。
///
/// 進行中の経路は申請時に <see cref="AttendanceRequestStep"/> (request_kind=Direct) へ凍結する
/// (以後の経路編集は進行中の申請に影響しない = 稟議と同設計)。
/// </summary>
public class DirectRequest : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>申請者 (users.id)。</summary>
    public Guid UserId { get; set; }

    /// <summary>対象の業務日付 (JST)。</summary>
    public DateOnly Date { get; set; }

    public DirectType Type { get; set; }

    /// <summary>申請理由 (必須。客観的記録の担保)。</summary>
    public string Reason { get; set; } = string.Empty;

    public DirectRequestStatus Status { get; set; }

    /// <summary>経路承認の現在ステップ (1..n)。経路未設定時は 1 (管理者単段フォールバック)。</summary>
    public int CurrentStep { get; set; } = 1;

    /// <summary>最終承認/却下したユーザ (users.id)。未処理・取下げなら NULL。</summary>
    public Guid? DecidedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
