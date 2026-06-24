namespace Akebono.Application.Products;

// ─────────────────────────────────────────────────
// バルク登録 (POST /api/v1/products/families/complete、P-01〜P-03)
// ─────────────────────────────────────────────────

public record CompleteFamilyRequest(
    FamilyInput Family,
    ExpansionInput Expansion,
    List<SupplierPriceInput> SupplierPrices);

public record FamilyInput(
    char PlannedYearCode,
    long ProductTypeId,
    long ProductSeasonId,
    long FactorySupplierId,
    long BrandId,
    long? FunctionId,
    long ProductGroupId,
    long UpperMaterialId,
    long InsoleMaterialId,
    long OutsoleMaterialId,
    string ProductName1,
    string? ProductName2,
    // 旧 品番台帳 項目 (Phase A、全て任意)
    short? ProductYear = null,
    long? ManagementSeasonId = null,
    long? PlannerUserId = null,
    string? ProvisionalNumber = null,
    DateOnly? SampleApprovalDate = null,
    decimal? RetailPrice = null,
    decimal? DeliveryPrice = null,
    decimal? PlanningCost = null,
    decimal? BrandCost = null,
    short? RoyaltyTarget = null,
    decimal? RoyaltyRate = null,
    // 旧 品番台帳 項目 追補 (PR1、末尾追加 = 下位互換)
    string? Remark = null,        // 商品本体 備考 (spec No.39)
    string? ColorRemark = null);  // 備考（色）(spec No.33、商品単位の単一テキスト)

public record ExpansionInput(
    List<long> ColorIds,
    List<long> SizeIds);

public record SupplierPriceInput(
    long SupplierId,
    decimal UnitPrice,
    string CurrencyCode,
    decimal? ExchangeRate,
    DateOnly EffectiveFrom,
    DateOnly DecidedAt,
    // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
    decimal? EstimateUnitPrice = null,
    DateOnly? EstimateReceivedDate = null,
    decimal? EstimateCost = null,
    decimal? EstimateMarginRate = null,
    decimal? PurchaseCost = null,
    decimal? PurchaseMarginRate = null,
    decimal? LossCost = null,
    decimal? DrayageCost = null,  // ドレー代 (旧「トレー代」、設計判断Q6)
    decimal? TaxRate = null);

public record CompleteFamilyResponse(
    FamilySummary Family,
    List<SkuSummary> Products,
    List<SupplierPriceSummary> SupplierPrices);

// ─────────────────────────────────────────────────
// 一覧 (GET /api/v1/products/families、P-04)
// ─────────────────────────────────────────────────

