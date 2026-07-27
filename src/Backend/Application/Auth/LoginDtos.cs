namespace Akebono.Application.Auth;

/// <summary>
/// Firebase Auth ログイン直後にフロントが呼ぶ <c>POST /api/maker/v1/auth/sync</c> の戻り値。
/// 認証は JwtBearer + Firebase JWKS で完結するため Backend は Token を発行しない (Iter 4 段階 B)。
/// Frontend は Firebase JS SDK が保持する ID Token を都度 <c>getIdToken()</c> で取得して送る。
/// TenantId / TenantCode はプラットフォーム統合 (AKB-DOC-12 §10) で追加。フロントは以降の
/// リクエストで X-Tenant-Id ヘッダに TenantId を明示する。
/// </summary>
public record SyncResponse(
    Guid UserId,
    string EmployeeNo,
    string DisplayName,
    bool IsActive,
    short ProductLedgerPermission,
    short PurchaseOrderCreatePermission,
    short PurchaseOrderInfoPermission,
    short ProcessRecordPermission,
    Guid TenantId,
    string TenantCode,
    // 勤怠 (Iteration 30)。末尾追加 + 既定値付きで下位互換 (旧フロントは無視するだけ)。
    // フロントの派生: canPunch = attendancePermission === 1 && punchRequired
    short AttendancePermission = 1,
    bool PunchRequired = true);

public record MeResponse(
    Guid UserId,
    string EmployeeNo,
    string DisplayName,
    bool IsActive,
    short ProductLedgerPermission,
    short PurchaseOrderCreatePermission,
    short PurchaseOrderInfoPermission,
    short ProcessRecordPermission,
    Guid TenantId,
    string TenantCode,
    // 勤怠 (Iteration 30)。SyncResponse と同じフィールドを返す (画面リロード後も権限が復元される)。
    short AttendancePermission = 1,
    bool PunchRequired = true);
