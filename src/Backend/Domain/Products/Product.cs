using Akebono.Domain.Entities;

using Akebono.Domain.Common;

namespace Akebono.Domain.Products;

/// <summary>
/// SKU = 11 桁品番 (Phase 5 §4.2)。色 × サイズの全組合せで 1 レコード (P-02 サイズ展開で一括生成)。
/// BR-02: SKU 不変、論理削除のみ。
/// </summary>
public class Product : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductFamilyId { get; set; }
    public Guid ColorId { get; set; }
    public Guid SizeId { get; set; }
    /// <summary>11 桁品番 (Sku Value Object で組み立て、UNIQUE 制約で二重生成防止)</summary>
    public string Sku { get; set; } = string.Empty;

    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    public ProductFamily? ProductFamily { get; set; }
    public Color? Color { get; set; }
    public Size? Size { get; set; }
}
