using Akebono.Application.Common;
using Akebono.Domain.Common;
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
            throw DomainException.Validation("単価は 0 より大きい値を指定してください");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 既存の現在有効レコード (EffectiveTo IS NULL) の効力終了。
            // PR2: size 次元込みで履歴を維持するため、同一 size バケットの現行行のみをクローズする
            // (size 専用単価の新設で全サイズ既定をクローズしない / 逆も同様)。
            // 注: EF Core で `p.SizeId == req.SizeId` は NULL を一致させない (size_id = @p)。
            // req.SizeId が NULL のときは size_id IS NULL に翻訳させるため三項演算子で分岐する。
            var current = await db.ProductSupplierPrices
                .Where(p => p.ProductFamilyId == familyId
                         && p.SupplierId == req.SupplierId
                         && (req.SizeId == null ? p.SizeId == null : p.SizeId == req.SizeId)
                         && p.EffectiveTo == null
                         && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            var now = SystemTime.UtcNow;
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
                // サイズ別仕入単価 (PR2)。NULL = 全サイズ共通の既定単価。
                SizeId = req.SizeId,
                UnitPrice = req.UnitPrice,
                CurrencyCode = req.CurrencyCode,
                ExchangeRate = req.ExchangeRate,
                // 旧 仕入コスト計算明細 項目 (Phase C、任意)
                EstimateUnitPrice = req.EstimateUnitPrice,
                EstimateReceivedDate = req.EstimateReceivedDate,
                EstimateCost = req.EstimateCost,
                EstimateMarginRate = req.EstimateMarginRate,
                PurchaseCost = req.PurchaseCost,
                PurchaseMarginRate = req.PurchaseMarginRate,
                LossCost = req.LossCost,
                DrayageCost = req.DrayageCost,
                TaxRate = req.TaxRate,
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
                note: $"family={familyId}, supplier={req.SupplierId}, size={req.SizeId?.ToString() ?? "all"}, price=***, effective_from={req.EffectiveFrom}",
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
            .Include(p => p.Size)  // PR2: サイズ別単価の表示名解決 (NULL-size 既定行では null)
            .Where(p => p.ProductFamilyId == familyId && !p.IsDeleted)
            .OrderByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "ProductSupplierPrice.List",
            entityType: "ProductSupplierPrice",
            note: $"family={familyId}, count={items.Count}",
            cancellationToken: ct);

        return items;
    }

    /// <summary>
    /// サイズ対応の現単価解決 (PR2)。あるサイズについて
    /// 「(family, supplier, そのsize) の現在有効行があればそれを、無ければ (…, NULL-size 既定) の
    /// 現在有効行」というフォールバックで現単価行を 1 件返す (どちらも無ければ null)。
    /// サイズ別単価が一切無い商品では NULL-size 既定のみが対象となり従来と同じ結果になる。
    ///
    /// 注: snapshot 書込はクライアント入力を verbatim 保存する方針 (下位互換) のため、本メソッドは
    /// 入力補助 (サジェスト) の解決にのみ使う。発注の unit_price_snapshot をサーバ側で上書きしない。
    /// </summary>
    public async Task<ProductSupplierPrice?> ResolveCurrentPriceAsync(
        long familyId, long supplierId, long? sizeId, CancellationToken ct = default)
    {
        // 1) そのサイズ専用の現在有効行 (sizeId が指定されている場合のみ)。
        if (sizeId is not null)
        {
            var sizeSpecific = await db.ProductSupplierPrices
                .Where(p => p.ProductFamilyId == familyId
                         && p.SupplierId == supplierId
                         && p.SizeId == sizeId   // 非NULL 指定なので size_id = @p で正しく一致
                         && p.EffectiveTo == null
                         && !p.IsDeleted)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync(ct);
            if (sizeSpecific is not null) return sizeSpecific;
        }

        // 2) フォールバック: 全サイズ共通の既定行 (size_id IS NULL)。
        return await db.ProductSupplierPrices
            .Where(p => p.ProductFamilyId == familyId
                     && p.SupplierId == supplierId
                     && p.SizeId == null
                     && p.EffectiveTo == null
                     && !p.IsDeleted)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }
}
