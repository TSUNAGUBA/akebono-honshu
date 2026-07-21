using Akebono.Domain.Entities;

using Akebono.Domain.Common;

namespace Akebono.Domain.Products;

/// <summary>
/// 商品企画レベル親 (Phase 5 §4.1)。11 桁品番の上位 9 桁を確定する企画単位。
/// 色 × サイズ展開の元、マルチ仕入先単価の保持単位。
/// </summary>
public class ProductFamily : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Idempotency-Key (AKB-DOC-12 §8)。作成 API のヘッダ値。NULL = 冪等キーなしで作成された行 (レガシー/シード)。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Idempotency-Key に対応する要求ペイロードの SHA-256 (同一キー・異ペイロード再送の検出用)。</summary>
    public string? IdempotencyPayloadHash { get; set; }

    // 11桁品番の上位 9 桁を構成する FK + 値
    public char PlannedYearCode { get; set; }
    public Guid ProductTypeId { get; set; }
    public Guid ProductSeasonId { get; set; }
    public string SequenceNo { get; set; } = string.Empty;
    public Guid FactorySupplierId { get; set; }

    // 商品属性
    public Guid BrandId { get; set; }
    public Guid? FunctionId { get; set; }
    public Guid ProductGroupId { get; set; }
    public Guid UpperMaterialId { get; set; }
    public Guid InsoleMaterialId { get; set; }
    public Guid OutsoleMaterialId { get; set; }

    public string ProductName1 { get; set; } = string.Empty;
    public string? ProductName2 { get; set; }

    // 旧 品番台帳 項目 (Phase A、全て NULL 許容 = 既存行は NULL のまま下位互換)
    /// <summary>商品年度 (9999=通年)</summary>
    public short? ProductYear { get; set; }
    /// <summary>管理季節 (product_seasons.id)</summary>
    public Guid? ManagementSeasonId { get; set; }
    /// <summary>企画者 (users.id)</summary>
    public Guid? PlannerUserId { get; set; }
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

    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    // ナビプロパティ (Include で FK ネスト返却に使用)
    public ProductType? ProductType { get; set; }
    public ProductSeason? ProductSeason { get; set; }
    // 工場 (Part2)。従来 Supplier を兼用していたが工場マスタへ分離。FK 列名 (factory_supplier_id) は互換維持。
    public Factory? FactorySupplier { get; set; }
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
