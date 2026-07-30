using Akebono.Domain.Attendance;
using Akebono.Domain.Entities;

namespace Akebono.Application.Attendance;

/// <summary>
/// 勤怠承認経路 (Iteration 33) の共有ヘルパー。経路 CRUD (<see cref="AttendanceRouteService"/>) と
/// 申請・承認 (<see cref="AttendanceService"/>) の双方が使う純粋な射影・判定をまとめる (原則3)。
/// </summary>
public static class AttendanceApproval
{
    /// <summary>
    /// オーナー (勤怠管理者) か。AuthEndpoints.CheckAttendanceAdminAsync と同一判定
    /// (勤怠権限 1/2 かつ process_record_permission &gt;= 1)。承認者フォールバック・承認可否の基準。
    /// </summary>
    public static bool IsOwner(User u)
        => (u.AttendancePermission == 1 || u.AttendancePermission == 2)
           && u.ProcessRecordPermission >= 1;

    /// <summary>在籍ユーザ → 承認者候補。</summary>
    public static ApproverCandidate ToCandidate(User u)
        => new(u.Id, u.EmployeeNo, u.Title, IsOwner(u), u.IsActive && u.DeletedAt == null);

    /// <summary>経路ステップ定義 → 純粋な承認者指定。</summary>
    public static ApproverStepSpec SpecOf(AttendanceRouteStep s)
        => new(s.StepOrder, s.ApproverType, s.ApproverRole, s.ApproverTitle, s.ApproverUserId, s.Mode);

    /// <summary>凍結スナップショット → 純粋な承認者指定。</summary>
    public static ApproverStepSpec SpecOf(AttendanceRequestStep s)
        => new(s.StepOrder, s.ApproverType, s.ApproverRole, s.ApproverTitle, s.ApproverUserId, s.Mode);

    /// <summary>
    /// 承認者指定 → レスポンス DTO。member 指定は <paramref name="userNames"/> で氏名を解決する
    /// (未解決 = 無効化・削除済みでも null で返し、UI は id 表示等にフォールバックできる)。
    /// </summary>
    public static ApproverStepDto ToStepDto(ApproverStepSpec spec, IReadOnlyDictionary<Guid, string> userNames)
    {
        string? name = null;
        if (spec.ApproverType == ApproverType.Member && spec.ApproverUserId is { } uid
            && userNames.TryGetValue(uid, out var n))
            name = n;
        return new ApproverStepDto(
            spec.Order, spec.ApproverType, spec.ApproverRole,
            spec.ApproverTitle, spec.ApproverUserId, name, spec.Mode);
    }
}
