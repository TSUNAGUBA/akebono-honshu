namespace Akebono.Application.Common;

/// <summary>
/// 業務エラー (想定内の失敗)。中央例外ハンドラ (Presentation) が
/// プラットフォーム標準のエラー封筒 {error: {code, message, userAction?, traceId, details[]}}
/// に変換する (AKB-DOC-12 §6.3)。
///
/// エラーコードは <see cref="AkbErrorCodes"/> の定数を使い、マジック文字列を散在させない
/// (AKB-DOC-12 §14.8 実装ガイド)。
/// </summary>
public class DomainException(string code, int statusCode, string message, string? userAction = null)
    : Exception(message)
{
    /// <summary>AKB-&lt;AREA&gt;-&lt;NNN&gt; 形式のエラーコード。</summary>
    public string Code { get; } = code;

    /// <summary>HTTP ステータスコード (400/403/404/409/422/500 等)。</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>利用者が次に取る行動 (任意、RP-5 アクション誘導)。</summary>
    public string? UserAction { get; } = userAction;

    /// <summary>フィールド単位の内訳 (任意。主に AKB-SYS-002)。</summary>
    public IReadOnlyList<object>? Details { get; init; }

    // ---- 頻出パターンのファクトリ ----

    /// <summary>422 バリデーションエラー (AKB-SYS-002)。</summary>
    public static DomainException Validation(string message, string? userAction = null)
        => new(AkbErrorCodes.SysValidation, StatusCodes422, message, userAction);

    /// <summary>409 一意制約違反 (AKB-SYS-007)。</summary>
    public static DomainException UniqueViolation(string message, string? userAction = null)
        => new(AkbErrorCodes.SysUniqueViolation, 409, message, userAction);

    /// <summary>404 リソース未検出 (AKB-TENANT-010、越境含む存在秘匿)。</summary>
    public static DomainException Concealed(string message)
        => new(AkbErrorCodes.TenantResourceConcealed, 404, message);


    private const int StatusCodes422 = 422;
}
