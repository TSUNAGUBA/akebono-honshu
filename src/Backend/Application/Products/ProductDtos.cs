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
    string ProductName1,
    string? ProductName2,
    string BrandName,
    string ProductTypeName,
    string ProductSeasonName,
    string FactorySupplierName,
    short Status,
    int SkuVariationCount,
    int ImageCount,
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
    string? OriginalFilename);

public record CurrentSupplierPrice(
    long Id,
    long SupplierId,
    string SupplierCode,
    string SupplierName,
    decimal UnitPrice,
    string CurrencyCode,
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
