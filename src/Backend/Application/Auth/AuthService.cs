using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Auth;

public class AuthService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>
    /// Firebase Auth でログインに成功したフロントが最初に呼ぶ sync endpoint の実装。
    /// JwtBearer ミドルウェアの OnTokenValidated が ID Token 検証 + users 引当 (!IsDeleted && IsActive) +
    /// Claim 付与まで済ませているため、本メソッドが [Authorize] を通って到達した時点で
    /// users 行は確実に有効 (active かつ非削除)。
    /// 未紐付け / inactive / soft-deleted ユーザは OnTokenValidated 段で Claim 未付与となり
    /// [Authorize] の 401 で SyncAsync に到達しない (拒否監査は OnTokenValidated 内で
    /// Auth.LoginRejected.Inactive / Auth.UidUnboundProbe として 5 分 de-dup で記録)。
    /// </summary>
    public async Task<SyncResponse?> SyncAsync(string firebaseUid, CancellationToken ct = default)
    {
        var user = await db.Users
            .Where(u => u.FirebaseUid == firebaseUid && !u.IsDeleted && u.IsActive)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            // OnTokenValidated を通過しているのに本メソッドで users 行が引けない極稀ケース
            // (cache TTL 内に user が編集された等)。Claim 付与済を信用せず安全側に倒し 403。
            // 拒否監査は OnTokenValidated で既に記録済 (Auth.LoginRejected.Inactive 等)。
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
