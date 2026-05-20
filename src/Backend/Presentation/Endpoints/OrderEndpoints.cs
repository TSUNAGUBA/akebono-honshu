using Akebono.Application.Auth;
using Akebono.Application.Common;
using Akebono.Application.Orders;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 発注書関連 REST endpoint (Phase 5 §2.5、O-01〜O-07)。
/// 編集系 (POST/PATCH/cancel/export) は purchase_order_create_permission >= 1 必須
/// (AuthEndpoints.CheckOrderEditAsync)。
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/v1/orders");

        // 一覧 (O-03)
        orders.MapGet("/", async (HttpContext http, ITokenService tokens, PurchaseOrderService svc,
                                    bool? includeCancelled, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, tokens, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            var items = await svc.ListAsync(actorId, includeCancelled ?? false, ct);
            return Results.Ok(new { data = items });
        });

        // 詳細 (O-04 編集画面ベース)
        orders.MapGet("/{id:long}", async (HttpContext http, ITokenService tokens, PurchaseOrderService svc,
                                            long id, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, tokens, out var actorId))
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            var detail = await svc.GetDetailAsync(id, actorId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        // 新規作成 (O-01)
        orders.MapPost("/", async (HttpContext http, ITokenService tokens, IAkebonoDbContext db,
                                    PurchaseOrderService svc, CreateOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, tokens, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            try
            {
                var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
                return Results.Created($"/api/v1/orders/{created.Id}", new { id = created.Id, mgmtNo = created.MgmtNo });
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 422, title: "Validation error", detail: ex.Message);
            }
        });

        // 編集 (O-04、F-16 EditReason 必須)
        orders.MapPatch("/{id:long}", async (HttpContext http, ITokenService tokens, IAkebonoDbContext db,
                                              PurchaseOrderService svc, long id, UpdateOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, tokens, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            try
            {
                var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
                return updated is null ? Results.NotFound() : Results.Ok(new { id = updated.Id, mgmtNo = updated.MgmtNo });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 409, title: "Conflict", detail: ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 422, title: "Validation error", detail: ex.Message);
            }
        });

        // 中止 (O-05)
        orders.MapPost("/{id:long}/cancel", async (HttpContext http, ITokenService tokens, IAkebonoDbContext db,
                                                     PurchaseOrderService svc, long id, CancelOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, tokens, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.CancelAsync(id, req, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // Excel 出力 (O-06、MVP クリティカルパス)
        orders.MapGet("/{id:long}/export.xlsx", async (HttpContext http, ITokenService tokens, IAkebonoDbContext db,
                                                        IPurchaseOrderExcelService excel, long id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, tokens, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            try
            {
                var (fileName, content) = await excel.ExportAsync(id, auth.ActorId!.Value, ct);
                return Results.File(content,
                    contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileDownloadName: fileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 404, title: "Not Found", detail: ex.Message);
            }
        });

        // 連絡文章テンプレ提案 (O-07)
        orders.MapGet("/communication-suggestions", async (HttpContext http, ITokenService tokens,
                                                            PurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, tokens, out _))
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            var items = await svc.GetCommunicationSuggestionsAsync(ct);
            return Results.Ok(new { data = items });
        });

        return app;
    }
}
