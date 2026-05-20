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
    /// 業務ユーザ ID (users.id BIGINT) を string 化して保持する。
    /// </summary>
    public const string AkebonoUserIdClaim = "akebono_user_id";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        // フロントが Firebase Auth ログイン直後に呼ぶ。ClaimsPrincipal から Firebase UID
        // (Claim "user_id") を取り出し、users.firebase_uid 引当 + 業務情報返却 + audit log。
        // 未紐付け UID は 403 を返し「Firebase は通ったが業務ユーザが紐付いていない」を区別。
        group.MapPost("/sync", [Authorize] async (HttpContext http, AuthService svc, CancellationToken ct) =>
        {
            var firebaseUid = http.User.FindFirst("user_id")?.Value
                ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return Results.Problem(statusCode: 401, title: "Unauthorized",
                    detail: "Firebase UID claim missing");

            var result = await svc.SyncAsync(firebaseUid, ct);
            return result is null
                ? Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: "Firebase ユーザは認証済ですが、業務ユーザに紐付いていません。管理者にお問合せください")
                : Results.Ok(result);
        });

        group.MapGet("/me", [Authorize] async (HttpContext http, AuthService svc, CancellationToken ct) =>
        {
            if (!TryGetUserId(http, out var userId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");

            var me = await svc.GetMeAsync(userId, ct);
            return me is null ? Results.NotFound() : Results.Ok(me);
        });

        return app;
    }

    /// <summary>
    /// JwtBearer + OnTokenValidated 後、ClaimsPrincipal に詰めた akebono_user_id Claim を読む。
    /// 業務ユーザが users テーブルに引当できなかった場合、Claim 自体が付かないため false。
    /// </summary>
    internal static bool TryGetUserId(HttpContext http, out long userId)
    {
        userId = 0;
        var raw = http.User.FindFirst(AkebonoUserIdClaim)?.Value;
        return long.TryParse(raw, out userId);
    }

    /// <summary>
    /// マスタ編集系操作 (M-01/M-02 Create/Update/Delete/Restore) に必要な権限チェック。
    /// product_ledger_permission >= 1 を要求 (Phase 5 §3.18 + C-02 4 権限カテゴリの 1 つ)。
    /// 401 (未認証) / 403 (権限不足) / 成功時は actorId を返す。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckMasterEditAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, Results.Problem(statusCode: 401, title: "Unauthorized"));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.IsDeleted)
            return new(null, Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "ユーザが無効化されています"));

        if (actor.ProductLedgerPermission < 1)
            return new(null, Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "この操作には品番台帳管理権限 (更新可能) が必要です"));

        return new(userId, null);
    }

    /// <summary>
    /// 発注書編集系操作 (O-01/O-04/O-05/O-06) に必要な権限チェック。
    /// purchase_order_create_permission >= 1 を要求 (Phase 5 §3.18 + C-02)。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckOrderEditAsync(
        HttpContext http,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, out var userId))
            return new(null, Results.Problem(statusCode: 401, title: "Unauthorized"));

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (actor is null || !actor.IsActive || actor.IsDeleted)
            return new(null, Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "ユーザが無効化されています"));

        if (actor.PurchaseOrderCreatePermission < 1)
            return new(null, Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "この操作には発注書作成権限 (更新可能) が必要です"));

        return new(userId, null);
    }
}

internal sealed record MasterEditAuth(long? ActorId, IResult? ErrorResult);
