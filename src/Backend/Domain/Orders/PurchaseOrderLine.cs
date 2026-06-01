using Akebono.Domain.Products;

namespace Akebono.Domain.Orders;

/// <summary>
/// 発注明細 (Phase 5 §5.2)。スナップショット (sku/name/unit_price/currency) で発注時点を凍結。
/// subtotal は DB の GENERATED ALWAYS AS (quantity * unit_price_snapshot) STORED 計算列。
/// </summary>
public class PurchaseOrderLine
{
    public long Id { get; set; }
    public long PurchaseOrderId { get; set; }
    public short LineNo { get; set; }
    public long ProductId { get; set; }

    public string SkuSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public string CurrencyCodeSnapshot { get; set; } = "JPY";

    /// <summary>DB の GENERATED 計算列 (quantity * unit_price_snapshot)。EF Core では読み取り専用。</summary>
    public decimal Subtotal { get; set; }

    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
    public Product? Product { get; set; }
}
