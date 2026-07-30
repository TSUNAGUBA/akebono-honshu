namespace Akebono.Domain.Attendance;

/// <summary>
/// 承認経路のロール (approver_type=Role のときに参照)。
///
/// office の承認ロールは admin / hr の 2 種だが、honshu は office の admin を
/// 「オーナー (勤怠管理者)」= process_record_permission &gt;= 1 かつ 勤怠権限 1/2 に写像しており
/// (AuthEndpoints.CheckAttendanceAdminAsync)、hr 中間ロールは持たない。したがって現状の値は
/// <see cref="Owner"/> の 1 種のみ。列挙で持つのは将来ロールを増やす余地を残すため。
/// 値は DB 列 (SMALLINT) と 1:1。JSON へは camelCase 文字列 (owner) で出る。
/// </summary>
public enum ApproverRole : short
{
    /// <summary>オーナー (勤怠管理者)。承認できるのは勤怠権限 1/2 かつ process_record_permission &gt;= 1 の在籍者。</summary>
    Owner = 0,
}
