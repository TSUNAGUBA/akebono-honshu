using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Products;

/// <summary>
/// BOM (素材構成・所要量) Application サービス (Phase 5 §B-01/B-02)。
/// BOM が所要量 SoT。ProductFamily の3FK代表素材 (upper/insole/outsole_material_id) へは
/// <b>書き戻さない</b> (疎結合、data-design §7.1 / 監査 M-3)。
/// BOM 未登録時は3FKから初期シードを返し、編集の出発点とする (フロント側で利用)。
/// </summary>
public class ProductMaterialService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>BOM 一覧取得 (B-01)。登録済が無い場合は空リスト (3FK初期シードはフロントで構築)。</summary>
    public async Task<List<ProductMaterialItem>> GetAsync(long familyId, CancellationToken ct = default)
    {
        return await db.ProductMaterials
            .Include(m => m.Material)
            .Include(m => m.RecommendedSupplier)
            .Where(m => m.ProductFamilyId == familyId && !m.IsDeleted)
            .OrderBy(m => m.MaterialRole).ThenBy(m => m.Id)
            .Select(m => new ProductMaterialItem(
                m.Id, (short)m.MaterialRole, m.MaterialId, m.Material!.Name,
                m.RequiredQtyPerUnit, m.Unit,
                m.RecommendedSupplierId, m.RecommendedSupplier != null ? m.RecommendedSupplier.Name : null,
                m.LossRate, m.Remark))
            .ToListAsync(ct);
    }

    /// <summary>
    /// BOM 一括更新 (B-01)。既存の有効行を論理削除 → 新規 INSERT を単一トランザクションで実施。
    /// 3FK へは書き戻さない。重複 (部位×素材) と所要量を検証。
    /// </summary>
    public async Task ReplaceAsync(long familyId, ReplaceBomRequest req, long actorUserId, CancellationToken ct = default)
    {
        var family = await db.ProductFamilies.FirstOrDefaultAsync(f => f.Id == familyId, ct)
            ?? throw new ArgumentException($"品番 family_id={familyId} 不在");

        // バリデーション
        foreach (var m in req.Materials)
        {
            if (m.RequiredQtyPerUnit <= 0 || string.IsNullOrWhiteSpace(m.Unit))
                throw new ArgumentException("所要量は正の数、単位は必須です (BOM-001)");
            if (m.MaterialRole is < 0 or > 4)
                throw new ArgumentException("部位区分が不正です (BOM-001)");
            if (m.LossRate is < 0 or >= 1)
                throw new ArgumentException("ロス率は 0 以上 1 未満です (BOM-001)");
        }
        var dupKey = req.Materials
            .GroupBy(m => (m.MaterialRole, m.MaterialId))
            .FirstOrDefault(g => g.Count() > 1);
        if (dupKey is not null)
            throw new InvalidOperationException("同一部位に同一素材が重複しています (BOM-002)");

        // FK 存在検証 (MaterialOrderService.CreateAsync と対称。移行データ等の materialId=0/不在を
        // DB の FK 制約違反 500 ではなく 422 で弾く)
        var materialIds = req.Materials.Select(m => m.MaterialId).Distinct().ToList();
        var existCount = await db.Materials.CountAsync(m => materialIds.Contains(m.Id), ct);
        if (existCount != materialIds.Count)
            throw new ArgumentException("指定された素材の一部が存在しません (BOM-001)");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.Now;
            var existing = await db.ProductMaterials
                .Where(m => m.ProductFamilyId == familyId && !m.IsDeleted)
                .ToListAsync(ct);
            foreach (var e in existing)
            {
                e.IsDeleted = true;
                e.UpdatedAt = now;
                e.UpdatedByUserId = actorUserId;
            }
            await db.SaveChangesAsync(ct);

            foreach (var m in req.Materials)
            {
                db.ProductMaterials.Add(new ProductMaterial
                {
                    ProductFamilyId = familyId,
                    MaterialRole = (MaterialRole)m.MaterialRole,
                    MaterialId = m.MaterialId,
                    RequiredQtyPerUnit = m.RequiredQtyPerUnit,
                    Unit = m.Unit,
                    RecommendedSupplierId = m.RecommendedSupplierId,
                    LossRate = m.LossRate,
                    Remark = m.Remark,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "ProductMaterial.Update",
                entityType: "ProductFamily", entityId: familyId,
                note: $"bom_lines={req.Materials.Count}", cancellationToken: ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 所要量展開 (B-02)。BOM × quantity で素材別に Σ(所要量×数量×(1+loss_rate)) を集計、
    /// 推奨仕入先別にグルーピング。金額は含まない。BOM 未登録時は ArgumentException (MORD-001)。
    /// </summary>
    public async Task<MaterialRequirements> GetRequirementsAsync(long familyId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0)
            throw new ArgumentException("数量は正の数を指定してください (MORD-002)");

        var bom = await db.ProductMaterials
            .Include(m => m.Material)
            .Include(m => m.RecommendedSupplier)
            .Where(m => m.ProductFamilyId == familyId && !m.IsDeleted)
            .ToListAsync(ct);
        if (bom.Count == 0)
            throw new ArgumentException("素材構成 (BOM) が未登録です。先に BOM を登録してください (MORD-001)");

        var groups = bom
            .GroupBy(m => (m.RecommendedSupplierId, SupplierName: m.RecommendedSupplier?.Name))
            .Select(g => new MaterialRequirementGroup(
                g.Key.RecommendedSupplierId,
                g.Key.SupplierName,
                g.Select(m => new MaterialRequirementLine(
                    m.MaterialId, m.Material!.Name, (short)m.MaterialRole,
                    decimal.Round(m.RequiredQtyPerUnit * quantity * (1 + m.LossRate), 4),
                    m.Unit)).ToList()))
            .OrderBy(g => g.RecommendedSupplierId ?? long.MaxValue)
            .ToList();

        return new MaterialRequirements(familyId, quantity, groups);
    }
}
