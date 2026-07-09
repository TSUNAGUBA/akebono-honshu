using Akebono.Application.Common;
using Akebono.Application.Migration;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Api.Endpoints;

/// <summary>
/// MIG-3 既存 CSV 取込 endpoint (Phase 7 Iteration 4 Hardening)。
/// 画面 /admin/legacy-import からアップロードされた CSV を Backend で取込む。
/// 認可: process_record_permission == 1 (Owner) のみ。
/// </summary>
public static class LegacyImportEndpoints
{
    private const long MaxCsvBytes = 50L * 1024 * 1024; // 50 MB

    public static IEndpointRouteBuilder MapLegacyImportEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/maker/v1/admin");

        admin.MapPost("/legacy-import", async (
            HttpContext http,
            IAkebonoDbContext db,
            LegacyImportService svc,
            IFormFile file,
            CancellationToken ct) =>
        {
            // 認可: process_record_permission = 1 (Owner) のみ
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);

            var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == actorId, ct);
            if (actor is null || !actor.IsActive || actor.IsDeleted)
                // 台帳 (AKB-DOC-12 §14.5) の AUTH-005 代表ステータスに合わせ 403
                return ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthAccountInactive,
                    "ユーザが無効化されています");

            if (actor.ProcessRecordPermission < 1)
                return ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                    "この操作にはオーナー権限 (process_record_permission = 1) が必要です");

            // バリデーション
            if (file is null || file.Length == 0)
                return ApiEnvelope.Error(http, 400, AkbErrorCodes.SysMalformedBody,
                    "CSV ファイルが添付されていません");

            if (file.Length > MaxCsvBytes)
                return ApiEnvelope.Error(http, 413, AkbErrorCodes.SysPayloadTooLarge,
                    $"ファイルサイズが上限 ({MaxCsvBytes / 1024 / 1024} MB) を超えています");

            // CSV を MemoryStream にコピー (DetectEncoding が seek を要求するため)
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;

            // 想定エラー (空 CSV 等) は LegacyImportService が DomainException で表現し、
            // 想定外 (DB 障害等) は中央 ApiExceptionMiddleware が固定メッセージ + traceId 付き
            // サーバーログへ変換する (AKB-DOC-12 §6.3: 生の例外メッセージ = 内部識別子・
            // 取込データ由来の値をレスポンスへ載せない)。ここで catch しない。
            var result = await svc.ExecuteAsync(ms, file.FileName, ct);
            return ApiEnvelope.Ok(http, result);
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<LegacyImportResult>(StatusCodes.Status200OK);

        return app;
    }
}
