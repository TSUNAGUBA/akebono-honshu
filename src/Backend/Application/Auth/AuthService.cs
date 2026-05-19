using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Auth;

public class AuthService(IAkebonoDbContext db, ITokenService tokenService, IAuditLogger audit)
{
    /// <summary>Iteration 0: ダミー認証。固定パスワード "localdev" のみ許可、ユーザ存在チェックのみ</summary>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        const string DummyPassword = "localdev";

        if (request.Password != DummyPassword)
        {
            await audit.LogAsync(null, "Login.Failure", note: $"Invalid password for {request.LoginId}", success: false, cancellationToken: ct);
            return null;
        }

        var user = await db.Users
            .Where(u => u.LoginId == request.LoginId && u.IsActive && !u.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            await audit.LogAsync(null, "Login.Failure", note: $"User not found: {request.LoginId}", success: false, cancellationToken: ct);
            return null;
        }

        var token = tokenService.IssueToken(user.Id, user.LoginId);

        await audit.LogAsync(user.Id, "Login.Success", entityType: "User", entityId: user.Id, cancellationToken: ct);

        return new LoginResponse(token, user.Id, user.DisplayName);
    }

    public async Task<MeResponse?> GetMeAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .Where(u => u.Id == userId && !u.IsDeleted)
            .FirstOrDefaultAsync(ct);

        return user is null
            ? null
            : new MeResponse(user.Id, user.EmployeeNo, user.DisplayName, user.IsActive);
    }
}
