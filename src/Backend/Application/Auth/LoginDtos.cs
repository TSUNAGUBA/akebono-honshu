namespace Akebono.Application.Auth;

public record LoginRequest(string LoginId, string Password);

/// <summary>ログインレスポンス。4 種権限を含み、Frontend での UI 制御に使用 (C-02)。</summary>
public record LoginResponse(
    string Token,
    long UserId,
    string DisplayName,
    short ProductLedgerPermission,
    short PurchaseOrderCreatePermission,
    short PurchaseOrderInfoPermission,
    short ProcessRecordPermission);

public record MeResponse(
    long UserId,
    string EmployeeNo,
    string DisplayName,
    bool IsActive,
    short ProductLedgerPermission,
    short PurchaseOrderCreatePermission,
    short PurchaseOrderInfoPermission,
    short ProcessRecordPermission);
