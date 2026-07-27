namespace Akebono.Domain.Attendance;

/// <summary>
/// 休暇 (有給・特別休暇) の計算の純粋関数群 (副作用なし・DB 非依存)。労基法 39 条に対応する。
///
/// 日付はすべて JST の業務日付 (<c>SystemTime.TodayJst</c>) を渡すこと。
/// </summary>
public static class LeaveCalc
{
    /// <summary>法定有給の繰越上限 (日)。当年度分 + 前年度繰越分。</summary>
    public const decimal CarryOverCapDays = 40m;

    /// <summary>年 5 日取得義務の対象になる付与日数の下限 (日)。</summary>
    public const decimal ObligationThresholdDays = 10m;

    /// <summary>年 5 日取得義務の必要取得日数 (日)。</summary>
    public const decimal ObligationRequiredDays = 5m;

    /// <summary>年 5 日取得義務の警告閾値 (期限までの残日数)。</summary>
    public const int ObligationWarnDaysLeft = 92;

    /// <summary>通常付与の判定閾値: 週所定時間 (h)。</summary>
    public const decimal NormalGrantWeeklyHours = 30m;

    /// <summary>通常付与の判定閾値: 週所定日数 (日)。</summary>
    public const decimal NormalGrantWeeklyDays = 5m;

    /// <summary>無期限の付与に用いる失効日。</summary>
    public static readonly DateOnly NoExpiryDate = new(9999, 12, 31);

    /// <summary>通常付与の日数 (勤続 0.5 年 → 6.5 年以上の 7 段階)。</summary>
    private static readonly decimal[] NormalGrant = new decimal[] { 10, 11, 12, 14, 16, 18, 20 };

    /// <summary>比例付与の日数 (週所定日数 4/3/2/1 日 × 7 段階)。</summary>
    private static readonly Dictionary<int, decimal[]> ProportionalGrant = new()
    {
        [4] = new decimal[] { 7, 8, 9, 10, 12, 13, 15 },
        [3] = new decimal[] { 5, 6, 6, 8, 9, 10, 11 },
        [2] = new decimal[] { 3, 4, 4, 5, 6, 6, 7 },
        [1] = new decimal[] { 1, 2, 2, 2, 3, 3, 3 },
    };

    /// <summary>
    /// 労基法 39 条の付与日数 (§4.7)。
    /// **「週30h以上 or 週5日以上 → 通常付与」の判定が比例付与より先**
    /// (週 4 日でも 32h なら通常付与 10 日)。勤続 6 ヶ月未満は 0。
    /// </summary>
    public static decimal LeaveGrantDays(decimal weeklyDays, decimal weeklyHours, double serviceYears)
    {
        if (serviceYears < 0.5) return 0m;

        var stage = Math.Min(6, (int)Math.Floor(serviceYears - 0.5 + 1e-9));
        if (stage < 0) stage = 0;

        if (weeklyHours >= NormalGrantWeeklyHours || weeklyDays >= NormalGrantWeeklyDays)
            return NormalGrant[stage];

        var key = Math.Max(1, Math.Min(4, (int)Math.Floor(weeklyDays)));
        return ProportionalGrant[key][stage];
    }

    /// <summary>失効日 (§4.9)。<paramref name="expiryMonths"/> が null なら無期限 (9999-12-31)。</summary>
    public static DateOnly ExpireDateFor(DateOnly grantDate, int? expiryMonths)
        => expiryMonths == null ? NoExpiryDate : grantDate.AddMonths(expiryMonths.Value);

    /// <summary>会計年度 (4 月起算) の期間。<paramref name="today"/> が属する年度を返す。</summary>
    public static (DateOnly Start, DateOnly End) FiscalYear(DateOnly today)
    {
        var startYear = today.Month >= 4 ? today.Year : today.Year - 1;
        return (new DateOnly(startYear, 4, 1), new DateOnly(startYear + 1, 3, 31));
    }

    /// <summary>
    /// 残数の FIFO 引当 (§4.9)。付与を <c>GrantDate</c> 昇順、消化を <c>Date</c> 昇順に処理し、
    /// 消化日に有効な (付与日 &lt;= 消化日 &lt; 失効日) 最も古い付与から引き当てる。
    /// 引当先が無い消化は残数に影響させない (データ不整合時の防御)。
    /// </summary>
    /// <param name="grants">対象種別の付与 (全件)。</param>
    /// <param name="approvedRequests">対象種別の**承認済み**申請のみ。</param>
    /// <param name="isStatutory">法定有給か。true のとき繰越上限 40 日を適用する。</param>
    /// <param name="today">JST の業務日付。失効判定の基準。</param>
    public static LeaveBalanceResult Balance(
        IReadOnlyList<LeaveGrant> grants,
        IReadOnlyList<LeaveRequest> approvedRequests,
        bool isStatutory,
        DateOnly today)
    {
        var ordered = grants.OrderBy(g => g.GrantDate).ToList();
        var consumed = new decimal[ordered.Count];

        foreach (var request in approvedRequests.OrderBy(r => r.Date))
        {
            var need = UnitDays(request.Unit);
            for (var i = 0; i < ordered.Count && need > 0m; i++)
            {
                var grant = ordered[i];
                if (grant.GrantDate > request.Date) continue;
                if (grant.ExpireDate <= request.Date) continue;

                var available = grant.Days - consumed[i];
                if (available <= 0m) continue;

                var take = Math.Min(available, need);
                consumed[i] += take;
                need -= take;
            }
            // need > 0 のまま残った消化 = 引当先なし。残数には影響させない。
        }

        var allocations = new List<LeaveAllocation>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var grant = ordered[i];
            allocations.Add(new LeaveAllocation(
                grant, consumed[i], Math.Max(0m, grant.Days - consumed[i]), grant.ExpireDate <= today));
        }

