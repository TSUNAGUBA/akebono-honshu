using Akebono.Domain.Common;
using Akebono.Domain.Entities;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 勤怠計算の純粋関数群 (副作用なし・DB 非依存)。労基法 32/34/36/37 条に対応する。
///
/// 時刻の扱い (SystemTime.cs が SoT): 打刻時刻は UTC 格納のため、
/// **深夜帯 (22時〜5時) の判定は必ず <see cref="SystemTime.ToJst"/> を通す**。
/// UTC の <see cref="DateTime.Hour"/> を直接使うと実行環境 TZ に依存して壊れる。
/// </summary>
public static class AttendanceCalc
{
    // ---- 労基法の定数 (分) ----

    /// <summary>法定労働時間 / 日 (労基法 32 条) = 8h。</summary>
    public const int LegalDailyMinutes = 8 * 60;

    /// <summary>法定労働時間 / 週 (労基法 32 条) = 40h。</summary>
    public const int LegalWeeklyMinutes = 40 * 60;

    /// <summary>36 協定 原則の月上限 = 45h。</summary>
    public const int OtMonthlyLimitMinutes = 45 * 60;

    /// <summary>月 45h の 80% (警告閾値) = 36h。</summary>
    public const int OtMonthlyWarnMinutes = 36 * 60;

    /// <summary>特別条項の単月上限 (時間外 + 休日労働) = 100h。</summary>
    public const int OtMonthlyHardLimitMin = 100 * 60;

    /// <summary>2〜6 ヶ月平均の上限 (時間外 + 休日労働) = 80h。</summary>
    public const int OtAvgLimitMinutes = 80 * 60;

    /// <summary>月 45h 超は年 6 回まで (特別条項)。</summary>
    public const int OtOver45MaxCount = 6;

    /// <summary>割増 50% になる月間時間外の閾値 (労基法 37 条) = 60h。</summary>
    public const int Over60ThresholdMinutes = 60 * 60;

    /// <summary>深夜帯の開始時 (JST)。</summary>
    public const int NightStartHour = 22;

    /// <summary>深夜帯の終了時 (JST、この時を含まない)。</summary>
    public const int NightEndHour = 5;

    /// <summary>1 日の分数。深夜帯の周期計算に使う。</summary>
    private const int MinutesPerDay = 24 * 60;

    /// <summary>深夜帯の開始 (0 時からの分数) = 22:00。</summary>
    private const int NightStartMinute = NightStartHour * 60;

    /// <summary>深夜帯の終了 (0 時からの分数、この分を含まない) = 05:00。</summary>
    private const int NightEndMinute = NightEndHour * 60;

    /// <summary>1 周期 (24h) あたりの深夜分数 = 300 (0:00〜5:00) + 120 (22:00〜24:00) = 420。</summary>
    private const int NightMinutesPerDay = NightEndMinute + (MinutesPerDay - NightStartMinute);

    /// <summary>
    /// 業務日付として受け付ける下限。これより前の日付は誤入力とみなす
    /// (0001 年のような極端な日付を通すと集計・ループが非現実的な範囲まで広がるため)。
    /// </summary>
    public static readonly DateOnly MinBusinessDate = new(2000, 1, 1);

    /// <summary>
    /// 業務日付が妥当な範囲 (<see cref="MinBusinessDate"/> 〜 <paramref name="today"/> の 1 年後) か。
    /// 上限を当日基準にするため <paramref name="today"/> は呼出側が渡す (本クラスは時刻に依存しない)。
    /// 月の検証にも使う (その月の 1 日を渡す)。
    /// </summary>
    public static bool IsBusinessDateInRange(DateOnly value, DateOnly today)
        => value >= MinBusinessDate && value <= today.AddYears(1);

    // ---- 36 協定アラートの識別子 ----
    // HTTP エラーコードではなくアラート識別子のため AkbErrorCodes には追加しない。

