using Akebono.Application.Common;
using Akebono.Application.Masters;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 17 マスタの REST エンドポイント定義 (M-01 / M-02 共通テンプレート + 個別拡張)。
/// Phase 5 api-design.md §2.3 + Iteration 1.F の C-02 権限制御:
///   - GET (list/single) は認証必須のみ
///   - POST/PATCH/DELETE/Restore は product_ledger_permission >= 1 必須
///     (AuthEndpoints.CheckMasterEditAsync で 401/403 を返却)
/// </summary>
public static class MasterEndpoints
{
    public static IEndpointRouteBuilder MapMasterEndpoints(this IEndpointRouteBuilder app)
    {
        MapSimple<Brand>(app, "brands");
        MapSimple<Function>(app, "functions");
        MapSimple<Country>(app, "countries");
        MapSimple<Currency>(app, "currencies");
        MapSimple<Department>(app, "departments");
        MapSimple<MaterialClassification>(app, "material-classifications");
        MapSimple<Warehouse>(app, "warehouses");

        MapSizes(app);
        MapSuppliers(app);
        MapExchangeRates(app);
        MapProductTypes(app);
        MapProductSeasons(app);
        MapProductGroups(app);
        MapColors(app);
        MapMaterials(app);
        MapDeliveryDestinations(app);
        MapDocumentTemplatePurchases(app);
        MapDocumentTemplateConfirmations(app);
        MapDocumentTextPurchases(app);

        return app;
    }

    private record SimpleWriteRequest(string Code, string Name);
    private record SizeWriteRequest(string Code, string Name, string ItemConversionCode);
    private record ProductTypeWriteRequest(string Code, string Name, string ItemConversionCode, string SizeDemographicCode);
    private record ProductSeasonWriteRequest(string Code, string Name, string ItemConversionCode, string? ConversionOrder);
    private record ProductGroupWriteRequest(string Code, string Name, decimal PlanningFee);
    private record ColorWriteRequest(string Code, string Name, string ItemConversionCode);
    private record MaterialWriteRequest(string Code, string Name, Guid MaterialClassificationId);
    private record DeliveryDestinationWriteRequest(
        string Code, string Name, string? CustomerName,
        string? Remark1, string? Remark2, string? Remark3);
    private record DocumentBodyWriteRequest(string Code, string Name, string Body);
    private record DocumentBodyWithFlagWriteRequest(string Code, string Name, string Body, bool StandardPrintFlag);