        var rawRemaining = allocations.Where(a => !a.Expired).Sum(a => a.Remaining);
        var remaining = isStatutory ? Math.Min(CarryOverCapDays, rawRemaining) : rawRemaining;
        var nextExpire = allocations
            .Where(a => !a.Expired && a.Remaining > 0m)
            .OrderBy(a => a.Grant.ExpireDate)
            .FirstOrDefault();

        var fiscalYear = FiscalYear(today);
        var usedThisFiscalYear = approvedRequests
            .Where(r => r.Date >= fiscalYear.Start && r.Date <= fiscalYear.End)
            .Sum(r => UnitDays(r.Unit));

        return new LeaveBalanceResult(allocations, rawRemaining, remaining, nextExpire, usedThisFiscalYear);
    }

    /// <summary>
    /// 年 5 日取得義務の判定 (§4.9、労基法 39 条 7 項)。
    /// 対象は「10 日以上の付与のうち付与日が今日以前で最新のもの」。
    /// 期限はその付与日の 1 年後。
    /// </summary>
    public static LeaveObligationResult Obligation(
        IReadOnlyList<LeaveGrant> grants,
        IReadOnlyList<LeaveRequest> approvedRequests,
        DateOnly today)
    {
        var target = grants
            .Where(g => g.Days >= ObligationThresholdDays && g.GrantDate <= today)
            .OrderByDescending(g => g.GrantDate)
            .FirstOrDefault();

        if (target == null)
            return new LeaveObligationResult(
                false, null, null, ObligationRequiredDays, 0m, 0, false, false);

        var deadline = target.GrantDate.AddYears(1);
        var taken = approvedRequests
            .Where(r => r.Date >= target.GrantDate && r.Date < deadline)
            .Sum(r => UnitDays(r.Unit));
        var daysLeft = deadline.DayNumber - today.DayNumber;
        var achieved = taken >= ObligationRequiredDays;
        var warn = !achieved && daysLeft < ObligationWarnDaysLeft;

        return new LeaveObligationResult(
            true, target.GrantDate, deadline, ObligationRequiredDays, taken, daysLeft, achieved, warn);
    }

    /// <summary>
    /// 周期自動付与の計画 (§4.9)。入社日 + 6 ヶ月、以降 1 年ごとに付与日を並べ、
    /// 「今日までに到来済み」かつ「既に時効切れではない」ものだけを返す。
    ///
    /// 冪等性 (CLAUDE.md 原則 2): 本関数は計画を返すだけで既存付与を一切考慮しない。
    /// 実際の投入側で既存の (user_id, leave_type_id, grant_date) を事前 SELECT して
    /// 除外し、**挿入のみ**行うこと (DB の一意制約が最終防壁)。
    /// </summary>
    public static IReadOnlyList<PeriodicGrantPlan> PlanPeriodicGrants(
        DateOnly hireDate, decimal weeklyDays, decimal weeklyHours, DateOnly today)
    {
        var plans = new List<PeriodicGrantPlan>();

        for (var years = 0; years < 100; years++)
        {
            var grantDate = hireDate.AddMonths(6 + years * 12);
            if (grantDate > today) break;

            // 時効 (2 年)。既に時効切れの付与は生成しない。
            var expire = grantDate.AddYears(2);
            if (expire <= today) continue;

            var days = LeaveGrantDays(weeklyDays, weeklyHours, years + 0.5);
            if (days <= 0m) continue;

            var kind = weeklyHours >= NormalGrantWeeklyHours || weeklyDays >= NormalGrantWeeklyDays
                ? LeaveGrantKind.Normal
                : LeaveGrantKind.Proportional;

            plans.Add(new PeriodicGrantPlan(grantDate, expire, days, kind));
        }

        return plans;
    }

    /// <summary>取得単位を日数へ (全日=1.0 / 半日=0.5)。</summary>
    public static decimal UnitDays(LeaveUnit unit) => unit == LeaveUnit.Half ? 0.5m : 1m;
}

/// <summary>FIFO 引当の結果 (付与 1 件あたり)。</summary>
public sealed record LeaveAllocation(
    LeaveGrant Grant,
    decimal Consumed,
    decimal Remaining,
    bool Expired);

/// <summary>
/// 残数の集計結果。<see cref="RawRemaining"/> は上限適用前、
/// <see cref="Remaining"/> は法定有給の繰越上限 (40 日) 適用後。
/// </summary>
public sealed record LeaveBalanceResult(
    IReadOnlyList<LeaveAllocation> Allocations,
    decimal RawRemaining,
    decimal Remaining,
    LeaveAllocation? NextExpire,
    decimal UsedThisFiscalYear);

/// <summary>年 5 日取得義務の判定結果。<c>Applicable</c> が false のとき他の値は既定値。</summary>
public sealed record LeaveObligationResult(
    bool Applicable,
    DateOnly? GrantDate,
    DateOnly? Deadline,
    decimal RequiredDays,
    decimal TakenDays,
    int DaysLeft,
    bool Achieved,
    bool Warn);

/// <summary>周期自動付与で投入すべき 1 件分の計画。</summary>
public sealed record PeriodicGrantPlan(
    DateOnly GrantDate,
    DateOnly ExpireDate,
    decimal Days,
    LeaveGrantKind Kind);
