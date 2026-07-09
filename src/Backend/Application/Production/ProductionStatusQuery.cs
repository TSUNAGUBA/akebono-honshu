using Akebono.Application.Common;
using Akebono.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Production;

public record ProductionStatusRow(
    Guid FamilyId,
    string Sku9,
    string ProductName,
    string MaterialOrder,          // done / undone
    string ProductionInstruction,  // done / undone
    bool BomRegistered);

/// <summary>
/// 品番ごと「未/済」2軸バッジの派生算出 (Phase 5 §PS-01、data-design §7.2)。
/// denormalized 列を持たず SoT (生産指示・素材発注レコード) を直読。
/// 全品番に対し集合クエリ3本 (各 部分インデックス利用) で N+1 を回避。
/// </summary>
public class ProductionStatusQuery(IAkebonoDbContext db)
{
    /// <summary>
    /// 有効な全品番の生産手配ステータスを返す。
    /// materialOrderState / productionInstructionState / bomState は "done"/"undone"/"unregistered" でフィルタ (null=絞らない)。
    /// </summary>
    public async Task<List<ProductionStatusRow>> ListAsync(
        string? materialOrderState, string? productionInstructionState, bool? bomRegistered,
        CancellationToken ct = default)
    {
        // 品番 (有効) 一覧 + 代表 SKU
        var families = await db.ProductFamilies
            .Where(f => f.DeletedAt == null)
            .Select(f => new
            {
                f.Id,
                f.ProductName1,
                FirstSku = db.Products.Where(p => p.ProductFamilyId == f.Id && p.DeletedAt == null)
                    .OrderBy(p => p.Sku).Select(p => p.Sku).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // 生産指示=済 (Issued/Completed)
        var piDone = (await db.ProductionInstructions
            .Where(p => p.DeletedAt == null && (p.Status == ProductionInstructionStatus.Issued || p.Status == ProductionInstructionStatus.Completed))
            .Select(p => p.ProductFamilyId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        // 素材発注=済 (明細の由来品番 × Ordered)
        var moDone = (await db.MaterialOrderLines
            .Where(l => l.ProductFamilyId != null && l.MaterialOrder!.DeletedAt == null && l.MaterialOrder.Status == MaterialOrderStatus.Ordered)
            .Select(l => l.ProductFamilyId!.Value)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        // BOM 登録済
        var bomSet = (await db.ProductMaterials
            .Where(m => m.DeletedAt == null)
            .Select(m => m.ProductFamilyId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var rows = families.Select(f => new ProductionStatusRow(
            f.Id,
            Sku9(f.FirstSku),
            f.ProductName1,
            moDone.Contains(f.Id) ? "done" : "undone",
            piDone.Contains(f.Id) ? "done" : "undone",
            bomSet.Contains(f.Id)));

        if (!string.IsNullOrEmpty(materialOrderState))
            rows = rows.Where(r => r.MaterialOrder == materialOrderState);
        if (!string.IsNullOrEmpty(productionInstructionState))
            rows = rows.Where(r => r.ProductionInstruction == productionInstructionState);
        if (bomRegistered is { } b)
            rows = rows.Where(r => r.BomRegistered == b);

        return rows.OrderBy(r => r.Sku9).ToList();
    }

    private static string Sku9(string? sku)
        => string.IsNullOrEmpty(sku) ? "" : (sku.Length >= 9 ? sku[..9] : sku);
}
