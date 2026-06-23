using Akebono.Domain.Entities;

namespace Akebono.Domain.Orders;

/// <summary>
/// 発注書ヘッダ (Phase 5 §5.1)。
/// Phase 6 簡素化: status 2 値 (Active/Cancelled)、Excel 出力は status と独立。
/// </summary>
public class PurchaseOrder
{
    public long Id { get; set; }

    /// <summary>作成管理番号 (作成時採番、例: "26-00001"、BR-03)</summary>
    public string MgmtNo { get; set; } = string.Empty;

    /// <summary>発注番号 (初回 Excel 出力時採番、例: "S3858"、BR-03)。未出力時は null</summary>
    public string? OrderNo { get; set; }

    public OrderStatus Status { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

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
    /// <summary>検品所出荷日</summary>
    public DateOnly? InspectionShippingDate { get; set; }
    /// <summary>海外出港日</summary>
    public DateOnly? OverseasDepartureDate { get; set; }
    /// <summary>納入倉庫2 (warehouses.id)</summary>
    public long? Warehouse2Id { get; set; }
    /// <summary>納入倉庫3 (warehouses.id)</summary>
    public long? Warehouse3Id { get; set; }

    public string? CommunicationText { get; set; }
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
