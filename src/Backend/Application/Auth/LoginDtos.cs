namespace Akebono.Application.Auth;

public record LoginRequest(string LoginId, string Password);

public record LoginResponse(string Token, long UserId, string DisplayName);

public record MeResponse(long UserId, string EmployeeNo, string DisplayName, bool IsActive);
