using Akebono.Application.Common;
using Akebono.Application.Products;
using Akebono.Application.Production;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 生産管理拡張 REST endpoint (Phase 5 生産拡張、B-01/02・PI-01〜04・MO-01〜04・PS-01)。
/// 認可: BOM は品番台帳管理権限 (CheckMasterEditAsync)、生産指示/素材発注は発注書作成権限
/// (CheckOrderEditAsync)。読取系は認証のみ。素材単価は既存仕入単価と同方式 (監査マスク)。
/// 業務エラー (検証 422 / 状態遷移 409) は service 層の DomainException を
/// ApiExceptionMiddleware がエラー封筒へ変換する。
/// </summary>
public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder app)
    {
        MapBom(app);
        MapProductionInstructions(app);
        MapMaterialOrders(app);
        MapProductionStatus(app);
        return app;
    }

    // ── BOM (B-01/B-02) ─────────────────────────────
    private static void MapBom(IEndpointRouteBuilder app)
    {
        var bom = app.MapGroup("/api/maker/v1/products/families/{familyId:guid}/materials");

        bom.MapGet("/", async (HttpContext http, ProductMaterialService svc, Guid familyId, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out _))
                return AuthEndpoints.UnauthorizedError(http);
            return ApiEnvelope.Ok(http, await svc.GetAsync(familyId, ct));
        });

        bom.MapPut("/", async (HttpContext http, IAkebonoDbContext db, ProductMaterialService svc,
                                Guid familyId, ReplaceBomRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            await svc.ReplaceAsync(familyId, req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Ok(http, await svc.GetAsync(familyId, ct));
        });

        // 所要量展開プレビュー (B-02、金額なし)
        app.MapGet("/api/maker/v1/products/families/{familyId:guid}/material-requirements",
            async (HttpContext http, ProductMaterialService svc, Guid familyId, int quantity, CancellationToken ct) =>
            {
                if (!AuthEndpoints.TryGetUserId(http, out _))
                    return AuthEndpoints.UnauthorizedError(http);
                return ApiEnvelope.Ok(http, await svc.GetRequirementsAsync(familyId, quantity, ct));
            });
    }

    // ── 生産指示 (PI-01〜04) ─────────────────────────
    private static void MapProductionInstructions(IEndpointRouteBuilder app)
    {
        var pi = app.MapGroup("/api/maker/v1/production-instructions");

        // 一覧 (PI-02)。キーセットページング (AKB-DOC-12 §7.1): ?limit=50&cursor=<opaque>、
        // 不正は 400 AKB-SYS-011 (PageCursor.Read が DomainException を投げ中央ハンドラが封筒化)。
        pi.MapGet("/", async (HttpContext http, ProductionInstructionService svc,
                               bool? includeCancelled, int? limit, string? cursor, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var page = PageCursor.Read(limit, cursor);
            var result = await svc.ListAsync(actorId, includeCancelled ?? false, page, ct);
            return ApiEnvelope.OkPaged(http, result, page.Limit);
        });

        pi.MapGet("/{id:guid}", async (HttpContext http, ProductionInstructionService svc, Guid id, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var detail = await svc.GetDetailAsync(id, actorId, ct);
            return detail is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, detail);
        });

        pi.MapPost("/", async (HttpContext http, IAkebonoDbContext db, ProductionInstructionService svc,
                                CreatePiRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            // Idempotency-Key (AKB-DOC-12 §8): 作成系 POST は必須。欠如は 400 AKB-SYS-004。
            var (idemKey, idemHash) = IdempotencyKeyReader.Read(http, req);
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, idemKey, idemHash, ct: ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/production-instructions/{created.Id}",
                new { id = created.Id, instructionNo = created.InstructionNo });
        });

        pi.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db, ProductionInstructionService svc,
                                          Guid id, UpdatePiRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null
                ? AuthEndpoints.NotFoundError(http)
                : ApiEnvelope.Ok(http, new { id = updated.Id, instructionNo = updated.InstructionNo });
        });

        pi.MapPost("/{id:guid}/issue", async (HttpContext http, IAkebonoDbContext db, ProductionInstructionService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return await svc.IssueAsync(id, auth.ActorId!.Value, ct)
                ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        pi.MapPost("/{id:guid}/complete", async (HttpContext http, IAkebonoDbContext db, ProductionInstructionService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return await svc.CompleteAsync(id, auth.ActorId!.Value, ct)
                ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        pi.MapPost("/{id:guid}/cancel", async (HttpContext http, IAkebonoDbContext db, ProductionInstructionService svc,
                                                Guid id, CancelPiRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return await svc.CancelAsync(id, req, auth.ActorId!.Value, ct)
                ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        pi.MapGet("/{id:guid}/export.xlsx", async (HttpContext http, IAkebonoDbContext db,
                                                    IProductionInstructionExcelService excel, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            // id 不在は Excel service が DomainException (404 AKB-TENANT-010 存在秘匿) を投げ、
            // 中央 ApiExceptionMiddleware が封筒化する。
            var (fileName, content) = await excel.ExportAsync(id, auth.ActorId!.Value, ct);
            return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        });
    }

    // ── 素材発注 (MO-01〜04) ─────────────────────────
    private static void MapMaterialOrders(IEndpointRouteBuilder app)
    {
        var mo = app.MapGroup("/api/maker/v1/material-orders");

        // BOM 展開→仕入先別ドラフト提案 (副作用なし、金額なし)
        mo.MapPost("/prepare", async (HttpContext http, MaterialOrderService svc, PrepareMaterialOrderRequest req, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out _))
                return AuthEndpoints.UnauthorizedError(http);
            return ApiEnvelope.Ok(http, await svc.PrepareAsync(req, ct));
        });

        // 一覧 (MO-02)。キーセットページング (AKB-DOC-12 §7.1): ?limit=50&cursor=<opaque>、
        // 不正は 400 AKB-SYS-011 (PageCursor.Read が DomainException を投げ中央ハンドラが封筒化)。
        mo.MapGet("/", async (HttpContext http, MaterialOrderService svc,
                               bool? includeCancelled, int? limit, string? cursor, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var page = PageCursor.Read(limit, cursor);
            var result = await svc.ListAsync(actorId, includeCancelled ?? false, page, ct);
            return ApiEnvelope.OkPaged(http, result, page.Limit);
        });

        mo.MapGet("/{id:guid}", async (HttpContext http, MaterialOrderService svc, Guid id, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var detail = await svc.GetDetailAsync(id, actorId, ct);
            return detail is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, detail);
        });

        mo.MapPost("/", async (HttpContext http, IAkebonoDbContext db, MaterialOrderService svc,
                                CreateMaterialOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            // Idempotency-Key (AKB-DOC-12 §8): 作成系 POST は必須。欠如は 400 AKB-SYS-004。
            var (idemKey, idemHash) = IdempotencyKeyReader.Read(http, req);
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, idemKey, idemHash, ct: ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/material-orders/{created.Id}",
                new { id = created.Id, orderNo = created.OrderNo });
        });

        mo.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db, MaterialOrderService svc,
                                          Guid id, UpdateMaterialOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null
                ? AuthEndpoints.NotFoundError(http)
                : ApiEnvelope.Ok(http, new { id = updated.Id, orderNo = updated.OrderNo });
        });

        mo.MapPost("/{id:guid}/order", async (HttpContext http, IAkebonoDbContext db, MaterialOrderService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return await svc.OrderAsync(id, auth.ActorId!.Value, ct)
                ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        mo.MapPost("/{id:guid}/cancel", async (HttpContext http, IAkebonoDbContext db, MaterialOrderService svc,
                                                Guid id, CancelMaterialOrderRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return await svc.CancelAsync(id, req, auth.ActorId!.Value, ct)
                ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        mo.MapGet("/{id:guid}/export.xlsx", async (HttpContext http, IAkebonoDbContext db,
                                                    IMaterialOrderExcelService excel, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckOrderEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            // id 不在は Excel service が DomainException (404 AKB-TENANT-010 存在秘匿) を投げ、
            // 中央 ApiExceptionMiddleware が封筒化する。
            var (fileName, content) = await excel.ExportAsync(id, auth.ActorId!.Value, ct);
            return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        });
    }

    // ── 未/済 一覧 (PS-01) ───────────────────────────
    private static void MapProductionStatus(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/maker/v1/production/status", async (HttpContext http, ProductionStatusQuery svc,
            string? materialOrderState, string? productionInstructionState, bool? bomRegistered, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out _))
                return AuthEndpoints.UnauthorizedError(http);
            var rows = await svc.ListAsync(materialOrderState, productionInstructionState, bomRegistered, ct);
            return ApiEnvelope.Ok(http, rows);
        });
    }
}
