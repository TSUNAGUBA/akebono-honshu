using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Auth;

public class AuthService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>
    /// Firebase Auth でログインに成功したフロントが最初に呼ぶ sync endpoint の実装。
    /// JwtBearer ミドルウェアが Firebase ID Token を検証済、ClaimsPrincipal から取得した
    /// Firebase UID で users.firebase_uid → 業務ユーザ情報 (権限含む) を引当てて返す。
    /// 未紐付け UID は null を返し、呼び出し側で 403 / 業務エラーに変換する。
    /// </summary>
    public async Task<SyncResponse?> SyncAsync(string firebaseUid, CancellationToken ct = default)
    {
        var user = await db.Users
            .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            await audit.LogAsync(null, "Login.Failure",
                note: $"Firebase UID not bound to users.firebase_uid: {firebaseUid}",
                success: false, cancellationToken: ct);
            return null;
        }

        if (!user.IsActive)
        {
            await audit.LogAsync(user.Id, "Login.Failure",
                note: $"User is inactive: {user.LoginId}",
                success: false, cancellationToken: ct);
            return null;
        }

        await audit.LogAsync(user.Id, "Login.Success",
            entityType: "User", entityId: user.Id, cancellationToken: ct);

        return new SyncResponse(user.Id, user.EmployeeNo, user.DisplayName, user.IsActive,
            user.ProductLedgerPermission,
            user.PurchaseOrderCreatePermission,
            user.PurchaseOrderInfoPermission,
            user.ProcessRecordPermission);
    }

    public async Task<MeResponse?> GetMeAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .Where(u => u.Id == userId && !u.IsDeleted)
            .FirstOrDefaultAsync(ct);

        return user is null
            ? null
            : new MeResponse(user.Id, user.EmployeeNo, user.DisplayName, user.IsActive,
                user.ProductLedgerPermission,
                user.PurchaseOrderCreatePermission,
                user.PurchaseOrderInfoPermission,
                user.ProcessRecordPermission);
    }
}
