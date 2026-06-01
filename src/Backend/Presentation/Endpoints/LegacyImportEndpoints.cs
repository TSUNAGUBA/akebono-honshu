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
        var admin = app.MapGroup("/api/v1/admin");

        admin.MapPost("/legacy-import", async (
            HttpContext http,
            IAkebonoDbContext db,
            LegacyImportService svc,
            IFormFile file,
            CancellationToken ct) =>
        {
            // 認可: process_record_permission = 1 (Owner) のみ
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");

            var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == actorId, ct);
            if (actor is null || !actor.IsActive || actor.IsDeleted)
                return Results.Problem(statusCode: 401, title: "Unauthorized",
                    detail: "ユーザが無効化されています");

            if (actor.ProcessRecordPermission < 1)
                return Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: "この操作にはオーナー権限 (process_record_permission = 1) が必要です");

            // バリデーション
            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 400, title: "Bad Request",
                    detail: "CSV ファイルが添付されていません");

            if (file.Length > MaxCsvBytes)
                return Results.Problem(statusCode: 413, title: "Payload Too Large",
                    detail: $"ファイルサイズが上限 ({MaxCsvBytes / 1024 / 1024} MB) を超えています");

            // CSV を MemoryStream にコピー (DetectEncoding が seek を要求するため)
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;

            try
            {
                var result = await svc.ExecuteAsync(ms, file.FileName, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 422, title: "CSV 取込エラー",
                    detail: ex.Message);
            }
            catch (DbUpdateException ex)
            {
                return Results.Problem(statusCode: 500, title: "DB 更新エラー",
                    detail: ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "取込中に予期せぬエラー",
                    detail: ex.Message);
            }
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<LegacyImportResult>(StatusCodes.Status200OK);

        return app;
    }
}