    /// <summary>月 45h 超過 (crit)。</summary>
    public const string AlertCodeOver45 = "AKB-ATT-A45";
    /// <summary>月 45h の 80% 到達 (warn)。</summary>
    public const string AlertCodeOver45Warn = "AKB-ATT-A45W";
    /// <summary>単月 100h 到達 (crit)。</summary>
    public const string AlertCodeOver100 = "AKB-ATT-A100";
    /// <summary>直近 2〜6 ヶ月平均 80h 超 (crit)。</summary>
    public const string AlertCodeAvg80 = "AKB-ATT-A80";
    /// <summary>月 45h 超が年 6 回 (crit)。</summary>
    public const string AlertCodeOver45Count = "AKB-ATT-A6C";
    /// <summary>月 45h 超が年 5 回 (warn)。</summary>
    public const string AlertCodeOver45CountWarn = "AKB-ATT-A6CW";

    /// <summary>アラート水準: 警告。</summary>
    public const string AlertLevelWarn = "warn";
    /// <summary>アラート水準: 重大。</summary>
    public const string AlertLevelCrit = "crit";

    private static readonly IReadOnlyList<PunchRecord> EmptyPunches = Array.Empty<PunchRecord>();

    /// <summary>
    /// 修正打刻による論理置換を解決し、有効打刻のみを <c>At</c> 昇順で返す (§4.1)。
    ///
    /// 置換は**レコード Id 単位で 1 件だけ**無効化する。<c>At</c> でグルーピングすると
    /// 「元の時刻へ戻す差戻し」で <c>At</c> が再利用されたときに連鎖して破綻するため。
    /// <paramref name="rows"/> は追記順 (ORDER BY at, created_at) で渡すこと。
    /// </summary>
    public static IReadOnlyList<PunchRecord> EffectivePunches(IReadOnlyList<PunchRecord> rows)
    {
        var superseded = new HashSet<Guid>();
        foreach (var p in rows)
        {
            if (p.Source != PunchSource.Fix || p.FixedFrom == null) continue;

            // 自己置換 (At == FixedFrom) でも自分自身は候補から外す (q.Id != p.Id)。
            // 既に無効化済みの行は二重に潰さない (!superseded.Contains)。
            var target = rows.FirstOrDefault(q =>
                q.Id != p.Id
                && !superseded.Contains(q.Id)
                && q.Kind == p.Kind
                && q.At == p.FixedFrom.Value);
            if (target != null) superseded.Add(target.Id);
        }

        return rows.Where(r => !superseded.Contains(r.Id)).OrderBy(r => r.At).ToList();
    }

    /// <summary>
    /// 有効打刻から実労働・休憩・深夜の分数を求める (§4.2)。
    /// 二重 Out は無視する (<c>outSeen</c>)。丸めは分単位の四捨五入 (負は 0 クランプ)。
    /// </summary>
    public static (int WorkMinutes, int BreakMinutes, int NightMinutes) CalcWorkedMinutes(
        IReadOnlyList<PunchRecord> punches)
    {
        var ordered = punches.OrderBy(p => p.At).ToList();

        var work = 0;
        var brk = 0;
        var night = 0;
        DateTime? inAt = null;
        DateTime? breakStart = null;
        var outSeen = false;

        foreach (var p in ordered)
        {
            switch (p.Kind)
            {
                case PunchKind.In:
                    inAt = p.At;
                    outSeen = false;
                    break;

                case PunchKind.BreakStart:
                    if (inAt != null && breakStart == null)
                    {
                        var seg = DiffMinutes(inAt.Value, p.At);
                        work += seg;
                        night += NightOverlap(inAt.Value, seg);
                        breakStart = p.At;
                    }
                    break;

                case PunchKind.BreakEnd:
                    if (breakStart != null)
                    {
                        brk += DiffMinutes(breakStart.Value, p.At);
                        inAt = p.At;
                        breakStart = null;
                    }
                    break;

                case PunchKind.Out:
                    if (inAt != null && !outSeen)
                    {
                        // 休憩中の退勤は労働に加算しない (休憩開始以降は労働時間ではない)。
                        if (breakStart == null)
                        {
                            var seg = DiffMinutes(inAt.Value, p.At);
                            work += seg;
                            night += NightOverlap(inAt.Value, seg);
                        }
                        breakStart = null;
                        inAt = null;
                        outSeen = true;
                    }
                    break;
            }
        }

        return (work, brk, night);
    }

