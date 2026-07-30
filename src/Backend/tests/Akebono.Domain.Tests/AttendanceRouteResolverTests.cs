using Akebono.Domain.Attendance;
using Xunit;

namespace Akebono.Domain.Tests;

/// <summary>
/// <see cref="AttendanceRouteResolver"/> の単体テスト (副作用なし・DB 非依存)。
/// office shared/domain/attendance-route.ts の resolveAttendanceRoute / directKindsOf 移植を検証する。
/// </summary>
public class AttendanceRouteResolverTests
{
    private static ApproverStepSpec Step(int order)
        => new(order, ApproverType.Role, ApproverRole.Owner, null, null, ApprovalMode.Serial);

    private static RouteCandidate Route(int idByte, params int[] orders)
        => new(new Guid($"00000000-0000-0000-0000-{idByte:D12}"),
               orders.Select(Step).ToList());

    // ── Resolve ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoCandidates_ReturnsEmpty()
        => Assert.Empty(AttendanceRouteResolver.Resolve([]));

    [Fact]
    public void Resolve_IgnoresRoutesWithNoSteps()
    {
        var result = AttendanceRouteResolver.Resolve([Route(1)]); // 0 ステップ
        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_PicksRouteWithMostSteps()
    {
        // 1 ステップ経路と 2 ステップ経路 → より厳格な (ステップ数が多い) 方を採用する。
        var result = AttendanceRouteResolver.Resolve([Route(1, 1), Route(2, 1, 2)]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Resolve_ReturnsStepsSortedByOrder()
    {
        var result = AttendanceRouteResolver.Resolve([Route(1, 3, 1, 2)]);
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(s => s.Order).ToArray());
    }

    // ── DirectKinds ─────────────────────────────────────────────────────────

    [Fact]
    public void DirectKinds_Chokkou_OnlyIn()
        => Assert.Equal(new[] { PunchKind.In }, AttendanceRouteResolver.DirectKinds(DirectType.Chokkou).ToArray());

    [Fact]
    public void DirectKinds_Chokki_OnlyOut()
        => Assert.Equal(new[] { PunchKind.Out }, AttendanceRouteResolver.DirectKinds(DirectType.Chokki).ToArray());

    [Fact]
    public void DirectKinds_Both_InAndOut()
        => Assert.Equal(new[] { PunchKind.In, PunchKind.Out }, AttendanceRouteResolver.DirectKinds(DirectType.Both).ToArray());
}
