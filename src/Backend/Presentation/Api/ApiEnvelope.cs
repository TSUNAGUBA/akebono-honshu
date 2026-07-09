using Akebono.Application.Common;

namespace Akebono.Api;

/// <summary>
/// プラットフォーム標準の API 封筒 (AKB-DOC-12 §6)。
///   成功: {data, meta: {requestId, source, warnings[]}}
///   エラー: {error: {code, message, userAction?, traceId, details[]}}
/// requestId / traceId は HttpContext.TraceIdentifier を用い、構造化ログと突合できる。
/// </summary>
public static class ApiEnvelope
{
    /// <summary>成功封筒 (200)。data は単一リソースまたは配列。</summary>
    public static IResult Ok(HttpContext http, object? data)
        => Results.Ok(new { data, meta = Meta(http) });

    /// <summary>作成成功封筒 (201 + Location)。</summary>
    public static IResult Created(HttpContext http, string location, object? data)
        => Results.Created(location, new { data, meta = Meta(http) });

    /// <summary>エラー封筒。</summary>
    public static IResult Error(
        HttpContext http, int statusCode, string code, string message,
        string? userAction = null, IReadOnlyList<object>? details = null)
        => Results.Json(
            ErrorBody(http, code, message, userAction, details),
            statusCode: statusCode);

    /// <summary>ミドルウェアから直接レスポンスへ書き込む場合のエラー封筒本体。</summary>
    public static object ErrorBody(
        HttpContext http, string code, string message,
        string? userAction = null, IReadOnlyList<object>? details = null)
        => new
        {
            error = new
            {
                code,
                message,
                userAction,
                traceId = http.TraceIdentifier,
                details = details ?? [],
            },
        };

    /// <summary>DomainException からのエラー封筒。</summary>
    public static IResult Error(HttpContext http, DomainException ex)
        => Error(http, ex.StatusCode, ex.Code, ex.Message, ex.UserAction, ex.Details);

    private static object Meta(HttpContext http)
        => new
        {
            requestId = http.TraceIdentifier,
            source = "live",
            warnings = Array.Empty<object>(),
        };
}
