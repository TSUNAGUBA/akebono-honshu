using Akebono.Domain.Attendance;

namespace Akebono.Application.Attendance;

// ─────────────────────────────────────────────────────────────────────────────
// 勤怠 API (移植仕様 §5.1〜§5.3) の DTO。
//   - JSON は camelCase、enum は camelCase 文字列 (Program.cs の JsonStringEnumConverter)。
//   - 分は int (分単位)、業務日付は DateOnly ("YYYY-MM-DD")、時刻は DateTime(Kind=Utc)。
//   - 追加フィールドは必ず末尾へデフォルト値付きで足すこと (原則7 下位互換)。
//   - リクエスト側の日付・種別・アクションは string で受け取り、Service 側で
//     明示的に検証して 422 (AKB-SYS-002) を返す (JSON 変換失敗による 400 を避ける)。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>打刻 1 件 (生打刻・有効打刻の共通表現)。</summary>
public record PunchDto(
    Guid Id,
    PunchKind Kind,
    DateTime At,
    PunchSource Source,
    DateTime? FixedFrom = null,
    string? FixReason = null,
    Guid? ApprovedByUserId = null);

/// <summary>6 バケット分解 (分)。night は他バケットと独立 (重複カウント)。</summary>
public record BucketsDto(
    int Scheduled,
    int StatutoryOt,
    int NonStatutoryOt,
    int Over60Ot,
    int Night,
    int LegalHoliday);

/// <summary>
/// 日次サマリ。<see cref="RawPunches"/> は ?raw=1 指定時のみ非 null
/// (修正前打刻を含むタイムライン表示用)。
/// </summary>
public record DaySummaryDto(
    DateOnly Date,
    int WorkMinutes,
    int BreakMinutes,
    int NightMinutes,
    BucketsDto Buckets,
    int BreakShortage,
    IReadOnlyList<PunchDto> Punches,
    IReadOnlyList<PunchDto>? RawPunches = null);

/// <summary>月次サマリ (日別 + 6 バケット合計 + 出勤日数)。</summary>
public record MonthSummaryDto(
    IReadOnlyList<DaySummaryDto> Days,
    BucketsDto Total,
    int WorkDays);

/// <summary>36 協定アラート。Level は "warn" / "crit"、Code は AKB-ATT-* (HTTP エラーコードではない)。</summary>
public record Article36AlertDto(string Level, string Code, string Message);

/// <summary>全員のタイムカード 1 行 (利用者 × 業務日)。有効打刻が 0 件の日は行を出さない。</summary>
public record TimecardRowDto(
    Guid UserId,
    string UserName,
    DateOnly Date,
    DateTime? InAt,
    DateTime? OutAt,
    int WorkMinutes,
    int BreakMinutes);

/// <summary>打刻リクエスト。Kind は "in" / "out" / "breakStart" / "breakEnd"。</summary>
public record PunchRequest(string Kind);

/// <summary>打刻結果。State は「打刻後」の状態。</summary>
public record PunchResultDto(Guid Id, PunchState State);

/// <summary>打刻ウィジェット用の当日状態。Punches は生打刻列 (置換解決前)。</summary>
public record PunchStateDto(PunchState State, IReadOnlyList<PunchDto> Punches);

/// <summary>
/// 打刻修正申請の作成リクエスト。RequestedAt は JST オフセット付き
/// ("YYYY-MM-DDTHH:mm:00+09:00") で受け取り、サーバで UTC 化して保存する。
/// </summary>
public record FixRequestCreateRequest(string Date, string Kind, string RequestedAt, string Reason);

/// <summary>打刻修正申請。</summary>
public record FixRequestDto(
    Guid Id,
    Guid UserId,
    string UserName,
    DateOnly Date,
    PunchKind Kind,
    DateTime RequestedAt,
    string Reason,
    FixRequestStatus Status,
    Guid? DecidedByUserId,
    string? DecidedByUserName,
    DateTime CreatedAt);

/// <summary>承認/却下リクエスト。Action は "approved" / "rejected"。</summary>
public record FixDecisionRequest(string Action);

/// <summary>ID のみを返す最小レスポンス (作成・承認/却下)。</summary>
public record IdResultDto(Guid Id);

/// <summary>勤怠ルール (勤務体系マスタ)。</summary>
public record AttendanceRuleDto(
    Guid Id,
    string Name,
    string WorkStart,
    string WorkEnd,
    int BreakMinutes,
    bool FlexEnabled,
    string? FlexCoreStart,
    string? FlexCoreEnd,
    int FlexSettlementMonths,
    int ClosingDay,
    int LegalHolidayWeekday,
    bool IsDefault,
    bool IsActive,
    DateTime? DeletedAt);

/// <summary>勤怠ルールの新規作成ペイロード (全項目必須)。</summary>
public record AttendanceRuleWriteRequest(
    string Name,
    string WorkStart,
    string WorkEnd,
    int BreakMinutes,
    bool FlexEnabled,
    string? FlexCoreStart,
    string? FlexCoreEnd,
    int FlexSettlementMonths,
    int ClosingDay,
    int LegalHolidayWeekday,
    bool IsDefault,
    bool IsActive);

/// <summary>
/// 勤怠ルールの部分更新ペイロード。null のフィールドは「未指定 = 更新しない」。
/// (Zod .partial() の教訓と同趣旨: 送っていない項目を既定値で潰さない)
/// FlexCoreStart / FlexCoreEnd は本 I/F では null クリアできない (未指定と区別できないため)。
/// </summary>
public record AttendanceRulePatchRequest(
    string? Name = null,
    string? WorkStart = null,
    string? WorkEnd = null,
    int? BreakMinutes = null,
    bool? FlexEnabled = null,
    string? FlexCoreStart = null,
    string? FlexCoreEnd = null,
    int? FlexSettlementMonths = null,
    int? ClosingDay = null,
    int? LegalHolidayWeekday = null,
    bool? IsDefault = null,
    bool? IsActive = null);
