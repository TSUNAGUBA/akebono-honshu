namespace Akebono.Domain.Attendance;

/// <summary>経路候補 (有効かつ対象区分の経路 1 件とそのステップ)。<see cref="AttendanceRouteResolver.Resolve"/> の入力。</summary>
public sealed record RouteCandidate(Guid Id, IReadOnlyList<ApproverStepSpec> Steps);

/// <summary>
/// 勤怠承認経路の解決 (純粋関数・EF/DB 非依存)。office の shared/domain/attendance-route.ts を移植。
/// 呼び出し側 (AttendanceService) は「有効 (is_active) かつ未削除かつ対象区分」の経路のみを候補として渡す。
/// </summary>
public static class AttendanceRouteResolver
{
    /// <summary>
    /// 候補から採用する経路のステップ (order 昇順) を返す。該当が無ければ空
    /// (呼び出し側は管理者 1 名の単段承認へフォールバックする)。
    /// 同一区分に複数の有効経路がある場合は **ステップ数が多い (= より厳格な)** 定義を採用し、
    /// タイは id 昇順で決定的に選ぶ (運用ミスで複数登録されても「緩い方が勝つ」事故を避ける)。
    /// </summary>
    public static IReadOnlyList<ApproverStepSpec> Resolve(IReadOnlyList<RouteCandidate> candidates)
    {
        var hit = candidates
            .Where(c => c.Steps.Count > 0)
            .OrderByDescending(c => c.Steps.Count)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
        if (hit is null) return [];
        return hit.Steps.OrderBy(s => s.Order).ToList();
    }

    /// <summary>
    /// 直行/直帰の種別が「打刻修正を申請できる打刻種別」を返す。
    /// Chokkou(直行)=出勤(In) / Chokki(直帰)=退勤(Out) / Both(直行直帰)=両方。
    /// 所定の休憩・その他の打刻には影響しない (In/Out のみ対象)。
    /// </summary>
    public static IReadOnlyList<PunchKind> DirectKinds(DirectType type) => type switch
    {
        DirectType.Chokkou => [PunchKind.In],
        DirectType.Chokki => [PunchKind.Out],
        _ => [PunchKind.In, PunchKind.Out],
    };
}
