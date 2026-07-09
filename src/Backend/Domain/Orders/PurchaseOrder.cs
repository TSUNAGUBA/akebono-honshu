using Akebono.Domain.Entities;

using Akebono.Domain.Common;

namespace Akebono.Domain.Orders;

/// <summary>
/// 発注書ヘッダ (Phase 5 §5.1)。
/// Phase 6 簡素化: status 2 値 (Active/Cancelled)、Excel 出力は status と独立。
/// </summary>
public class PurchaseOrder : ITenantScoped
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Idempotency-Key (AKB-DOC-12 §8)。作成 API のヘッダ値。NULL = 冪等キーなしで作成された行 (レガシー/シード)。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Idempotency-Key に対応する要求ペイロードの SHA-256 (同一キー・異ペイロード再送の検出用)。</summary>
    public string? IdempotencyPayloadHash { get; set; }

    /// <summary>作成管理番号 (作成時採番、例: "26-00001"、BR-03)</summary>
    public string MgmtNo { get; set; } = string.Empty;

    /// <summary>
    /// 発注番号 (例: "S3858"、BR-03)。帳票出力フォームで手入力する (旧システムの発注書出力画面と同様)。
    /// 後方互換: フォームで未入力かつ既存採番が無い初回出力時は自動採番 ("S00001") にフォールバックする。
    /// 未出力時は null。
    /// </summary>
    public string? OrderNo { get; set; }

    // 帳票出力フォーム 手入力項目 (旧システム「発注書出力」画面)。発注番号 (OrderNo 上述) と合わせて
    // 出力時にフォームで入力し発注に保存する (再出力時は保存値を初期表示)。全 NULL 許容 = 既存行は不変。
    /// <summary>発注日 (帳票 order date)。出力フォームで手入力。未入力可。</summary>
    public DateOnly? OrderDate { get; set; }
    /// <summary>出荷指示番号 (旧システム発注書出力画面の手入力欄)。出力フォームで手入力。未入力可。</summary>
    public string? ShippingInstructionNo { get; set; }

    public OrderStatus Status { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    // 発注状態 4 値モデル (§3b)。未発注 / 発注済 / 発注中止 / 発注削除。
    // 「発注済」はユーザー操作で明示設定する (OrderedAt)。ダウンロード (Excel 出力) では状態を変えない。
    // 導出優先順位: 発注削除(IsDeleted) > 発注中止(Status=Cancelled) > 発注済(OrderedAt!=null) > 未発注。
    /// <summary>発注済日時 (NULL=未発注)。発注を「発注済にする」操作で SET、「未発注に戻す」で NULL。出力とは独立 (§3b)。</summary>
    public DateTime? OrderedAt { get; set; }
    /// <summary>発注済にした操作者 (users.id)</summary>
    public long? OrderedByUserId { get; set; }
    // 納品完了 (旧 5 値モデル) は §3b で廃止。列は後方互換のため残すが、状態導出・UI では使用しない。
    /// <summary>[廃止] 納品完了日時。§3b の 4 値化で状態導出から除外 (列は後方互換のため保持)。</summary>
    public DateTime? DeliveredAt { get; set; }
    /// <summary>[廃止] 納品完了操作者 (users.id)</summary>
    public long? DeliveredByUserId { get; set; }
    /// <summary>論理削除フラグ (TRUE=発注削除)。NOT NULL DEFAULT FALSE。物理削除はしない</summary>
    public bool IsDeleted { get; set; }
    /// <summary>論理削除日時</summary>
    public DateTime? DeletedAt { get; set; }
    /// <summary>論理削除操作者 (users.id)</summary>
    public long? DeletedByUserId { get; set; }

    public long SupplierId { get; set; }
    /// <summary>仕入先 official_name のスナップショット (F-22 帳票宛名第 1 要素)</summary>
    public string? SupplierOfficialNameSnapshot { get; set; }
    /// <summary>仕入先 code のスナップショット (F-22 帳票宛名第 2 要素)</summary>
    public string? SupplierCodeSnapshot { get; set; }

    public long DeliveryDestinationId { get; set; }
    /// <summary>取引先名スナップショット (内部識別用、Excel 帳票には印字されない)</summary>
    public string? CustomerNameSnapshot { get; set; }

    public long DepartmentId { get; set; }
    public long WarehouseId { get; set; }
    public DateOnly DueDate { get; set; }

    public long OrdererUserId { get; set; }
    public long? SubOrderer1UserId { get; set; }
    public long? SubOrderer2UserId { get; set; }
    public long? SubOrderer3UserId { get; set; }
    public long? SubOrderer4UserId { get; set; }
    public long? SubOrderer5UserId { get; set; }
    public long? SubOrderer6UserId { get; set; }
    public long ManagerUserId { get; set; }

    // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外 NULL 許容 = 既存行は NULL のまま下位互換)
    /// <summary>発注区分 (国内=false/海外=true)。NOT NULL DEFAULT FALSE。</summary>
    public bool IsOverseas { get; set; }
    /// <summary>荷揚地 / Port of entry</summary>
    public string? LandingPlace { get; set; }
    /// <summary>得意先 / 受注先</summary>
    public string? CustomerRef { get; set; }
    /// <summary>工場出荷日</summary>
    public DateOnly? FactoryShippingDate { get; set; }
    /// <summary>納品所出荷日 (旧名: 検品所出荷日、設計判断Q6 で名称統一。列 delivery_place_shipping_date)</summary>
    public DateOnly? DeliveryPlaceShippingDate { get; set; }
    /// <summary>海外出港日</summary>
    public DateOnly? OverseasDepartureDate { get; set; }
    /// <summary>納入倉庫2 (warehouses.id)</summary>
    public long? Warehouse2Id { get; set; }
    /// <summary>納入倉庫3 (warehouses.id)</summary>
    public long? Warehouse3Id { get; set; }

    public string? CommunicationText { get; set; }
    // 連絡文書 6 行 (構造化、PR6)。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」。新フローの SoT。
    // 既存 CommunicationText は後方互換のため温存 (6 列が全 NULL の旧発注は Excel/編集ロードでフォールバック)。
    /// <summary>連絡文書01行 (spec 明細 No.27、列 communication_line_1)</summary>
    public string? CommunicationLine1 { get; set; }
    /// <summary>連絡文書02行 (spec 明細 No.28、列 communication_line_2)</summary>
    public string? CommunicationLine2 { get; set; }
    /// <summary>連絡文書03行 (spec 明細 No.29、列 communication_line_3)</summary>
    public string? CommunicationLine3 { get; set; }
    /// <summary>連絡文書04行 (spec 明細 No.30、列 communication_line_4)</summary>
    public string? CommunicationLine4 { get; set; }
    /// <summary>連絡文書05行 (spec 明細 No.31、列 communication_line_5)</summary>
    public string? CommunicationLine5 { get; set; }
    /// <summary>連絡文書06行 (spec 明細 No.32、列 communication_line_6)</summary>
    public string? CommunicationLine6 { get; set; }
    public DateTime? FirstExportedAt { get; set; }
    public DateTime? LastExportedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    // ナビプロパティ
    public Supplier? Supplier { get; set; }
    public DeliveryDestination? DeliveryDestination { get; set; }
    public Department? Department { get; set; }
    public Warehouse? Warehouse { get; set; }
    // 旧 発注書 国内/海外 項目のナビプロパティ (Phase B、納入倉庫2/3)
    public Warehouse? Warehouse2 { get; set; }
    public Warehouse? Warehouse3 { get; set; }
    public User? Orderer { get; set; }
    public User? Manager { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = new();
    public List<PurchaseOrderExportLog> ExportLogs { get; set; } = new();
}
