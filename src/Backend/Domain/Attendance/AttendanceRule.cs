using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 勤務体系マスタ (attendance_rules)。所定労働時間・休憩・フレックス・締め日・法定休日曜日を保持する。
///
/// ルール解決は「既定ルール方式」(<see cref="AttendanceCalc.ResolveRule"/>):
///   1. users.attendance_rule_id が指す有効ルール
///   2. 有効かつ既定 (<see cref="IsDefault"/>) のルール
///   3. 一覧の先頭 (ORDER BY name)
/// いずれも無ければ null (所定 480 分・法定休日=日曜 で扱う)。
///
/// <see cref="IsDefault"/> はテナント内で高々 1 件。排他制御は Service 側で行う
/// (既定に設定したら同テナントの他ルールの既定を落とす)。
/// </summary>
public class AttendanceRule : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>始業時刻 'HH:mm' (壁時計。JST)。</summary>
    public string WorkStart { get; set; } = "09:00";

    /// <summary>終業時刻 'HH:mm' (壁時計。JST)。</summary>
    public string WorkEnd { get; set; } = "18:00";

    /// <summary>所定休憩時間 (分)。0〜240。</summary>
    public int BreakMinutes { get; set; } = 60;

    /// <summary>フレックスタイム制。office の flex JSON を平坦化した (jsonb を使わない)。</summary>
    public bool FlexEnabled { get; set; }

    /// <summary>コアタイム開始 'HH:mm' (フレックス時のみ)。</summary>
    public string? FlexCoreStart { get; set; }

    /// <summary>コアタイム終了 'HH:mm' (フレックス時のみ)。</summary>
    public string? FlexCoreEnd { get; set; }

    /// <summary>清算期間 (月)。1〜3。</summary>
    public int FlexSettlementMonths { get; set; } = 1;

    /// <summary>締め日。31 = 月末。1〜31。</summary>
    public int ClosingDay { get; set; } = 31;

    /// <summary>法定休日の曜日 (0=日曜 〜 6=土曜)。<see cref="DayOfWeek"/> の数値と一致。</summary>
    public int LegalHolidayWeekday { get; set; }

    /// <summary>既定ルール。テナント内で高々 1 件 (Service で排他制御)。</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>論理削除 (第二段階規約: deleted_at TIMESTAMPTZ NULL に統一)。</summary>
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