    /// <summary>
    /// <paramref name="atUtc"/> から <paramref name="durationMinutes"/> 分の区間のうち、
    /// 深夜帯 (JST 22:00〜翌 05:00) に重なる分数 (§4.3)。
    /// 実行環境 TZ に依存させないため必ず <see cref="SystemTime.ToJst"/> 経由で判定する。
    ///
    /// **計算量は O(1)**。1 分刻みで数えると、極端な打刻時刻 (誤登録された修正打刻等) で
    /// 区間長が数百日分になったとき CPU を占有するため、完全周期 (1440 分 = 深夜 420 分) を
    /// 掛け算で処理し、端数のみ累積関数 <see cref="NightMinutesBefore"/> で求める。
    /// 1 分刻み実装との結果一致は 開始 0〜1439 分 × 区間 0〜3000 分 の全数比較 +
    /// 境界 (0 / 1440 の倍数 / 2880 超 / 負値) で検証済み。
    /// </summary>
    public static int NightOverlap(DateTime atUtc, int durationMinutes)
    {
        if (durationMinutes <= 0) return 0;

        var jst = SystemTime.ToJst(atUtc);
        var startMin = jst.Hour * 60 + jst.Minute;

        // 完全周期は 1 周期あたり必ず NightMinutesPerDay 分。
        var count = durationMinutes / MinutesPerDay * NightMinutesPerDay;

        // 端数 [startMin, startMin + rest) を日跨ぎで分割して数える (startMin < 1440、rest < 1440)。
        var rest = durationMinutes % MinutesPerDay;
        var end = startMin + rest;
        count += NightMinutesBefore(Math.Min(end, MinutesPerDay)) - NightMinutesBefore(startMin);
        if (end > MinutesPerDay) count += NightMinutesBefore(end - MinutesPerDay);

        return count;
    }

    /// <summary>
    /// 0 時から <paramref name="minuteOfDay"/> 分まで (その分を含まない) の深夜分数。
    /// <paramref name="minuteOfDay"/> は 0〜1440 を想定 (<see cref="NightOverlap"/> 専用の累積関数)。
    /// </summary>
    private static int NightMinutesBefore(int minuteOfDay)
        => Math.Min(minuteOfDay, NightEndMinute) + Math.Max(0, minuteOfDay - NightStartMinute);

    /// <summary>
    /// 実労働時間を割増区分の 6 バケットへ分解する (§4.4、労基法 37 条)。
    /// <paramref name="monthNonStatutoryOtSoFar"/> は当月の当日より前までの
    /// 法定外残業 (60h 超含む) 累計。月 60h 超の振り分けに必要。
    /// </summary>
    public static AttendanceBuckets SplitBuckets(
        int workMinutes,
        int scheduledMinutes,
        int nightMinutes,
        bool isLegalHoliday,
        int monthNonStatutoryOtSoFar)
    {
        // 法定休日労働に時間外の概念はない (全量が法定休日労働)。
        if (isLegalHoliday)
            return new AttendanceBuckets(0, 0, 0, 0, nightMinutes, workMinutes);

        var scheduled = Math.Min(workMinutes, scheduledMinutes);
        var beyondScheduled = Math.Max(0, workMinutes - scheduledMinutes);
        var statutoryRoom = Math.Max(0, LegalDailyMinutes - scheduledMinutes);
        // 所定超〜法定内 = 割増なし
        var statutoryOt = Math.Min(beyondScheduled, statutoryRoom);
        // 法定超 = 25% 割増
        var nonStatutory = Math.Max(0, beyondScheduled - statutoryRoom);
        var roomTo60 = Math.Max(0, Over60ThresholdMinutes - monthNonStatutoryOtSoFar);
        var nonStatutoryOt = Math.Min(nonStatutory, roomTo60);
        // 月 60h 超 = 50% 割増
        var over60Ot = Math.Max(0, nonStatutory - roomTo60);

        // 深夜は他バケットと独立 (重複カウント)。
        return new AttendanceBuckets(scheduled, statutoryOt, nonStatutoryOt, over60Ot, nightMinutes, 0);
    }

