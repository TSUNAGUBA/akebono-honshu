namespace Akebono.Application.Masters;

/// <summary>為替マスタ 一覧行 (§2f)。</summary>
public record ExchangeRateListItem(
    long Id,
    string YearMonth,
    string CurrencyCode,
    decimal Rate,
    bool DeleteFlag,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>為替マスタ 登録/更新 (§2f)。</summary>
public record ExchangeRateWriteRequest(
    string YearMonth,
    string CurrencyCode,
    decimal Rate);
