using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Users;

/// <summary>
/// 利用者マスタ (Part5) のサービス。一覧/取得 + 作成/更新/論理削除/復元。
/// 権限 (閲覧/操作) を含む利用者情報を管理する。書込はオーナー権限
/// (process_record_permission >= 1) が必要 (エンドポイント側で AuthEndpoints.CheckUserAdminAsync)。
/// </summary>
public class UserQueryService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>
    /// 労務個人情報 (勤怠権限・打刻要否・勤務体系・入社日・週所定日数/時間) を落とした投影。
    /// 入社日と週所定は有給の比例付与判定に直結するため、認証のみで開いている一覧/単票から
    /// 全在籍者分を列挙させない。
    ///
    /// **境界は勤怠 API と意図的に揃えない (2026-07-27):** 勤怠 API の管理操作は
    /// 「勤怠参照権限 AND オーナー」だが、こちらは **オーナー単独** で開く。
    /// 本列は利用者マスタが**設定する対象そのもの**であり、編集フォームは全項目を明示送信するため、
    /// レダクションすると「オーナーだが勤怠権限 0」の管理者が利用者を開いて保存しただけで
    /// **他人の勤怠設定を fail-close 値へ巻き戻す** (Iteration 30 で解消した BLOCKER の再発)。
    /// 利用者マスタ管理は「他人の勤怠設定を管理する」機能であり、
    /// 「自分が勤怠機能を使う」こととは別の権限軸として扱う。
    /// </summary>
    private static UserListItem WithoutLaborInfo(UserListItem user) => user with
    {
        AttendancePermission = null,
        PunchRequired = null,
        AttendanceRuleId = null,
        HireDate = null,
        WeeklyDays = null,
        WeeklyHours = null,
    };

    /// <summary>
    /// 労務個人情報を返してよい相手か = オーナーか。
    /// 判定式は <c>AuthEndpoints.CheckUserAdminAsync</c> と同じ
    /// (有効・未削除 かつ process_record_permission &gt;= 1。オーナー判定のみ &gt;= 1 を使う)。
    /// </summary>
    private Task<bool> IsOwnerAsync(Guid actorUserId, CancellationToken ct)
        => db.Users.AnyAsync(
            u => u.Id == actorUserId && u.IsActive && u.DeletedAt == null && u.ProcessRecordPermission >= 1,
            ct);

    /// <summary>
    /// 一覧。**労務個人情報 (勤怠 6 列) はオーナーにのみ返す**。
    /// 本一覧は発注・商品フォームの担当者候補にも使うため認証のみで開いており、
    /// そのままでは勤怠権限 0 の利用者でも全在籍者の入社日・週所定を列挙できてしまう。
    /// </summary>
    public async Task<List<UserListItem>> ListAsync(Guid actorUserId, CancellationToken ct = default)
    {
        var users = await db.Users
            .Where(u => u.DeletedAt == null)
            .OrderBy(u => u.EmployeeNo)
            .Select(u => new UserListItem(
                u.Id, u.EmployeeNo, u.LoginId, u.DisplayName, u.IsActive,
                u.Email, u.IsPlanningStaff, u.IsSalesStaff,
                u.ProductLedgerPermission, u.PurchaseOrderCreatePermission,
                u.PurchaseOrderInfoPermission, u.ProcessRecordPermission,
                u.FirebaseUid != null,
                u.AttendancePermission, u.PunchRequired, u.AttendanceRuleId,
                u.HireDate, u.WeeklyDays, u.WeeklyHours))
            .ToListAsync(ct);

        if (!await IsOwnerAsync(actorUserId, ct))
            users = users.ConvertAll(WithoutLaborInfo);

        await audit.LogAsync(actorUserId, "User.List", entityType: "User", note: $"Returned {users.Count} users", cancellationToken: ct);

        return users;
    }

    /// <summary>
    /// 単票取得 (API 用)。労務個人情報はオーナーにのみ返す (<see cref="ListAsync"/> と同じ規則)。
    /// GET /users/{id} も一覧と同じく認証のみで開いているため、こちらだけ素通しにしない。
    /// </summary>
    public async Task<UserListItem?> GetForActorAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var user = await GetAsync(id, ct);
        if (user is null) return null;
        return await IsOwnerAsync(actorUserId, ct) ? user : WithoutLaborInfo(user);
    }

    /// <summary>
    /// 単票取得 (**内部用・労務個人情報を含む全項目**)。
    /// オーナー以外へ露出させないため private とし、外部へ返す経路は
    /// <see cref="GetForActorAsync"/> を通す。作成・更新の応答組み立てからのみ直接呼ぶ
    /// (エンドポイントが CheckUserAdminAsync でオーナーを検証済みのため全項目を返してよい)。
    /// </summary>
    private async Task<UserListItem?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserListItem(
                u.Id, u.EmployeeNo, u.LoginId, u.DisplayName, u.IsActive,
                u.Email, u.IsPlanningStaff, u.IsSalesStaff,
                u.ProductLedgerPermission, u.PurchaseOrderCreatePermission,
                u.PurchaseOrderInfoPermission, u.ProcessRecordPermission,
                u.FirebaseUid != null,
                u.AttendancePermission, u.PunchRequired, u.AttendanceRuleId,
                u.HireDate, u.WeeklyDays, u.WeeklyHours))
            .FirstOrDefaultAsync(ct);

    // 権限値の範囲チェック (Phase 5 §3.18 の 4 権限カテゴリ + 勤怠権限 = 5 件) + 必須項目・週所定の範囲。
    private static void ValidatePermissions(UserWriteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeNo))
            throw DomainException.Validation("社員番号は必須です", "社員番号を入力して再送信してください");
        if (string.IsNullOrWhiteSpace(req.LoginId))
            throw DomainException.Validation("ログイン ID は必須です", "ログイン ID を入力して再送信してください");
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            throw DomainException.Validation("表示名は必須です", "表示名を入力して再送信してください");
        if (req.ProductLedgerPermission is < 0 or > 3)
            throw DomainException.Validation("品番台帳権限は 0〜3 で指定してください", "権限を選び直して再送信してください");
        if (req.PurchaseOrderCreatePermission is < 0 or > 2)
            throw DomainException.Validation("発注書作成権限は 0〜2 で指定してください", "権限を選び直して再送信してください");
        if (req.PurchaseOrderInfoPermission is < 0 or > 1)
            throw DomainException.Validation("発注情報権限は 0 または 1 で指定してください", "権限を選び直して再送信してください");
        if (req.ProcessRecordPermission is < 0 or > 1)
            throw DomainException.Validation("工程実績(オーナー)権限は 0 または 1 で指定してください", "権限を選び直して再送信してください");
        // 勤怠 (Iteration 30)。0=なし / 1=更新可能 / 2=参照のみ (非単調スケール)。
        if (req.AttendancePermission is < 0 or > 2)
            throw DomainException.Validation("勤怠権限は 0〜2 で指定してください", "権限を選び直して再送信してください");
        if (req.WeeklyDays < 0m || req.WeeklyDays > 7m)
            throw DomainException.Validation("週所定日数は 0〜7 で指定してください",
                "週所定日数を 0〜7 の範囲で入力してください");
        if (req.WeeklyHours < 0m || req.WeeklyHours > 168m)
            throw DomainException.Validation("週所定時間は 0〜168 で指定してください",
                "週所定時間を 0〜168 の範囲で入力してください");
    }

    /// <summary>
    /// 勤務体系 (attendance_rules) の存在確認。未存在の ID をそのまま保存すると
    /// FK 違反が 500 になるため、422 で復帰導線付きに変換する。NULL (既定ルール) は素通し。
    /// </summary>
    private async Task EnsureAttendanceRuleExistsAsync(Guid? attendanceRuleId, CancellationToken ct)
    {
        if (attendanceRuleId == null) return;

        var exists = await db.AttendanceRules
            .AnyAsync(r => r.Id == attendanceRuleId.Value && r.DeletedAt == null, ct);
        if (!exists)
            throw DomainException.Validation("指定された勤務体系が見つかりません",
                "勤怠設定の勤務体系一覧から選び直してください");
    }

    public async Task<UserListItem> CreateAsync(UserWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        ValidatePermissions(req);
        await EnsureAttendanceRuleExistsAsync(req.AttendanceRuleId, ct);
        var now = SystemTime.UtcNow;
        var entity = new User
        {
            EmployeeNo = req.EmployeeNo.Trim(),
            LoginId = req.LoginId.Trim(),
            DisplayName = req.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            // ログイン連携 (任意)。未指定なら未連携 (初回ログイン時に別途連携)。
            FirebaseUid = string.IsNullOrWhiteSpace(req.FirebaseUid) ? null : req.FirebaseUid.Trim(),
            IsPlanningStaff = req.IsPlanningStaff,
            IsSalesStaff = req.IsSalesStaff,
            ProductLedgerPermission = req.ProductLedgerPermission,
            PurchaseOrderCreatePermission = req.PurchaseOrderCreatePermission,
            PurchaseOrderInfoPermission = req.PurchaseOrderInfoPermission,
            ProcessRecordPermission = req.ProcessRecordPermission,
            // 勤怠 (Iteration 30)
            AttendancePermission = req.AttendancePermission,
            PunchRequired = req.PunchRequired,
            AttendanceRuleId = req.AttendanceRuleId,
            HireDate = req.HireDate,
            WeeklyDays = req.WeeklyDays,
            WeeklyHours = req.WeeklyHours,
            IsActive = req.IsActive,
            CreatedAt = now,
            CreatedByUserId = actorUserId,
            UpdatedAt = now,
            UpdatedByUserId = actorUserId,
        };
        db.Users.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "User.Create", entityType: "User", entityId: entity.Id,
            note: $"EmployeeNo={entity.EmployeeNo}, LoginId={entity.LoginId}", cancellationToken: ct);

        return (await GetAsync(entity.Id, ct))!;
    }

    // 有効なオーナー (process_record_permission>=1 かつ 有効 かつ 未削除) の人数 (現テナント)。
    private Task<int> CountActiveOwnersAsync(CancellationToken ct)
        => db.Users.CountAsync(u => u.ProcessRecordPermission >= 1 && u.IsActive && u.DeletedAt == null, ct);

    /// <summary>
    /// 部分更新 (PATCH)。**未指定 (null) のフィールドは現在値を保持する**。
    /// 勤怠列 (入社日・週所定日数/時間・勤務体系) を送っていないリクエストで初期化しないため、
    /// 作成用の <see cref="UserWriteRequest"/> ではなく <see cref="UserPatchRequest"/> を受け取る。
    /// 「未指定 = 現在値」を先に確定させてから作成時と同じ検証を通す
    /// (部分更新でも範囲検証・ロックアウト防止を必ず効かせるため。AttendanceRuleService と同じ形)。
    /// </summary>
    public async Task<UserListItem?> UpdateAsync(Guid id, UserPatchRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (entity is null) return null;

        var merged = new UserWriteRequest(
            req.EmployeeNo ?? entity.EmployeeNo,
            req.LoginId ?? entity.LoginId,
            req.DisplayName ?? entity.DisplayName,
            // null = 未指定 (保持) / 空文字 = クリア。
            req.Email ?? entity.Email,
            req.IsPlanningStaff ?? entity.IsPlanningStaff,
            req.IsSalesStaff ?? entity.IsSalesStaff,
            req.ProductLedgerPermission ?? entity.ProductLedgerPermission,
            req.PurchaseOrderCreatePermission ?? entity.PurchaseOrderCreatePermission,
            req.PurchaseOrderInfoPermission ?? entity.PurchaseOrderInfoPermission,
            req.ProcessRecordPermission ?? entity.ProcessRecordPermission,
            req.IsActive ?? entity.IsActive,
            // FirebaseUid は検証対象外。実際の代入は下で「空なら保持」の規則で行う。
            req.FirebaseUid,
            req.AttendancePermission ?? entity.AttendancePermission,
            req.PunchRequired ?? entity.PunchRequired,
            // NULL 許容列はクリアフラグが優先。フラグ無し + 未指定なら現在値を保持する。
            req.ClearAttendanceRule ? null : (req.AttendanceRuleId ?? entity.AttendanceRuleId),
            req.ClearHireDate ? null : (req.HireDate ?? entity.HireDate),
            req.WeeklyDays ?? entity.WeeklyDays,
            req.WeeklyHours ?? entity.WeeklyHours);

        ValidatePermissions(merged);
        await EnsureAttendanceRuleExistsAsync(merged.AttendanceRuleId, ct);

        // 自己ロックアウト防止: 自分自身のオーナー権限の解除・自分の無効化は禁止。
        if (id == actorUserId && (merged.ProcessRecordPermission < 1 || !merged.IsActive))
            throw DomainException.Validation("自分自身のオーナー権限の解除・無効化はできません",
                "他のオーナーに操作を依頼してください");

        // 全体ロックアウト防止: 最後の有効オーナーを「非オーナー化 or 無効化」することは禁止。
        var wasActiveOwner = entity.ProcessRecordPermission >= 1 && entity.IsActive && entity.DeletedAt == null;
        var willBeActiveOwner = merged.ProcessRecordPermission >= 1 && merged.IsActive;
        if (wasActiveOwner && !willBeActiveOwner && await CountActiveOwnersAsync(ct) <= 1)
            throw DomainException.Validation("最後の有効なオーナーの権限解除・無効化はできません",
                "先に別の利用者へオーナー権限を付与してから再操作してください");

        entity.EmployeeNo = merged.EmployeeNo.Trim();
        entity.LoginId = merged.LoginId.Trim();
        entity.DisplayName = merged.DisplayName.Trim();
        entity.Email = string.IsNullOrWhiteSpace(merged.Email) ? null : merged.Email.Trim();
        // FirebaseUid は空なら既存値を保持する (連携解除は本フォームでは行わない、非破壊)。
        if (!string.IsNullOrWhiteSpace(req.FirebaseUid)) entity.FirebaseUid = req.FirebaseUid.Trim();
        entity.IsPlanningStaff = merged.IsPlanningStaff;
        entity.IsSalesStaff = merged.IsSalesStaff;
        entity.ProductLedgerPermission = merged.ProductLedgerPermission;
        entity.PurchaseOrderCreatePermission = merged.PurchaseOrderCreatePermission;
        entity.PurchaseOrderInfoPermission = merged.PurchaseOrderInfoPermission;
        entity.ProcessRecordPermission = merged.ProcessRecordPermission;
        // 勤怠 (Iteration 30)
        entity.AttendancePermission = merged.AttendancePermission;
        entity.PunchRequired = merged.PunchRequired;
        entity.AttendanceRuleId = merged.AttendanceRuleId;
        entity.HireDate = merged.HireDate;
        entity.WeeklyDays = merged.WeeklyDays;
        entity.WeeklyHours = merged.WeeklyHours;
        entity.IsActive = merged.IsActive;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "User.Update", entityType: "User", entityId: entity.Id,
            note: $"EmployeeNo={entity.EmployeeNo}", cancellationToken: ct);

        return await GetAsync(entity.Id, ct);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        // 自分自身の削除は禁止 (ロックアウト防止)。
        if (id == actorUserId)
            throw DomainException.Validation("自分自身は削除できません", "他のオーナーに操作を依頼してください");
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (entity is null) return false;

        // 全体ロックアウト防止: 最後の有効オーナーの削除は禁止。
        var wasActiveOwner = entity.ProcessRecordPermission >= 1 && entity.IsActive && entity.DeletedAt == null;
        if (wasActiveOwner && await CountActiveOwnersAsync(ct) <= 1)
            throw DomainException.Validation("最後の有効なオーナーは削除できません",
                "先に別の利用者へオーナー権限を付与してから再操作してください");

        var now = SystemTime.UtcNow;
        entity.DeletedAt = now;
        entity.IsActive = false;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "User.Delete", entityType: "User", entityId: entity.Id,
            note: $"EmployeeNo={entity.EmployeeNo}", cancellationToken: ct);

        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (entity is null) return false;

        entity.DeletedAt = null;
        entity.IsActive = true;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "User.Restore", entityType: "User", entityId: entity.Id, cancellationToken: ct);

        return true;
    }
}
