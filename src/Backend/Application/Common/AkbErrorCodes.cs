namespace Akebono.Application.Common;

/// <summary>
/// エラーコード定数 (あけぼの SCM プラットフォーム体系 AKB-&lt;AREA&gt;-&lt;NNN&gt;)。
///
/// 台帳の SoT:
///   - 共通 AREA (AUTH / TENANT / SYS): akebono-scm-platform AKB-DOC-12 §14.5〜§14.7
///   - MAKER AREA: akebono-scm-platform AKB-DOC-05 §エラーコード
/// 本クラスは台帳のうち本アプリが使用するコードのみを定数化する (新規採番はしない)。
/// 旧体系 (ORDER-NNN / PINST-NNN 等) からの写像表は docs/platform-integration/README.md 参照。
/// </summary>
public static class AkbErrorCodes
{
    // ---- AUTH (認証・認可) ----
    /// <summary>認証トークン欠如 (401)</summary>
    public const string AuthTokenMissing = "AKB-AUTH-001";
    /// <summary>認証トークン不正・署名検証失敗 (401)</summary>
    public const string AuthTokenInvalid = "AKB-AUTH-002";
    /// <summary>認証トークン期限切れ (401)</summary>
    public const string AuthTokenExpired = "AKB-AUTH-003";
    /// <summary>アカウント無効・停止 (403)</summary>
    public const string AuthAccountInactive = "AKB-AUTH-005";
    /// <summary>権限不足 (403)</summary>
    public const string AuthInsufficientPermission = "AKB-AUTH-010";

    // ---- TENANT (テナント境界) ----
    /// <summary>X-Tenant-Id ヘッダと claims の不一致 (403)</summary>
    public const string TenantHeaderMismatch = "AKB-TENANT-002";
    /// <summary>テナントステータス不許可 (suspended / terminated 等、403)</summary>
    public const string TenantStatusNotAllowed = "AKB-TENANT-004";
    /// <summary>RLS テナントコンテキスト未設定 = 内部安全違反 (500)</summary>
    public const string TenantContextNotInjected = "AKB-TENANT-005";
    /// <summary>リソース未検出 (越境含む存在秘匿、404)</summary>
    public const string TenantResourceConcealed = "AKB-TENANT-010";

    // ---- SYS (横断システム) ----
    /// <summary>リクエストボディ不正 (400)</summary>
    public const string SysMalformedBody = "AKB-SYS-001";
    /// <summary>バリデーションエラー (422)</summary>
    public const string SysValidation = "AKB-SYS-002";
    /// <summary>Idempotency-Key 欠如 (400)</summary>
    public const string SysIdempotencyKeyMissing = "AKB-SYS-004";
    /// <summary>Idempotency-Key 競合: 同一キー・異なるペイロード (409)</summary>
    public const string SysIdempotencyKeyConflict = "AKB-SYS-005";
    /// <summary>楽観ロック競合 (409)</summary>
    public const string SysOptimisticConflict = "AKB-SYS-006";
    /// <summary>一意制約違反 (409)</summary>
    public const string SysUniqueViolation = "AKB-SYS-007";
    /// <summary>リクエストサイズ超過 (413)</summary>
    public const string SysPayloadTooLarge = "AKB-SYS-010";
    /// <summary>内部サーバーエラー (500)</summary>
    public const string SysInternal = "AKB-SYS-020";

    // ---- MAKER (メーカーサービス業務。採番 SoT = AKB-DOC-05) ----
    /// <summary>採番衝突 (advisory lock リトライ上限超過、409)</summary>
    public const string MakerNumberingConflict = "AKB-MAKER-002";
    /// <summary>所要量未定義 (BOM 未登録・所要量なし、422)</summary>
    public const string MakerBomRequirementMissing = "AKB-MAKER-011";
    /// <summary>生産指示の状態遷移違反 (409)</summary>
    public const string MakerProductionStateViolation = "AKB-MAKER-020";
    /// <summary>発注の状態遷移違反 (409)</summary>
    public const string MakerPurchaseOrderStateViolation = "AKB-MAKER-031";
}
