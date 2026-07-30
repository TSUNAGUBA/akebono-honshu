namespace Akebono.Domain.Attendance;

/// <summary>
/// 勤怠承認経路の区分 (attendance_routes.category)。経路設定のキー。
/// 稟議の金額帯に相当するものは持たず、区分だけで経路を選ぶ (AttendanceRouteResolver)。
/// 値は DB 列 (SMALLINT) と 1:1。JSON へは camelCase 文字列 (direct / fix) で出る。
/// </summary>
public enum AttendanceRequestCategory : short
{
    /// <summary>直行/直帰申請。</summary>
    Direct = 0,
    /// <summary>打刻修正申請。</summary>
    Fix = 1,
}
