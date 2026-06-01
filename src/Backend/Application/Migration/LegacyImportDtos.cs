namespace Akebono.Application.Migration;

/// <summary>
/// MIG-3 既存 CSV 取込結果 (画面 /admin/legacy-import で表示)。
/// Phase 7 Iteration 4 Hardening MIG-3。
/// </summary>
public sealed record LegacyImportResult(
    LegacyImportPhaseResult PrePatch,
    LegacyImportPhaseResult MasterFill,
    LegacyImportStagingResult Staging,
    LegacyImportImportResult Import,
    LegacyImportFallback Fallbacks,
    IReadOnlyList<string> Warnings,
    TimeSpan Elapsed);

public sealed record LegacyImportPhaseResult(bool Applied, string Detail);

public sealed record LegacyImportStagingResult(
    int RowsLoaded,
    int DistinctFamilyCount,
    int RowsWithCostPrice);

public sealed record LegacyImportImportResult(
    int ProductFamiliesInserted,
    int ProductsInserted,
    int SupplierPricesInserted);

public sealed record LegacyImportFallback(
    int ColorFallback,
    int SizeFallback,
    int SupplierFallback);
