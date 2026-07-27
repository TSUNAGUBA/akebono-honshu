using System.Security.Claims;
using Akebono.Application.Auth;
using Akebono.Application.Common;
using Akebono.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// JwtBearer の OnTokenValidated で users テーブル引当後、ClaimsPrincipal に追加する Claim 名。
    /// 業務ユーザ ID (users.id UUID) を string 化して保持する。
    /// </summary>
    public const string AkebonoUserIdClaim = "akebono_user_id";

    /// <summary>
    /// OnTokenValidated で解決したテナント ID (uuid) を保持する Claim 名。
    /// 一次ソースは Firebase Custom Claims の tenant_id (SoT = akebono-backoffice)、
    /// MVP 暫定フォールバックは users.tenant_id。TenantResolutionMiddleware が
    /// この Claim を ITenantContext へ確定する。
    /// </summary>
    public const string AkebonoTenantIdClaim = "akebono_tenant_id";

    /// <summary>
    /// OnTokenValidated で解決したテナントステータス (tenant.status) を保持する Claim 名。
    /// TenantResolutionMiddleware が trial/active 以外を 403 AKB-TENANT-004 で拒否する。
    /// Firebase Custom Claims 経由でテナントが確定した場合 (bko 接続後) は本 Claim を
    /// 付与しない (停止はプラットフォーム側の Claims 剥奪で反映される)。
    /// </summary>
    public const string AkebonoTenantStatusClaim = "akebono_tenant_status";

    /// <summary>
    /// Firebase ID Token の ClaimsPrincipal から Firebase UID を取得する。
    /// Firebase は `user_id` claim を発行するが、ASP.NET Core の JwtBearer ハンドラが
    /// 一部の claim を `ClaimTypes.NameIdentifier` にマップするケースに備え両方を試す。
    /// </summary>
    internal static string? GetFirebaseUid(ClaimsPrincipal? principal)
        => principal?.FindFirst("user_id")?.Value
           ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// akebono_user_id クレーム未解決時のエラー封筒。
    ///   - 未認証 (トークンなし/無効): 401 AKB-AUTH-001
    ///   - 認証済みだが業務ユーザ未解決 (未紐付け・無効化・キャッシュ失効後の再解決失敗):
    ///     403 AKB-AUTH-005 (台帳の意味論に合わせる。トークンは有効なため 001 は不正確)
    /// </summary>
    internal static IResult UnauthorizedError(HttpContext http)
        => http.User.Identity?.IsAuthenticated == true
            ? ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "業務ユーザに紐付いていないか無効化されています",
                userAction: "管理者にお問合せください")
            : ApiEnvelope.Error(http, 401, AkbErrorCodes.AuthTokenMissing, "Unauthorized");

    /// <summary>ID 指定リソース未検出時の 404 エラー封筒 (AKB-TENANT-010、越境含む存在秘匿)。</summary>
    internal static IResult NotFoundError(HttpContext http)
        => ApiEnvelope.Error(http, 404, AkbErrorCodes.TenantResourceConcealed,
            "指定されたリソースが見つかりません");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/maker/v1/auth");

        // フロントが Firebase Auth ログイン直後に呼ぶ。ClaimsPrincipal から Firebase UID を取得し、
        // users.firebase_uid 引当 + 業務情報返却 + audit log (Login.Success のみ)。
        // 未紐付け / inactive / soft-deleted ユーザは OnTokenValidated 段で akebono_user_id
        // Claim が付与されない。トークン自体は有効なため [Authorize] は通過し、本 endpoint の
        // SyncAsync が null を返して 403 AKB-AUTH-005 になる (拒否監査は OnTokenValidated 内で
        // Auth.LoginRejected.Inactive / Auth.UidUnboundProbe として 5 分 de-dup で記録)。
        group.MapPost("/sync", [Authorize] async (HttpContext http, AuthService svc, CancellationToken ct) =>
        {
            var firebaseUid = GetFirebaseUid(http.User);
            if (string.IsNullOrEmpty(firebaseUid))
                return ApiEnvelope.Error(http, 401, AkbErrorCodes.AuthTokenMissing,
                    "Firebase UID claim missing");

            var result = await svc.SyncAsync(firebaseUid, ct);
            return result is null
                ? ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                    "Firebase ユーザは認証済ですが、業務ユーザに紐付いていないか無効化されています。管理者にお問合せください")
                : ApiEnvelope.Ok(http, result);
        });

        group.MapGet("/me", [Authorize] async (HttpContext http, AuthService svc, CancellationToken ct) =>
        {
            if (!TryGetUserId(http, out var userId))
                return UnauthorizedError(http);

            var me = await svc.GetMeAsync(userId, ct);
            return me is null ? NotFoundError(http) : ApiEnvelope.Ok(http, me);
        });

        return app;
    }

    /// <summary>
    /// JwtBearer + OnTokenValidated 後、ClaimsPrincipal に詰めた akebono_user_id Claim を読む。
    /// 業務ユーザが users テーブルに引当できなかった場合、Claim 自体が付かないため false。
    /// </summary>
    internal static bool TryGetUserId(HttpContext http, out Guid userId)
    {
        userId = Guid.Empty;
        var raw = http.User.FindFirst(AkebonoUserIdClaim)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    /// <summary>
    /// マスタ編集系操作 (M-01/M-02 Create/Update/Delete/Restore) に必要な権限チェック。
    /// product_ledger_permission == 1 (更新可能) を要求 (Phase 5 §3.18 + C-02 権限カテゴリの 1 つ)。
    /// 権限スケールは非単調 (0=なし, 1=更新可能, 2=参照のみ, 3=参照のみ制限) のため、
    /// 書込判定に >= 1 を使うと「参照のみ」に書込を許すバグになる。
    /// 401 (未認証) / 403 (権限不足) / 成功時は actorId を返す。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckMasterEditAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, UnauthorizedError(http));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.DeletedAt != null)
            // 台帳 (AKB-DOC-12 §14.5) の AUTH-005 代表ステータスに合わせ 403
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "ユーザが無効化されています"));

        // 権限スケールは非単調 (0=なし, 1=更新可能, 2=参照のみ, 3=参照のみ制限)。
        // 書込は「更新可能 (==1)」のみ許可する。参照のみ (2/3) は書込不可 (旧 >=1 は 2/3 に誤って書込を許していた)。
        if (actor.ProductLedgerPermission != 1)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には品番台帳管理権限 (更新可能) が必要です"));

        return new(userId, null);
    }

    /// <summary>
    /// 発注書編集系操作 (O-01/O-04/O-05/O-06) に必要な権限チェック。
    /// purchase_order_create_permission == 1 (更新可能) を要求 (Phase 5 §3.18 + C-02)。
    /// 権限スケールは非単調のため、書込判定に >= 1 を使うと「参照のみ」に書込を許すバグになる。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckOrderEditAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, UnauthorizedError(http));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.DeletedAt != null)
            // 台帳 (AKB-DOC-12 §14.5) の AUTH-005 代表ステータスに合わせ 403
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "ユーザが無効化されています"));

        // 権限スケールは非単調 (0=なし, 1=更新可能, 2=参照のみ)。書込は「更新可能 (==1)」のみ許可する
        // (旧 >=1 は 参照のみ(2) に誤って書込を許していた)。
        if (actor.PurchaseOrderCreatePermission != 1)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には発注書作成権限 (更新可能) が必要です"));

        return new(userId, null);
    }

    /// <summary>
    /// 利用者マスタ管理 (Part5、利用者の作成/更新/権限変更/論理削除) に必要な権限チェック。
    /// オーナー権限 (process_record_permission >= 1) を要求する (利用者・権限の管理は最上位権限に限定)。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckUserAdminAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, UnauthorizedError(http));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.DeletedAt != null)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "ユーザが無効化されています"));

        if (actor.ProcessRecordPermission < 1)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には利用者管理権限 (オーナー) が必要です"));

        return new(userId, null);
    }

    /// <summary>
    /// 勤怠の書込操作 (打刻・各種申請) に必要な権限チェック。
    /// attendance_permission == 1 (更新可能) を要求する (Iteration 30、5 つ目の権限カテゴリ)。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckAttendanceWriteAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, UnauthorizedError(http));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.DeletedAt != null)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "ユーザが無効化されています"));

        // 権限スケールは非単調 (0=なし, 1=更新可能, 2=参照のみ)。書込は「更新可能 (==1)」のみ許可する
        // (>=1 とすると 参照のみ(2) に誤って書込を許してしまう)。
        if (actor.AttendancePermission != 1)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には勤怠権限 (更新可能) が必要です"));

        return new(userId, null);
    }

    /// <summary>
    /// 勤怠の参照操作 (自分の勤怠・集計・一覧) に必要な権限チェック。
    /// attendance_permission が 1 (更新可能) または 2 (参照のみ) を要求する (0 は不可)。
    /// 他の利用者の勤怠を参照する場合は別途 <see cref="CheckAttendanceAdminAsync"/> が必要。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckAttendanceReadAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, UnauthorizedError(http));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.DeletedAt != null)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "ユーザが無効化されています"));

        if (actor.AttendancePermission != 1 && actor.AttendancePermission != 2)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には勤怠権限 (参照以上) が必要です"));

        return new(userId, null);
    }

    /// <summary>
    /// 勤怠の管理操作 (全員のタイムカード参照 / 打刻修正・休暇申請の承認却下 / 休暇付与 /
    /// 勤怠ルール・休暇種別の設定) に必要な権限チェック。
    ///
    /// **勤怠参照権限 (attendance_permission 1 or 2) AND オーナー権限 (process_record_permission >= 1)**
    /// の両方を要求する。オーナーであることだけでは足りない —
    /// attendance_permission = 0 は「勤怠機能の利用を明示的に禁じた」状態であり、
    /// そこへ管理操作 (全員の打刻記録の閲覧・承認・付与) を許すと禁止設定が意味を失う。
    /// 本ヘルパーが参照権限を内包するため、呼出側で CheckAttendanceReadAsync を重ねる必要はない。
    ///
    /// (office の admin 相当。hr 中間ロールは honshu に無いため作らない = 権限を緩めない方向で統合)。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckAttendanceAdminAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, UnauthorizedError(http));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.DeletedAt != null)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                "ユーザが無効化されています"));

        // 勤怠機能自体を使えない利用者は、オーナーであっても管理操作に入れない。
        // (非単調スケールのため == 1 / == 2 の列挙で判定する。>= 1 は 0 を弾けない書き方ではないが、
        //  スケールの意味が変わったときに壊れるため CheckAttendanceReadAsync と同じ列挙に揃える)
        if (actor.AttendancePermission != 1 && actor.AttendancePermission != 2)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には勤怠権限 (参照以上) が必要です"));

        if (actor.ProcessRecordPermission < 1)
            return new(null, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "この操作には勤怠管理権限 (オーナー) が必要です"));

        return new(userId, null);
    }
}

internal sealed record MasterEditAuth(Guid? ActorId, IResult? ErrorResult);
