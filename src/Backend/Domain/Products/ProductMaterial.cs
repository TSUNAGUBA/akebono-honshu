using Akebono.Domain.Entities;

namespace Akebono.Domain.Products;

/// <summary>
/// BOM = 素材構成・所要量 (Phase 5 data-design-production §4.1)。
/// 品番 (product_family) 1 つを生産するのに必要な素材の構成。1足あたり所要量を保持。
/// product_materials が所要量 SoT、ProductFamily の3FK代表素材は表示用 (疎結合・書戻しなし)。
/// </summary>
public class ProductMaterial
{
    public long Id { get; set; }
    public long ProductFamilyId { get; set; }

    /// <summary>部位区分 (0甲皮/1中底/2底/3付属/4副資材)</summary>
    public MaterialRole MaterialRole { get; set; }
    public long MaterialId { get; set; }

    /// <summary>1足あたり所要量 (例: 0.3000 ㎡、1.0000 組)</summary>
    public decimal RequiredQtyPerUnit { get; set; }

    /// <summary>単位 (足/組/枚/個/m/㎡/cm/本)</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>推奨素材仕入先 (素材発注時の初期値、NULL 可)</summary>
    public long? RecommendedSupplierId { get; set; }

    /// <summary>ロス率 (0〜1未満、例 0.0500=5%)。発注数 = 所要量×数量×(1+loss_rate)</summary>
    public decimal LossRate { get; set; }

    public string? Remark { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    // ナビプロパティ
    public ProductFamily? ProductFamily { get; set; }
    public Material? Material { get; set; }
    public Supplier? RecommendedSupplier { get; set; }
}
