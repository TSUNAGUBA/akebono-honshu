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
///   2. 業務 API (認証系を除く /api/maker/v1/**) では X-Tenant-Id ヘッダを必須とし、
///      欠如は 400 AKB-SYS-003 で拒否する (AKB-DOC-12 §10.2 の突合を常時有効化する)
///   3. X-Tenant-Id ヘッダをクレームと突合し、不一致は 403 AKB-TENANT-002 で拒否する
///
/// 未認証リクエスト・クレーム欠落時は ITenantContext を未確定のままにする
/// (テナントスコープの DB アクセスはアプリ層フィルタ + RLS でフェイルクローズ。
///  401 は各 endpoint / OnChallenge が返すため、本ミドルウェアはヘッダ検査を行わない)。
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantHeaderName = "X-Tenant-Id";

    /// <summary>利用を許可するテナントステータス (AKB-DOC-09 ライフサイクル)。</summary>
    private static readonly string[] AllowedTenantStatuses = ["trial", "active"];

    /// <summary>X-Tenant-Id を必須とする業務 API のプレフィクス。</summary>
    private static readonly PathString ApiPrefix = new("/api/maker/v1");

    /// <summary>
    /// X-Tenant-Id 必須の適用除外: 認証系 (sync / me)。ログイン直後のテナント解決前
    /// (フロントは auth 応答から tenantId を得る) に呼ばれるため、ヘッダを要求できない。
    /// </summary>
    private static readonly PathString AuthPrefix = new("/api/maker/v1/auth");

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(AuthEndpoints.AkebonoTenantIdClaim)?.Value is { } claimValue
            && Guid.TryParse(claimValue, out var tenantId))
        {
            // 空値ヘッダ (X-Tenant-Id:) は「不一致」ではなく「欠如」として扱う (400 AKB-SYS-003 が台帳意味に忠実)
            var hasHeader = context.Request.Headers.TryGetValue(TenantHeaderName, out var headerValues)
                && !string.IsNullOrWhiteSpace(headerValues.ToString());

            // 業務 API はヘッダ必須 (AKB-SYS-003 必須ヘッダ欠如)。認証系のみ適用除外。
            if (!hasHeader
                && context.Request.Path.StartsWithSegments(ApiPrefix)
                && !context.Request.Path.StartsWithSegments(AuthPrefix))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(ApiEnvelope.ErrorBody(
                    context, AkbErrorCodes.SysHeaderMissing,
                    "X-Tenant-Id ヘッダが必要です",
                    userAction: "クライアントを最新化するか、X-Tenant-Id ヘッダを付与して再実行してください"));
                return;
            }

            if (hasHeader)
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