    /// <summary>
    /// 労基法 34 条の必要休憩時間 (分)。6h 超で 45 分、8h 超で 60 分 (境界は厳密に「超」)。
    /// </summary>
    public static int RequiredBreakMinutes(int workMinutes)
    {
        if (workMinutes > 8 * 60) return 60;
        if (workMinutes > 6 * 60) return 45;
        return 0;
    }

    /// <summary>
    /// 36 協定の判定 (§4.6)。<paramref name="months"/> は月昇順で、**最終要素が当月**。
    /// 返す <c>Code</c> は HTTP エラーコードではなくアラート識別子。
    /// </summary>
    public static IReadOnlyList<Article36Alert> JudgeArticle36(
        IReadOnlyList<MonthOtRecord> months, int over45CountThisYear)
    {
        var alerts = new List<Article36Alert>();
        if (months.Count == 0) return alerts;

        // 最終要素が当月 (呼出側は月昇順で渡す)。
        var cur = months[months.Count - 1];
        var curOt = cur.NonStatutoryOtMin;
        var curTotal = cur.NonStatutoryOtMin + cur.LegalHolidayMin;

        // 45h ちょうどは適法 (「超」判定)。
        if (curOt > OtMonthlyLimitMinutes)
            alerts.Add(new Article36Alert(AlertLevelCrit, AlertCodeOver45,
                $"時間外労働が月45時間を超過しています（{curOt / 60}h）"));
        else if (curOt >= OtMonthlyWarnMinutes)
            alerts.Add(new Article36Alert(AlertLevelWarn, AlertCodeOver45Warn,
                $"時間外労働が月45時間の80%に達しています（{curOt / 60}h）"));

        // 100h は「到達」で違反 (>=)。
        if (curTotal >= OtMonthlyHardLimitMin)
            alerts.Add(new Article36Alert(AlertLevelCrit, AlertCodeOver100,
                "時間外+休日労働が単月100時間に到達しています（特別条項違反）"));

        var maxSpan = Math.Min(6, months.Count);
        for (var span = 2; span <= maxSpan; span++)
        {
            var sum = 0;
            for (var i = months.Count - span; i < months.Count; i++)
                sum += months[i].NonStatutoryOtMin + months[i].LegalHolidayMin;

            var avg = (double)sum / span;
            if (avg > OtAvgLimitMinutes)
            {
                // 最初にヒットした span で打ち切る (同じ事象を重複通知しない)。
                alerts.Add(new Article36Alert(AlertLevelCrit, AlertCodeAvg80,
                    $"時間外+休日労働の直近{span}ヶ月平均が80時間を超えています"));
                break;
            }
        }

        if (over45CountThisYear >= OtOver45MaxCount)
            alerts.Add(new Article36Alert(AlertLevelCrit, AlertCodeOver45Count,
                "月45時間超が年6回に達しています（特別条項の上限）"));
        else if (over45CountThisYear == OtOver45MaxCount - 1)
            alerts.Add(new Article36Alert(AlertLevelWarn, AlertCodeOver45CountWarn,
                $"月45時間超が年{over45CountThisYear}回です（上限まで残り1回）"));

        return alerts;
    }

    /// <summary>打刻の状態機械で、その状態から打てる種別 (§4.8)。</summary>
    public static IReadOnlyList<PunchKind> AllowedKinds(PunchState state)
    {
        switch (state)
        {
            case PunchState.Before:
                return new[] { PunchKind.In };
            case PunchState.Working:
                return new[] { PunchKind.BreakStart, PunchKind.Out };
            case PunchState.Breaking:
                return new[] { PunchKind.BreakEnd };
            default:
                return Array.Empty<PunchKind>();
        }
    }

