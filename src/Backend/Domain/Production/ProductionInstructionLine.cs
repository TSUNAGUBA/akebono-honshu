using Akebono.Domain.Products;

namespace Akebono.Domain.Production;

/// <summary>
/// 生産指示明細 (色×サイズ別の生産数量、Phase 5 data-design-production §4.3)。
/// </summary>
public class ProductionInstructionLine
{
    public long Id { get; set; }
    public long ProductionInstructionId { get; set; }
    public short LineNo { get; set; }
    public long ProductId { get; set; }

    /// <summary>11桁品番スナップショット</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>商品名スナップショット</summary>
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    // ナビプロパティ
    public ProductionInstruction? ProductionInstruction { get; set; }
    public Product? Product { get; set; }
}
