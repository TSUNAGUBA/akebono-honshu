using Akebono.Domain.Entities;

using Akebono.Domain.Common;

namespace Akebono.Domain.Products;

/// <summary>
/// マルチ仕入先単価 (Phase 5 §4.4)。アイテム (product_family) 単位で複数 (仕入先, 単価, 有効開始日) を保持。
/// BR-04 履歴管理: 同一企画 × 同一仕入先で複数履歴可、現在有効 = effective_to IS NULL。
/// 新単価設定時は旧レコードの effective_to を新単価の effective_from - 1day で UPDATE + 新レコード INSERT
/// (トランザクション境界、Application 層で実装)。
///
/// 機密度 中-高 (NFR §6.2): 監査ログには金額本体ではなくマスク値 ("***") のみ記録。
/// </summary>
public class ProductSupplierPrice : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductFamilyId { get; set; }
    public Guid SupplierId { get; set; }
    /// <summary>
    /// サイズ別仕入単価 (PR2、設計判断Q4)。NULL = 全サイズ共通の既定単価 (従来挙動、既存行は NULL の
    /// まま下位互換)。非NULL = そのサイズ専用単価 (既定をオーバーライド)。現単価解決は
    /// 「サイズ専用の有効行があればそれを、無ければ NULL-size 既定行」のフォールバック (Application 層)。
    /// </summary>
    public Guid? SizeId { get; set; }
    public decimal UnitPrice { get; set; }
    public string CurrencyCode { get; set; } = "JPY";
    public decimal? ExchangeRate { get; set; }
    // 旧 仕入コスト計算明細 項目 (Phase C、全 NULL 許容)
    public decimal? EstimateUnitPrice { get; set; }
    public DateOnly? EstimateReceivedDate { get; set; }
    public decimal? EstimateCost { get; set; }
    public decimal? EstimateMarginRate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public decimal? PurchaseMarginRate { get; set; }
    public decimal? LossCost { get; set; }
    /// <summary>ドレー代 (旧 spec 仕入先サブ No.10 / 設計判断Q6 で「トレー代」から名称統一)</summary>
    public decimal? DrayageCost { get; set; }
    public decimal? TaxRate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateOnly DecidedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }

    public ProductFamily? ProductFamily { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>サイズ別単価の表示名解決用 (PR2)。NULL-size (全サイズ既定) 行では null。</summary>
    public Size? Size { get; set; }
}
