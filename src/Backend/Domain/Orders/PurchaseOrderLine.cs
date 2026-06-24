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

    // 旧 発注明細 項目 (Phase B、全 NULL 許容 = 既存行は NULL のまま下位互換)
    /// <summary>倉庫1入数 / 入数</summary>
    public int? PackQuantity { get; set; }
    /// <summary>見積単価</summary>
    public decimal? EstimateUnitPrice { get; set; }
    /// <summary>仮番号 (発注時点の商品 family.ProvisionalNumber をコピーして凍結)</summary>
    public string? ProvisionalNumberSnapshot { get; set; }
    /// <summary>発注明細 備考 (行レベル、spec 明細 No.26)。NULL 許容 = 既存行は NULL のまま下位互換。</summary>
    public string? Remark { get; set; }

    /// <summary>DB の GENERATED 計算列 (quantity * unit_price_snapshot)。EF Core では読み取り専用。</summary>
    public decimal Subtotal { get; set; }

    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
    public Product? Product { get; set; }

    /// <summary>
    /// 分納×倉庫の多次元明細 (PR5b、任意 = 0 件可)。0 件 = 従来どおりの単一明細
    /// (<see cref="Quantity"/> をそのまま使う)。N 件 = 分納で構成 (<see cref="Quantity"/> は SUM)。
    /// 発注の作成/更新時に全置換するコレクション。
    /// </summary>
    public List<PurchaseOrderLineDelivery> Deliveries { get; set; } = new();
}
