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
    DateTime UpdatedAt);

public record SupplierWriteRequest(
    string Code,
    string Name,
    string? OfficialName,
    string ItemConversionCode,
    long CountryId,
    short SupplierType,
    short AlertTarget);
