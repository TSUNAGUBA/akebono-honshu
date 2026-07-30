using Akebono.Domain.Common;

namespace Akebono.Domain.Attendance;

/// <summary>
/// 勤怠承認経路 (attendance_routes)。区分 (<see cref="AttendanceRequestCategory"/>) ごとに承認ステップを持つ。
/// 稟議の WorkflowRoute と異なり金額帯は持たず、区分だけで経路を選ぶ
/// (<see cref="AttendanceRouteResolver.Resolve"/>)。ステップは <see cref="AttendanceRouteStep"/> に持つ
/// (honshu は jsonb を使わず子テーブルで正規化する。ナビゲーションは張らず Service が route_id で明示取得)。
///
/// 論理削除 (deleted_at) + 復元に対応 (勤務体系マスタ <see cref="AttendanceRule"/> と同方針)。
/// 区分に有効な経路が無い場合、申請は管理者 1 名の単段承認へフォールバックする (下位互換 = 原則7)。
/// </summary>
public class AttendanceRoute : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public AttendanceRequestCategory Category { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>論理削除 (deleted_at TIMESTAMPTZ NULL)。復元で取り消せる。</summary>
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
