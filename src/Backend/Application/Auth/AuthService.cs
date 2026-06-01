using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Akebono.Application.Auth;

public class AuthService(IAkebonoDbContext db, IAuditLogger audit, ILogger<AuthService> logger)
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
            // (cache TTL 内に user が IsActive=false / IsDeleted=true へ編集された race)。
            // Claim 付与済を信用せず安全側に倒し 403。拒否監査は OnTokenValidated で既に
            // 記録済のため audit は呼ばないが、CloudWatch から事象を観測できるよう warn ログを残す
            // (旧実装で Login.Failure audit が拾っていた可観測性を log で確保)。
            // CloudWatch Logs への UID 露出を最小化するため、OnTokenValidated 側と同じく
            // 先頭 8 文字に短縮して記録 (audit_logs.note との整合性、5 周目 P2-NEW-2 反映)。
            var uidShort = firebaseUid.Length > 8
                ? firebaseUid.Substring(0, 8) + "..."
                : firebaseUid;
            logger.LogWarning(
                "SyncAsync: OnTokenValidated 通過後に users 行が引けず (uid={UidShort})。" +
                "cache TTL 内に user が編集された race の可能性",
                uidShort);
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