public record FamilyListItem(
    long Id,
    string Sku9Digit,
    /// <summary>品番 7 桁 (planned_year + type + season + sequence_no + factory)。新規企画品の業務キー。</summary>
    string ItemNumber,
    /// <summary>他品番 6 桁 (品番から工場 CD を除いた、商品ファミリーの識別子)。</summary>
    string ItemFamilyNumber,
    /// <summary>既存システム由来の品番 (例: FA2071F)。新規企画品では null。</summary>
    string? LegacyId,
    string ProductName1,
    string? ProductName2,
    string BrandName,
    string ProductTypeName,
    string ProductSeasonName,
    string FactorySupplierName,
    short Status,
    int SkuVariationCount,
    int ImageCount,
    /// <summary>代表画像 (order_no が最小の有効画像) の s3_key。画像未登録時は null</summary>
    string? PrimaryImageS3Key,
    /// <summary>
    /// 代表画像の配信用 URL (Iter 4 段階 C 追加)。Local 時は absolute URL、S3 時は Pre-signed URL (TTL 15min)。
    /// 画像未登録時 / URL 取得失敗時 (S3 throttling / IAM 一時失効等) は null。
    /// Frontend は `v-if` でガードしてプレースホルダーを表示する (Iter 4 段階 C-1 reviewer 指摘 M3/M6 対応、
    /// CLAUDE.md 原則 4 非ブロッキングなエラーハンドリング)。
    /// </summary>
    string? PrimaryImageUrl,
    decimal? CurrentMinPrice,
    decimal? CurrentMaxPrice,
    string CurrencyCode,
    DateTime UpdatedAt,
    // 一覧の SPLIT フィルタ用 ID / 値 (P-04 商品一覧の絞込をクライアント側で行うために追加。全て末尾追加 = 下位互換)。
    // 既存の *Name 列は表示用、以下の *Id 列はフィルタの一致判定用 (MasterSelect の数値 ID と突合)。
    /// <summary>商品タイプ (product_types.id)。一覧フィルタ「商品タイプ」用。</summary>
    long ProductTypeId = 0,
    /// <summary>商品季節 (product_seasons.id)。一覧フィルタ「商品季節」用。</summary>
    long ProductSeasonId = 0,
    /// <summary>仕入先 = 工場 (suppliers.id)。一覧フィルタ「仕入先」用。</summary>
    long FactorySupplierId = 0,
    /// <summary>ブランド (brands.id)。一覧フィルタ「ブランド」用。</summary>
    long BrandId = 0,
    /// <summary>商品年度 (9999=通年、Phase A)。一覧フィルタ「商品年度」用 (前方/部分一致)。未設定時 null。</summary>
    short? ProductYear = null,
    /// <summary>仮番号 (Phase A)。一覧フィルタ「仮番号」用 (部分一致)。未設定時 null。</summary>
    string? ProvisionalNumber = null,
    /// <summary>企画者 (users.id、Phase A)。一覧フィルタ「企画者」用。未設定時 null。</summary>
    long? PlannerUserId = null,
    /// <summary>企画者名 (表示用、Phase A)。未設定時 null。</summary>
    string? PlannerName = null);

// ─────────────────────────────────────────────────
// 詳細 (GET /api/v1/products/families/{id}、P-05)
// ─────────────────────────────────────────────────

public record FamilyDetail(
    FamilyFullInfo Family,
    List<SkuSummary> Products,
    List<ImageSummary> Images,
    List<CurrentSupplierPrice> CurrentSupplierPrices);

public record FamilyFullInfo(
    long Id,
    char PlannedYearCode,
    string SequenceNo,
    /// <summary>品番 7 桁 (planned_year + type + season + sequence_no + factory)。新規企画品の業務キー。</summary>
    string ItemNumber,
    /// <summary>他品番 6 桁 (品番から工場 CD を除いた、商品ファミリーの識別子)。</summary>
    string ItemFamilyNumber,
    /// <summary>既存システム由来の品番 (例: FA2071F)。新規企画品では null。</summary>
    string? LegacyId,
    long ProductTypeId,
    string ProductTypeName,
    long ProductSeasonId,
    string ProductSeasonName,
    long FactorySupplierId,
    string FactorySupplierName,
    long BrandId,
    string BrandName,
    long? FunctionId,
    string? FunctionName,
    long ProductGroupId,
    string ProductGroupName,
    long UpperMaterialId,
    string UpperMaterialName,
    long InsoleMaterialId,
    string InsoleMaterialName,
    long OutsoleMaterialId,
    string OutsoleMaterialName,
    string ProductName1,
    string? ProductName2,
    short Status,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // 旧 品番台帳 項目 (Phase A、全て任意)。表示用に管理季節名・企画者名も解決して返す。
    short? ProductYear,
    long? ManagementSeasonId,
    string? ManagementSeasonName,
    long? PlannerUserId,
    string? PlannerName,
    string? ProvisionalNumber,
    DateOnly? SampleApprovalDate,
    decimal? RetailPrice,
    decimal? DeliveryPrice,
    decimal? PlanningCost,
    decimal? BrandCost,
    short? RoyaltyTarget,
    decimal? RoyaltyRate,
    // 旧 品番台帳 項目 追補 (PR1、末尾追加 = 下位互換)
    string? Remark = null,        // 商品本体 備考 (spec No.39)
    string? ColorRemark = null,   // 備考（色）(spec No.33)
    // 登録者/最終更新者 表示 (PR1、spec No.27/28)。users.display_name を join 解決。
    // 既存列 (created_by_user_id / updated_by_user_id) からの導出のため新規列追加なし。
    string? CreatedByUserName = null,
    string? UpdatedByUserName = null);

