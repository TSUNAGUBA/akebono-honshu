namespace Akebono.Domain.Attendance;

/// <summary>
/// 休暇種別の付与方式 (leave_types.grant_method)。
/// <see cref="Periodic"/> は入社日起算の周期自動付与 (労基法 39 条) の対象で、手動付与はできない。
/// </summary>
public enum LeaveGrantMethod : short
{
    /// <summary>周期自動付与 (入社日 + 6ヶ月、以降 1 年ごと)</summary>
    Periodic = 0,
    /// <summary>手動付与 (個別付与・一括付与)</summary>
    Manual = 1,
}
