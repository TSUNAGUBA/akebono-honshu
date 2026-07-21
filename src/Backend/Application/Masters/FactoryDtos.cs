namespace Akebono.Application.Masters;

/// <summary>工場マスタ (Part2) の一覧項目。仕入先 (SupplierListItem) から通貨・ドレー代を除いた構成。</summary>
public record FactoryListItem(
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
    DateTime UpdatedAt);

public record FactoryWriteRequest(
    string Code,
    string Name,
    string? OfficialName,
    string ItemConversionCode,
    Guid CountryId,
    short SupplierType,
    short AlertTarget);
