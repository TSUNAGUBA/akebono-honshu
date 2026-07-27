namespace Akebono.Domain.Attendance;

/// <summary>
/// 打刻の入力経路 (punch_records.source)。
/// <see cref="Fix"/> は打刻修正申請の承認により追記された打刻で、
/// <c>fixed_from</c> が指す旧打刻を論理的に置換する (元打刻は削除しない = 記録系保護)。
/// </summary>
public enum PunchSource : short
{
    /// <summary>Web 画面からの打刻</summary>
    Web = 0,
    /// <summary>モバイルからの打刻</summary>
    Mobile = 1,
    /// <summary>打刻修正申請の承認による追記 (論理置換)</summary>
    Fix = 2,
}
