using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

/// <summary>
/// 為替マスタ (§2f)。年月 (YYYY-MM) × 通貨コードごとの対円レートを保持する。
/// 仕入先の適用通貨 (Supplier.CurrencyCode) と発注/単価の年月から適用レートを解決し、
/// 商品⑤仕入単価で円換算価格を表示する。code/name を持たないため MasterEntityBase は継承せず
/// 独自の監査列を持つ (bespoke master、M-04 仕入先と同様の個別対応)。
/// </summary>
public class ExchangeRate : ITenantScoped
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>対象年月 (YYYY-MM 形式、例: "2026-07")</summary>
    public string YearMonth { get; set; } = string.Empty;

    /// <summary>通貨コード (USD/CNY 等、3 桁)。JPY は円のため通常登録不要 (レート=1)。</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>対円レート (1 通貨単位 = Rate 円)。例: USD=150.00, CNY=21.50</summary>
    public decimal Rate { get; set; }

    public bool DeleteFlag { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }
}
