namespace Akebono.Domain.Attendance;

/// <summary>
/// 承認ステップの承認者指定 (純粋な値オブジェクト。EF エンティティ非依存)。
/// 経路定義 (<see cref="AttendanceRouteStep"/>)・凍結スナップショット (<see cref="AttendanceRequestStep"/>)・
/// API DTO のいずれからも本型へ射影し、承認者解決 (<see cref="ApproverResolver.Pick"/>) と
/// 経路解決 (<see cref="AttendanceRouteResolver.Resolve"/>) を一元化する。
/// </summary>
public sealed record ApproverStepSpec(
    int Order,
    ApproverType ApproverType,
    ApproverRole? ApproverRole,
    string? ApproverTitle,
    Guid? ApproverUserId,
    ApprovalMode Mode);
