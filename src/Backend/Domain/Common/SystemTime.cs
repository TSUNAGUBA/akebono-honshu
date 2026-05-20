namespace Akebono.Domain.Common;

/// <summary>
/// JST (Asia/Tokyo) 固定の現在時刻を提供する。
///
/// Iter 4 段階 B 確定の DB 設計: 全 timestamp カラムは TIMESTAMP (without time zone) で
/// JST naive datetime を格納する。これに合わせて C# 側も DateTime.Kind=Unspecified の
/// JST 値を Entity に詰めることで Npgsql が暗黙キャストせずそのまま書込めるようにする。
///
/// 本来 UTC 統一が国際標準だが、Phase 6 オペレーター要件 (pgAdmin 表示で +09:00 が
/// 不要、生の日時で帳票や監査追跡を行いたい) により JST naive に統一した。
/// </summary>
public static class SystemTime
{
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>JST の現在時刻。Kind=Unspecified を返すため Npgsql の timestamp without
    /// time zone カラムにそのまま書込可能 (Kind=Utc/Local だと例外になる場合がある)。</summary>
    public static DateTime Now => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Jst),
        DateTimeKind.Unspecified);
}
