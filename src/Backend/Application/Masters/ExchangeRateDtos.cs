namespace Akebono.Application.Masters;

/// <summary>為替マスタ 一覧行 (§2f)。第二段階規約: 論理削除は deleted_at (null = 有効)。</summary>
public record ExchangeRateListItem(
    Guid Id,
    string YearMonth,
    string CurrencyCode,
    decimal Rate,
    // 税率(%) (Part5)。通貨×年月ごとの仕入税率。NULL = 未設定。
    decimal? TaxRate,
    DateTime? DeletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>為替マスタ 登録/更新 (§2f)。TaxRate は税率(%)、NULL 許容 (Part5)。</summary>
public record ExchangeRateWriteRequest(
    string YearMonth,
    string CurrencyCode,
    decimal Rate,
    decimal? TaxRate);
