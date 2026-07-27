namespace Akebono.Domain.Attendance;

/// <summary>休暇の取得単位 (leave_requests.unit)。残数への引当日数は 全日=1.0 / 半日=0.5。</summary>
public enum LeaveUnit : short
{
    /// <summary>全日 (1.0 日)</summary>
    Full = 0,
    /// <summary>半日 (0.5 日)</summary>
    Half = 1,
}
