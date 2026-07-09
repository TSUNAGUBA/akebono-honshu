namespace Akebono.Domain.Common;

/// <summary>
/// 現在時刻の提供。
///
/// プラットフォーム統合改修 (ADR-006 相当): 全 timestamp カラムは TIMESTAMPTZ で
/// UTC を格納する。C# 側は DateTime.Kind=Utc の値を Entity に詰める
/// (Npgsql 非レガシーモードでは timestamptz 列に Kind=Utc 以外を書くと例外になる)。
///
/// 旧方式 (JST-naive TIMESTAMP + Npgsql.EnableLegacyTimestampBehavior) は
/// あけぼの SCM プラットフォームの標準 (timestamptz/UTC 統一) に合わせて廃止した。
/// JST が必要なのは「表示・帳票・業務上の日付や年度の判定」のみであり、
/// それらは <see cref="JstNow"/> / <see cref="ToJst"/> を使う (格納には使わない)。
/// </summary>
public static class SystemTime
{
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>格納用の現在時刻 (Kind=Utc)。Entity の created_at / updated_at 等はこれを使う。</summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>表示・帳票・業務日付 (採番の年度判定等) 用の JST 現在時刻 (Kind=Unspecified)。
    /// DB へ格納しないこと。</summary>
    public static DateTime JstNow => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Jst),
        DateTimeKind.Unspecified);

    /// <summary>UTC の格納値を表示用 JST へ変換する (Kind=Unspecified)。</summary>
    public static DateTime ToJst(DateTime utc) => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc), Jst),
        DateTimeKind.Unspecified);

    /// <summary>業務上の「今日」(JST)。締め日・年度・採番の判定はこれを使う。</summary>
    public static DateOnly TodayJst => DateOnly.FromDateTime(JstNow);
}
