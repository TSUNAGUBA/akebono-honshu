using Akebono.Domain.Entities;

namespace Akebono.Domain.Products;

/// <summary>
/// 商品企画レベル親 (Phase 5 §4.1)。11 桁品番の上位 9 桁を確定する企画単位。
/// 色 × サイズ展開の元、マルチ仕入先単価の保持単位。
/// </summary>
public class ProductFamily
{
    public long Id { get; set; }

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

    public List<Product> Products { get; set; } = new();
    public List<ProductImage> Images { get; set; } = new();
    public List<ProductSupplierPrice> SupplierPrices { get; set; } = new();
}
