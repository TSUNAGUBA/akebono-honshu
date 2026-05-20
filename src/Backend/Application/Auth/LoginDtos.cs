namespace Akebono.Application.Auth;

/// <summary>
/// Firebase Auth ログイン直後にフロントが呼ぶ <c>POST /api/v1/auth/sync</c> の戻り値。
/// 認証は JwtBearer + Firebase JWKS で完結するため Backend は Token を発行しない (Iter 4 段階 B)。
/// Frontend は Firebase JS SDK が保持する ID Token を都度 <c>getIdToken()</c> で取得して送る。
/// </summary>
public record SyncResponse(
    long UserId,
    string EmployeeNo,
    string DisplayName,
    bool IsActive,
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