    // ── 共通ヘルパー: GET は認証のみ、DELETE/Restore は編集権限要求 ─────────
    private static RouteGroupBuilder MapBase<T>(IEndpointRouteBuilder app, string path)
        where T : MasterEntityBase, new()
    {
        var group = app.MapGroup($"/api/maker/v1/masters/{path}");

        group.MapGet("/", async (HttpContext http, MasterService<T> svc,
                                  bool? includeDeleted, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var items = await svc.ListAsync(actorId, includeDeleted ?? false, ct);
            return ApiEnvelope.Ok(http, items);
        });

        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, MasterService<T> svc, CancellationToken ct) =>
        {
            var entity = await svc.GetAsync(id, ct);
            return entity is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, entity);
        });

        group.MapDelete("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<T> svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        group.MapPost("/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                     MasterService<T> svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.RestoreAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        return group;
    }

    // ── 拡張なしマスタ用 (POST/PATCH も共通) ─────────────────────────────
    private static void MapSimple<T>(IEndpointRouteBuilder app, string path)
        where T : MasterEntityBase, new()
    {
        var group = MapBase<T>(app, path);

        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<T> svc, SimpleWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new T { Code = req.Code, Name = req.Name };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/{path}/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<T> svc, Guid id, SimpleWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code;
                e.Name = req.Name;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapSizes(IEndpointRouteBuilder app)
    {
        var group = MapBase<Size>(app, "sizes");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<Size> svc, SizeWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new Size { Code = req.Code, Name = req.Name, ItemConversionCode = req.ItemConversionCode };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/sizes/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<Size> svc, Guid id, SizeWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code;
                e.Name = req.Name;
                e.ItemConversionCode = req.ItemConversionCode;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapSuppliers(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/maker/v1/masters/suppliers");

        group.MapGet("/", async (HttpContext http, SupplierService svc,
                                  bool? includeDeleted, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var items = await svc.ListAsync(actorId, includeDeleted ?? false, ct);
            return ApiEnvelope.Ok(http, items);
        });

        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, SupplierService svc, CancellationToken ct) =>
        {
            var entity = await svc.GetAsync(id, ct);
            return entity is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, entity);
        });

        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    SupplierService svc, SupplierWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/suppliers/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              SupplierService svc, Guid id, SupplierWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        group.MapDelete("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              SupplierService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        group.MapPost("/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                     SupplierService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.RestoreAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });
    }

    // 為替マスタ (§2f、bespoke master)。年月 (YYYY-MM) × 通貨ごとの対円レートを CRUD する。
    // 同一 (年月, 通貨) の重複 (旧 EXR-004) や入力検証は service 層が DomainException を投げ、
    // ApiExceptionMiddleware が 409/422 のエラー封筒へ変換する。
    private static void MapExchangeRates(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/maker/v1/masters/exchange-rates");

        group.MapGet("/", async (HttpContext http, ExchangeRateService svc,
                                  bool? includeDeleted, CancellationToken ct) =>
        {
            if (!AuthEndpoints.TryGetUserId(http, out var actorId))
                return AuthEndpoints.UnauthorizedError(http);
            var items = await svc.ListAsync(actorId, includeDeleted ?? false, ct);
            return ApiEnvelope.Ok(http, items);
        });

        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, ExchangeRateService svc, CancellationToken ct) =>
        {
            var entity = await svc.GetAsync(id, ct);
            return entity is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, entity);
        });

        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    ExchangeRateService svc, ExchangeRateWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/exchange-rates/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              ExchangeRateService svc, Guid id, ExchangeRateWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        group.MapDelete("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              ExchangeRateService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        group.MapPost("/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                     ExchangeRateService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            // 復元先に同一 (年月, 通貨) の有効行がある場合は service が DomainException (409) を投げる。
            var ok = await svc.RestoreAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });
    }

    private static void MapProductTypes(IEndpointRouteBuilder app)
    {
        var group = MapBase<ProductType>(app, "product-types");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<ProductType> svc, ProductTypeWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new ProductType
            {
                Code = req.Code, Name = req.Name,
                ItemConversionCode = req.ItemConversionCode,
                SizeDemographicCode = req.SizeDemographicCode,
            };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/product-types/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<ProductType> svc, Guid id, ProductTypeWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name;
                e.ItemConversionCode = req.ItemConversionCode;
                e.SizeDemographicCode = req.SizeDemographicCode;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapProductSeasons(IEndpointRouteBuilder app)
    {
        var group = MapBase<ProductSeason>(app, "product-seasons");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<ProductSeason> svc, ProductSeasonWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new ProductSeason
            {
                Code = req.Code, Name = req.Name,
                ItemConversionCode = req.ItemConversionCode,
                ConversionOrder = req.ConversionOrder,
            };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/product-seasons/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<ProductSeason> svc, Guid id, ProductSeasonWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name;
                e.ItemConversionCode = req.ItemConversionCode;
                e.ConversionOrder = req.ConversionOrder;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapProductGroups(IEndpointRouteBuilder app)
    {
        var group = MapBase<ProductGroup>(app, "product-groups");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<ProductGroup> svc, ProductGroupWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new ProductGroup { Code = req.Code, Name = req.Name, PlanningFee = req.PlanningFee };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/product-groups/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<ProductGroup> svc, Guid id, ProductGroupWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name; e.PlanningFee = req.PlanningFee;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapColors(IEndpointRouteBuilder app)
    {
        var group = MapBase<Color>(app, "colors");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<Color> svc, ColorWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new Color { Code = req.Code, Name = req.Name, ItemConversionCode = req.ItemConversionCode };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/colors/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<Color> svc, Guid id, ColorWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name; e.ItemConversionCode = req.ItemConversionCode;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapMaterials(IEndpointRouteBuilder app)
    {
        var group = MapBase<Material>(app, "materials");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<Material> svc, MaterialWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new Material { Code = req.Code, Name = req.Name, MaterialClassificationId = req.MaterialClassificationId };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/materials/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<Material> svc, Guid id, MaterialWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name; e.MaterialClassificationId = req.MaterialClassificationId;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapDeliveryDestinations(IEndpointRouteBuilder app)
    {
        var group = MapBase<DeliveryDestination>(app, "delivery-destinations");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<DeliveryDestination> svc, DeliveryDestinationWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new DeliveryDestination
            {
                Code = req.Code, Name = req.Name,
                CustomerName = req.CustomerName,
                Remark1 = req.Remark1, Remark2 = req.Remark2, Remark3 = req.Remark3,
            };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/delivery-destinations/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<DeliveryDestination> svc, Guid id, DeliveryDestinationWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name;
                e.CustomerName = req.CustomerName;
                e.Remark1 = req.Remark1; e.Remark2 = req.Remark2; e.Remark3 = req.Remark3;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapDocumentTemplatePurchases(IEndpointRouteBuilder app)
    {
        var group = MapBase<DocumentTemplatePurchase>(app, "document-template-purchases");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<DocumentTemplatePurchase> svc, DocumentBodyWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new DocumentTemplatePurchase { Code = req.Code, Name = req.Name, Body = req.Body };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/document-template-purchases/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<DocumentTemplatePurchase> svc, Guid id, DocumentBodyWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name; e.Body = req.Body;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapDocumentTemplateConfirmations(IEndpointRouteBuilder app)
    {
        var group = MapBase<DocumentTemplateConfirmation>(app, "document-template-confirmations");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<DocumentTemplateConfirmation> svc, DocumentBodyWithFlagWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new DocumentTemplateConfirmation
            {
                Code = req.Code, Name = req.Name, Body = req.Body,
                StandardPrintFlag = req.StandardPrintFlag,
            };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/document-template-confirmations/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<DocumentTemplateConfirmation> svc, Guid id, DocumentBodyWithFlagWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name; e.Body = req.Body;
                e.StandardPrintFlag = req.StandardPrintFlag;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }

    private static void MapDocumentTextPurchases(IEndpointRouteBuilder app)
    {
        var group = MapBase<DocumentTextPurchase>(app, "document-text-purchases");
        group.MapPost("/", async (HttpContext http, IAkebonoDbContext db,
                                    MasterService<DocumentTextPurchase> svc, DocumentBodyWithFlagWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var entity = new DocumentTextPurchase
            {
                Code = req.Code, Name = req.Name, Body = req.Body,
                StandardPrintFlag = req.StandardPrintFlag,
            };
            var created = await svc.CreateAsync(entity, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/masters/document-text-purchases/{created.Id}", created);
        });
        group.MapPatch("/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                              MasterService<DocumentTextPurchase> svc, Guid id, DocumentBodyWithFlagWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckMasterEditAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, e =>
            {
                e.Code = req.Code; e.Name = req.Name; e.Body = req.Body;
                e.StandardPrintFlag = req.StandardPrintFlag;
            }, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });
    }
}
