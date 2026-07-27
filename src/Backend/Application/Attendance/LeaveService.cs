using System.Globalization;
using Akebono.Application.Common;
using Akebono.Domain.Attendance;
using Akebono.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Attendance;

/// <summary>
/// 休暇 (有給・特別休暇) Application サービス (移植仕様 §4.9 / §5.4)。
///
/// 責務:
///   - 休暇種別マスタの CRUD (#15〜#19)
///   - 残数 (FIFO 引当) / 年 5 日取得義務 / 履歴のサマリ (#20 / #27)
///   - 休暇申請の作成・一覧・承認/却下 (#21〜#23)
///   - 付与: 個別 / 一括 / 周期自動 (#24〜#26)
///
/// 設計上の約束:
///   - 残数・義務・失効日・周期付与の**計算は全て純粋関数の
///     <see cref="LeaveCalc"/> (Domain 層) が SoT**。本サービスは I/O と冪等制御のみを担う。
///   - **付与は追記のみ (原則 2)**。同一 (user, leaveType, grantDate) が既にあればスキップし、
///     既存の付与レコードは絶対に更新・削除しない。一括/周期付与は「事前 SELECT で除外 → AddRange」。
///   - 法定有給 (IsStatutory=true) の種別は作成・編集・論理削除を禁止する (409 AKB-SYS-007)。
///   - 監査ログは主処理の commit 後に記録する (§7)。記録失敗は AuditLogger 側で warning 化され
///     主要フローを止めない (原則 4)。
/// </summary>
public class LeaveService(IAkebonoDbContext db, IAuditLogger audit)
{
    // ═══════════════════════════════════════════════
    // 休暇種別 (#15〜#19)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 休暇種別の一覧 (#15)。
    /// includeInactive=false: 有効かつ未削除のみ。true: 無効・論理削除済も含む (復元 UI 用)。
    /// </summary>
    public async Task<List<LeaveTypeDto>> ListTypesAsync(bool includeInactive, CancellationToken ct = default)
    {
        var query = db.LeaveTypes.AsQueryable();
        if (!includeInactive) query = query.Where(t => t.DeletedAt == null && t.IsActive);

        var types = await query
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);

