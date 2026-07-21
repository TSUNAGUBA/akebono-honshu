using Akebono.Application.Common;
using Akebono.Application.Users;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 利用者マスタ (Part5) エンドポイント。
///   - GET (list/single) は認証必須 (発注/商品フォームの担当者候補にも使用)。
///   - POST/PATCH/DELETE/Restore は利用者管理権限 (オーナー = process_record_permission >= 1) 必須
///     (AuthEndpoints.CheckUserAdminAsync)。
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/maker/v1/users");

        group.MapGet("/", async (HttpContext http, UserQueryService svc, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);

            var users = await svc.ListAsync(actorId, ct);
            return ApiEnvelope.Ok(http, users);
        });

        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, UserQueryService svc, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out _))
                return AuthEndpoints.UnauthorizedError(http);
            var user = await svc.GetAsync(id, ct);
            return user is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, user);
        });

        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    UserQueryService svc, UserWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckUserAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/users/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              UserQueryService svc, Guid id, UserWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckUserAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        group.MapDelete("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              UserQueryService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckUserAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        group.MapPost("/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                     UserQueryService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckUserAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.RestoreAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        return app;
    }
}
