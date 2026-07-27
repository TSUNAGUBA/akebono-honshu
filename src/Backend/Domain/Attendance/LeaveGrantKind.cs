namespace Akebono.Domain.Attendance;

/// <summary>
/// 休暇付与の区分 (leave_grants.kind)。
/// 労基法 39 条の通常付与 / 比例付与 (週所定日数が少ない労働者) と、
/// 特別休暇等の任意付与を区別する。
/// </summary>
public enum LeaveGrantKind : short
{
    /// <summary>通常付与 (週30時間以上 または 週5日以上)</summary>
    Normal = 0,
    /// <summary>比例付与 (週4日以下かつ週30時間未満)</summary>
    Proportional = 1,
    /// <summary>特別付与 (手動付与・特別休暇等)</summary>
    Special = 2,
}
