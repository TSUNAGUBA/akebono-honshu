using Akebono.Domain.Entities;
using Akebono.Domain.Products;

namespace Akebono.Domain.Production;

/// <summary>
/// 素材発注明細 (所要量展開、Phase 5 data-design-production §4.5)。
/// subtotal は DB 側 GENERATED 列 (既存 PurchaseOrderLine と同方針)。
/// unit_price は機密度 中-高 (既存仕入単価と同等保護)。
/// </summary>
public class MaterialOrderLine
{
    public long Id { get; set; }
    public long MaterialOrderId { get; set; }
    public short LineNo { get; set; }
    public long MaterialId { get; set; }
    public string MaterialNameSnapshot { get; set; } = string.Empty;

    /// <summary>由来品番 (未/済の品番ロールアップに使用、NULL 可)</summary>
    public long? ProductFamilyId { get; set; }
    /// <summary>由来の生産指示明細 (トレース用、NULL 可)</summary>
    public long? SourcePiLineId { get; set; }

    /// <summary>発注数量 (推奨= Σ所要量×生産数量×(1+loss_rate)、手調整可)</summary>
    public decimal RequiredQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal? UnitPrice { get; set; }
    public string CurrencyCode { get; set; } = "JPY";

    /// <summary>DB 計算列 (required_quantity * COALESCE(unit_price,0))</summary>
    public decimal Subtotal { get; set; }

    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    // ナビプロパティ
    public MaterialOrder? MaterialOrder { get; set; }
    public Material? Material { get; set; }
    public ProductFamily? ProductFamily { get; set; }
}
