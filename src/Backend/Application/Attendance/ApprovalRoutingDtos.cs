using Akebono.Domain.Attendance;

namespace Akebono.Application.Attendance;

// ─────────────────────────────────────────────────────────────────────────────
// 勤怠承認経路 (Iteration 33) の DTO。
//   - JSON は camelCase、enum は camelCase 文字列 (Program.cs の JsonStringEnumConverter)。
//   - レスポンス側は typed enum (camelCase 文字列で出力)。リクエスト側の enum 判別子
//     (category / approverType / approverRole / mode / type / action) は **string で受け取り**、
//     Service で明示検証して 422 (AKB-SYS-002) を返す (JSON 変換失敗の 400 を避ける = AttendanceDtos と同方針)。
//   - 追加フィールドは必ず末尾へデフォルト値付きで足すこと (原則7 下位互換)。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 承認ステップ (経路定義・凍結スナップショット 共通のレスポンス表現)。
/// <see cref="ApproverUserName"/> は approverType=member のとき解決した氏名 (無効化ユーザでも表示のため保持)。
/// </summary>
public record ApproverStepDto(
    int Order,
    ApproverType ApproverType,
    ApproverRole? ApproverRole,
    string? ApproverTitle,
    Guid? ApproverUserId,
    string? ApproverUserName,
    ApprovalMode Mode);

/// <summary>勤怠承認経路。</summary>
public record AttendanceRouteDto(
    Guid Id,
    AttendanceRequestCategory Category,
    bool IsActive,
    IReadOnlyList<ApproverStepDto> Steps,
    DateTime? DeletedAt);

/// <summary>承認ステップの書込ペイロード (enum 判別子は文字列で受ける)。</summary>
public record ApproverStepWriteRequest(
    int Order,
    string ApproverType,
    string? ApproverRole = null,
    string? ApproverTitle = null,
    string? ApproverUserId = null,
    string? Mode = null);

/// <summary>経路の新規作成ペイロード。category は作成時のみ (以後不変)。steps は 1 件以上。</summary>
public record AttendanceRouteWriteRequest(
    string Category,
    IReadOnlyList<ApproverStepWriteRequest> Steps,
    bool IsActive = true);

/// <summary>
/// 経路の更新ペイロード (部分更新)。null のフィールドは更新しない。
/// Steps を指定した場合は経路のステップを **全置換** する (承認経路の編集 = ステップ一式の差し替え)。
/// category は識別キーのため更新対象にしない。
/// </summary>
public record AttendanceRoutePatchRequest(
    IReadOnlyList<ApproverStepWriteRequest>? Steps = null,
    bool? IsActive = null);

/// <summary>直行/直帰申請の作成リクエスト。Type は "chokkou" / "chokki" / "both"。</summary>
public record DirectRequestCreateRequest(string Date, string Type, string Reason);

/// <summary>直行/直帰申請。RouteSnapshot は申請時に凍結した経路 (空 = 管理者単段)。</summary>
public record DirectRequestDto(
    Guid Id,
    Guid UserId,
    string UserName,
    DateOnly Date,
    DirectType Type,
    string Reason,
    DirectRequestStatus Status,
    int CurrentStep,
    IReadOnlyList<ApproverStepDto> RouteSnapshot,
    Guid? DecidedByUserId,
    string? DecidedByUserName,
    DateTime CreatedAt);

/// <summary>直行/直帰申請の承認/却下/取下げ。Action は "approved" / "rejected" / "withdrawn"。</summary>
public record DirectDecisionRequest(string Action);
