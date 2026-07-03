namespace Akebono.Application.Masters;

public record SupplierListItem(
    long Id,
    string Code,
    string Name,
    string? OfficialName,
    string ItemConversionCode,
    long CountryId,
    string? CountryName,
    short SupplierType,
    short AlertTarget,
    bool DeleteFlag,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // 適用通貨 (§2f) / ドレー代 (§2i、仕入先ごと)。末尾追加 = 下位互換。
    string CurrencyCode = "JPY",
    decimal? DrayageCost = null);

public record SupplierWriteRequest(
    string Code,
    string Name,
    string? OfficialName,
    string ItemConversionCode,
    long CountryId,
    short SupplierType,
    short AlertTarget,
    // 適用通貨 (§2f) / ドレー代 (§2i)。末尾追加 = 下位互換 (旧クライアントは既定 JPY / null)。
    string CurrencyCode = "JPY",
    decimal? DrayageCost = null);
