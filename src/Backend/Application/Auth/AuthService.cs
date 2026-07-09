using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Akebono.Application.Auth;

public class AuthService(IAkebonoDbContext db, IAuditLogger audit, ILogger<AuthService> logger)
{
    /// <summary>
    /// Firebase Auth でログインに成功したフロントが最初に呼ぶ sync endpoint の実装。
    /// JwtBearer ミドルウェアの OnTokenValidated が ID Token 検証 + users 引当 (!IsDeleted && IsActive) +
    /// Claim 付与まで済ませている。未紐付け / inactive / soft-deleted ユーザは Claim 未付与の
    /// まま到達し (トークン自体は有効なため [Authorize] は通過する)、本メソッドが null を返して
    /// endpoint 側で 403 AKB-AUTH-005 になる (拒否監査は OnTokenValidated 内で
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

        // テナント情報 (フロントは以降 X-Tenant-Id ヘッダにこの値を載せる)。
        // tenant はテナントレジストリ投影 (RLS 適用外・読取専用) のため素直に引ける。
        var tenantCode = await db.Tenants
            .Where(t => t.TenantId == user.TenantId)
            .Select(t => t.TenantCode)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return new SyncResponse(user.Id, user.EmployeeNo, user.DisplayName, user.IsActive,
            user.ProductLedgerPermission,
            user.PurchaseOrderCreatePermission,
            user.PurchaseOrderInfoPermission,
            user.ProcessRecordPermission,
            user.TenantId,
            tenantCode);
    }

    public async Task<MeResponse?> GetMeAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .Where(u => u.Id == userId && !u.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (user is null) return null;

        var tenantCode = await db.Tenants
            .Where(t => t.TenantId == user.TenantId)
            .Select(t => t.TenantCode)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return new MeResponse(user.Id, user.EmployeeNo, user.DisplayName, user.IsActive,
            user.ProductLedgerPermission,
            user.PurchaseOrderCreatePermission,
            user.PurchaseOrderInfoPermission,
            user.ProcessRecordPermission,
            user.TenantId,
            tenantCode);
    }
}
