namespace Akebono.Domain.Attendance;

/// <summary>
/// 承認者の指定方法 (attendance_route_steps.approver_type / attendance_request_steps.approver_type)。
/// office の PermissionRule.subjectKind (title/role/member) と同じ 3 種。値は DB 列 (SMALLINT) と 1:1。
/// JSON へは camelCase 文字列 (title / role / member) で出る。
/// </summary>
public enum ApproverType : short
{
    /// <summary>役職 (users.title に一致する在籍者)。</summary>
    Title = 0,
    /// <summary>ロール (honshu の権限ロール。<see cref="ApproverRole"/>)。</summary>
    Role = 1,
    /// <summary>個人 (users.id 指定)。</summary>
    Member = 2,
}
