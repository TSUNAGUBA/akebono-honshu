namespace Akebono.Domain.Attendance;

/// <summary>
/// 承認者候補 (在籍者)。<see cref="ApproverResolver.Pick"/> の入力。
/// <see cref="IsOwner"/> は honshu の「オーナー (勤怠管理者)」= 勤怠権限 1/2 かつ
/// process_record_permission &gt;= 1 (AuthEndpoints.CheckAttendanceAdminAsync と同じ判定) を表す。
/// office の MemberRole='admin' に相当し、承認者が解決できないときのフォールバック先になる。
/// </summary>
public sealed record ApproverCandidate(
    Guid Id,
    string EmployeeNo,
    string? Title,
    bool IsOwner,
    bool IsActive);

/// <summary>
/// 承認者の解決 (純粋関数・EF/DB 非依存)。承認ステップの指定方法 (役職/ロール/個人) から
/// 在籍メンバーを決定的に 1 名選ぶ。office の shared/domain/approver.ts の pickApprover を honshu へ移植。
///
/// honshu には office の旧形式 (approverRole=president/director/... の legacy) の経路データが
/// 存在しない (本機能で初めて経路を持つ) ため、legacy 正規化は移植しない (原則3: 不要な抽象を作らない)。
/// </summary>
public static class ApproverResolver
{
    /// <summary>
    /// 承認ステップ → 承認者 (在籍者) を 1 名解決する。
    ///   - Member: 指定ユーザ (在籍優先。不在なら任意のオーナーへフォールバック)
    ///   - Title:  役職ラベル一致 (決定的順の先頭)。不在なら任意のオーナー
    ///   - Role:   ロール (現状 Owner のみ)。任意のオーナー
    /// 該当が全く無ければ null (呼び出し側は管理者単段等へフォールバックする)。
    ///
    /// 決定性: 在籍者を employee_no (テナント内一意) → id で整列し first-match を採る
    /// (office は id 昇順。honshu は氏名でなく employee_no を安定キーに使う既存慣習に合わせる)。
    /// </summary>
    public static ApproverCandidate? Pick(IReadOnlyList<ApproverCandidate> candidates, ApproverStepSpec step)
    {
        var actives = candidates
            .Where(c => c.IsActive)
            .OrderBy(c => c.EmployeeNo, StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToList();
        var anyOwner = actives.FirstOrDefault(c => c.IsOwner);

        return step.ApproverType switch
        {
            ApproverType.Member => actives.FirstOrDefault(c => c.Id == step.ApproverUserId) ?? anyOwner,
            // 役職ラベルは完全一致。指定が空 (誤設定) のときは一致者なし → オーナーへフォールバック。
            ApproverType.Title => (string.IsNullOrEmpty(step.ApproverTitle)
                ? null
                : actives.FirstOrDefault(c => c.Title == step.ApproverTitle)) ?? anyOwner,
            // Role は現状 Owner のみ。オーナー該当者 (= フォールバック先) をそのまま返す。
            _ => anyOwner,
        };
    }
}
