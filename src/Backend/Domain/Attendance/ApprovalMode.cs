namespace Akebono.Domain.Attendance;

/// <summary>
/// 承認ステップの承認方式 (attendance_route_steps.mode)。office 踏襲で保持するが、解決は現状すべて
/// <see cref="Serial"/> (ステップごとに単一承認者の直列ゲート) として扱う。All / Majority は
/// 将来拡張の予約値で、現時点では承認進行のロジックが分岐しない (office の実装と同じ)。
/// 値は DB 列 (SMALLINT) と 1:1。JSON へは camelCase 文字列 (serial / all / majority) で出る。
/// </summary>
public enum ApprovalMode : short
{
    /// <summary>直列 (現状唯一実装されている方式)。</summary>
    Serial = 0,
    /// <summary>全員承認 (予約値)。</summary>
    All = 1,
    /// <summary>過半数承認 (予約値)。</summary>
    Majority = 2,
}
