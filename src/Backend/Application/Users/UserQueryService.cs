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
                u.FirebaseUid != null))
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "User.List", entityType: "User", note: $"Returned {users.Count} users", cancellationToken: ct);

        return users;
    }

    public async Task<UserListItem?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserListItem(
                u.Id, u.EmployeeNo, u.LoginId, u.DisplayName, u.IsActive,
                u.Email, u.IsPlanningStaff, u.IsSalesStaff,
                u.ProductLedgerPermission, u.PurchaseOrderCreatePermission,
                u.PurchaseOrderInfoPermission, u.ProcessRecordPermission,
                u.FirebaseUid != null))
            .FirstOrDefaultAsync(ct);

    // 権限値の範囲チェック (Phase 5 §3.18 の 4 権限カテゴリ) + 必須項目。
    private static void ValidatePermissions(UserWriteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeNo)) throw DomainException.Validation("社員番号は必須です");
        if (string.IsNullOrWhiteSpace(req.LoginId)) throw DomainException.Validation("ログイン ID は必須です");
        if (string.IsNullOrWhiteSpace(req.DisplayName)) throw DomainException.Validation("表示名は必須です");
        if (req.ProductLedgerPermission is < 0 or > 3) throw DomainException.Validation("品番台帳権限は 0〜3 で指定してください");
        if (req.PurchaseOrderCreatePermission is < 0 or > 2) throw DomainException.Validation("発注書作成権限は 0〜2 で指定してください");
        if (req.PurchaseOrderInfoPermission is < 0 or > 1) throw DomainException.Validation("発注情報権限は 0 または 1 で指定してください");
        if (req.ProcessRecordPermission is < 0 or > 1) throw DomainException.Validation("工程実績(オーナー)権限は 0 または 1 で指定してください");
    }

    public async Task<UserListItem> CreateAsync(UserWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        ValidatePermissions(req);
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

    public async Task<UserListItem?> UpdateAsync(Guid id, UserWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        ValidatePermissions(req);
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (entity is null) return null;

        // 自己ロックアウト防止: 自分自身のオーナー権限の解除・自分の無効化は禁止。
        if (id == actorUserId && (req.ProcessRecordPermission < 1 || !req.IsActive))
            throw DomainException.Validation("自分自身のオーナー権限の解除・無効化はできません");

        // 全体ロックアウト防止: 最後の有効オーナーを「非オーナー化 or 無効化」することは禁止。
        var wasActiveOwner = entity.ProcessRecordPermission >= 1 && entity.IsActive && entity.DeletedAt == null;
        var willBeActiveOwner = req.ProcessRecordPermission >= 1 && req.IsActive;
        if (wasActiveOwner && !willBeActiveOwner && await CountActiveOwnersAsync(ct) <= 1)
            throw DomainException.Validation("最後の有効なオーナーの権限解除・無効化はできません");

        entity.EmployeeNo = req.EmployeeNo.Trim();
        entity.LoginId = req.LoginId.Trim();
        entity.DisplayName = req.DisplayName.Trim();
        entity.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        // FirebaseUid は空なら既存値を保持する (連携解除は本フォームでは行わない、非破壊)。
        if (!string.IsNullOrWhiteSpace(req.FirebaseUid)) entity.FirebaseUid = req.FirebaseUid.Trim();
        entity.IsPlanningStaff = req.IsPlanningStaff;
        entity.IsSalesStaff = req.IsSalesStaff;
        entity.ProductLedgerPermission = req.ProductLedgerPermission;
        entity.PurchaseOrderCreatePermission = req.PurchaseOrderCreatePermission;
        entity.PurchaseOrderInfoPermission = req.PurchaseOrderInfoPermission;
        entity.ProcessRecordPermission = req.ProcessRecordPermission;
        entity.IsActive = req.IsActive;
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
        if (id == actorUserId) throw DomainException.Validation("自分自身は削除できません");
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (entity is null) return false;

        // 全体ロックアウト防止: 最後の有効オーナーの削除は禁止。
        var wasActiveOwner = entity.ProcessRecordPermission >= 1 && entity.IsActive && entity.DeletedAt == null;
        if (wasActiveOwner && await CountActiveOwnersAsync(ct) <= 1)
            throw DomainException.Validation("最後の有効なオーナーは削除できません");

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
