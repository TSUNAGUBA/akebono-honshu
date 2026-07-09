using Akebono.Domain.Common;

namespace Akebono.Domain.Orders;

/// <summary>
/// Excel 出力履歴 (Phase 5 §5.3、監査用・非 UI 露出)。
/// 旧設計の purchase_order_revisions テーブルは改訂概念廃止により削除、本テーブルで代替。
/// audit_logs (Excel.Export) と一部冗長だが、発注書詳細画面の履歴タブ用キャッシュ位置付け。
/// </summary>
public class PurchaseOrderExportLog : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public DateTime ExportedAt { get; set; }
    public Guid ExportedByUserId { get; set; }
    /// <summary>この出力が初回かどうか (purchase_orders.first_exported_at 採番のトリガとなった可視化)</summary>
    public bool IsFirstExport { get; set; }
    /// <summary>テンプレートバージョン (例: "iter3-v1")。Iter 4 で本テンプレ化時にバージョンアップ</summary>
    public string ExcelTemplateVersion { get; set; } = string.Empty;

    public PurchaseOrder? PurchaseOrder { get; set; }
}
