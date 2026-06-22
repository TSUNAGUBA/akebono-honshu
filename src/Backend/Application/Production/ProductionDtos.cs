namespace Akebono.Application.Production;

// ═══════════════════════════════════════════════════
// 生産指示書 (PI-01〜04)
// ═══════════════════════════════════════════════════
public record CreatePiRequest(
    long ProductFamilyId,
    long FactorySupplierId,
    DateOnly DueDate,
    string? CommunicationText,
    List<PiLineInput> Lines);

public record PiLineInput(long ProductId, int Quantity);

public record UpdatePiRequest(
    long FactorySupplierId,
    DateOnly DueDate,
    string? CommunicationText,
    List<PiLineInput> Lines);

public record CancelPiRequest(string Reason);

public record PiListItem(
    long Id,
    string InstructionNo,
    string ProductSku9,
    string ProductName,
    string FactoryCode,
    string FactoryName,
    int PlannedQuantity,
    DateOnly DueDate,
    short Status,
    string ExportState,        // unexported / exported
    DateTime? FirstExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record PiDetail(
    long Id,
    string InstructionNo,
    long ProductFamilyId,
    string ProductSku9,
    string ProductName,
    long FactorySupplierId,
    string FactoryCode,
    string FactoryName,
    int PlannedQuantity,
    DateOnly DueDate,
    short Status,
    DateTime? InstructedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    string? CommunicationText,
    DateTime? FirstExportedAt,
    DateTime? LastExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<PiLineDetail> Lines);

public record PiLineDetail(
    long Id,
    short LineNo,
    long ProductId,
    string Sku,
    string ProductName,
    string ColorName,
    string SizeName,
    int Quantity);

// ═══════════════════════════════════════════════════
// 素材発注書 (MO-01〜04)
// ═══════════════════════════════════════════════════
public record PrepareMaterialOrderRequest(
    long? ProductionInstructionId,
    long? ProductFamilyId,
    int? Quantity);

public record CreateMaterialOrderRequest(
    long MaterialSupplierId,
    long? ProductionInstructionId,
    DateOnly DueDate,
    string? CommunicationText,
    List<MaterialOrderLineInput> Lines);

public record MaterialOrderLineInput(
    long MaterialId,
    long? ProductFamilyId,
    long? SourcePiLineId,
    decimal RequiredQuantity,
    string Unit,
    decimal? UnitPrice,
    string CurrencyCode);

public record UpdateMaterialOrderRequest(
    DateOnly DueDate,
    string? CommunicationText,
    List<MaterialOrderLineInput> Lines);

public record CancelMaterialOrderRequest(string Reason);

public record MaterialOrderListItem(
    long Id,
    string OrderNo,
    long MaterialSupplierId,
    string MaterialSupplierCode,
    string MaterialSupplierName,
    long? ProductionInstructionId,
    DateOnly DueDate,
    short Status,
    int LineCount,
    // 素材単価(機密 中-高)。現状は認証済ユーザに開示 (既存仕入単価と同水準)。開示は MaterialPrice.View
    // で監査記録 (金額マスク)。price 権限ゲート/一覧デフォルトマスクは専用権限カラム未整備のため段階C へ繰延
    decimal TotalAmount,
    string CurrencyCode,
    string ExportState,
    DateTime? FirstExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record MaterialOrderDetail(
    long Id,
    string OrderNo,
    long MaterialSupplierId,
    string MaterialSupplierCode,
    string MaterialSupplierName,
    string? SupplierOfficialNameSnapshot,
    string? SupplierCodeSnapshot,
    long? ProductionInstructionId,
    string? ProductionInstructionNo,
    DateOnly DueDate,
    short Status,
    DateTime? InstructedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    string? CommunicationText,
    DateTime? FirstExportedAt,
    DateTime? LastExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<MaterialOrderLineDetail> Lines);

public record MaterialOrderLineDetail(
    long Id,
    short LineNo,
    long MaterialId,
    string MaterialName,
    long? ProductFamilyId,
    decimal RequiredQuantity,
    string Unit,
    decimal? UnitPrice,        // 機密。開示は GetDetailAsync で MaterialPrice.View 監査 (金額マスク)。上部 TotalAmount 注記参照
    string CurrencyCode,
    decimal Subtotal);
