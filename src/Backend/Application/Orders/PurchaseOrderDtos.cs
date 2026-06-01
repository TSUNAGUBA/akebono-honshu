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
    List<OrderLineInput> Lines);

public record OrderLineInput(
    long ProductId,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot);

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
    DateTime UpdatedAt);

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
    List<OrderLineDetail> Lines);

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
    decimal Subtotal);

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
    List<UpdateLineInput> Lines);

public record UpdateLineInput(
    long? Id,
    long ProductId,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot);

// ─────────────────────────────────────────────────
// 中止 (POST /api/v1/orders/{id}/cancel、O-05)
// ─────────────────────────────────────────────────
public record CancelOrderRequest(string CancelReason);

// ─────────────────────────────────────────────────
// 連絡文章 (O-07、テンプレ複写)
// ─────────────────────────────────────────────────
public record CommunicationTextSuggestion(string Body, bool StandardPrintFlag, string SourceLabel);
