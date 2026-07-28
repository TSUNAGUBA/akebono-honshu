using Akebono.Domain.Attendance;
using Xunit;

namespace Akebono.Domain.Tests;

/// <summary>
/// C-4: 週40時間 (労基法32条) の法定外残業分解 <see cref="AttendanceCalc.SplitBucketsWeekly"/> の単体テスト。
/// 週内の「日8h以内労働」累計が40hを超えた分を法定外残業へ振り替える。日8h超 (日次法定外) とは
/// 母数が重ならない (二重計上なし)。Python 参照実装で検証したケースを移植。
/// </summary>
public class AttendanceCalcWeeklyTests
{
    // 8h = 480分, 40h = 2400分, 60h = 3600分。

    [Fact]
    public void WeeklyUnder40h_MatchesDailySplit_NoWeeklyOt()
    {
        // 週累計 0、所定=実労働=8h → 全量所定内。週OTなし。従来の日次分解と一致。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(480, 480, 0, false, 0, 0);

        Assert.Equal(new AttendanceBuckets(480, 0, 0, 0, 0, 0), buckets);
        Assert.Equal(480, within8h);
        Assert.Equal(0, totalNonStat);
    }

    [Fact]
    public void WeekCrosses40h_FullDayBecomesWeeklyOt()
    {
        // 週の6日目 (前5日で 2400分=40h 到達済み) の 8h は全量が週40h超の法定外残業。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(480, 480, 0, false, 0, 2400);

        Assert.Equal(new AttendanceBuckets(0, 0, 480, 0, 0, 0), buckets);
        Assert.Equal(480, within8h);
        Assert.Equal(480, totalNonStat);
    }

    [Fact]
    public void WeekPartiallyCrosses40h_SplitsAtBoundary()
    {
        // 週累計 36h。当日8hのうち 4h で40hに到達、残4hが週OT。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(480, 480, 0, false, 0, 2160);

        Assert.Equal(new AttendanceBuckets(240, 0, 240, 0, 0, 0), buckets);
        Assert.Equal(480, within8h);
        Assert.Equal(240, totalNonStat);
    }

    [Fact]
    public void DailyOver8h_AndWeeklyOt_NoDoubleCount()
    {
        // 当日10h・週累計40h到達済み: 日次法定外=2h(120) + 週法定外=8h以内の480 = 600。二重計上なし。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(600, 480, 0, false, 0, 2400);

        Assert.Equal(new AttendanceBuckets(0, 0, 600, 0, 0, 0), buckets);
        Assert.Equal(480, within8h);
        Assert.Equal(600, totalNonStat);
    }

    [Fact]
    public void LegalHoliday_ExcludedFromWeeklyBase()
    {
        // 法定休日は週40hの母数に入れない。全量が法定休日労働、within8h/totalNonStat は 0。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(300, 480, 30, true, 0, 2400);

        Assert.Equal(new AttendanceBuckets(0, 0, 0, 0, 30, 300), buckets);
        Assert.Equal(0, within8h);
        Assert.Equal(0, totalNonStat);
    }

    [Fact]
    public void WeeklyOt_InteractsWith60hThreshold()
    {
        // 月の法定外累計が59h、当日の週OTが8h(480) → 60h(3600)超に60分乗るまで、残420分は50%割増(over60)。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(480, 480, 0, false, 3540, 2400);

        Assert.Equal(new AttendanceBuckets(0, 0, 60, 420, 0, 0), buckets);
        Assert.Equal(480, within8h);
        Assert.Equal(480, totalNonStat);
    }

    [Fact]
    public void ScheduledLessThan8h_StatutoryOtFillsToDailyLimit()
    {
        // 所定7h・実労働8h・週OTなし: 所定内7h + 所定超〜法定内1h(割増なし=statutoryOt)。
        var (buckets, within8h, totalNonStat) =
            AttendanceCalc.SplitBucketsWeekly(480, 420, 0, false, 0, 0);

        Assert.Equal(new AttendanceBuckets(420, 60, 0, 0, 0, 0), buckets);
        Assert.Equal(480, within8h);
        Assert.Equal(0, totalNonStat);
    }

    // 分保存則 (法定休日以外): Scheduled+StatutoryOt+NonStatutoryOt+Over60Ot == 実労働時間。
    // どの (work, weekCum, monthSoFar) でも成立することを確認する。
    [Theory]
    [InlineData(480, 0, 0)]
    [InlineData(480, 2400, 0)]
    [InlineData(480, 2160, 0)]
    [InlineData(600, 2400, 0)]
    [InlineData(600, 0, 3540)]
    [InlineData(300, 0, 0)]
    [InlineData(720, 1800, 3000)]
    public void MinuteConservation_NonHoliday(int work, int weekCum, int monthSoFar)
    {
        var (b, _, _) = AttendanceCalc.SplitBucketsWeekly(work, 480, 0, false, monthSoFar, weekCum);
        Assert.Equal(work, b.Scheduled + b.StatutoryOt + b.NonStatutoryOt + b.Over60Ot);
        Assert.Equal(0, b.LegalHoliday);
    }
}