        return types.Select(ToDto).ToList();
    }

    /// <summary>休暇種別の作成 (#16)。IsStatutory は常に false (法定有給は API から作成できない)。</summary>
    public async Task<LeaveTypeDto> CreateTypeAsync(
        LeaveTypeWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var name = (req.Name ?? string.Empty).Trim();
        ValidateTypeName(name);
        ValidateExpiryMonths(req.ExpiryMonths);
        ValidateDisplayOrder(req.DisplayOrder);
        await EnsureNoActiveDuplicateNameAsync(name, null, ct);

        var now = SystemTime.UtcNow;
        var entity = new LeaveType
        {
            Name = name,
            GrantMethod = req.GrantMethod,
            ExpiryMonths = req.ExpiryMonths,
            // 法定有給は db/init のシード 1 件のみ。API 経由では作れない (改竄防止、§5.4)。
            IsStatutory = false,
            Description = (req.Description ?? string.Empty).Trim(),
            DisplayOrder = req.DisplayOrder,
            IsActive = req.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "LeaveType.Create", entityType: "LeaveType", entityId: entity.Id,
            note: $"name={entity.Name}", cancellationToken: ct);

        return ToDto(entity);
    }

    /// <summary>
    /// 休暇種別の部分更新 (#17)。**null のフィールドは更新しない**(既存値を保持する)。
    /// 法定有給は 409。存在しない場合は null を返す (endpoint が 404 へ変換)。
    /// </summary>
    public async Task<LeaveTypeDto?> UpdateTypeAsync(
        Guid id, LeaveTypePatchRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return null;
        EnsureNotStatutory(entity, "法定有給の種別は変更できません");

        if (req.Name is not null)
        {
            var name = req.Name.Trim();
            ValidateTypeName(name);
            await EnsureNoActiveDuplicateNameAsync(name, id, ct);
            entity.Name = name;
        }

        if (req.GrantMethod is { } grantMethod) entity.GrantMethod = grantMethod;

        // ClearExpiryMonths が明示された場合のみ無期限へ戻す。
        // それ以外で ExpiryMonths が null なら「未指定」として既存値を保持する。
        if (req.ClearExpiryMonths)
        {
            entity.ExpiryMonths = null;
        }
        else if (req.ExpiryMonths is { } expiryMonths)
        {
            ValidateExpiryMonths(expiryMonths);
            entity.ExpiryMonths = expiryMonths;
        }

        if (req.Description is not null) entity.Description = req.Description.Trim();
        if (req.DisplayOrder is { } displayOrder)
        {
            ValidateDisplayOrder(displayOrder);
            entity.DisplayOrder = displayOrder;
        }
        if (req.IsActive is { } isActive) entity.IsActive = isActive;

        entity.UpdatedAt = SystemTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "LeaveType.Update", entityType: "LeaveType", entityId: entity.Id,
            note: $"name={entity.Name}", cancellationToken: ct);

        return ToDto(entity);
    }

    /// <summary>休暇種別の論理削除 (#18)。付与・申請の実績は残す (記録系保護)。</summary>
    public async Task<bool> SoftDeleteTypeAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;
        EnsureNotStatutory(entity, "法定有給の種別は削除できません");

        var now = SystemTime.UtcNow;
        entity.DeletedAt = now;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "LeaveType.Delete", entityType: "LeaveType", entityId: entity.Id,
            note: $"name={entity.Name}", cancellationToken: ct);

        return true;
    }

    /// <summary>休暇種別の復元 (#19)。同名の有効な種別がある場合は 409。</summary>
    public async Task<bool> RestoreTypeAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;

        // 復元先に同名の有効行がある場合は部分 UNIQUE 索引に抵触するため事前に弾く。
        if (entity.DeletedAt != null)
            await EnsureNoActiveDuplicateNameAsync(entity.Name, id, ct);

        entity.DeletedAt = null;
        entity.UpdatedAt = SystemTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "LeaveType.Restore", entityType: "LeaveType", entityId: entity.Id,
            note: $"name={entity.Name}", cancellationToken: ct);

        return true;
    }

    // ═══════════════════════════════════════════════
    // サマリ (#20 / #27)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 本人 (またはオーナーが指定したメンバー) の休暇サマリ (#20)。
    /// 残数は FIFO 引当・年 5 日義務・失効日いずれも <see cref="LeaveCalc"/> が算出する。
    /// </summary>
    public async Task<LeaveSummaryDto> GetSummaryAsync(Guid targetUserId, CancellationToken ct = default)
    {
        var today = SystemTime.TodayJst;

        // 履歴には削除済種別の実績も出るため、名称解決用には全種別を読む。
        var allTypes = await db.LeaveTypes
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);
        var typeById = allTypes.ToDictionary(t => t.Id);

        var grants = await db.LeaveGrants.Where(g => g.UserId == targetUserId).ToListAsync(ct);
        var requests = await db.LeaveRequests.Where(r => r.UserId == targetUserId).ToListAsync(ct);

        var byType = new List<LeaveByTypeDto>();
        foreach (var type in allTypes)
        {
            if (type.DeletedAt != null) continue;

            var typeGrants = grants.Where(g => g.LeaveTypeId == type.Id).ToList();
            var typeTakes = ApprovedOf(requests, type.Id);
            var granted = typeGrants.Sum(g => g.Days);
            var taken = typeTakes.Sum(r => LeaveCalc.UnitDays(r.Unit));

            // 無効化済かつ実績が無い種別は一覧に出さない (ノイズ抑制)。
            if (!type.IsActive && granted == 0m && taken == 0m) continue;

            var balance = LeaveCalc.Balance(typeGrants, typeTakes, type.IsStatutory, today);
            byType.Add(new LeaveByTypeDto(
                type.Id, type.Name, type.IsStatutory,
                granted, taken, balance.Remaining, balance.NextExpire?.Grant.ExpireDate));
        }

        // 有給 KPI・年 5 日義務は法定有給 (IsStatutory) の種別が対象。
        decimal paidRemaining = 0m;
        decimal paidUsedThisFiscalYear = 0m;
        LeaveNextExpireDto? nextExpire = null;
        var obligation = new LeaveObligationDto(
            false, null, null, LeaveCalc.ObligationRequiredDays, 0m, 0, false, false);

        var statutory = allTypes.FirstOrDefault(t => t.IsStatutory && t.DeletedAt == null);
        if (statutory is not null)
        {
            var statutoryGrants = grants.Where(g => g.LeaveTypeId == statutory.Id).ToList();
            var statutoryTakes = ApprovedOf(requests, statutory.Id);

            var balance = LeaveCalc.Balance(statutoryGrants, statutoryTakes, statutory.IsStatutory, today);
            paidRemaining = balance.Remaining;
            paidUsedThisFiscalYear = balance.UsedThisFiscalYear;
            // 直近の失効予定 = 未失効かつ残数のある引当のうち失効日が最小のもの。
            if (balance.NextExpire is { } next)
                nextExpire = new LeaveNextExpireDto(next.Grant.ExpireDate, next.Remaining);

            var judged = LeaveCalc.Obligation(statutoryGrants, statutoryTakes, today);
            obligation = new LeaveObligationDto(
                judged.Applicable, judged.GrantDate, judged.Deadline,
                judged.RequiredDays, judged.TakenDays, judged.DaysLeft, judged.Achieved, judged.Warn);
        }

        var granterNames = await LoadGranterNamesAsync(grants, ct);
        var history = BuildHistory(grants, requests, typeById, granterNames);

        return new LeaveSummaryDto(
            paidRemaining, paidUsedThisFiscalYear, nextExpire, byType, obligation, history);
    }

    /// <summary>休暇管理一覧 (#27)。在籍中メンバー × 種別の付与/取得/残。実績が無い組合せは出さない。</summary>
    public async Task<List<LeaveAdminRowDto>> AdminSummaryAsync(CancellationToken ct = default)
    {
        var today = SystemTime.TodayJst;

        var types = await db.LeaveTypes
            .Where(t => t.DeletedAt == null)
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);
        if (types.Count == 0) return [];

        var users = await db.Users
            .Where(u => u.IsActive && u.DeletedAt == null)
            .OrderBy(u => u.EmployeeNo)
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);
        if (users.Count == 0) return [];

        var userIds = users.Select(u => u.Id).ToList();
        var grants = await db.LeaveGrants.Where(g => userIds.Contains(g.UserId)).ToListAsync(ct);
        var takes = await db.LeaveRequests
            .Where(r => userIds.Contains(r.UserId) && r.Status == LeaveRequestStatus.Approved)
            .ToListAsync(ct);

        var grantsByKey = grants.ToLookup(g => (g.UserId, g.LeaveTypeId));
        var takesByKey = takes.ToLookup(r => (r.UserId, r.LeaveTypeId));

        var rows = new List<LeaveAdminRowDto>();
        foreach (var user in users)
        {
            foreach (var type in types)
            {
                var typeGrants = grantsByKey[(user.Id, type.Id)].ToList();
                var typeTakes = takesByKey[(user.Id, type.Id)].ToList();
                if (typeGrants.Count == 0 && typeTakes.Count == 0) continue;

                var balance = LeaveCalc.Balance(typeGrants, typeTakes, type.IsStatutory, today);
                rows.Add(new LeaveAdminRowDto(
                    user.Id, user.DisplayName, type.Id, type.Name,
                    typeGrants.Sum(g => g.Days),
                    typeTakes.Sum(r => LeaveCalc.UnitDays(r.Unit)),
                    balance.Remaining,
                    typeGrants.Count == 0 ? (DateOnly?)null : typeGrants.Max(g => g.GrantDate)));
            }
        }

        return rows;
    }

    // ═══════════════════════════════════════════════
    // 休暇申請 (#21〜#23)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 休暇申請の一覧 (#21)。allScope=true は全員分 (呼び出し側でオーナーを検証済であること)。
    /// status は "pending" / "approved" / "rejected"。作成日時の降順。
    /// </summary>
    public async Task<List<LeaveRequestDto>> ListRequestsAsync(
        Guid actorUserId, bool allScope, string? status, CancellationToken ct = default)
    {
        var query = db.LeaveRequests.AsQueryable();
        if (!allScope) query = query.Where(r => r.UserId == actorUserId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseStatus(status);
            query = query.Where(r => r.Status == parsed);
        }

        var rows = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return await ToRequestDtosAsync(rows, ct);
    }

    /// <summary>休暇申請の作成 (#22)。対象は常に本人。status=Pending で追記する。</summary>
    public async Task<LeaveIdDto> CreateRequestAsync(
        Guid targetUserId, LeaveRequestWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var type = await db.LeaveTypes
            .FirstOrDefaultAsync(t => t.Id == req.LeaveTypeId && t.DeletedAt == null && t.IsActive, ct)
            ?? throw DomainException.Validation("休暇種別が存在しません", "有効な休暇種別を選択してください");

        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length > 255)
            throw DomainException.Validation("理由は 255 文字以内で入力してください");

        // 同一日の二重申請 (未処理・承認済) を防ぐ。却下済は再申請できる。
        var duplicated = await db.LeaveRequests.AnyAsync(
            r => r.UserId == targetUserId && r.Date == req.Date
                 && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved), ct);
        if (duplicated)
            throw DomainException.UniqueViolation("同じ日付の休暇申請が既にあります",
                "申請一覧から既存の申請を確認してください");

        var now = SystemTime.UtcNow;
        var entity = new LeaveRequest
        {
            UserId = targetUserId,
            LeaveTypeId = type.Id,
            Date = req.Date,
            Unit = req.Unit,
            Status = LeaveRequestStatus.Pending,
            Reason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.LeaveRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "LeaveRequest.Create", entityType: "LeaveRequest", entityId: entity.Id,
            note: $"date={FormatDate(entity.Date)}, type={type.Name}", cancellationToken: ct);

        return new LeaveIdDto(entity.Id);
    }

    /// <summary>
    /// 休暇申請の承認/却下 (#23)。
    /// **Status != Pending の再確認はトランザクション内で行い、二重処理を 409 で防ぐ。**
    /// 存在しない場合は null を返す (endpoint が 404 へ変換)。
    /// </summary>
    public async Task<LeaveIdDto?> DecideRequestAsync(
        Guid id, LeaveDecisionRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var approved = ParseDecision(req.Action);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var entity = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (entity is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            // 二重承認/却下の防止 (原則 2)。判定は必ずトランザクション内。
            if (entity.Status != LeaveRequestStatus.Pending)
                throw new DomainException(AkbErrorCodes.SysUniqueViolation, 409,
                    "この申請は処理済みです",
                    "申請一覧を再読み込みして現在の状態を確認してください");

            entity.Status = approved ? LeaveRequestStatus.Approved : LeaveRequestStatus.Rejected;
            entity.DecidedByUserId = actorUserId;
            entity.UpdatedAt = SystemTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId,
                approved ? "LeaveRequest.Approve" : "LeaveRequest.Reject",
                entityType: "LeaveRequest", entityId: entity.Id,
                note: $"date={FormatDate(entity.Date)}", cancellationToken: ct);

            return new LeaveIdDto(entity.Id);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ═══════════════════════════════════════════════
    // 付与 (#24〜#26)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 個別付与 (#24)。同一 (user, 種別, 付与日) が既にある場合は**既存 ID + Skipped=1** を返す。
    /// 既存の付与レコードは更新も削除もしない (原則 2 / 記録系保護)。
    /// </summary>
    public async Task<LeaveGrantResultDto> GrantAsync(
        LeaveGrantWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (req.Days <= 0m)
            throw DomainException.Validation("付与日数は正の数で指定してください");

        var type = await LoadManualGrantTypeAsync(req.LeaveTypeId, ct);

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId && u.DeletedAt == null, ct);
        if (!userExists)
            throw DomainException.Validation("利用者が存在しません", "在籍中の利用者を選択してください");

        var existing = await db.LeaveGrants.FirstOrDefaultAsync(
            g => g.UserId == req.UserId && g.LeaveTypeId == type.Id && g.GrantDate == req.GrantDate, ct);
        if (existing is not null) return new LeaveGrantResultDto(existing.Id, 1);

        var now = SystemTime.UtcNow;
        var entity = new LeaveGrant
        {
            UserId = req.UserId,
            LeaveTypeId = type.Id,
            GrantDate = req.GrantDate,
            Days = req.Days,
            // 手動付与は Special (通常/比例は周期自動付与が採番する)。
            Kind = LeaveGrantKind.Special,
            ExpireDate = LeaveCalc.ExpireDateFor(req.GrantDate, type.ExpiryMonths),
            GrantedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.LeaveGrants.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "LeaveGrant.Create", entityType: "LeaveGrant", entityId: entity.Id,
            note: $"user={req.UserId}, type={type.Name}, date={FormatDate(req.GrantDate)}, days={FormatDays(req.Days)}",
            cancellationToken: ct);

        return new LeaveGrantResultDto(entity.Id, 0);
    }

    /// <summary>
    /// 一括付与 (#25)。対象は "all" (在籍中の全ユーザ) のみ。
    /// 既存分は事前 SELECT で除外してから AddRange する (冪等・原則 2)。
    /// </summary>
    public async Task<LeaveGrantBulkResultDto> BulkGrantAsync(
        LeaveBulkGrantRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (req.Days <= 0m)
            throw DomainException.Validation("付与日数は正の数で指定してください");

        // honshu には雇用区分・部署による付与対象の絞り込み属性が無いため all のみ (移植時の意図的な縮小)。
        if (!string.Equals((req.Target ?? string.Empty).Trim(), "all", StringComparison.OrdinalIgnoreCase))
            throw DomainException.Validation("一括付与の対象は all のみ指定できます",
                "対象に all を指定するか、個別付与を使用してください");

        var type = await LoadManualGrantTypeAsync(req.LeaveTypeId, ct);
        var grantDate = req.GrantDate ?? SystemTime.TodayJst;
        var expireDate = LeaveCalc.ExpireDateFor(grantDate, type.ExpiryMonths);

        var userIds = await db.Users
            .Where(u => u.IsActive && u.DeletedAt == null)
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (userIds.Count == 0) return new LeaveGrantBulkResultDto(0, 0);

        var alreadyGranted = await db.LeaveGrants
            .Where(g => g.LeaveTypeId == type.Id && g.GrantDate == grantDate && userIds.Contains(g.UserId))
            .Select(g => g.UserId)
            .ToListAsync(ct);
        var alreadyGrantedSet = alreadyGranted.ToHashSet();

        var now = SystemTime.UtcNow;
        var added = new List<LeaveGrant>();
        foreach (var userId in userIds)
        {
            if (alreadyGrantedSet.Contains(userId)) continue;
            added.Add(new LeaveGrant
            {
                UserId = userId,
                LeaveTypeId = type.Id,
                GrantDate = grantDate,
                Days = req.Days,
                Kind = LeaveGrantKind.Special,
                ExpireDate = expireDate,
                GrantedByUserId = actorUserId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        if (added.Count > 0)
        {
            db.LeaveGrants.AddRange(added);
            await db.SaveChangesAsync(ct);
        }

        var skipped = userIds.Count - added.Count;
        await audit.LogAsync(actorUserId, "LeaveGrant.BulkCreate", entityType: "LeaveGrant",
            note: $"type={type.Name}, date={FormatDate(grantDate)}, granted={added.Count}, skipped={skipped}",
            cancellationToken: ct);

        return new LeaveGrantBulkResultDto(added.Count, skipped);
    }

    /// <summary>
    /// 周期自動付与の実行 (#26、労基法 39 条)。
    /// 対象は「法定有給かつ GrantMethod=Periodic」の種別 1 件と、入社日を持つ在籍中ユーザ。
    /// **既存付与は事前 SELECT で除外してから AddRange する**(EF から ON CONFLICT を使えないため。
    /// 一意制約 (tenant_id, user_id, leave_type_id, grant_date) は最終防衛線)。
    /// 何度実行しても既存の付与は一切変更されない (原則 2)。
    /// </summary>
    public async Task<LeaveGrantBulkResultDto> RunPeriodicGrantsAsync(
        Guid actorUserId, CancellationToken ct = default)
    {
        var type = await db.LeaveTypes.FirstOrDefaultAsync(
            t => t.IsStatutory && t.GrantMethod == LeaveGrantMethod.Periodic && t.DeletedAt == null, ct);
        // 周期付与の種別が無い場合は何もしない (主要フローを止めない、原則 4)。
        if (type is null) return new LeaveGrantBulkResultDto(0, 0);

        var today = SystemTime.TodayJst;
        var users = await db.Users
            .Where(u => u.IsActive && u.DeletedAt == null && u.HireDate != null)
            .Select(u => new { u.Id, u.HireDate, u.WeeklyDays, u.WeeklyHours })
            .ToListAsync(ct);
        if (users.Count == 0) return new LeaveGrantBulkResultDto(0, 0);

        var userIds = users.Select(u => u.Id).ToList();
        var existing = await db.LeaveGrants
            .Where(g => g.LeaveTypeId == type.Id && userIds.Contains(g.UserId))
            .Select(g => new { g.UserId, g.GrantDate })
            .ToListAsync(ct);
        var existingKeys = existing.Select(e => (e.UserId, e.GrantDate)).ToHashSet();

        var now = SystemTime.UtcNow;
        var added = new List<LeaveGrant>();
        var skipped = 0;
        foreach (var user in users)
        {
            var plans = LeaveCalc.PlanPeriodicGrants(
                user.HireDate!.Value, user.WeeklyDays, user.WeeklyHours, today);

            foreach (var plan in plans)
            {
                if (!existingKeys.Add((user.Id, plan.GrantDate)))
                {
                    // 既存 (= DB にある) か、同一実行内で既に生成済。いずれもスキップ。
                    skipped++;
                    continue;
                }

                added.Add(new LeaveGrant
                {
                    UserId = user.Id,
                    LeaveTypeId = type.Id,
                    GrantDate = plan.GrantDate,
                    Days = plan.Days,
                    Kind = plan.Kind,
                    ExpireDate = plan.ExpireDate,
                    // NULL = 周期自動付与 (付与者なし)。
                    GrantedByUserId = null,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        if (added.Count > 0)
        {
            db.LeaveGrants.AddRange(added);
            await db.SaveChangesAsync(ct);
        }

        await audit.LogAsync(actorUserId, "LeaveGrant.PeriodicRun", entityType: "LeaveGrant",
            note: $"type={type.Name}, granted={added.Count}, skipped={skipped}", cancellationToken: ct);

        return new LeaveGrantBulkResultDto(added.Count, skipped);
    }

    // ═══════════════════════════════════════════════
    // 内部ヘルパー
    // ═══════════════════════════════════════════════

    private static LeaveTypeDto ToDto(LeaveType t)
        => new(t.Id, t.Name, t.GrantMethod, t.ExpiryMonths, t.IsStatutory,
               t.Description, t.DisplayOrder, t.IsActive, t.DeletedAt);

    /// <summary>指定種別の承認済 (= 残数を消化する) 申請のみを日付昇順で抽出する。</summary>
    private static List<LeaveRequest> ApprovedOf(IEnumerable<LeaveRequest> requests, Guid leaveTypeId)
        => requests
            .Where(r => r.LeaveTypeId == leaveTypeId && r.Status == LeaveRequestStatus.Approved)
            .OrderBy(r => r.Date)
            .ToList();

    private static void ValidateTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw DomainException.Validation("名称を入力してください");
        if (name.Length > 64)
            throw DomainException.Validation("名称は 64 文字以内で入力してください");
    }

    private static void ValidateExpiryMonths(int? expiryMonths)
    {
        if (expiryMonths is { } months && months is < 1 or > 120)
            throw DomainException.Validation("有効期間は 1〜120 ヶ月で指定してください（無期限にする場合は未指定）");
    }

    private static void ValidateDisplayOrder(int displayOrder)
    {
        if (displayOrder < 0)
            throw DomainException.Validation("表示順は 0 以上で指定してください");
    }

    /// <summary>法定有給の種別は作成・編集・論理削除を禁止する (§5.4、409 AKB-SYS-007)。</summary>
    private static void EnsureNotStatutory(LeaveType type, string message)
    {
        if (type.IsStatutory)
            throw new DomainException(AkbErrorCodes.SysUniqueViolation, 409, message,
                "法定有給 (労基法 39 条) の種別はシステムが管理します。別の休暇種別を作成してください");
    }

    /// <summary>未削除の同名種別を検出して 409 を返す (部分 UNIQUE 索引と同じ条件)。</summary>
    private async Task EnsureNoActiveDuplicateNameAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var duplicated = await db.LeaveTypes.AnyAsync(
            t => t.DeletedAt == null && t.Name == name && (excludeId == null || t.Id != excludeId), ct);
        if (duplicated)
            throw DomainException.UniqueViolation($"休暇種別「{name}」は既に登録されています",
                "別の名称を指定するか、既存の種別を編集してください");
    }

    /// <summary>
    /// 手動付与 (#24 / #25) 可能な種別を取得する。
    /// 周期自動付与 (GrantMethod=Periodic) の種別は「周期付与を実行」経由でのみ付与する。
    /// </summary>
    private async Task<LeaveType> LoadManualGrantTypeAsync(Guid leaveTypeId, CancellationToken ct)
    {
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == leaveTypeId && t.DeletedAt == null, ct)
            ?? throw DomainException.Validation("休暇種別が存在しません", "有効な休暇種別を選択してください");

        if (type.GrantMethod == LeaveGrantMethod.Periodic)
            throw DomainException.Validation("周期自動付与の種別は手動付与できません",
                "「周期付与を実行」から付与してください");

        return type;
    }

    private static LeaveRequestStatus ParseStatus(string status)
        => status.Trim().ToLowerInvariant() switch
        {
            "pending" => LeaveRequestStatus.Pending,
            "approved" => LeaveRequestStatus.Approved,
            "rejected" => LeaveRequestStatus.Rejected,
            _ => throw DomainException.Validation(
                "状態は pending / approved / rejected のいずれかを指定してください"),
        };

    /// <summary>承認=true / 却下=false。それ以外は 422。</summary>
    private static bool ParseDecision(string? action)
        => (action ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "approved" => true,
            "rejected" => false,
            _ => throw DomainException.Validation(
                "操作は approved / rejected のいずれかを指定してください"),
        };

    private async Task<List<LeaveRequestDto>> ToRequestDtosAsync(List<LeaveRequest> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var userIds = rows.Select(r => r.UserId)
            .Concat(rows.Where(r => r.DecidedByUserId != null).Select(r => r.DecidedByUserId!.Value))
            .Distinct()
            .ToList();
        var userRows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);
        var userNames = userRows.ToDictionary(u => u.Id, u => u.DisplayName);

        var typeIds = rows.Select(r => r.LeaveTypeId).Distinct().ToList();
        var typeRows = await db.LeaveTypes
            .Where(t => typeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);
        var typeNames = typeRows.ToDictionary(t => t.Id, t => t.Name);

        return rows.Select(r => new LeaveRequestDto(
            r.Id,
            r.UserId,
            userNames.TryGetValue(r.UserId, out var userName) ? userName : string.Empty,
            r.LeaveTypeId,
            typeNames.TryGetValue(r.LeaveTypeId, out var typeName) ? typeName : string.Empty,
            r.Date,
            r.Unit,
            r.Status,
            r.Reason,
            r.DecidedByUserId,
            r.DecidedByUserId is { } decidedBy && userNames.TryGetValue(decidedBy, out var decidedName)
                ? decidedName
                : null,
            r.CreatedAt)).ToList();
    }

    /// <summary>手動付与レコードの付与者名を解決する (周期自動付与は GrantedByUserId が NULL)。</summary>
    private async Task<Dictionary<Guid, string>> LoadGranterNamesAsync(
        List<LeaveGrant> grants, CancellationToken ct)
    {
        var granterIds = grants
            .Where(g => g.GrantedByUserId != null)
            .Select(g => g.GrantedByUserId!.Value)
            .Distinct()
            .ToList();
        if (granterIds.Count == 0) return new Dictionary<Guid, string>();

        var granterRows = await db.Users
            .Where(u => granterIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);
        return granterRows.ToDictionary(u => u.Id, u => u.DisplayName);
    }

    /// <summary>
    /// 付与・取得の履歴行を組み立てる (§5.4 history)。
    /// 並びは日付降順 (同日は付与 → 取得)。Days は符号や「—」を含むため文字列で返す。
    /// </summary>
    private static List<LeaveHistoryDto> BuildHistory(
        List<LeaveGrant> grants,
        List<LeaveRequest> requests,
        IReadOnlyDictionary<Guid, LeaveType> typeById,
        IReadOnlyDictionary<Guid, string> granterNames)
    {
        var rows = new List<LeaveHistoryDto>();

        foreach (var g in grants)
        {
            var typeName = typeById.TryGetValue(g.LeaveTypeId, out var type) ? type.Name : string.Empty;
            var by = g.Kind switch
            {
                LeaveGrantKind.Normal => "通常付与",
                LeaveGrantKind.Proportional => "比例付与",
                _ => g.GrantedByUserId is { } granterId && granterNames.TryGetValue(granterId, out var granter)
                    ? $"{granter} が付与"
                    : "管理者が付与",
            };
            var expire = g.ExpireDate >= LeaveCalc.NoExpiryDate ? "なし" : FormatDate(g.ExpireDate);

            rows.Add(new LeaveHistoryDto(
                g.GrantDate, "grant", g.LeaveTypeId, typeName,
                $"{typeName}: {by}（失効 {expire}）",
                string.Empty,
                $"+{FormatDays(g.Days)}"));
        }

        foreach (var r in requests)
        {
            var typeName = typeById.TryGetValue(r.LeaveTypeId, out var type) ? type.Name : string.Empty;
            var unitLabel = r.Unit == LeaveUnit.Half ? "半日" : "全日";
            var reason = string.IsNullOrWhiteSpace(r.Reason) ? string.Empty : $"・{r.Reason.Trim()}";
            var days = r.Status == LeaveRequestStatus.Approved
                ? $"-{FormatDays(LeaveCalc.UnitDays(r.Unit))}"
                : "—";

            rows.Add(new LeaveHistoryDto(
                r.Date, "take", r.LeaveTypeId, typeName,
                $"{typeName}: {unitLabel}{reason}",
                StatusText(r.Status),
                days));
        }

        return rows
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>JSON の enum 表記 (camelCase) と揃えた状態文字列。</summary>
    private static string StatusText(LeaveRequestStatus status) => status switch
    {
        LeaveRequestStatus.Approved => "approved",
        LeaveRequestStatus.Rejected => "rejected",
        _ => "pending",
    };

    /// <summary>日数の表示 (10 → "10"、0.5 → "0.5")。実行環境のカルチャに依存させない。</summary>
    private static string FormatDays(decimal days)
        => days.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>業務日付の表示 (監査ログ・履歴用)。</summary>
    private static string FormatDate(DateOnly date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
