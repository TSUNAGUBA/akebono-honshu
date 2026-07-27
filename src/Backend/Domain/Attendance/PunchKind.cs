namespace Akebono.Domain.Attendance;

/// <summary>
/// 打刻種別 (punch_records.kind)。値は DB 列 (SMALLINT) と 1:1。
/// JSON へは camelCase 文字列 (in / out / breakStart / breakEnd) で出る。
/// </summary>
public enum PunchKind : short
{
    /// <summary>出勤</summary>
    In = 0,
    /// <summary>退勤</summary>
    Out = 1,
    /// <summary>休憩開始</summary>
    BreakStart = 2,
    /// <summary>休憩終了</summary>
    BreakEnd = 3,
}