public record SkuSummary(
    long Id,
    string Sku,
    long ColorId,
    string ColorCode,
    string ColorName,
    long SizeId,
    string SizeCode,
    string SizeName,
    bool IsDeleted);

public record ImageSummary(
    long Id,
    short OrderNo,
    string S3Key,
    string? ThumbS3Key,
    string MimeType,
    int FileSizeBytes,
    string? OriginalFilename,
    /// <summary>
    /// 画像配信用 URL (Iter 4 段階 C で追加)。
    /// Local 時は absolute URL (`${apiOrigin}/uploads/...`)、S3 時は Pre-signed URL (TTL 15min)。
    /// URL 取得失敗時 (S3 throttling 等) は null (Iter 4 段階 C-1 reviewer 指摘 M3/M6、
    /// CLAUDE.md 原則 4 非ブロッキングなエラーハンドリング)。Frontend は `v-if` で
    /// ガードしてプレースホルダーを表示する。
    /// </summary>
    string? Url);

public record CurrentSupplierPrice(
    long Id,
    long SupplierId,
    string SupplierCode,
    string SupplierName,
    decimal UnitPrice,
    string CurrencyCode,
    decimal? ExchangeRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateOnly DecidedAt,
    // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
    decimal? EstimateUnitPrice,
    DateOnly? EstimateReceivedDate,
    decimal? EstimateCost,
    decimal? EstimateMarginRate,
    decimal? PurchaseCost,
    decimal? PurchaseMarginRate,
    decimal? LossCost,
    decimal? DrayageCost,  // ドレー代 (旧「トレー代」、設計判断Q6)
    decimal? TaxRate);

public record FamilySummary(long Id, string SequenceNo, char PlannedYearCode);
public record SupplierPriceSummary(long Id, long SupplierId, decimal UnitPrice, DateOnly EffectiveFrom);

// ─────────────────────────────────────────────────
// 更新 (PATCH /api/v1/products/families/{id}、P-05)
// ─────────────────────────────────────────────────

public record UpdateFamilyRequest(
    long BrandId,
    long? FunctionId,
    long ProductGroupId,
    long UpperMaterialId,
    long InsoleMaterialId,
    long OutsoleMaterialId,
    string ProductName1,
    string? ProductName2,
    short Status,
    // 旧 品番台帳 項目 (Phase A、全て任意)
    short? ProductYear = null,
    long? ManagementSeasonId = null,
    long? PlannerUserId = null,
    string? ProvisionalNumber = null,
    DateOnly? SampleApprovalDate = null,
    decimal? RetailPrice = null,
    decimal? DeliveryPrice = null,
    decimal? PlanningCost = null,
    decimal? BrandCost = null,
    short? RoyaltyTarget = null,
    decimal? RoyaltyRate = null,
    // 旧 品番台帳 項目 追補 (PR1、末尾追加 = 下位互換)
    string? Remark = null,        // 商品本体 備考 (spec No.39)
    string? ColorRemark = null);  // 備考（色）(spec No.33、商品単位の単一テキスト)

// ─────────────────────────────────────────────────
// 新単価追加 (POST /api/v1/products/families/{id}/supplier-prices、BR-04 履歴管理)
// ─────────────────────────────────────────────────

public record AddSupplierPriceRequest(
    long SupplierId,
    decimal UnitPrice,
    string CurrencyCode,
    decimal? ExchangeRate,
    DateOnly EffectiveFrom,
    DateOnly DecidedAt,
    // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
    decimal? EstimateUnitPrice = null,
    DateOnly? EstimateReceivedDate = null,
    decimal? EstimateCost = null,
    decimal? EstimateMarginRate = null,
    decimal? PurchaseCost = null,
    decimal? PurchaseMarginRate = null,
    decimal? LossCost = null,
    decimal? DrayageCost = null,  // ドレー代 (旧「トレー代」、設計判断Q6)
    decimal? TaxRate = null);
