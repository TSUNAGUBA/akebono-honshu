using Akebono.Application.Common;
using Akebono.Application.Orders;
using Akebono.Application.Products;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 発注書関連 REST endpoint (Phase 5 §2.5、O-01〜O-07)。
/// 編集系 (POST/PATCH/cancel/export) は purchase_order_create_permission >= 1 必須
/// (AuthEndpoints.CheckOrderEditAsync)。
/// 業務エラー (検証 422 / 状態遷移・重複 409) は service 層の DomainException を
/// ApiExceptionMiddleware がエラー封筒へ変換する。
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/maker/v1/orders");

        // 一覧 (O-03)。キーセットページング (AKB-DOC-12 §7.1): ?limit=50&cursor=<opaque>、
        // 不正は 400 AKB-SYS-011 (PageCursor.Read が DomainException を投げ中央ハンドラが封筒化)。
        orders.MapGet("/", async (HttpContext http, PurchaseOrderService svc,
                                    bool? includeCancelled, int? limit, string? cursor, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var page = PageCursor.Read(limit, cursor);
            var result = await svc.ListAsync(actorId, includeCancelled ?? false, page, ct);
            return ApiEnvelope.OkPaged(http, result, page.Limit);
        });

        // 詳細 (O-04 編集画面ベース)
        orders.MapGet("/{id:guid}", async (HttpContext http, PurchaseOrderService svc,
                                            Guid id, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var detail = await svc.GetDetailAsync(id, actorId, ct);
            return detail is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, detail);
        });

        // 新規作成 (O-01)
        orders.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    PurchaseOrderService svc, CreateOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            // Idempotency-Key (AKB-DOC-12 §8): 作成系 POST は必須。欠如は 400 AKB-SYS-004。
            var (idemKey, idemHash) = IdempotencyKeyReader.Read(http, req);
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, idemKey, idemHash, ct: ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/orders/{created.Id}",
                new { id = created.Id, mgmtNo = created.MgmtNo });
        });

        // 編集 (O-04、F-16 EditReason 必須)
        orders.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              PurchaseOrderService svc, Guid id, UpdateOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null
                ? AuthEndpoints.NotFoundError(http)
                : ApiEnvelope.Ok(http, new { id = updated.Id, mgmtNo = updated.MgmtNo });
        });

        // 中止 (O-05)。削除済 (終端状態) の中止は service 層の DomainException (409、§3b)。
        orders.MapPost("/{id:guid}/cancel", async (HttpContext http, IAkebonoDbContext db,
                                                     PurchaseOrderService svc, Guid id, CancelOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.CancelAsync(id, req, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // 発注済にする (§3b)。未発注 → 発注済 (ordered_at を SET)。ダウンロードとは独立したユーザー操作。
        // 削除済/中止済 (終端状態) は service 層の DomainException (409)。
        orders.MapPost("/{id:guid}/mark-ordered", async (HttpContext http, IAkebonoDbContext db,
                                                         PurchaseOrderService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.MarkOrderedAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // 未発注に戻す (§3b)。発注済 → 未発注 (ordered_at を NULL)。
        // 削除済/中止済 (終端状態) は service 層の DomainException (409)。
        orders.MapPost("/{id:guid}/unmark-ordered", async (HttpContext http, IAkebonoDbContext db,
                                                           PurchaseOrderService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.UnmarkOrderedAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // 発注削除 (§3b)。論理削除 (deleted_at SET)。物理削除はしない。
        orders.MapPost("/{id:guid}/delete", async (HttpContext http, IAkebonoDbContext db,
                                                    PurchaseOrderService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // 発注状態の一括変更 (§3c)。チェックした発注を指定状態へ一括変更する。
        // 認可は編集と同じ (purchase_order:write)。終端状態で変更できない発注はスキップし {updated, skipped} を返す。
        // target 不正 / orderIds 空は service 層の DomainException を中央ハンドラが変換する。
        orders.MapPost("/bulk-status", async (HttpContext http, IAkebonoDbContext db,
                                              PurchaseOrderService svc, BulkStatusRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await svc.BulkSetStatusAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Ok(http, result);
        });

        // Excel 出力 (O-06)。旧システムの「発注書出力」画面と同様に、出力前に「発注日」「出荷指示番号」
        // 「発注番号」を手入力するフォームを経由して出力する (即時出力は廃止。下の POST /{id}/export)。
        // 帳票出力フォーム経由の出力 (旧システム「発注書出力」画面相当)。
        // 「発注日」「出荷指示番号」「発注番号」を手入力し、出力帳票 (発注書 / 管理表 / 発注書+管理表) を
        // 選んで出力する。入力 3 項目は発注に保存してから帳票を生成する (再出力時に初期表示)。
        //   format=order      → 発注書 .xlsx (単一)
        //   format=management → 管理表 .xlsx (単一)
        //   format=both       → 発注書+管理表 を ZIP (OrderBulkExportService を [id] で再利用)
        // 発注番号重複 / 削除済 (終端状態) は service 層の DomainException (409)。
        orders.MapPost("/{id:guid}/export", async (HttpContext http, IAkebonoDbContext db,
                                                    PurchaseOrderService svc,
                                                    IPurchaseOrderExcelService excel,
                                                    IOrderManagementTableExcelService mgmt,
                                                    IOrderBulkExportService bulk,
                                                    Guid id, ExportOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var format = (req.Format ?? "").Trim().ToLowerInvariant();
            if (format is not ("order" or "management" or "both"))
                return ApiEnvelope.Error(http, 400, AkbErrorCodes.SysValidation,
                    $"format は order / management / both のいずれかです: '{req.Format}'");

            // 手入力 3 項目 (発注日 / 出荷指示番号 / 発注番号) を発注に保存してから帳票を生成する。
            var applied = await svc.ApplyExportFormFieldsAsync(
                id, req.OrderDate, req.ShippingInstructionNo, req.OrderNo, auth.ActorId!.Value, ct);
            if (!applied) return AuthEndpoints.NotFoundError(http);

            if (format == "both")
            {
                var result = await bulk.ExportAsync(
                    new BulkExportRequest(new List<Guid> { id }, "both"), auth.ActorId!.Value, ct);
                return Results.File(result.Content, contentType: result.ContentType, fileDownloadName: result.FileName);
            }

            var (fileName, content) = format == "order"
                ? await excel.ExportAsync(id, auth.ActorId!.Value, ct)
                : await mgmt.ExportAsync(new List<Guid> { id }, auth.ActorId!.Value, ct);
            return Results.File(content,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: fileName);
        });

        // 一括ダウンロード (#3b)。発注一覧でチェックした発注を
        // 発注書 (ZIP) / 管理表 (xlsx) / 発注書+管理表 (ZIP) で束ねて返す。
        // 認可は単一 Excel 出力と同じ (purchase_order_create_permission >= 1)。
        // orderIds 空 / format 不正 / 有効発注 0 件は service 層の DomainException を中央ハンドラが変換する。
        orders.MapPost("/bulk-export", async (HttpContext http, IAkebonoDbContext db,
                                              IOrderBulkExportService bulk, BulkExportRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await bulk.ExportAsync(req, auth.ActorId!.Value, ct);
            return Results.File(result.Content, contentType: result.ContentType, fileDownloadName: result.FileName);
        });

        // 連絡文章テンプレ提案 (O-07)
        orders.MapGet("/communication-suggestions", async (HttpContext http,
                                                            PurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out _))
                return AuthEndpoints.UnauthorizedError(http);
            var items = await svc.GetCommunicationSuggestionsAsync(ct);
            return ApiEnvelope.Ok(http, items);
        });

        // 単価サジェスト (PR2、size-aware)。発注明細の unit_price_snapshot 入力補助。
        // SKU (productId) の size に対応する現単価を「(family, supplier, SKUのsize) → 無ければ
        // (…, NULL-size 既定)」のフォールバックで解決して返す。現単価が無ければ Found=false。
        // 認可: 発注編集権限と同じ (CheckOrderEditAsync) — 単価は機密度 中-高 (NFR §6.2)。
        // 注: 本 endpoint は読取専用の入力補助で、snapshot をサーバ側で上書きしない (下位互換)。
        orders.MapGet("/price-suggestion", async (HttpContext http, IAkebonoDbContext db,
                                                   ProductSupplierPriceService priceSvc, IAuditLogger audit,
                                                   Guid productId, Guid supplierId, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            // SKU から family / size を解決。発注先 supplier は発注ヘッダ由来 (クエリ引数)。
            var product = await db.Products
                .Where(p => p.Id == productId)
                .Select(p => new { p.ProductFamilyId, p.SizeId })
                .FirstOrDefaultAsync(ct);
            if (product is null)
                return AuthEndpoints.NotFoundError(http);

            var price = await priceSvc.ResolveCurrentPriceAsync(
                product.ProductFamilyId, supplierId, product.SizeId, ct);

            // 単価読出パスの監査証跡 (reviewer I-1)。他の単価読出 (ProductSupplierPrice.List /
            // ProductFamily.View) と整合させ、誰がどの family/supplier/size の現単価を参照したかを記録する。
            // 金額自体はマスク (price=***)。単価は機密度 中-高 (NFR §6.2)、原則6 アクセス制御整合。
            await audit.LogAsync(auth.ActorId!.Value, "ProductSupplierPrice.Suggest",
                entityType: "ProductSupplierPrice",
                note: $"family={product.ProductFamilyId}, supplier={supplierId}, sku_size={product.SizeId}, found={price is not null}, price=***",
                cancellationToken: ct);

            var suggestion = price is null
                ? new SupplierPriceSuggestion(false, null, null, null, null, false)
                : new SupplierPriceSuggestion(
                    true, price.UnitPrice, price.CurrencyCode, price.ExchangeRate,
                    price.SizeId, price.SizeId is not null);
            return ApiEnvelope.Ok(http, suggestion);
        });

        return app;
    }
}
