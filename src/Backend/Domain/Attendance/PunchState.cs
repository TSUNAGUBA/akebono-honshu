namespace Akebono.Domain.Attendance;

/// <summary>
/// 打刻の状態機械 (当日の有効打刻から導出する。DB 列は持たない導出値)。
/// 許容遷移は <see cref="AttendanceCalc.AllowedKinds"/> / <see cref="AttendanceCalc.IsAllowed"/> を参照。
/// </summary>
public enum PunchState : short
{
    /// <summary>未出勤 (打刻なし)</summary>
    Before = 0,
    /// <summary>勤務中</summary>
    Working = 1,
    /// <summary>休憩中</summary>
    Breaking = 2,
    /// <summary>退勤済み</summary>
    Done = 3,
}
