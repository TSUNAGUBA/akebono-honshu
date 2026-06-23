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
    string? ProductName2);

public record ExpansionInput(
    List<long> ColorIds,
    List<long> SizeIds);

public record SupplierPriceInput(
    long SupplierId,
    decimal UnitPrice,
    string CurrencyCode,
    decimal? ExchangeRate,
    DateOnly EffectiveFrom,
    DateOnly DecidedAt);

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
    DateTime UpdatedAt);

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
    DateTime UpdatedAt);

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
    DateOnly DecidedAt);

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
    short Status);

// ─────────────────────────────────────────────────
// 新単価追加 (POST /api/v1/products/families/{id}/supplier-prices、BR-04 履歴管理)
// ─────────────────────────────────────────────────

public record AddSupplierPriceRequest(
    long SupplierId,
    decimal UnitPrice,
    string CurrencyCode,
    decimal? ExchangeRate,
    DateOnly EffectiveFrom,
    DateOnly DecidedAt);
