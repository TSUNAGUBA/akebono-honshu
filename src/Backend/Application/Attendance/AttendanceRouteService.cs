using Akebono.Application.Common;
using Akebono.Domain.Attendance;
using Akebono.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Attendance;

/// <summary>
/// 勤怠承認経路 (attendance_routes / attendance_route_steps) の CRUD サービス (Iteration 33)。
///
/// 設計方針 (勤務体系マスタ <see cref="AttendanceRuleService"/> に倣う):
///   - 参照は勤怠権限 (1 or 2)、書込は勤怠参照権限 AND オーナー (エンドポイント側で確認済み)。
///   - 更新は経路の **ステップ一式の差し替え** (承認経路の編集はステップの全置換で表現する)。
///     null のフィールドは更新しない (部分更新。CLAUDE.md の Zod .partial() の教訓と同趣旨)。
///   - 削除は論理削除 (deleted_at)。復元エンドポイントで取り消せる (操作の取消可能性)。
///   - 承認者が member(個人) 指定のステップは、指定ユーザが当テナントに実在することを検証する
///     (approver_user_id → users(id) の FK 違反を 500 でなく 422 で返すため)。
/// </summary>
public class AttendanceRouteService(IAkebonoDbContext db, IAuditLogger audit)
{
    private const int TitleMaxLength = 64;
    private const int MaxSteps = 20;

    /// <summary>一覧。includeInactive=false は「有効 かつ 未削除」のみ。category 昇順 → id 昇順。</summary>
    public async Task<IReadOnlyList<AttendanceRouteDto>> ListAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var query = db.AttendanceRoutes.AsNoTracking();
        if (!includeInactive) query = query.Where(r => r.DeletedAt == null && r.IsActive);

        var routes = await query.OrderBy(r => r.Category).ThenBy(r => r.Id).ToListAsync(ct);
        if (routes.Count == 0) return [];

        var routeIds = routes.Select(r => r.Id).ToList();
        var steps = await db.AttendanceRouteSteps.AsNoTracking()
            .Where(s => routeIds.Contains(s.RouteId))
            .ToListAsync(ct);
        var names = await ResolveMemberNamesAsync(steps.Select(s => s.ApproverUserId), ct);

