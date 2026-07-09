namespace Akebono.Application.Products;

// ─────────────────────────────────────────────────
// BOM 一覧 (GET /products/families/{id}/materials、B-01)
// ─────────────────────────────────────────────────
public record ProductMaterialItem(
    Guid Id,
    short MaterialRole,
    Guid MaterialId,
    string MaterialName,
    decimal RequiredQtyPerUnit,
    string Unit,
    Guid? RecommendedSupplierId,
    string? RecommendedSupplierName,
    decimal LossRate,
    string? Remark);

// ─────────────────────────────────────────────────
// BOM 一括更新 (PUT /products/families/{id}/materials、B-01)
// ─────────────────────────────────────────────────
public record ReplaceBomRequest(List<BomLineInput> Materials);

public record BomLineInput(
    short MaterialRole,
    Guid MaterialId,
    decimal RequiredQtyPerUnit,
    string Unit,
    Guid? RecommendedSupplierId,
    decimal LossRate,
    string? Remark);

// ─────────────────────────────────────────────────
// 所要量展開 (GET /products/families/{id}/material-requirements、B-02)
// 金額は含まない (純粋に数量のみ)
// ─────────────────────────────────────────────────
public record MaterialRequirements(
    Guid FamilyId,
    int TotalQuantity,
    List<MaterialRequirementGroup> Groups);

public record MaterialRequirementGroup(
    Guid? RecommendedSupplierId,
    string? RecommendedSupplierName,
    List<MaterialRequirementLine> Lines);

public record MaterialRequirementLine(
    Guid MaterialId,
    string MaterialName,
    short MaterialRole,
    decimal RequiredQuantity,
    string Unit);
