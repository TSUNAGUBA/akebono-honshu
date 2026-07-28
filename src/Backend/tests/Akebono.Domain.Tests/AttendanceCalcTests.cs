using Akebono.Domain.Attendance;
using Xunit;

namespace Akebono.Domain.Tests;

/// <summary>
/// <see cref="AttendanceCalc"/> の純粋関数 (副作用なし・DB非依存) の単体テスト。
/// 労基法 32/34/37 条まわりの分解・深夜計算・有効打刻解決・36協定判定を検証する。
/// </summary>
public class AttendanceCalcTests
{
    // ── 日次 6 バケット分解 (SplitBuckets, 労基法37条) ────────────────────────
    [Fact]
    public void SplitBuckets_ExactlyScheduled_AllScheduled()
        => Assert.Equal(new AttendanceBuckets(480, 0, 0, 0, 0, 0),
            AttendanceCalc.SplitBuckets(480, 480, 0, false, 0));

    [Fact]
    public void SplitBuckets_Over8h_NonStatutoryOt()
        => Assert.Equal(new AttendanceBuckets(480, 0, 120, 0, 0, 0),
            AttendanceCalc.SplitBuckets(600, 480, 0, false, 0));

    [Fact]
    public void SplitBuckets_ScheduledUnder8h_StatutoryOtNoPremium()
        => Assert.Equal(new AttendanceBuckets(420, 60, 0, 0, 0, 0),
            AttendanceCalc.SplitBuckets(480, 420, 0, false, 0));

    [Fact]
    public void SplitBuckets_LegalHoliday_AllToHolidayBucket()
        => Assert.Equal(new AttendanceBuckets(0, 0, 0, 0, 20, 300),
            AttendanceCalc.SplitBuckets(300, 480, 20, true, 0));

    [Fact]
    public void SplitBuckets_MonthlyOver60_SplitsToOver60Bucket()
        // 当月既に59h法定外 → 当日の法定外120分は 60分(25%)+60分(50%) に分かれる。
        => Assert.Equal(new AttendanceBuckets(480, 0, 60, 60, 0, 0),
            AttendanceCalc.SplitBuckets(600, 480, 0, false, 3540));

    // ── 休憩必要時間 (RequiredBreakMinutes, 労基法34条・境界は厳密に「超」) ──────
    [Theory]
    [InlineData(360, 0)]    // 6h ちょうど → 0
    [InlineData(361, 45)]   // 6h 超 → 45
    [InlineData(480, 45)]   // 8h ちょうど → 45
    [InlineData(481, 60)]   // 8h 超 → 60
    public void RequiredBreakMinutes_Boundaries(int work, int expected)
        => Assert.Equal(expected, AttendanceCalc.RequiredBreakMinutes(work));

    // ── 深夜帯重なり (NightOverlap, JST 22:00〜翌05:00・ToJst=Asia/Tokyo +9) ────
    [Fact]
    public void NightOverlap_DaytimeInterval_Zero()
        // UTC 00:00 = JST 09:00、480分 (〜17:00) は深夜なし。
        => Assert.Equal(0, AttendanceCalc.NightOverlap(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 480));

