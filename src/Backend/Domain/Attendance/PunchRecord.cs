using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 打刻 (punch_records)。**記録系・追記のみ**のテーブル。
///
/// 保護規約 (CLAUDE.md 原則 2 / 監査):
///   - UPDATE / DELETE しない。訂正は打刻修正申請の承認による**論理置換**で行う
///     (<see cref="Source"/> = <see cref="PunchSource.Fix"/> の行を追記し、
///      <see cref="FixedFrom"/> に置換対象の旧打刻の <see cref="At"/> を入れる)。
///   - そのため <c>updated_at</c> 列を持たない (09-updated-at-triggers.sql の対象外)。
///   - 有効打刻の解決は <see cref="AttendanceCalc.EffectivePunches"/>。
///
/// 時刻の扱い (SystemTime.cs が SoT):
///   - <see cref="At"/> は TIMESTAMPTZ / DateTime.Kind=Utc。採番は <c>SystemTime.UtcNow</c>。
///   - <see cref="Date"/> は **JST の業務日付**。採番は <c>SystemTime.TodayJst</c>。
///   - 深夜帯判定・時刻表示は必ず <c>SystemTime.ToJst</c> を通す (UTC の Hour を直接使わない)。
/// </summary>
public class PunchRecord : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>打刻者 (users.id)。</summary>
    public Guid UserId { get; set; }

    /// <summary>業務日付 (JST)。集計はこの日付で束ねる。</summary>
    public DateOnly Date { get; set; }

    public PunchKind Kind { get; set; }

    /// <summary>打刻時刻 (UTC 格納)。表示・深夜帯判定は SystemTime.ToJst で JST 化する。</summary>
    public DateTime At { get; set; }

    public PunchSource Source { get; set; }

    /// <summary>置換した旧打刻の <see cref="At"/> (UTC)。NULL = 通常打刻。</summary>
    public DateTime? FixedFrom { get; set; }

    /// <summary>修正理由 (客観的記録の担保。修正打刻のみ)。</summary>
    public string? FixReason { get; set; }

    /// <summary>修正を承認したオーナー (users.id)。修正打刻のみ。</summary>
    public Guid? ApprovedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
