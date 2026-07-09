using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Akebono.Application.Products;

/// <summary>
/// 商品企画 (product_families) 関連の Application サービス。
/// バルク登録 (P-01〜P-03) は 1 トランザクション、F-06 ロールバック対応。
/// </summary>
public class ProductFamilyService(
    IAkebonoDbContext db,
    IAuditLogger audit,
    IImageStorageService imageStorage,
    ITenantContext tenantContext,
    ILogger<ProductFamilyService> logger)
{
    /// <summary>
    /// URL 取得失敗を非ブロッキング化 (Iter 4 段階 C-1 reviewer 指摘 M6 / 原則 4)。
    /// S3 throttling / IAM 一時失効等で 1 枚の URL 発行が失敗しても一覧/詳細全体は 200 で返す。
    /// </summary>
    private async Task<string?> SafeGetUrlAsync(string key, CancellationToken ct)
    {
        try { return await imageStorage.GetUrlAsync(key, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "画像 URL 取得失敗 (key={Key}) - placeholder で fallback", key);
            return null;
        }
    }

    /// <summary>
    /// バルク登録 (POST /api/maker/v1/products/families/complete)。
    /// family + products (色×サイズ全組合せ) + supplier_prices を 1 トランザクション。
    /// </summary>
    public async Task<CompleteFamilyResponse> CreateCompleteAsync(
        CompleteFamilyRequest req,
        long actorUserId,
        CancellationToken ct = default)
    {
        // バリデーション (最小限)
        if (req.Expansion.ColorIds.Count == 0 || req.Expansion.SizeIds.Count == 0)
            throw DomainException.Validation("色とサイズを少なくとも 1 件ずつ指定してください");
        // アソート/セット明細 (PR3)。null/空は許容 (通常商品)、行があれば各行を検証。
        ValidateSetComponents(req.SetComponents);

        // FK 参照先を一括取得 (Sku 組立に必要)
        var productType = await db.ProductTypes.FirstOrDefaultAsync(x => x.Id == req.Family.ProductTypeId, ct)
            ?? throw DomainException.Validation($"product_type_id={req.Family.ProductTypeId} 不在");
        var season = await db.ProductSeasons.FirstOrDefaultAsync(x => x.Id == req.Family.ProductSeasonId, ct)
            ?? throw DomainException.Validation($"product_season_id={req.Family.ProductSeasonId} 不在");
        var factory = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == req.Family.FactorySupplierId, ct)
            ?? throw DomainException.Validation($"factory_supplier_id={req.Family.FactorySupplierId} 不在");

        var colors = await db.Colors.Where(c => req.Expansion.ColorIds.Contains(c.Id)).ToListAsync(ct);
        if (colors.Count != req.Expansion.ColorIds.Count)
            throw DomainException.Validation("指定された color_id の一部が見つかりません");
        var sizes = await db.Sizes.Where(s => req.Expansion.SizeIds.Contains(s.Id)).ToListAsync(ct);
        if (sizes.Count != req.Expansion.SizeIds.Count)
            throw DomainException.Validation("指定された size_id の一部が見つかりません");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // sequence_no 自動採番 (同一 planned_year + type + season + factory 内の最大 + 1)。
            // トランザクション内で tenant_id を含む advisory lock を取得して直列化する
            // (AKB-DOC-05 採番フロー。同時作成での連番衝突を防ぎ、UNIQUE 制約が最終防壁)。
            var seqLockKey = $"{tenantContext.RequireTenantId()}:FAM-{req.Family.PlannedYearCode}-" +
                             $"{req.Family.ProductTypeId}-{req.Family.ProductSeasonId}-{req.Family.FactorySupplierId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({seqLockKey})::bigint)", ct);
            var maxSeq = await db.ProductFamilies
                .Where(pf => pf.PlannedYearCode == req.Family.PlannedYearCode
                          && pf.ProductTypeId == req.Family.ProductTypeId
                          && pf.ProductSeasonId == req.Family.ProductSeasonId
                          && pf.FactorySupplierId == req.Family.FactorySupplierId)
                .Select(pf => pf.SequenceNo)
                .ToListAsync(ct);
            var nextSeq = (maxSeq.Count == 0 ? 1 : maxSeq.Select(s => int.TryParse(s, out var n) ? n : 0).Max() + 1)
                .ToString("D3");

            var now = SystemTime.UtcNow;
            var family = new ProductFamily
            {
                PlannedYearCode = req.Family.PlannedYearCode,
                ProductTypeId = req.Family.ProductTypeId,
                ProductSeasonId = req.Family.ProductSeasonId,
                SequenceNo = nextSeq,
                FactorySupplierId = req.Family.FactorySupplierId,
                BrandId = req.Family.BrandId,
                FunctionId = req.Family.FunctionId,
                ProductGroupId = req.Family.ProductGroupId,
                UpperMaterialId = req.Family.UpperMaterialId,
                InsoleMaterialId = req.Family.InsoleMaterialId,
                OutsoleMaterialId = req.Family.OutsoleMaterialId,
                ProductName1 = req.Family.ProductName1,
                ProductName2 = req.Family.ProductName2,
                // 旧 品番台帳 項目 (Phase A、任意)
                ProductYear = req.Family.ProductYear,
                ManagementSeasonId = req.Family.ManagementSeasonId,
                PlannerUserId = req.Family.PlannerUserId,
                ProvisionalNumber = req.Family.ProvisionalNumber,
                SampleApprovalDate = req.Family.SampleApprovalDate,
                RetailPrice = req.Family.RetailPrice,
                DeliveryPrice = req.Family.DeliveryPrice,
                PlanningCost = req.Family.PlanningCost,
                BrandCost = req.Family.BrandCost,
                RoyaltyTarget = req.Family.RoyaltyTarget,
                RoyaltyRate = req.Family.RoyaltyRate,
                // 旧 品番台帳 項目 追補 (PR1、任意)。備考 / 備考（色）。
                Remark = req.Family.Remark,
                ColorRemark = req.Family.ColorRemark,
                Status = 1, // Active
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            };
            db.ProductFamilies.Add(family);
            await db.SaveChangesAsync(ct);

            // SKU 全組合せ生成
            var products = new List<Product>();
            foreach (var color in colors)
            foreach (var size in sizes)
            {
                var sku = Sku.Build(
                    family.PlannedYearCode,
                    productType.ItemConversionCode[0],
                    season.ItemConversionCode[0],
                    family.SequenceNo,
                    factory.ItemConversionCode[0],
                    color.ItemConversionCode,
                    size.ItemConversionCode);
                products.Add(new Product
                {
                    ProductFamilyId = family.Id,
                    ColorId = color.Id,
                    SizeId = size.Id,
                    Sku = sku.Value,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            db.Products.AddRange(products);

            // 仕入単価バルク INSERT
            var prices = req.SupplierPrices.Select(p => new ProductSupplierPrice
            {
                ProductFamilyId = family.Id,
                SupplierId = p.SupplierId,
                // サイズ別仕入単価 (PR2)。NULL = 全サイズ共通の既定単価 (バルク登録の既定挙動)。
                SizeId = p.SizeId,
                UnitPrice = p.UnitPrice,
                CurrencyCode = p.CurrencyCode,
                ExchangeRate = p.ExchangeRate,
                // 旧 仕入コスト計算明細 項目 (Phase C、任意)
                EstimateUnitPrice = p.EstimateUnitPrice,
                EstimateReceivedDate = p.EstimateReceivedDate,
                EstimateCost = p.EstimateCost,
                EstimateMarginRate = p.EstimateMarginRate,
                PurchaseCost = p.PurchaseCost,
                PurchaseMarginRate = p.PurchaseMarginRate,
                LossCost = p.LossCost,
                DrayageCost = p.DrayageCost,
                TaxRate = p.TaxRate,
                EffectiveFrom = p.EffectiveFrom,
                EffectiveTo = null,
                DecidedAt = p.DecidedAt,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            }).ToList();
            db.ProductSupplierPrices.AddRange(prices);

            // アソート/セット明細 (PR3、旧 spec No.37/38)。null/空は「明細なし (通常商品)」として
            // INSERT をスキップ。line_no は配列順で 1 から採番 (BOM/色サイズ展開と同じ全置換パターン)。
            var setComponents = BuildSetComponentEntities(family.Id, req.SetComponents, now, actorUserId);
            db.ProductSetComponents.AddRange(setComponents);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "ProductFamily.Create",
                entityType: "ProductFamily", entityId: family.Id,
                note: $"family={family.Id}, products={products.Count}, prices={prices.Count}, prices_amount=***, set_components={setComponents.Count}",
                cancellationToken: ct);

            return new CompleteFamilyResponse(
                new FamilySummary(family.Id, family.SequenceNo, family.PlannedYearCode),
                products.Select(p => new SkuSummary(
                    p.Id, p.Sku, p.ColorId,
                    colors.First(c => c.Id == p.ColorId).Code,
                    colors.First(c => c.Id == p.ColorId).Name,
                    p.SizeId,
                    sizes.First(s => s.Id == p.SizeId).Code,
                    sizes.First(s => s.Id == p.SizeId).Name,
                    p.IsDeleted)).ToList(),
                prices.Select(p => new SupplierPriceSummary(p.Id, p.SupplierId, p.UnitPrice, p.EffectiveFrom, p.SizeId)).ToList());
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>商品企画一覧 (P-04)。N+1 対策で Include + 集計を SQL レベルで実行。</summary>
    public async Task<List<FamilyListItem>> ListAsync(long actorUserId, bool includeDeleted, CancellationToken ct = default)
    {
        var query = db.ProductFamilies
            .Include(pf => pf.Brand)
            .Include(pf => pf.ProductType)
            .Include(pf => pf.ProductSeason)
            .Include(pf => pf.FactorySupplier)
            // 企画者名 (Phase A) を一覧フィルタ/表示用に解決。未設定 (PlannerUserId == null) の family は null のまま。
            .Include(pf => pf.Planner)
            .AsQueryable();
        if (!includeDeleted) query = query.Where(pf => !pf.IsDeleted);

        var items = await query
            .OrderByDescending(pf => pf.UpdatedAt)
            .Select(pf => new
            {
                Family = pf,
                SkuCount = pf.Products.Count(p => !p.IsDeleted),
                ImageCount = pf.Images.Count(i => !i.IsDeleted),
                // 画像利用シーンの代表画像 (§2a): 本番画像 (image_category=1) があればその order_no 最小、
                // 無ければ企画画像 (image_category=0) の order_no 最小。区分降順→order_no 昇順の先頭で解決する。
                PrimaryImageS3Key = pf.Images
                    .Where(i => !i.IsDeleted)
                    .OrderByDescending(i => i.ImageCategory)
                    .ThenBy(i => i.OrderNo)
                    .Select(i => i.S3Key)
                    .FirstOrDefault(),
                // PR2: 現在有効な単価行の最小/最大。size 専用行 (size_id 非NULL) と全サイズ既定行
                // (size_id NULL) の両方を含む = 商品の実際の価格レンジを表す。サイズ別単価が無い商品では
                // 全サイズ既定のみが対象となり従来と同じ値 (下位互換)。
                MinPrice = pf.SupplierPrices.Where(p => !p.IsDeleted && p.EffectiveTo == null).Min(p => (decimal?)p.UnitPrice),
                MaxPrice = pf.SupplierPrices.Where(p => !p.IsDeleted && p.EffectiveTo == null).Max(p => (decimal?)p.UnitPrice),
                Currency = pf.SupplierPrices.Where(p => !p.IsDeleted && p.EffectiveTo == null)
                                            .Select(p => p.CurrencyCode).FirstOrDefault() ?? "JPY",
                // 旧 11 桁 SKU (例: FA2071F4010) の前 7 桁 = 旧品番 7 桁。
                // MIG-3 で factory_supplier がフォールバック解決された family でも、
                // products.legacy_id には実際の旧 SKU がそのまま入っているため、こちらを優先。
                LegacyProductSku = pf.Products
                    .Where(p => p.LegacyId != null)
                    .Select(p => p.LegacyId)
                    .FirstOrDefault(),
            }).ToListAsync(ct);

        await audit.LogAsync(actorUserId, "ProductFamily.List",
            entityType: "ProductFamily", note: $"count={items.Count}", cancellationToken: ct);

        // 代表画像 URL を IImageStorageService で生成 (Local: absolute URL / S3: Pre-signed URL)。
        // N 件分の Pre-signed URL 発行を Task.WhenAll で並列化 (reviewer 指摘 C3、S3 throttling
        // 影響を最小化)。署名生成は純計算だが、AssumeRole credential resolution の初回 RTT を吸収する。
        // 失敗時は SafeGetUrlAsync が null を返し、原則 4 (非ブロッキング) を維持する。
        var primaryUrls = await Task.WhenAll(items.Select(x =>
            x.PrimaryImageS3Key is not null ? SafeGetUrlAsync(x.PrimaryImageS3Key, ct) : Task.FromResult<string?>(null)));

        var result = new List<FamilyListItem>(items.Count);
        for (var idx = 0; idx < items.Count; idx++)
        {
            var x = items[idx];
            var (itemNumber, itemFamilyNumber, sku9Digit) = BuildItemNumbers(x.Family, x.LegacyProductSku);
            result.Add(new FamilyListItem(
                x.Family.Id,
                sku9Digit,
                itemNumber,
                itemFamilyNumber,
                x.Family.LegacyId,
                x.Family.ProductName1,
                x.Family.ProductName2,
                x.Family.Brand?.Name ?? "?",
                x.Family.ProductType?.Name ?? "?",
                x.Family.ProductSeason?.Name ?? "?",
                x.Family.FactorySupplier?.Name ?? "?",
                x.Family.Status,
                x.SkuCount,
                x.ImageCount,
                x.PrimaryImageS3Key,
                primaryUrls[idx],
                x.MinPrice, x.MaxPrice, x.Currency,
                x.Family.UpdatedAt,
                // 一覧 SPLIT フィルタ用 ID / 値 (クライアント側絞込)。表示名は上の *Name、ここはフィルタ突合用。
                x.Family.ProductTypeId,
                x.Family.ProductSeasonId,
                x.Family.FactorySupplierId,
                x.Family.BrandId,
                x.Family.ProductYear,
                x.Family.ProvisionalNumber,
                x.Family.PlannerUserId,
                x.Family.Planner?.DisplayName));
        }
        return result;
    }

    /// <summary>
    /// アソート/セット明細 (PR3) の入力検証。null/空は通常商品として許容 (検証なし)。
    /// 行があれば各行の子品番 (手入力テキスト、必須) と数量 (正の整数) を検証する。
    /// FK は張らない設計のため子品番の存在検証は行わない (旧 spec の「手入力」に忠実)。
    /// </summary>
    private static void ValidateSetComponents(List<SetComponentInput>? components)
    {
        if (components is null || components.Count == 0) return;
        foreach (var c in components)
        {
            if (string.IsNullOrWhiteSpace(c.ChildItemNumber))
                throw DomainException.Validation("アソート/セット明細の子品番は必須です");
            if (c.ChildItemNumber.Trim().Length > 32)
                throw DomainException.Validation("アソート/セット明細の子品番は 32 文字以内です");
            if (c.Quantity <= 0)
                throw DomainException.Validation("アソート/セット明細の数量は正の整数で指定してください");
        }
    }

    /// <summary>
    /// アソート/セット明細 (PR3) の入力を永続化エンティティへ変換する。line_no は配列順で 1 から採番。
    /// null/空は空リストを返す (明細なし = 通常商品)。子品番は前後空白を除去して格納。
    /// 呼び出し前に <see cref="ValidateSetComponents"/> で検証済であることを前提とする。
    /// </summary>
    private static List<ProductSetComponent> BuildSetComponentEntities(
        long familyId, List<SetComponentInput>? components, DateTime now, long actorUserId)
    {
        if (components is null || components.Count == 0) return new List<ProductSetComponent>();
        var result = new List<ProductSetComponent>(components.Count);
        short lineNo = 1;
        foreach (var c in components)
        {
            result.Add(new ProductSetComponent
            {
                ProductFamilyId = familyId,
                ChildItemNumber = c.ChildItemNumber.Trim(),
                Quantity = c.Quantity,
                LineNo = lineNo++,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            });
        }
        return result;
    }

    /// <summary>
    /// 品番 7 桁・他品番 6 桁・互換用 sku9Digit を組み立てる。
    /// - 新規企画品 (legacy_id == null):
    ///     他品番 = planned_year(1) + type(1) + season(1) + sequence_no(3)
    ///     品番   = 他品番 + factory(1)
    /// - 既存品 (legacy_id != null、MIG-3 取込済):
    ///     他品番 = legacy_id (= 旧 legacy_family_code、例: FA2071)
    ///     品番   = 旧 products.legacy_id (旧 11 桁 SKU) の前 7 桁を優先 (例: FA2071F)。
    ///              旧 SKU が取得できない場合のみ、family.LegacyId + factory.item_conversion_code に
    ///              フォールバック (MIG-3 fallback supplier の場合は工場 CD が実値とずれる)。
    /// sku9Digit は下位互換のため常に「planned_year + type + season + seq + factory」を返す。
    /// </summary>
    private static (string ItemNumber, string ItemFamilyNumber, string Sku9Digit) BuildItemNumbers(
        ProductFamily family, string? legacyProductSku)
    {
        var typeCode = family.ProductType?.ItemConversionCode ?? "?";
        var seasonCode = family.ProductSeason?.ItemConversionCode ?? "?";
        var factoryCode = family.FactorySupplier?.ItemConversionCode ?? "?";

        string itemFamilyNumber;
        string itemNumber;
        if (!string.IsNullOrEmpty(family.LegacyId))
        {
            itemFamilyNumber = family.LegacyId;
            itemNumber = (!string.IsNullOrEmpty(legacyProductSku) && legacyProductSku.Length >= 7)
                ? legacyProductSku[..7]
                : $"{family.LegacyId}{factoryCode}";
        }
        else
        {
            itemFamilyNumber = $"{family.PlannedYearCode}{typeCode}{seasonCode}{family.SequenceNo}";
            itemNumber = $"{itemFamilyNumber}{factoryCode}";
        }

        var sku9Digit = $"{family.PlannedYearCode}{typeCode}{seasonCode}{family.SequenceNo}{factoryCode}";
        return (itemNumber, itemFamilyNumber, sku9Digit);
    }

    /// <summary>商品企画詳細 (P-05)。1 リクエストで family + products + images + 現在有効単価を返却。</summary>
    public async Task<FamilyDetail?> GetDetailAsync(long familyId, long actorUserId, CancellationToken ct = default)
    {
        var family = await db.ProductFamilies
            .Include(pf => pf.ProductType)
            .Include(pf => pf.ProductSeason)
            .Include(pf => pf.FactorySupplier)
            .Include(pf => pf.Brand)
            .Include(pf => pf.Function)
            .Include(pf => pf.ProductGroup)
            .Include(pf => pf.UpperMaterial)
            .Include(pf => pf.InsoleMaterial)
            .Include(pf => pf.OutsoleMaterial)
            // 旧 品番台帳 項目の表示名解決 (Phase A): 管理季節名・企画者名
            .Include(pf => pf.ManagementSeason)
            .Include(pf => pf.Planner)
            .FirstOrDefaultAsync(pf => pf.Id == familyId, ct);
        if (family is null) return null;

        var products = await db.Products
            .Include(p => p.Color)
            .Include(p => p.Size)
            .Where(p => p.ProductFamilyId == familyId)
            .OrderBy(p => p.Sku)
            .ToListAsync(ct);

        // §2a: 区分 (企画→本番) ごとに order_no 昇順で返す。フロントは image_category でグルーピング表示する。
        var images = await db.ProductImages
            .Where(i => i.ProductFamilyId == familyId && !i.IsDeleted)
            .OrderBy(i => i.ImageCategory)
            .ThenBy(i => i.OrderNo)
            .ToListAsync(ct);

        var currentPrices = await db.ProductSupplierPrices
            .Include(p => p.Supplier)
            .Include(p => p.Size)  // PR2: サイズ別単価の表示名解決 (NULL-size 既定行では null)
            .Where(p => p.ProductFamilyId == familyId && !p.IsDeleted && p.EffectiveTo == null)
            .ToListAsync(ct);

        // アソート/セット明細 (PR3、旧 spec No.37/38)。line_no 昇順 (表示順)。明細なしの商品では空リスト。
        var setComponents = await db.ProductSetComponents
            .Where(c => c.ProductFamilyId == familyId)
            .OrderBy(c => c.LineNo).ThenBy(c => c.Id)
            .Select(c => new SetComponentSummary(c.Id, c.ChildItemNumber, c.Quantity, c.LineNo))
            .ToListAsync(ct);

        // 登録者/最終更新者名 (PR1、spec No.27/28)。created_by_user_id / updated_by_user_id は
        // scalar FK (ナビ無し) のため、users を 1 クエリで引いて display_name を解決する。
        // 削除済ユーザでも表示は維持したいので IsDeleted で絞らない (監査表示の性質)。
        var actorIds = new[] { family.CreatedByUserId, family.UpdatedByUserId };
        var userNames = await db.Users
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        userNames.TryGetValue(family.CreatedByUserId, out var createdByUserName);
        userNames.TryGetValue(family.UpdatedByUserId, out var updatedByUserName);

        await audit.LogAsync(actorUserId, "ProductFamily.View",
            entityType: "ProductFamily", entityId: familyId, cancellationToken: ct);

        var legacyProductSku = products.FirstOrDefault(p => p.LegacyId != null)?.LegacyId;
        var (itemNumber, itemFamilyNumber, _) = BuildItemNumbers(family, legacyProductSku);

        // 画像 URL を IImageStorageService で生成 (Local: absolute URL / S3: Pre-signed URL)。
        // 画像点数は最大 5 枚 (BR-10)、SafeGetUrlAsync で原則 4 (非ブロッキング) 維持。
        var imageUrls = await Task.WhenAll(images.Select(i => SafeGetUrlAsync(i.S3Key, ct)));
        var imageSummaries = new List<ImageSummary>(images.Count);
        for (var idx = 0; idx < images.Count; idx++)
        {
            var i = images[idx];
            imageSummaries.Add(new ImageSummary(i.Id, i.OrderNo, i.ImageCategory, i.S3Key, i.ThumbS3Key,
                i.MimeType, i.FileSizeBytes, i.OriginalFilename, imageUrls[idx]));
        }

        return new FamilyDetail(
            new FamilyFullInfo(
                family.Id, family.PlannedYearCode, family.SequenceNo,
                itemNumber, itemFamilyNumber, family.LegacyId,
                family.ProductTypeId, family.ProductType?.Name ?? "?",
                family.ProductSeasonId, family.ProductSeason?.Name ?? "?",
                family.FactorySupplierId, family.FactorySupplier?.Name ?? "?",
                family.BrandId, family.Brand?.Name ?? "?",
                family.FunctionId, family.Function?.Name,
                family.ProductGroupId, family.ProductGroup?.Name ?? "?",
                family.UpperMaterialId, family.UpperMaterial?.Name ?? "?",
                family.InsoleMaterialId, family.InsoleMaterial?.Name ?? "?",
                family.OutsoleMaterialId, family.OutsoleMaterial?.Name ?? "?",
                family.ProductName1, family.ProductName2,
                family.Status, family.IsDeleted,
                family.CreatedAt, family.UpdatedAt,
                // 旧 品番台帳 項目 (Phase A)。管理季節名・企画者名は Include 済ナビから解決 (未設定時 null)。
                family.ProductYear,
                family.ManagementSeasonId, family.ManagementSeason?.Name,
                family.PlannerUserId, family.Planner?.DisplayName,
                family.ProvisionalNumber, family.SampleApprovalDate,
                family.RetailPrice, family.DeliveryPrice,
                family.PlanningCost, family.BrandCost,
                family.RoyaltyTarget, family.RoyaltyRate,
                // 旧 品番台帳 項目 追補 (PR1)。備考 / 備考（色）。
                family.Remark, family.ColorRemark,
                // 登録者/最終更新者名 (PR1、spec No.27/28)。未解決 (該当ユーザ不在) 時は null。
                createdByUserName, updatedByUserName),
            products.Select(p => new SkuSummary(
                p.Id, p.Sku, p.ColorId, p.Color?.Code ?? "?", p.Color?.Name ?? "?",
                p.SizeId, p.Size?.Code ?? "?", p.Size?.Name ?? "?", p.IsDeleted)).ToList(),
            imageSummaries,
            currentPrices.Select(p => new CurrentSupplierPrice(
                p.Id, p.SupplierId, p.Supplier?.Code ?? "?", p.Supplier?.Name ?? "?",
                p.UnitPrice, p.CurrencyCode, p.ExchangeRate, p.EffectiveFrom, p.EffectiveTo, p.DecidedAt,
                // 旧 仕入コスト計算明細 項目 (Phase C)
                p.EstimateUnitPrice, p.EstimateReceivedDate, p.EstimateCost, p.EstimateMarginRate,
                p.PurchaseCost, p.PurchaseMarginRate, p.LossCost, p.DrayageCost, p.TaxRate,
                // サイズ別仕入単価 (PR2)。SizeId=NULL は全サイズ共通の既定単価 (SizeName も null)。
                p.SizeId, p.Size?.Name)).ToList(),
            // アソート/セット明細 (PR3、旧 spec No.37/38)。明細なしの商品では空リスト。
            setComponents);
    }

    /// <summary>
    /// 商品企画更新 (P-05)。属性カラムのみ。FK 構成 (planned_year/type/season/seq/factory) は不変。
    ///
    /// アソート/セット明細 (PR3) は PATCH セマンティクスで扱う:
    ///   - <c>SetComponents == null</c> (未指定): 既存明細を<b>保持</b> (変更しない)。
    ///     明細を管理しない既存/外部呼び出しが明細を破壊しないための下位互換 (原則 2/7)。
    ///   - <c>SetComponents != null</c> (空リスト含む): 既存を全削除して再挿入する<b>全置換</b>
    ///     (BOM ReplaceAsync と同パターン)。空リストは「明細なし (通常商品)」として全削除のみ。
    /// 明細を変更する場合は family 更新と同一トランザクションで delete→insert する。
    /// </summary>
    public async Task<ProductFamily?> UpdateAsync(long familyId, UpdateFamilyRequest req, long actorUserId, CancellationToken ct = default)
    {
        var family = await db.ProductFamilies.FirstOrDefaultAsync(pf => pf.Id == familyId, ct);
        if (family is null) return null;

        // アソート/セット明細を全置換する場合は先に検証 (DB 書込前に 422 で弾く)。
        if (req.SetComponents is not null)
            ValidateSetComponents(req.SetComponents);

        var now = SystemTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            family.BrandId = req.BrandId;
            family.FunctionId = req.FunctionId;
            family.ProductGroupId = req.ProductGroupId;
            family.UpperMaterialId = req.UpperMaterialId;
            family.InsoleMaterialId = req.InsoleMaterialId;
            family.OutsoleMaterialId = req.OutsoleMaterialId;
            family.ProductName1 = req.ProductName1;
            family.ProductName2 = req.ProductName2;
            family.Status = req.Status;
            // 旧 品番台帳 項目 (Phase A、任意)
            family.ProductYear = req.ProductYear;
            family.ManagementSeasonId = req.ManagementSeasonId;
            family.PlannerUserId = req.PlannerUserId;
            family.ProvisionalNumber = req.ProvisionalNumber;
            family.SampleApprovalDate = req.SampleApprovalDate;
            family.RetailPrice = req.RetailPrice;
            family.DeliveryPrice = req.DeliveryPrice;
            family.PlanningCost = req.PlanningCost;
            family.BrandCost = req.BrandCost;
            family.RoyaltyTarget = req.RoyaltyTarget;
            family.RoyaltyRate = req.RoyaltyRate;
            // 旧 品番台帳 項目 追補 (PR1、任意)。備考 / 備考（色）。
            family.Remark = req.Remark;
            family.ColorRemark = req.ColorRemark;
            family.UpdatedAt = now;
            family.UpdatedByUserId = actorUserId;

            // アソート/セット明細の全置換 (PR3、SetComponents != null のときのみ)。
            // 物理削除 → 再挿入 (audit 列のみで論理削除フラグを持たない明細テーブルのため、
            // BOM の論理削除とは異なり物理 DELETE。子品番は family 削除時に CASCADE で消える)。
            var setComponentCount = -1; // -1 = 明細を変更しなかった (audit note 用)
            if (req.SetComponents is not null)
            {
                var existing = await db.ProductSetComponents
                    .Where(c => c.ProductFamilyId == familyId)
                    .ToListAsync(ct);
                db.ProductSetComponents.RemoveRange(existing);

                var replacement = BuildSetComponentEntities(familyId, req.SetComponents, now, actorUserId);
                db.ProductSetComponents.AddRange(replacement);
                setComponentCount = replacement.Count;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // 明細を全置換した場合は件数、未指定 (保持) の場合は "preserved" を記録 (監査の追跡性)。
            await audit.LogAsync(actorUserId, "ProductFamily.Update",
                entityType: "ProductFamily", entityId: familyId,
                note: setComponentCount >= 0 ? $"set_components={setComponentCount}" : "set_components=preserved",
                cancellationToken: ct);

            return family;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>商品企画論理削除 (P-05)。配下 SKU も連動で論理削除。</summary>
    public async Task<bool> SoftDeleteAsync(long familyId, long actorUserId, CancellationToken ct = default)
    {
        var family = await db.ProductFamilies.FirstOrDefaultAsync(pf => pf.Id == familyId, ct);
        if (family is null) return false;

        family.IsDeleted = true;
        family.UpdatedAt = SystemTime.UtcNow;
        family.UpdatedByUserId = actorUserId;

        var skus = await db.Products.Where(p => p.ProductFamilyId == familyId && !p.IsDeleted).ToListAsync(ct);
        foreach (var sku in skus)
        {
            sku.IsDeleted = true;
            sku.UpdatedAt = SystemTime.UtcNow;
            sku.UpdatedByUserId = actorUserId;
        }

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ProductFamily.Delete",
            entityType: "ProductFamily", entityId: familyId,
            note: $"cascaded sku count={skus.Count}", cancellationToken: ct);

        return true;
    }
}
