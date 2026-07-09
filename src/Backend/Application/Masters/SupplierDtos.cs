namespace Akebono.Application.Masters;

public record SupplierListItem(
    Guid Id,
    string Code,
    string Name,
    string? OfficialName,
    string ItemConversionCode,
    Guid CountryId,
    string? CountryName,
    short SupplierType,
    short AlertTarget,
    // 第二段階規約: 論理削除は deleted_at (null = 有効)。FE 汎用マスタ画面は deletedAt で判定する。
    DateTime? DeletedAt,
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
    Guid CountryId,
    short SupplierType,
    short AlertTarget,
    // 適用通貨 (§2f) / ドレー代 (§2i)。末尾追加 = 下位互換 (旧クライアントは既定 JPY / null)。
    string CurrencyCode = "JPY",
    decimal? DrayageCost = null);
