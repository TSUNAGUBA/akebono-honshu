namespace Akebono.Domain.Attendance;

/// <summary>
/// 直行/直帰の種別 (direct_requests.type)。承認された日に申請できる打刻修正の種別を決める
/// (<see cref="AttendanceRouteResolver.DirectKinds"/>): chokkou→出勤(in) / chokki→退勤(out) /
/// both→両方。所定の休憩打刻には影響しない (in/out のみ対象)。
/// 値は DB 列 (SMALLINT) と 1:1。JSON へは camelCase 文字列 (chokkou / chokki / both) で出る。
/// </summary>
public enum DirectType : short
{
    /// <summary>直行 (出勤の打刻修正を許可)。</summary>
    Chokkou = 0,
    /// <summary>直帰 (退勤の打刻修正を許可)。</summary>
    Chokki = 1,
    /// <summary>直行直帰 (出勤・退勤の両方を許可)。</summary>
    Both = 2,
}
