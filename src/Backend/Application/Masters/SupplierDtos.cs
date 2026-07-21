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
    Guid CountryId,
    short SupplierType,
    short AlertTarget,
    // 適用通貨 (§2f) / ドレー代 (§2i)。末尾追加 = 下位互換 (旧クライアントは既定 JPY / null)。
    string CurrencyCode = "JPY",
    decimal? DrayageCost = null,
    // 工場コード (item_conversion_code)。Part2 で工場マスタへ分離したため仕入先では入力しない (任意・既定 "")。
    // UPDATE 時に空なら既存値を保持する (SupplierService、非破壊 = 原則2/7)。
    string ItemConversionCode = "");
