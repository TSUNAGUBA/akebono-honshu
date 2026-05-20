using Akebono.Application.Common;
using Akebono.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Products;

/// <summary>
/// マルチ仕入先単価 (Phase 5 §4.4)。BR-04 履歴管理:
/// 新単価設定時は、同一企画 × 同一仕入先の旧レコード (EffectiveTo IS NULL) を
/// effective_to = 新単価.effective_from - 1day で UPDATE → 新レコード INSERT を
/// 1 トランザクション内で実施。
///
/// 機密度 中-高 (NFR §6.2): 監査ログには金額本体ではなくマスク "***" のみ記録。
/// </summary>
public class ProductSupplierPriceService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>新単価を追加 (BR-04 履歴管理)。旧単価の effective_to を自動更新。</summary>
    public async Task<ProductSupplierPrice> AddAsync(
        long familyId,
        AddSupplierPriceRequest req,
        long actorUserId,
        CancellationToken ct = default)
    {
        if (req.UnitPrice <= 0)
            throw new ArgumentException("単価は 0 より大きい値を指定してください (PRICE-002)");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 既存の現在有効レコード (EffectiveTo IS NULL) の効力終了
            var current = await db.ProductSupplierPrices
                .Where(p => p.ProductFamilyId == familyId
                         && p.SupplierId == req.SupplierId
                         && p.EffectiveTo == null
                         && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            var now = DateTime.UtcNow;
            if (current is not null)
            {
                current.EffectiveTo = req.EffectiveFrom.AddDays(-1);
                current.UpdatedAt = now;
                current.UpdatedByUserId = actorUserId;
            }

            var entity = new ProductSupplierPrice
            {
                ProductFamilyId = familyId,
                SupplierId = req.SupplierId,
                UnitPrice = req.UnitPrice,
                CurrencyCode = req.CurrencyCode,
                ExchangeRate = req.ExchangeRate,
                EffectiveFrom = req.EffectiveFrom,
                EffectiveTo = null,
                DecidedAt = req.DecidedAt,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            };
            db.ProductSupplierPrices.Add(entity);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "ProductSupplierPrice.Add",
                entityType: "ProductSupplierPrice", entityId: entity.Id,
                note: $"family={familyId}, supplier={req.SupplierId}, price=***, effective_from={req.EffectiveFrom}",
                cancellationToken: ct);

            return entity;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>履歴を含む単価一覧 (現在 + 過去)。</summary>
    public async Task<List<ProductSupplierPrice>> ListHistoryAsync(
        long familyId, long actorUserId, CancellationToken ct = default)
    {
        var items = await db.ProductSupplierPrices
            .Include(p => p.Supplier)
            .Where(p => p.ProductFamilyId == familyId && !p.IsDeleted)
            .OrderByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "ProductSupplierPrice.List",
            entityType: "ProductSupplierPrice",
            note: $"family={familyId}, count={items.Count}",
            cancellationToken: ct);

        return items;
    }
}