    /// <summary>その状態でその打刻が許されるか。false なら打刻順序違反 (409)。</summary>
    public static bool IsAllowed(PunchState state, PunchKind kind)
        => AllowedKinds(state).Contains(kind);

    /// <summary>当日の打刻列から現在の状態を導出する (§4.8)。</summary>
    public static PunchState PunchStateOf(IReadOnlyList<PunchRecord> rows)
    {
        var effective = EffectivePunches(rows);
        if (effective.Count == 0) return PunchState.Before;

        var last = effective[effective.Count - 1];
        if (last.Kind == PunchKind.Out) return PunchState.Done;
        if (last.Kind == PunchKind.BreakStart) return PunchState.Breaking;
        return PunchState.Working;
    }

    /// <summary>
    /// 利用者に適用する勤務体系を解決する (§4.8、既定ルール方式)。
    /// 個別割当 (有効) → 既定 (有効) → 一覧の先頭 の順。無ければ null。
    /// <paramref name="rules"/> は ORDER BY name で渡すこと (3 の「先頭」が安定するため)。
    /// </summary>
    public static AttendanceRule? ResolveRule(User user, IReadOnlyList<AttendanceRule> rules)
    {
        if (user.AttendanceRuleId != null)
        {
            var assigned = rules.FirstOrDefault(r => r.Id == user.AttendanceRuleId.Value && r.IsActive);
            if (assigned != null) return assigned;
        }

        var fallback = rules.FirstOrDefault(r => r.IsActive && r.IsDefault);
        if (fallback != null) return fallback;

        return rules.Count > 0 ? rules[0] : null;
    }

    /// <summary>'HH:mm' を 0 時からの分数へ。解釈できない値は 0 とする。</summary>
    public static int HhmmToMin(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return 0;

        var parts = hhmm.Split(':');
        if (parts.Length != 2) return 0;
        if (!int.TryParse(parts[0], out var h)) return 0;
        if (!int.TryParse(parts[1], out var m)) return 0;
        return h * 60 + m;
    }

    /// <summary>
    /// 日次サマリ (§4.8)。<paramref name="monthOtSoFar"/> は当月の当日より前までの
    /// 法定外残業累計 (60h 超バケットの振り分けに必要)。
    /// 単日だけを計算すると <c>Over60Ot</c> が常に 0 になるため、
    /// API は原則 <see cref="MonthSummary"/> から射影すること。
    /// </summary>
    public static DaySummaryResult DaySummary(
        IReadOnlyList<PunchRecord> rows, AttendanceRule? rule, DateOnly date, int monthOtSoFar = 0)
    {
        var effective = EffectivePunches(rows);
        var worked = CalcWorkedMinutes(effective);

        var scheduled = rule != null
            ? Math.Max(0, HhmmToMin(rule.WorkEnd) - HhmmToMin(rule.WorkStart) - rule.BreakMinutes)
            : LegalDailyMinutes;
        var isLegalHoliday = (int)date.DayOfWeek == (rule != null ? rule.LegalHolidayWeekday : 0);

        var buckets = SplitBuckets(
            worked.WorkMinutes, scheduled, worked.NightMinutes, isLegalHoliday, monthOtSoFar);
        var breakShortage = Math.Max(0,
            RequiredBreakMinutes(worked.WorkMinutes) - worked.BreakMinutes);

        return new DaySummaryResult(
            date, worked.WorkMinutes, worked.BreakMinutes, worked.NightMinutes,
            buckets, breakShortage, effective);
    }

