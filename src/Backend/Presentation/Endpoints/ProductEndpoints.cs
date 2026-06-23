using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Application.Products;
using Akebono.Domain.Products;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 商品関連 REST endpoint (Phase 5 §2.4、P-01〜P-06)。
/// 編集系は product_ledger_permission >= 1 要求 (AuthEndpoints.CheckMasterEditAsync)。
/// 画像は Iteration 2 でローカルファイル保存 (wwwroot/uploads/product-images/{familyId}/)、
/// Iteration 4 で S3 + Pre-signed URL に置換予定。
/// </summary>
public static class ProductEndpoints
{
    /// <summary>画像保存先ディレクトリの相対パス (Backend Presentation の wwwroot 配下)。</summary>
    private const string ImageUploadDirRelative = "uploads/product-images";
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var families = app.MapGroup("/api/v1/products/families");

        // 一覧 (P-04)
        families.MapGet("/", async (HttpContext http, ProductFamilyService svc,
                                     bool? includeDeleted, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            var items = await svc.ListAsync(actorId, includeDeleted ?? false, ct);
            return Results.Ok(new { data = items });
        });

        // 詳細 (P-05)
        families.MapGet("/{id:long}", async (HttpContext http, ProductFamilyService svc,
                                              long id, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            var detail = await svc.GetDetailAsync(id, actorId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        // バルク登録 (P-01〜P-03)
        families.MapPost("/complete", async (HttpContext http, IAkebonoDbContext db,
                                              ProductFamilyService svc, CompleteFamilyRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            try
            {
                var resp = await svc.CreateCompleteAsync(req, auth.ActorId!.Value, ct);
                return Results.Created($"/api/v1/products/families/{resp.Family.Id}", resp);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 422, title: "Validation error", detail: ex.Message);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_product_families") == true)
            {
                return Results.Problem(statusCode: 409, title: "Conflict", detail: "同一企画キーの product_families が既に存在します (PROD-003)");
            }
        });

        // 更新 (P-05)
        families.MapPatch("/{id:long}", async (HttpContext http, IAkebonoDbContext db,
                                                 ProductFamilyService svc, long id, UpdateFamilyRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        // 論理削除 (P-05)
        families.MapDelete("/{id:long}", async (HttpContext http, IAkebonoDbContext db,
                                                  ProductFamilyService svc, long id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // 単価追加 (BR-04 履歴管理)
        families.MapPost("/{id:long}/supplier-prices", async (HttpContext http, IAkebonoDbContext db,
                                                                ProductSupplierPriceService svc, long id, AddSupplierPriceRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            try
            {
                var entity = await svc.AddAsync(id, req, auth.ActorId!.Value, ct);
                return Results.Created($"/api/v1/products/families/{id}/supplier-prices/{entity.Id}",
                    new SupplierPriceSummary(entity.Id, entity.SupplierId, entity.UnitPrice, entity.EffectiveFrom));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 422, title: "Validation error", detail: ex.Message);
            }
        });

        // 単価履歴一覧
        families.MapGet("/{id:long}/supplier-prices", async (HttpContext http,
                                                               ProductSupplierPriceService svc, long id, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            var items = await svc.ListHistoryAsync(id, actorId, ct);
            var data = items.Select(p => new CurrentSupplierPrice(
                p.Id, p.SupplierId, p.Supplier?.Code ?? "?", p.Supplier?.Name ?? "?",
                p.UnitPrice, p.CurrencyCode, p.ExchangeRate, p.EffectiveFrom, p.EffectiveTo, p.DecidedAt));
            return Results.Ok(new { data });
        });

        // ── P-06 画像管理 ─────────────────────────────────
        // 画像アップロード: IFormFile を IImageStorageService 経由で保存 + メタ DB 登録。
        // 保存先は Local (wwwroot) / S3 を appsettings.ImageStorage:Provider で切替 (Iter 4 段階 C)。
        families.MapPost("/{id:long}/images", async (HttpContext http, IAkebonoDbContext db,
                                                       IImageStorageService imageStorage,
                                                       long id, IFormFile file, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 422, title: "Validation error", detail: "ファイルが空です");
            if (file.Length > MaxImageBytes)
                return Results.Problem(statusCode: 422, title: "Validation error", detail: "ファイルサイズは 5MB 以下にしてください");
            if (!AllowedMimeTypes.Contains(file.ContentType))
                return Results.Problem(statusCode: 422, title: "Validation error", detail: "対応形式は JPEG / PNG / WebP のみ");

            var family = await db.ProductFamilies.FirstOrDefaultAsync(pf => pf.Id == id && !pf.IsDeleted, ct);
            if (family is null) return Results.NotFound();

            var currentCount = await db.ProductImages.CountAsync(i => i.ProductFamilyId == id && !i.IsDeleted, ct);
            if (currentCount >= 5)
                return Results.Problem(statusCode: 409, title: "Conflict", detail: "画像は最大 5 枚までです (BR-10)");

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext))
                ext = file.ContentType switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".bin",
                };
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var s3Key = $"{ImageUploadDirRelative}/{id}/{fileName}";

            await using (var stream = file.OpenReadStream())
            {
                await imageStorage.SaveAsync(s3Key, stream, file.ContentType, ct);
            }

            // ストレージ書込後 → DB save の順。DB save 失敗時は S3 孤児を best-effort 削除する
            // (reviewer 指摘 M5、CLAUDE.md 原則 4 非ブロッキング・原則 6 SoT 一貫性)。
            // 削除失敗は logger に記録のみで主要フローに伝播させない。
            var now = SystemTime.Now;
            var image = new ProductImage
            {
                ProductFamilyId = id,
                S3Key = s3Key,
                OrderNo = (short)(currentCount + 1),
                MimeType = file.ContentType,
                FileSizeBytes = (int)file.Length,
                OriginalFilename = file.FileName,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = auth.ActorId!.Value, UpdatedByUserId = auth.ActorId!.Value,
            };
            db.ProductImages.Add(image);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                try { await imageStorage.DeleteAsync(s3Key, CancellationToken.None); }
                catch (Exception cleanupEx)
                {
                    // domain entity を category にしないよう、ILoggerFactory で文字列 category を指定
                    // (reviewer 2 周目指摘 M-NEW-1 対応)。Endpoints 側は static class のため
                    // `ILogger<ProductEndpoints>` は使用できず、ILoggerFactory.CreateLogger 経由が慣例。
                    var cleanupLogger = http.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Akebono.Api.Endpoints.ProductEndpoints.ImageUpload");
                    cleanupLogger.LogError(cleanupEx,
                        "DB save 失敗後の S3 孤児削除に失敗 (key={Key}) - 手動削除が必要", s3Key);
                }
                throw;
            }

            var url = await imageStorage.GetUrlAsync(s3Key, ct);
            return Results.Created($"/api/v1/products/families/{id}/images/{image.Id}",
                new ImageSummary(image.Id, image.OrderNo, image.S3Key, image.ThumbS3Key,
                    image.MimeType, image.FileSizeBytes, image.OriginalFilename, url));
        }).DisableAntiforgery();

        // 並び順変更
        families.MapPatch("/{id:long}/images/reorder", async (HttpContext http, IAkebonoDbContext db,
                                                                long id, List<long> orderedIds, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var images = await db.ProductImages
                .Where(i => i.ProductFamilyId == id && !i.IsDeleted)
                .ToListAsync(ct);

            // 一度全ての order_no を 100 + index に逃がしてから再設定 (UNIQUE 衝突回避)
            for (var i = 0; i < images.Count; i++)
            {
                images[i].OrderNo = (short)(100 + i);
            }
            await db.SaveChangesAsync(ct);

            short newOrder = 1;
            foreach (var imageId in orderedIds)
            {
                var img = images.FirstOrDefault(x => x.Id == imageId);
                if (img is null) continue;
                img.OrderNo = newOrder++;
                img.UpdatedAt = SystemTime.Now;
                img.UpdatedByUserId = auth.ActorId!.Value;
            }
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        // 画像論理削除
        families.MapDelete("/{id:long}/images/{imageId:long}", async (HttpContext http, IAkebonoDbContext db,
                                                                       long id, long imageId, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var image = await db.ProductImages
                .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductFamilyId == id, ct);
            if (image is null) return Results.NotFound();

            image.IsDeleted = true;
            image.UpdatedAt = SystemTime.Now;
            image.UpdatedByUserId = auth.ActorId!.Value;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }
}
