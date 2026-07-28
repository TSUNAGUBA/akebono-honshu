using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 打刻修正申請 (attendance_fix_requests)。申請対象は常に本人の打刻。
///
/// 承認するとオーナーが <see cref="PunchRecord"/> を
/// <c>Source=Fix / At=RequestedAt / FixedFrom=旧打刻の At</c> で**追記**する
/// (元打刻は削除しない)。二重承認は
/// 「トランザクション内で <see cref="Status"/> を再確認 → Pending 以外なら 409」で防ぐ。
/// </summary>
public class AttendanceFixRequest : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>申請者 = 修正対象の打刻者 (users.id)。</summary>
    public Guid UserId { get; set; }

    /// <summary>修正対象の業務日付 (JST)。</summary>
    public DateOnly Date { get; set; }

    /// <summary>修正対象の打刻種別。</summary>
    public PunchKind Kind { get; set; }

    /// <summary>
    /// 修正対象の打刻 (punch_records.id)。同種の打刻が複数ある日 (休憩を複数回とった日など) で
    /// 「どれを直すか」を指定するために持つ (C-2)。NULL のとき (旧データ・単一打刻の日) は
    /// 承認時に「同種の先頭 1 件」へフォールバックする (下位互換)。FK は張らず soft reference と
    /// する (punch_records は追記のみで id は安定。作成時に有効打刻であることを検証する)。
    /// </summary>
    public Guid? TargetPunchId { get; set; }

    /// <summary>修正後の打刻時刻 (UTC 格納)。</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>修正理由 (必須。客観的記録の担保)。</summary>
    public string Reason { get; set; } = string.Empty;

    public FixRequestStatus Status { get; set; }

    /// <summary>承認/却下したオーナー (users.id)。未処理なら NULL。</summary>
    public Guid? DecidedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
