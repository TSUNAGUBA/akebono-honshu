using Akebono.Api.Endpoints;
using Akebono.Application.Common;

namespace Akebono.Api;

/// <summary>
/// テナントコンテキスト確定ミドルウェア (AKB-DOC-12 §10.2)。
/// UseAuthentication の後段に配置する。
///
///   1. OnTokenValidated が付与した akebono_tenant_id クレームを読み、ITenantContext に設定する
///      (クレームの一次ソースは Firebase Custom Claims の tenant_id、MVP 暫定は users.tenant_id。
///       解決ロジックは Program.cs の OnTokenValidated 参照)
///   2. クライアントが X-Tenant-Id ヘッダを送っている場合はクレームと突合し、
///      不一致は 403 AKB-TENANT-002 で拒否する
///
/// 未認証リクエスト・クレーム欠落時は ITenantContext を未確定のままにする
/// (テナントスコープの DB アクセスはアプリ層フィルタ + RLS でフェイルクローズ)。
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantHeaderName = "X-Tenant-Id";

    /// <summary>利用を許可するテナントステータス (AKB-DOC-09 ライフサイクル)。</summary>
    private static readonly string[] AllowedTenantStatuses = ["trial", "active"];

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(AuthEndpoints.AkebonoTenantIdClaim)?.Value is { } claimValue
            && Guid.TryParse(claimValue, out var tenantId))
        {
            if (context.Request.Headers.TryGetValue(TenantHeaderName, out var headerValues)
                && headerValues.Count > 0)
            {
                if (!Guid.TryParse(headerValues.ToString(), out var headerTenantId)
                    || headerTenantId != tenantId)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ApiEnvelope.ErrorBody(
                        context, AkbErrorCodes.TenantHeaderMismatch,
                        "X-Tenant-Id ヘッダが認証テナントと一致しません",
                        userAction: "再ログインするか、テナントを選択し直してください"));
                    return;
                }
            }

            // テナントステータス不許可 (suspended / terminating / terminated) は 403
            // AKB-TENANT-004 で拒否する。ステータス Claim は RDS 解決経路でのみ付与される
            // (Custom Claims 経路では停止 = Claims 剥奪がプラットフォーム側の責務。
            //  Claim 欠落時は許可として扱う)。
            var statusClaim = context.User.FindFirst(AuthEndpoints.AkebonoTenantStatusClaim)?.Value;
            if (statusClaim is not null && !AllowedTenantStatuses.Contains(statusClaim))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(ApiEnvelope.ErrorBody(
                    context, AkbErrorCodes.TenantStatusNotAllowed,
                    "テナントの契約状態では本サービスを利用できません",
                    userAction: "契約状態を管理者・運営へ確認してください"));
                return;
            }

            tenantContext.TenantId = tenantId;
        }

        await next(context);
    }
}
