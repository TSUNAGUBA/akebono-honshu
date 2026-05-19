using Akebono.Application.Auth;
using Akebono.Application.Common;
using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", async (LoginRequest req, AuthService svc, CancellationToken ct) =>
        {
            var result = await svc.LoginAsync(req, ct);
            return result is null
                ? Results.Problem(statusCode: 401, title: "Login failed", detail: "Invalid credentials")
                : Results.Ok(result);
        });

        group.MapGet("/me", async (HttpContext http, ITokenService tokens, AuthService svc, CancellationToken ct) =>
        {
            if (!TryGetUserId(http, tokens, out var userId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");

            var me = await svc.GetMeAsync(userId, ct);
            return me is null ? Results.NotFound() : Results.Ok(me);
        });

        return app;
    }

    internal static bool TryGetUserId(HttpContext http, ITokenService tokens, out long userId)
    {
        userId = 0;
        var header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
            return false;
        return tokens.TryValidate(header[7..], out userId);
    }

    /// <summary>
    /// マスタ編集系操作 (M-01/M-02 Create/Update/Delete/Restore) に必要な権限チェック。
    /// product_ledger_permission >= 1 を要求 (Phase 5 §3.18 + C-02 4 権限カテゴリの 1 つ)。
    /// 401 (未認証) / 403 (権限不足) / 成功時は actorId を返す。
    /// </summary>
    internal static async Task<MasterEditAuth> CheckMasterEditAsync(
        HttpContext http,
        ITokenService tokens,
        IAkebonoDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(http, tokens, out var userId))
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
}

internal sealed record MasterEditAuth(long? ActorId, IResult? ErrorResult);