    /// <summary>
    /// 月次サマリ (§4.8)。月内を日付昇順に走査して法定外残業の累計を繰り越すため、
    /// 月 60h 超バケットが正しく振り分けられる。日次・週次 API もここから射影する。
    /// </summary>
    public static MonthSummaryResult MonthSummary(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<PunchRecord>> punchesByDate,
        AttendanceRule? rule, int year, int month)
    {
        var days = new List<DaySummaryResult>();
        var otSoFar = 0;
        var daysInMonth = DateTime.DaysInMonth(year, month);

        for (var d = 1; d <= daysInMonth; d++)
        {
            var date = new DateOnly(year, month, d);
            var rows = punchesByDate.TryGetValue(date, out var found) ? found : EmptyPunches;
            var summary = DaySummary(rows, rule, date, otSoFar);
            otSoFar += summary.Buckets.NonStatutoryOt + summary.Buckets.Over60Ot;
            days.Add(summary);
        }

        var total = new AttendanceBuckets(
            days.Sum(s => s.Buckets.Scheduled),
            days.Sum(s => s.Buckets.StatutoryOt),
            days.Sum(s => s.Buckets.NonStatutoryOt),
            days.Sum(s => s.Buckets.Over60Ot),
            days.Sum(s => s.Buckets.Night),
            days.Sum(s => s.Buckets.LegalHoliday));
        var workDays = days.Count(s => s.WorkMinutes > 0);

        return new MonthSummaryResult(days, total, workDays);
    }

    /// <summary>
    /// 直近 6 ヶ月 (<paramref name="endYear"/>/<paramref name="endMonth"/> を最終月) から
    /// 36 協定アラートを判定する (§4.8)。
    /// </summary>
    public static IReadOnlyList<Article36Alert> Article36Alerts(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<PunchRecord>> punchesByDate,
        AttendanceRule? rule, int endYear, int endMonth)
    {
        var months = new List<MonthOtRecord>();
        var over45 = 0;
        var anchor = new DateOnly(endYear, endMonth, 1);

        for (var back = 5; back >= 0; back--)
        {
            var target = anchor.AddMonths(-back);
            var summary = MonthSummary(punchesByDate, rule, target.Year, target.Month);
            var record = new MonthOtRecord(
                $"{target.Year:D4}-{target.Month:D2}",
                summary.Total.NonStatutoryOt + summary.Total.Over60Ot,
                summary.Total.LegalHoliday);

            if (record.NonStatutoryOtMin > OtMonthlyLimitMinutes) over45++;
            months.Add(record);
        }

        return JudgeArticle36(months, over45);
    }

    /// <summary>区間の分数 (四捨五入・負は 0 クランプ)。</summary>
    private static int DiffMinutes(DateTime from, DateTime to)
    {
        var minutes = (int)Math.Round((to - from).TotalMinutes, MidpointRounding.AwayFromZero);
        return Math.Max(0, minutes);
    }
}

/// <summary>
/// 割増区分の 6 バケット (単位: 分)。<see cref="Night"/> は他バケットと独立した重複カウント。
/// </summary>
public sealed record AttendanceBuckets(
    int Scheduled,
    int StatutoryOt,
    int NonStatutoryOt,
    int Over60Ot,
    int Night,
    int LegalHoliday);

/// <summary>日次サマリ。<see cref="Punches"/> は論理置換解決後の有効打刻。</summary>
public sealed record DaySummaryResult(
    DateOnly Date,
    int WorkMinutes,
    int BreakMinutes,
    int NightMinutes,
    AttendanceBuckets Buckets,
    int BreakShortage,
    IReadOnlyList<PunchRecord> Punches);

/// <summary>月次サマリ。<see cref="Total"/> は 6 バケットの単純合計。</summary>
public sealed record MonthSummaryResult(
    IReadOnlyList<DaySummaryResult> Days,
    AttendanceBuckets Total,
    int WorkDays);

/// <summary>36 協定判定の入力 (1 ヶ月分)。<c>Month</c> は "yyyy-MM"。</summary>
public sealed record MonthOtRecord(
    string Month,
    int NonStatutoryOtMin,
    int LegalHolidayMin);

/// <summary>
/// 36 協定アラート。<c>Level</c> は "warn" / "crit"、
/// <c>Code</c> は AKB-ATT-* のアラート識別子 (HTTP エラーコードではない)。
/// </summary>
public sealed record Article36Alert(
    string Level,
    string Code,
    string Message);
