using Akebono.Domain.Entities;

using Akebono.Domain.Common;

namespace Akebono.Domain.Products;

/// <summary>
/// 商品企画レベル親 (Phase 5 §4.1)。11 桁品番の上位 9 桁を確定する企画単位。
/// 色 × サイズ展開の元、マルチ仕入先単価の保持単位。
/// </summary>
public class ProductFamily : ITenantScoped
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }

    // 11桁品番の上位 9 桁を構成する FK + 値
    public char PlannedYearCode { get; set; }
    public long ProductTypeId { get; set; }
    public long ProductSeasonId { get; set; }
    public string SequenceNo { get; set; } = string.Empty;
    public long FactorySupplierId { get; set; }

    // 商品属性
    public long BrandId { get; set; }
    public long? FunctionId { get; set; }
    public long ProductGroupId { get; set; }
    public long UpperMaterialId { get; set; }
    public long InsoleMaterialId { get; set; }
    public long OutsoleMaterialId { get; set; }

    public string ProductName1 { get; set; } = string.Empty;
    public string? ProductName2 { get; set; }

    // 旧 品番台帳 項目 (Phase A、全て NULL 許容 = 既存行は NULL のまま下位互換)
    /// <summary>商品年度 (9999=通年)</summary>
    public short? ProductYear { get; set; }
    /// <summary>管理季節 (product_seasons.id)</summary>
    public long? ManagementSeasonId { get; set; }
    /// <summary>企画者 (users.id)</summary>
    public long? PlannerUserId { get; set; }
    /// <summary>仮番号</summary>
    public string? ProvisionalNumber { get; set; }
    /// <summary>サンプル合格日</summary>
    public DateOnly? SampleApprovalDate { get; set; }
    /// <summary>小売価格</summary>
    public decimal? RetailPrice { get; set; }
    /// <summary>納品価格</summary>
    public decimal? DeliveryPrice { get; set; }
    /// <summary>企画費</summary>
    public decimal? PlanningCost { get; set; }
    /// <summary>ブランド費</summary>
    public decimal? BrandCost { get; set; }
    /// <summary>版権対象 (1=小売価格, 2=納品価格)</summary>
    public short? RoyaltyTarget { get; set; }
    /// <summary>版権料率(%)</summary>
    public decimal? RoyaltyRate { get; set; }

    // 旧 品番台帳 項目 追補 (PR1、全て NULL 許容 = 既存行は NULL のまま下位互換)
    /// <summary>商品本体 備考 (旧 spec No.39)</summary>
    public string? Remark { get; set; }
    /// <summary>備考（色）(旧 spec No.33)。色ごとではなく商品単位の単一テキスト。</summary>
    public string? ColorRemark { get; set; }

    /// <summary>0=Draft, 1=Active, 2=Discontinued</summary>
    public short Status { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    // ナビプロパティ (Include で FK ネスト返却に使用)
    public ProductType? ProductType { get; set; }
    public ProductSeason? ProductSeason { get; set; }
    public Supplier? FactorySupplier { get; set; }
    public Brand? Brand { get; set; }
    public Function? Function { get; set; }
    public ProductGroup? ProductGroup { get; set; }
    public Material? UpperMaterial { get; set; }
    public Material? InsoleMaterial { get; set; }
    public Material? OutsoleMaterial { get; set; }

    // 旧 品番台帳 項目のナビプロパティ (Phase A)
    public ProductSeason? ManagementSeason { get; set; }
    public User? Planner { get; set; }

    public List<Product> Products { get; set; } = new();
    public List<ProductImage> Images { get; set; } = new();
    public List<ProductSupplierPrice> SupplierPrices { get; set; } = new();
    // アソート/セット明細 (PR3、旧 spec No.37/38)。商品の作成/更新時に全置換するコレクション。
    public List<ProductSetComponent> SetComponents { get; set; } = new();
}
