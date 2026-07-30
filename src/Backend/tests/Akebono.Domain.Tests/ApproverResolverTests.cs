using Akebono.Domain.Attendance;
using Xunit;

namespace Akebono.Domain.Tests;

/// <summary>
/// <see cref="ApproverResolver.Pick"/> の単体テスト (副作用なし・DB 非依存)。
/// office shared/domain/approver.ts の pickApprover 移植の挙動を検証する:
///   在籍フィルタ → employee_no 昇順の決定性 → 役職/ロール/個人の解決 → オーナーへのフォールバック → null。
/// </summary>
public class ApproverResolverTests
{
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static ApproverCandidate Member(int n, string empNo, string? title = null, bool owner = false, bool active = true)
        => new(Id(n), empNo, title, owner, active);

    private static ApproverStepSpec Step(ApproverType type, ApproverRole? role = null, string? title = null, Guid? userId = null)
        => new(1, type, role, title, userId, ApprovalMode.Serial);

    // ── 個人 (member) ─────────────────────────────────────────────────────

    [Fact]
    public void Pick_Member_ReturnsSpecifiedActiveUser()
    {
        var cands = new[] { Member(1, "001", owner: true), Member(2, "002") };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Member, userId: Id(2)));
        Assert.Equal(Id(2), picked!.Id);
    }

    [Fact]
    public void Pick_Member_InactiveTarget_FallsBackToFirstOwner()
    {
        // 指定メンバー (m2) が非在籍 → 任意のオーナー (m1) へフォールバック。
        var cands = new[] { Member(1, "001", owner: true), Member(2, "002", active: false) };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Member, userId: Id(2)));
        Assert.Equal(Id(1), picked!.Id);
    }

    // ── 役職 (title) ──────────────────────────────────────────────────────

    [Fact]
    public void Pick_Title_MatchesByTitle()
    {
        var cands = new[] { Member(1, "001", title: "部長", owner: true), Member(2, "002", title: "課長") };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Title, title: "課長"));
        Assert.Equal(Id(2), picked!.Id);
    }

    [Fact]
    public void Pick_Title_NoMatch_FallsBackToFirstOwner()
    {
        var cands = new[] { Member(1, "001", title: "部長", owner: true), Member(2, "002", title: "課長") };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Title, title: "存在しない役職"));
        Assert.Equal(Id(1), picked!.Id);
    }

    [Fact]
    public void Pick_Title_TieBrokenByEmployeeNoAscending()
    {
        // 同じ役職が複数在籍 → employee_no 昇順の先頭 ("001") を決定的に選ぶ。
        var cands = new[] { Member(2, "002", title: "課長"), Member(1, "001", title: "課長"), Member(9, "009", owner: true) };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Title, title: "課長"));
        Assert.Equal("001", picked!.EmployeeNo);
    }

    // ── ロール (owner) ────────────────────────────────────────────────────

    [Fact]
    public void Pick_Role_Owner_ReturnsFirstOwner()
    {
        var cands = new[] { Member(1, "001"), Member(2, "002", owner: true), Member(3, "003", owner: true) };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Role, role: ApproverRole.Owner));
        Assert.Equal("002", picked!.EmployeeNo);
    }

    // ── フォールバックが尽きるケース ─────────────────────────────────────

    [Fact]
    public void Pick_NoOwnerAndNoMatch_ReturnsNull()
    {
        // オーナー不在 + 指定メンバー不在 → null (呼び出し側は管理者単段フォールバックへ)。
        var cands = new[] { Member(1, "001"), Member(2, "002") };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Member, userId: Id(99)));
        Assert.Null(picked);
    }

    [Fact]
    public void Pick_InactiveUsersExcluded()
    {
        // 非在籍のオーナーはフォールバック先にならない。
        var cands = new[] { Member(1, "001", owner: true, active: false), Member(2, "002") };
        var picked = ApproverResolver.Pick(cands, Step(ApproverType.Role, role: ApproverRole.Owner));
        Assert.Null(picked);
    }
}
