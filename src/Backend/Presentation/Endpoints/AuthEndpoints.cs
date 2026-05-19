using Akebono.Application.Auth;

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
}
