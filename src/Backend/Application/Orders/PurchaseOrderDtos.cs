using Akebono.Domain.Orders;

namespace Akebono.Application.Orders;

// ─────────────────────────────────────────────────
// 新規作成 (POST /api/v1/orders、O-01)
// ─────────────────────────────────────────────────
public record CreateOrderRequest(
    long SupplierId,
    long DeliveryDestinationId,
    long DepartmentId,
    long WarehouseId,
    DateOnly DueDate,
    long OrdererUserId,
    long ManagerUserId,
    long? SubOrderer1UserId,
    long? SubOrderer2UserId,
    long? SubOrderer3UserId,
    long? SubOrderer4UserId,
    long? SubOrderer5UserId,
    long? SubOrderer6UserId,
    string? CommunicationText,
    List<OrderLineInput> Lines,
    // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
    bool IsOverseas = false,
    string? LandingPlace = null,
    string? CustomerRef = null,
    DateOnly? FactoryShippingDate = null,
    DateOnly? InspectionShippingDate = null,
    DateOnly? OverseasDepartureDate = null,
    long? Warehouse2Id = null,
    long? Warehouse3Id = null);

public record OrderLineInput(
    long ProductId,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot,
    // 旧 発注明細 項目 (Phase B、任意)。仮番号は商品 family からコピーするため入力には含めない。
    int? PackQuantity = null,
    decimal? EstimateUnitPrice = null);

// ─────────────────────────────────────────────────
// 一覧 (GET /api/v1/orders、O-03)
// ─────────────────────────────────────────────────
public record OrderListItem(
    long Id,
    string MgmtNo,
    string? OrderNo,
    short Status,
    string SupplierCode,
    string SupplierName,
    string DeliveryDestinationName,
    DateOnly DueDate,
    string? OrdererName,
    int LineCount,
    decimal TotalAmount,
    string CurrencyCode,
    DateTime? FirstExportedAt,
    DateTime? LastExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // 発注区分 国内/海外 (Phase B、is_overseas)。一覧でのタブ絞込・区分バッジ表示用。
    bool IsOverseas = false,
    // 発注状態 5 値モデル (#3a)。DeliveredAt/IsDeleted から納品完了/発注削除 を導出 (フロント側)。
    DateTime? DeliveredAt = null,
    bool IsDeleted = false,
    // 一覧 SPLIT フィルタ (#3a) 用フィールド。発注先/発注者/得意先/単価未決定で client-side 絞込。
    long SupplierId = 0,
    long OrdererUserId = 0,
    string? CustomerName = null,
    // 明細に単価未決定 (unit_price_snapshot <= 0) を含むか (クエリで EXISTS 集計)。
    bool HasUndecidedPrice = false);

// ─────────────────────────────────────────────────
// 詳細 (GET /api/v1/orders/{id}、O-04 編集画面ベース)
// ─────────────────────────────────────────────────
public record OrderDetail(
    long Id,
    string MgmtNo,
    string? OrderNo,
    short Status,
    DateTime? CancelledAt,
    string? CancelReason,
    long SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierOfficialNameSnapshot,
    string? SupplierCodeSnapshot,
    long DeliveryDestinationId,
    string DeliveryDestinationName,
    string? CustomerNameSnapshot,
    long DepartmentId,
    string DepartmentName,
    long WarehouseId,
    string WarehouseName,
    DateOnly DueDate,
    long OrdererUserId,
    string OrdererName,
    long ManagerUserId,
    string ManagerName,
    long? SubOrderer1UserId,
    long? SubOrderer2UserId,
    long? SubOrderer3UserId,
    long? SubOrderer4UserId,
    long? SubOrderer5UserId,
    long? SubOrderer6UserId,
    string? CommunicationText,
    DateTime? FirstExportedAt,
    DateTime? LastExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<OrderLineDetail> Lines,
    // 旧 発注書 国内/海外 項目 (Phase B)。納入倉庫2/3 は表示用に名前も解決して返す (未設定時 null)。
    bool IsOverseas = false,
    string? LandingPlace = null,
    string? CustomerRef = null,
    DateOnly? FactoryShippingDate = null,
    DateOnly? InspectionShippingDate = null,
    DateOnly? OverseasDepartureDate = null,
    long? Warehouse2Id = null,
    string? Warehouse2Name = null,
    long? Warehouse3Id = null,
    string? Warehouse3Name = null,
    // 発注状態 5 値モデル (#3a)。納品完了/発注削除 の状態表示・操作可否判定に使う。
    // 操作者名は cancelled_by と同じく詳細では非表示 (日時のみ表示)。
    DateTime? DeliveredAt = null,
    bool IsDeleted = false,
    DateTime? DeletedAt = null);

public record OrderLineDetail(
    long Id,
    short LineNo,
    long ProductId,
    string Sku,
    string ProductName,
    string ColorName,
    string SizeName,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot,
    decimal Subtotal,
    // 旧 発注明細 項目 (Phase B、任意)
    int? PackQuantity = null,
    decimal? EstimateUnitPrice = null,
    string? ProvisionalNumberSnapshot = null);

// ─────────────────────────────────────────────────
// 更新 (PATCH /api/v1/orders/{id}、O-04、edit_reason 必須 F-16)
// ─────────────────────────────────────────────────
public record UpdateOrderRequest(
    EditReason EditReason,
    string? EditNote,
    long SupplierId,
    long DeliveryDestinationId,
    long DepartmentId,
    long WarehouseId,
    DateOnly DueDate,
    long OrdererUserId,
    long ManagerUserId,
    long? SubOrderer1UserId,
    long? SubOrderer2UserId,
    long? SubOrderer3UserId,
    long? SubOrderer4UserId,
    long? SubOrderer5UserId,
    long? SubOrderer6UserId,
    string? CommunicationText,
    List<UpdateLineInput> Lines,
    // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
    bool IsOverseas = false,
    string? LandingPlace = null,
    string? CustomerRef = null,
    DateOnly? FactoryShippingDate = null,
    DateOnly? InspectionShippingDate = null,
    DateOnly? OverseasDepartureDate = null,
    long? Warehouse2Id = null,
    long? Warehouse3Id = null);

public record UpdateLineInput(
    long? Id,
    long ProductId,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot,
    // 旧 発注明細 項目 (Phase B、任意)。仮番号は商品 family からコピーするため入力には含めない。
    int? PackQuantity = null,
    decimal? EstimateUnitPrice = null);

// ─────────────────────────────────────────────────
// 中止 (POST /api/v1/orders/{id}/cancel、O-05)
// ─────────────────────────────────────────────────
public record CancelOrderRequest(string CancelReason);

// ─────────────────────────────────────────────────
// 連絡文章 (O-07、テンプレ複写)
// ─────────────────────────────────────────────────
public record CommunicationTextSuggestion(string Body, bool StandardPrintFlag, string SourceLabel);
