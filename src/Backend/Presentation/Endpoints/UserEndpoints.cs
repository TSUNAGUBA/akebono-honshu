using Akebono.Application.Users;

namespace Akebono.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users");

        group.MapGet("/", async (HttpContext http, UserQueryService svc, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");

            var users = await svc.ListAsync(actorId, ct);
            return Results.Ok(new { data = users });
        });

        return app;
    }
}