        var byRoute = steps.GroupBy(s => s.RouteId).ToDictionary(g => g.Key, g => g.ToList());
        return routes.Select(r => ToDto(r, byRoute.GetValueOrDefault(r.Id, []), names)).ToList();
    }

    /// <summary>
    /// 新規作成 (経路 + ステップ)。ステップは attendance_route_steps → attendance_routes の FK を持つため、
    /// 親を先に保存してから子を保存する (PurchaseOrderService と同じ順序保存パターン。単一トランザクション)。
    /// </summary>
    public async Task<AttendanceRouteDto> CreateAsync(
        AttendanceRouteWriteRequest req, Guid actorId, CancellationToken ct = default)
    {
        var category = ParseCategory(req.Category);
        var parsed = ParseSteps(req.Steps);
        await EnsureMemberApproversExistAsync(parsed, ct);

        var now = SystemTime.UtcNow;
        var route = new AttendanceRoute
        {
            Id = Guid.NewGuid(),
            Category = category,
            IsActive = req.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.AttendanceRoutes.Add(route);
            await db.SaveChangesAsync(ct);          // 親 (route) を先に確定させる (FK 先)
            AddStepsFor(route.Id, parsed, now);
            await db.SaveChangesAsync(ct);           // 子 (steps) を追加
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await audit.LogAsync(actorId, "AttendanceRoute.Create",
            entityType: "AttendanceRoute", entityId: route.Id,
            note: $"Category={category}, Steps={parsed.Count}", cancellationToken: ct);

        return await GetDtoAsync(route.Id, ct) ?? ToDto(route, [], new Dictionary<Guid, string>());
    }

    /// <summary>
    /// 部分更新。未指定 (null) は現在値を保持する。Steps を指定した場合はステップを全置換する
    /// (旧ステップを先に削除・確定 → 新ステップを追加。UNIQUE(route_id, step_order) の一時衝突を避ける
    ///  = PurchaseOrderService.UpdateAsync と同じ手順)。Id 不在は null 返却 (エンドポイントで 404)。
    /// </summary>
    public async Task<AttendanceRouteDto?> UpdateAsync(
        Guid id, AttendanceRoutePatchRequest req, Guid actorId, CancellationToken ct = default)
    {
        var route = await db.AttendanceRoutes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (route is null) return null;

        var now = SystemTime.UtcNow;
        List<ParsedStep>? parsed = null;
        if (req.Steps is not null)
        {
            parsed = ParseSteps(req.Steps);
            await EnsureMemberApproversExistAsync(parsed, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (parsed is not null)
            {
                // 旧ステップを削除・確定してから新ステップを追加する (order の一時衝突を避ける)。
                var existing = await db.AttendanceRouteSteps.Where(s => s.RouteId == id).ToListAsync(ct);
                db.AttendanceRouteSteps.RemoveRange(existing);
                await db.SaveChangesAsync(ct);
                AddStepsFor(id, parsed, now);
            }

            if (req.IsActive is { } active) route.IsActive = active;
            route.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await audit.LogAsync(actorId, "AttendanceRoute.Update",
            entityType: "AttendanceRoute", entityId: route.Id,
            note: $"Category={route.Category}", cancellationToken: ct);

        return await GetDtoAsync(route.Id, ct);
    }

    /// <summary>論理削除 (deleted_at SET + 無効化)。復元で取り消せる。</summary>
    public async Task<bool> SoftDeleteAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var route = await db.AttendanceRoutes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (route is null) return false;

        var now = SystemTime.UtcNow;
        route.DeletedAt = now;
        // 削除済み経路が解決に混ざらないよう無効化する (設定系の整合を優先)。
        route.IsActive = false;
        route.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceRoute.Delete",
            entityType: "AttendanceRoute", entityId: route.Id,
            note: $"Category={route.Category}", cancellationToken: ct);
        return true;
    }

    /// <summary>論理削除の取消。IsActive は削除時に落ちているため復元時に true へ戻す。</summary>
    public async Task<bool> RestoreAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var route = await db.AttendanceRoutes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (route is null) return false;

        route.DeletedAt = null;
        route.IsActive = true;
        route.UpdatedAt = SystemTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceRoute.Restore",
            entityType: "AttendanceRoute", entityId: route.Id,
            note: $"Category={route.Category}", cancellationToken: ct);
        return true;
    }

    // ── 内部ヘルパー ─────────────────────────────────────────────────────

    private void AddStepsFor(Guid routeId, IReadOnlyList<ParsedStep> steps, DateTime now)
    {
        foreach (var s in steps)
            db.AttendanceRouteSteps.Add(new AttendanceRouteStep
            {
                RouteId = routeId,
                StepOrder = s.Order,
                ApproverType = s.Type,
                ApproverRole = s.Role,
                ApproverTitle = s.Title,
                ApproverUserId = s.UserId,
                Mode = s.Mode,
                CreatedAt = now,
            });
    }

    private async Task<AttendanceRouteDto?> GetDtoAsync(Guid id, CancellationToken ct)
    {
        var route = await db.AttendanceRoutes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (route is null) return null;
        var steps = await db.AttendanceRouteSteps.AsNoTracking()
            .Where(s => s.RouteId == id).ToListAsync(ct);
        var names = await ResolveMemberNamesAsync(steps.Select(s => s.ApproverUserId), ct);
        return ToDto(route, steps, names);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveMemberNamesAsync(
        IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var idList = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<Guid, string>();
        return await db.Users.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    /// <summary>member 指定の承認者が全て当テナントに実在するか検証する (FK 違反を 422 で先に弾く)。</summary>
    private async Task EnsureMemberApproversExistAsync(IReadOnlyList<ParsedStep> steps, CancellationToken ct)
    {
        var memberIds = steps
            .Where(s => s.Type == ApproverType.Member && s.UserId is not null)
            .Select(s => s.UserId!.Value)
            .Distinct()
            .ToList();
        if (memberIds.Count == 0) return;

        var found = await db.Users.AsNoTracking()
            .Where(u => memberIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (found.Count != memberIds.Count)
            throw DomainException.Validation("承認者に指定した利用者が見つかりません",
                "承認者（個人）を選び直してください");
    }

    private static AttendanceRouteDto ToDto(
        AttendanceRoute r, IReadOnlyList<AttendanceRouteStep> steps,
        IReadOnlyDictionary<Guid, string> names)
    {
        var stepDtos = steps
            .OrderBy(s => s.StepOrder)
            .Select(s => AttendanceApproval.ToStepDto(AttendanceApproval.SpecOf(s), names))
            .ToList();
        return new AttendanceRouteDto(r.Id, r.Category, r.IsActive, stepDtos, r.DeletedAt);
    }

    // ── 入力検証 (すべて 422 AKB-SYS-002) ─────────────────────────────────

    private sealed record ParsedStep(
        int Order, ApproverType Type, ApproverRole? Role, string? Title, Guid? UserId, ApprovalMode Mode);

    private static List<ParsedStep> ParseSteps(IReadOnlyList<ApproverStepWriteRequest>? steps)
    {
        if (steps is null || steps.Count == 0)
            throw DomainException.Validation("承認ステップを 1 つ以上設定してください",
                "承認者を 1 名以上追加してください");
        if (steps.Count > MaxSteps)
            throw DomainException.Validation($"承認ステップは {MaxSteps} 件までです",
                "ステップ数を減らしてください");

        var parsed = steps.Select(ParseStep).ToList();

        if (parsed.Select(s => s.Order).Distinct().Count() != parsed.Count)
            throw DomainException.Validation("承認ステップの順序（order）が重複しています",
                "各ステップの順序が重複しないように設定してください");

        return parsed;
    }

    private static ParsedStep ParseStep(ApproverStepWriteRequest s)
    {
        if (s.Order < 1)
            throw DomainException.Validation("承認ステップの順序（order）は 1 以上で指定してください",
                "ステップの順序を 1 以上にしてください");

        var type = ParseApproverType(s.ApproverType);
        var mode = ParseMode(s.Mode);
        ApproverRole? role = null;
        string? title = null;
        Guid? userId = null;

        switch (type)
        {
            case ApproverType.Title:
                title = (s.ApproverTitle ?? string.Empty).Trim();
                if (title.Length == 0)
                    throw DomainException.Validation("役職を選択してください",
                        "承認者の役職を指定してください");
                if (title.Length > TitleMaxLength)
                    throw DomainException.Validation($"役職は {TitleMaxLength} 文字以内で指定してください",
                        "役職名を短くしてください");
                break;
            case ApproverType.Role:
                role = ParseApproverRole(s.ApproverRole);
                break;
            default: // Member
                var raw = (s.ApproverUserId ?? string.Empty).Trim();
                if (raw.Length == 0 || !Guid.TryParse(raw, out var uid))
                    throw DomainException.Validation("承認者（個人）を選択してください",
                        "承認者を 1 名選択してください");
                userId = uid;
                break;
        }

        return new ParsedStep(s.Order, type, role, title, userId, mode);
    }

    private static AttendanceRequestCategory ParseCategory(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "direct" => AttendanceRequestCategory.Direct,
            "fix" => AttendanceRequestCategory.Fix,
            _ => throw DomainException.Validation("区分は direct / fix のいずれかを指定してください",
                "承認経路の区分を選び直してください"),
        };

    private static ApproverType ParseApproverType(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "title" => ApproverType.Title,
            "role" => ApproverType.Role,
            "member" => ApproverType.Member,
            _ => throw DomainException.Validation("承認者の指定方法は title / role / member のいずれかを指定してください",
                "承認者の指定方法（役職 / ロール / 個人）を選び直してください"),
        };

    private static ApproverRole ParseApproverRole(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "owner" => ApproverRole.Owner,
            _ => throw DomainException.Validation("ロールを選択してください",
                "承認者のロール（オーナー）を選択してください"),
        };

    private static ApprovalMode ParseMode(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v.Length == 0) return ApprovalMode.Serial; // 既定
        return v switch
        {
            "serial" => ApprovalMode.Serial,
            "all" => ApprovalMode.All,
            "majority" => ApprovalMode.Majority,
            _ => throw DomainException.Validation("承認方式は serial / all / majority のいずれかを指定してください",
                "承認方式を選び直してください"),
        };
    }
}
