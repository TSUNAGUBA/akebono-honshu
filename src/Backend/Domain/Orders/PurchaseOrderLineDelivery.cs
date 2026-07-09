using Akebono.Domain.Entities;

using Akebono.Domain.Common;

namespace Akebono.Domain.Orders;

/// <summary>
/// 分納×倉庫の多次元明細 (PR5b、Phase 5 §5.2b)。
/// 旧 spec 発注明細 No.6「発注明細日1-11」/ No.7-17「発注明細数1〜11」/ No.18-23「倉庫1〜3 入数/発注数」。
///
/// 1 発注明細 (<see cref="PurchaseOrderLine"/>) を「(倉庫 × 納期) の分納行」の集合で多次元化する。
/// 設計判断 Q1=可変の分納テーブル (11 固定ではない) / Q2=明細を倉庫×納期で多次元化。
/// 発注の作成/更新時に全置換するコレクション (アソート/セット明細 = ProductSetComponent と同じパターン)。
///
/// 下位互換 (最重要): 分納は明細あたり任意 (0 件可)。
///   分納 0 件 = 従来どおりの単一明細 (<see cref="PurchaseOrderLine.Quantity"/> をそのまま単一数量として使う)。
///   分納 N 件 = その明細は分納で構成。<see cref="PurchaseOrderLine.Quantity"/> には分納数量の合計 (SUM) を
///     設定する (既存の subtotal GENERATED 列 = quantity * unit_price_snapshot がそのまま正しい金額になる)。
/// </summary>
public class PurchaseOrderLineDelivery : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>親 = 発注明細 (purchase_order_lines.id)。ON DELETE CASCADE。</summary>
    public Guid PurchaseOrderLineId { get; set; }

    /// <summary>倉庫 (warehouses.id)。NULL = 倉庫未指定。任意参照 FK (cascade なし)。</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>納期 / 発注明細日。NULL = 発注明細日未指定。</summary>
    public DateOnly? DeliveryDate { get; set; }

    /// <summary>発注明細数 (分納数量)。正の整数 (CHECK quantity &gt; 0)。</summary>
    public int Quantity { get; set; }

    /// <summary>倉庫別入数。NULL 許容 (単一明細時は purchase_order_lines.pack_quantity を使う)。</summary>
    public int? PackQuantity { get; set; }

    /// <summary>表示順 (配列順で採番)。</summary>
    public short Seq { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }

    // ナビプロパティ
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public Warehouse? Warehouse { get; set; }
}
