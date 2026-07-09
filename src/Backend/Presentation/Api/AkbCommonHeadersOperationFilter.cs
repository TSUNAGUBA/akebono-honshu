using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Akebono.Api;

/// <summary>
/// OpenAPI へ共通必須ヘッダを反映する operation filter (AKB-DOC-12 §4-1
/// 「定義を見れば使い方がわかる粒度」)。実装側はヘッダを HttpContext から直接読むため
/// ApiExplorer のパラメータに現れず、生成仕様だけを見るクライアントが必須条件を
/// 知り得ない。本フィルタで補完する。
///   - X-Tenant-Id: 業務 API (認証系 /auth/* を除く /api/maker/v1/**) で必須
///     (欠如 400 AKB-SYS-003。判定 SoT は TenantResolutionMiddleware)
///   - Idempotency-Key: 作成系 POST 4 本で必須 (欠如 400 AKB-SYS-004。
///     判定 SoT は IdempotencyKeyReader + 各エンドポイント)
/// エンドポイント追加時は対象パスをここにも反映すること (CI openapi-check が
/// 生成物の鮮度は守るが、本フィルタの対象漏れまでは検出しない)。
/// </summary>
public sealed class AkbCommonHeadersOperationFilter : IOperationFilter
{
    /// <summary>Idempotency-Key 必須の作成系 POST (docs/platform-integration/README.md §7.1 と対)。</summary>
    private static readonly string[] IdempotentCreatePaths =
    [
        "api/maker/v1/orders",
        "api/maker/v1/production-instructions",
        "api/maker/v1/material-orders",
        "api/maker/v1/products/families/complete",
    ];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = (context.ApiDescription.RelativePath ?? string.Empty).Trim('/');
        if (!path.StartsWith("api/maker/v1/", StringComparison.OrdinalIgnoreCase)) return;

        operation.Parameters ??= new List<OpenApiParameter>();

        // 認証系はテナント解決前に呼ばれるため X-Tenant-Id の適用除外 (セグメント境界で判定)
        var isAuth = path.Equals("api/maker/v1/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("api/maker/v1/auth/", StringComparison.OrdinalIgnoreCase);
        if (!isAuth)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = TenantResolutionMiddleware.TenantHeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Description = "認証テナントの UUID。欠如は 400 AKB-SYS-003、認証クレームとの不一致は 403 AKB-TENANT-002 (AKB-DOC-12 §10.2)。",
                Schema = new OpenApiSchema { Type = "string", Format = "uuid" },
            });
        }

        if (string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
            && IdempotentCreatePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyKeyReader.HeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Description = "作成操作ごとに一意な冪等キー (uuid v4 推奨、128 文字以内)。欠如は 400 AKB-SYS-004、同一キー・異なるペイロードの再送は 409 AKB-SYS-005 (AKB-DOC-12 §8)。",
                Schema = new OpenApiSchema { Type = "string", MaxLength = 128 },
            });
        }
    }
}
