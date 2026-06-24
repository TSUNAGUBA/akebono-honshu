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
    DateOnly? DeliveryPlaceShippingDate = null,
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
    decimal? EstimateUnitPrice = null,
    // 発注明細 備考 (spec 明細 No.26、任意)。末尾追加で下位互換。
    string? Remark = null);

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
    DateOnly? DeliveryPlaceShippingDate = null,
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
    string? ProvisionalNumberSnapshot = null,
    // 発注明細 備考 (spec 明細 No.26、任意)。末尾追加で下位互換。
    string? Remark = null);

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
    DateOnly? DeliveryPlaceShippingDate = null,
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
    decimal? EstimateUnitPrice = null,
    // 発注明細 備考 (spec 明細 No.26、任意)。末尾追加で下位互換。
    string? Remark = null);

// ─────────────────────────────────────────────────
// 中止 (POST /api/v1/orders/{id}/cancel、O-05)
// ─────────────────────────────────────────────────
public record CancelOrderRequest(string CancelReason);

// ─────────────────────────────────────────────────
// 連絡文章 (O-07、テンプレ複写)
// ─────────────────────────────────────────────────
public record CommunicationTextSuggestion(string Body, bool StandardPrintFlag, string SourceLabel);

// ─────────────────────────────────────────────────
// 単価サジェスト (PR2、size-aware)。発注明細の unit_price_snapshot 入力補助。
// GET /api/v1/orders/price-suggestion?productId=&supplierId=
//   SKU (productId) の size に対応する現単価を「(family, supplier, SKUのsize) の現単価 →
//   無ければ (…, NULL-size 既定) の現単価」のフォールバックで解決して返す。
//   現単価が一切無ければ Found=false (フロントは従来どおり手入力)。
//   注: snapshot 書込は従来どおりクライアント入力値を verbatim 保存する (本サジェストは入力補助のみで、
//   サーバ側で snapshot を上書きしない = 下位互換。「単価未決定」状態 unit_price<=0 も維持される)。
// ─────────────────────────────────────────────────
public record SupplierPriceSuggestion(
    bool Found,
    decimal? UnitPrice,
    string? CurrencyCode,
    decimal? ExchangeRate,
    // 解決に使われた行が size 専用か全サイズ既定か (UI 表示・デバッグ用)。
    long? ResolvedSizeId,
    bool IsSizeSpecific);
