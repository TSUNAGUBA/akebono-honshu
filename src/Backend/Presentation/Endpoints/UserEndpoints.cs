using Akebono.Application.Users;

namespace Akebono.Api.Endpoints;

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

        return app;
    }
}