    [Fact]
    public void NightOverlap_WithinNight_CountsFully()
        // UTC 13:00 = JST 22:00、60分 (22:00〜23:00) は全量深夜。
        => Assert.Equal(60, AttendanceCalc.NightOverlap(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc), 60));

    [Fact]
    public void NightOverlap_CrossMidnight_FullNightWindow()
        // JST 22:00 から 480分 (〜翌06:00): 22-24時(120) + 00-05時(300) = 420。
        => Assert.Equal(420, AttendanceCalc.NightOverlap(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc), 480));

    [Fact]
    public void NightOverlap_EndBoundary0500_NotCounted()
        // UTC 20:00 = 翌 JST 05:00、60分 (05:00〜06:00) は深夜外 (05:00 は含まない)。
        => Assert.Equal(0, AttendanceCalc.NightOverlap(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc), 60));

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void NightOverlap_NonPositiveDuration_Zero(int dur)
        => Assert.Equal(0, AttendanceCalc.NightOverlap(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc), dur));

    // ── 週境界 (WeekStart, 日曜起点) ──────────────────────────────────────────
    [Theory]
    [InlineData(2026, 7, 26)]  // 日
    [InlineData(2026, 7, 27)]  // 月
    [InlineData(2026, 7, 28)]  // 火
    [InlineData(2026, 8, 1)]   // 土
    public void WeekStart_ReturnsSundayOfSameWeek(int y, int m, int d)
    {
        var date = new DateOnly(y, m, d);
        var ws = AttendanceCalc.WeekStart(date);
        Assert.Equal(DayOfWeek.Sunday, ws.DayOfWeek);
        Assert.True(ws <= date);
        Assert.True(date.DayNumber - ws.DayNumber < 7);
    }

    // ── HH:mm → 分 (HhmmToMin, 解釈不能は 0) ──────────────────────────────────
    [Theory]
    [InlineData("09:00", 540)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 1439)]
    [InlineData(null, 0)]
    [InlineData("abc", 0)]
    [InlineData("9", 0)]
    public void HhmmToMin_ParsesOrZero(string? hhmm, int expected)
        => Assert.Equal(expected, AttendanceCalc.HhmmToMin(hhmm));

    // ── 36協定判定 (JudgeArticle36) ──────────────────────────────────────────
    [Fact]
    public void JudgeArticle36_Over45h_Crit()
    {
        var alerts = AttendanceCalc.JudgeArticle36(
            new[] { new MonthOtRecord("2026-07", 46 * 60, 0) }, 0);
        Assert.Contains(alerts, a => a.Code == AttendanceCalc.AlertCodeOver45 && a.Level == AttendanceCalc.AlertLevelCrit);
    }

    [Fact]
    public void JudgeArticle36_Exactly45h_NotCrit_ButWarn()
    {
        var alerts = AttendanceCalc.JudgeArticle36(
            new[] { new MonthOtRecord("2026-07", 45 * 60, 0) }, 0);
        Assert.DoesNotContain(alerts, a => a.Code == AttendanceCalc.AlertCodeOver45);
        Assert.Contains(alerts, a => a.Code == AttendanceCalc.AlertCodeOver45Warn && a.Level == AttendanceCalc.AlertLevelWarn);
    }

    [Fact]
    public void JudgeArticle36_SingleMonth100h_Crit()
    {
        var alerts = AttendanceCalc.JudgeArticle36(
            new[] { new MonthOtRecord("2026-07", 90 * 60, 10 * 60) }, 0);
        Assert.Contains(alerts, a => a.Code == AttendanceCalc.AlertCodeOver100 && a.Level == AttendanceCalc.AlertLevelCrit);
    }

    [Fact]
    public void JudgeArticle36_Empty_NoAlerts()
        => Assert.Empty(AttendanceCalc.JudgeArticle36(System.Array.Empty<MonthOtRecord>(), 0));

    // ── 有効打刻解決 (EffectivePunches, 論理置換) ────────────────────────────
    [Fact]
    public void EffectivePunches_FixSupersedesOriginalByAt()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var orig = new PunchRecord
        {
            Id = g1, Kind = PunchKind.In, Source = PunchSource.Web,
            At = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var fix = new PunchRecord
        {
            Id = g2, Kind = PunchKind.In, Source = PunchSource.Fix,
            At = new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc),
            FixedFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var effective = AttendanceCalc.EffectivePunches(new[] { orig, fix });

        Assert.Single(effective);
        Assert.Equal(g2, effective[0].Id);
    }

    [Fact]
    public void EffectivePunches_NoFix_ReturnsAllOrderedByAt()
    {
        var late = new PunchRecord
        {
            Id = Guid.NewGuid(), Kind = PunchKind.Out, Source = PunchSource.Web,
            At = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
        };
        var early = new PunchRecord
        {
            Id = Guid.NewGuid(), Kind = PunchKind.In, Source = PunchSource.Web,
            At = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var effective = AttendanceCalc.EffectivePunches(new[] { late, early });

        Assert.Equal(2, effective.Count);
        Assert.Equal(PunchKind.In, effective[0].Kind);   // At 昇順で In が先
        Assert.Equal(PunchKind.Out, effective[1].Kind);
    }

    // ── 実労働・休憩・深夜の集計 (CalcWorkedMinutes) ──────────────────────────
    [Fact]
    public void CalcWorkedMinutes_InOut_NoBreak()
    {
        var punches = new[]
        {
            new PunchRecord { Kind = PunchKind.In, Source = PunchSource.Web, At = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new PunchRecord { Kind = PunchKind.Out, Source = PunchSource.Web, At = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc) },
        };
        var (work, brk, night) = AttendanceCalc.CalcWorkedMinutes(punches);
        Assert.Equal(480, work);
        Assert.Equal(0, brk);
        Assert.Equal(0, night);   // JST 09:00〜17:00 は深夜外
    }

    [Fact]
    public void CalcWorkedMinutes_WithBreak_ExcludesBreakFromWork()
    {
        var punches = new[]
        {
            new PunchRecord { Kind = PunchKind.In, Source = PunchSource.Web, At = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new PunchRecord { Kind = PunchKind.BreakStart, Source = PunchSource.Web, At = new DateTime(2026, 1, 1, 4, 0, 0, DateTimeKind.Utc) },
            new PunchRecord { Kind = PunchKind.BreakEnd, Source = PunchSource.Web, At = new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc) },
            new PunchRecord { Kind = PunchKind.Out, Source = PunchSource.Web, At = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc) },
        };
        var (work, brk, _) = AttendanceCalc.CalcWorkedMinutes(punches);
        Assert.Equal(420, work);   // 240 + 180
        Assert.Equal(60, brk);
    }
}
