using Akebono.Application.Common;
using Akebono.Application.Products;
using Akebono.Domain.Common;
using Akebono.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Production;

/// <summary>
/// 素材発注書 (生地材料発注) Application サービス (Phase 5 §MO-01〜04)。
/// 既存の完成品発注 (PurchaseOrderService) とは別系統。
/// BOM 展開は ProductMaterialService を再利用。採番 (YY-MO-NNNNN) は advisory lock で直列化。
/// 素材単価は機密度 中-高: 既存仕入単価と同方式 (監査ログには金額を残さずマスク。書込権限は
/// purchase_order_create_permission で制御 = endpoint 側 CheckOrderEditAsync)。
/// </summary>
public class MaterialOrderService(IAkebonoDbContext db, IAuditLogger audit, ProductMaterialService bom)
{
    private const int MaxNumberingRetries = 3;

    /// <summary>
    /// BOM 展開→仕入先別ドラフト提案 (MO-01 prepare、副作用なし)。
    /// 生産指示 id 指定時はその品番＋生産数量を、直接指定時は family＋quantity を使用。
    /// </summary>
    public async Task<MaterialRequirements> PrepareAsync(PrepareMaterialOrderRequest req, CancellationToken ct = default)
    {
        long familyId;
        int quantity;
        if (req.ProductionInstructionId is { } piId)
        {
            var pi = await db.ProductionInstructions.FirstOrDefaultAsync(p => p.Id == piId && !p.IsDeleted, ct)
                ?? throw new ArgumentException("生産指示が存在しません (PINST-003)");
            familyId = pi.ProductFamilyId;
            quantity = pi.PlannedQuantity;
        }
        else if (req.ProductFamilyId is { } fid && req.Quantity is { } q)
        {
            familyId = fid;
            quantity = q;
        }
        else
        {
            throw new ArgumentException("生産指示 id、または品番＋数量を指定してください (MORD-002)");
        }

        return await bom.GetRequirementsAsync(familyId, quantity, ct);
    }

    /// <summary>新規作成 (MO-01)。素材仕入先1社あて。status=Draft。素材名スナップショット。</summary>
    public async Task<MaterialOrder> CreateAsync(CreateMaterialOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        if (req.Lines.Count == 0)
            throw new ArgumentException("明細を 1 件以上指定してください (MORD-002)");
        if (req.Lines.Any(l => l.RequiredQuantity <= 0 || (l.UnitPrice.HasValue && l.UnitPrice.Value < 0)))
            throw new ArgumentException("数量は正、単価は 0 以上を指定してください (MORD-002)");

        var materialIds = req.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.Materials.Where(m => materialIds.Contains(m.Id)).ToListAsync(ct);
        if (materials.Count != materialIds.Count)
            throw new ArgumentException("指定された素材の一部が存在しません (MORD-002)");
        var materialById = materials.ToDictionary(m => m.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.Now;
            var orderNo = await GenerateOrderNoAsync(ct);
            var order = new MaterialOrder
            {
                OrderNo = orderNo,
                MaterialSupplierId = req.MaterialSupplierId,
                ProductionInstructionId = req.ProductionInstructionId,
                DueDate = req.DueDate,
                Status = MaterialOrderStatus.Draft,
                CommunicationText = req.CommunicationText,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            };
            db.MaterialOrders.Add(order);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            foreach (var l in req.Lines)
            {
                db.MaterialOrderLines.Add(new MaterialOrderLine
                {
                    MaterialOrderId = order.Id,
                    LineNo = lineNo++,
                    MaterialId = l.MaterialId,
                    MaterialNameSnapshot = materialById[l.MaterialId].Name,
                    ProductFamilyId = l.ProductFamilyId,
                    SourcePiLineId = l.SourcePiLineId,
                    RequiredQuantity = l.RequiredQuantity,
                    Unit = l.Unit,
                    UnitPrice = l.UnitPrice,
                    CurrencyCode = string.IsNullOrEmpty(l.CurrencyCode) ? "JPY" : l.CurrencyCode,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // 機密配慮: 金額はマスク
            await audit.LogAsync(actorUserId, "MaterialOrder.Create",
                entityType: "MaterialOrder", entityId: order.Id,
                note: $"order_no={order.OrderNo}, lines={req.Lines.Count}, total=***", cancellationToken: ct);
            return order;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>一覧 (MO-02)。</summary>
    public async Task<List<MaterialOrderListItem>> ListAsync(long actorUserId, bool includeCancelled, CancellationToken ct = default)
    {
        var query = db.MaterialOrders
            .Include(o => o.MaterialSupplier)
            .Where(o => !o.IsDeleted);
        if (!includeCancelled)
            query = query.Where(o => o.Status != MaterialOrderStatus.Cancelled);

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                O = o,
                LineCount = o.Lines.Count,
                Total = o.Lines.Sum(l => (decimal?)l.Subtotal) ?? 0m,
                Currency = o.Lines.Select(l => l.CurrencyCode).FirstOrDefault() ?? "JPY",
            })
            .ToListAsync(ct);

        var result = rows.Select(x => new MaterialOrderListItem(
            x.O.Id, x.O.OrderNo, x.O.MaterialSupplierId,
            x.O.MaterialSupplier?.Code ?? "?", x.O.MaterialSupplier?.Name ?? "?",
            x.O.ProductionInstructionId, x.O.DueDate, (short)x.O.Status,
            x.LineCount, x.Total, x.Currency,
            x.O.FirstExportedAt is null ? "unexported" : "exported",
            x.O.FirstExportedAt, x.O.CreatedAt, x.O.UpdatedAt)).ToList();

        // 機密(素材単価=金額)を含む一覧の開示を監査 (金額はマスク)
        await audit.LogAsync(actorUserId, "MaterialPrice.View",
            entityType: "MaterialOrder", note: $"list count={result.Count}, total=***", cancellationToken: ct);
        return result;
    }

    /// <summary>詳細 (MO-03)。素材単価を含むため開示を監査 (金額マスク)。</summary>
    public async Task<MaterialOrderDetail?> GetDetailAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders
            .Include(o => o.MaterialSupplier)
            .Include(o => o.ProductionInstruction)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        var lines = await db.MaterialOrderLines
            .Include(l => l.Material)
            .Where(l => l.MaterialOrderId == id)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "MaterialPrice.View",
            entityType: "MaterialOrder", entityId: id, note: $"order_no={order.OrderNo}, total=***", cancellationToken: ct);

        return new MaterialOrderDetail(
            order.Id, order.OrderNo, order.MaterialSupplierId,
            order.MaterialSupplier?.Code ?? "?", order.MaterialSupplier?.Name ?? "?",
            order.SupplierOfficialNameSnapshot, order.SupplierCodeSnapshot,
            order.ProductionInstructionId, order.ProductionInstruction?.InstructionNo,
            order.DueDate, (short)order.Status, order.InstructedAt, order.CancelledAt, order.CancelReason,
            order.CommunicationText, order.FirstExportedAt, order.LastExportedAt, order.CreatedAt, order.UpdatedAt,
            lines.Select(l => new MaterialOrderLineDetail(
                l.Id, l.LineNo, l.MaterialId, l.Material?.Name ?? l.MaterialNameSnapshot,
                l.ProductFamilyId, l.RequiredQuantity, l.Unit, l.UnitPrice, l.CurrencyCode, l.Subtotal)).ToList());
    }

    /// <summary>編集 (MO-03)。Draft のみ全編集。明細は全削除→再INSERT。</summary>
    public async Task<MaterialOrder?> UpdateAsync(long id, UpdateMaterialOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, ct);
        if (order is null) return null;
        if (order.Status != MaterialOrderStatus.Draft)
            throw new InvalidOperationException("下書きの素材発注のみ編集できます (MORD-003)");
        if (req.Lines.Count == 0 || req.Lines.Any(l => l.RequiredQuantity <= 0 || (l.UnitPrice.HasValue && l.UnitPrice.Value < 0)))
            throw new ArgumentException("数量は正、単価は 0 以上を指定してください (MORD-002)");

        var materialIds = req.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.Materials.Where(m => materialIds.Contains(m.Id)).ToListAsync(ct);
        if (materials.Count != materialIds.Count)
            throw new ArgumentException("指定された素材の一部が存在しません (MORD-002)");
        var materialById = materials.ToDictionary(m => m.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.Now;
            order.DueDate = req.DueDate;
            order.CommunicationText = req.CommunicationText;
            order.UpdatedAt = now;
            order.UpdatedByUserId = actorUserId;

            var existing = await db.MaterialOrderLines.Where(l => l.MaterialOrderId == id).ToListAsync(ct);
            db.MaterialOrderLines.RemoveRange(existing);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            foreach (var l in req.Lines)
            {
                db.MaterialOrderLines.Add(new MaterialOrderLine
                {
                    MaterialOrderId = order.Id,
                    LineNo = lineNo++,
                    MaterialId = l.MaterialId,
                    MaterialNameSnapshot = materialById[l.MaterialId].Name,
                    ProductFamilyId = l.ProductFamilyId,
                    SourcePiLineId = l.SourcePiLineId,
                    RequiredQuantity = l.RequiredQuantity,
                    Unit = l.Unit,
                    UnitPrice = l.UnitPrice,
                    CurrencyCode = string.IsNullOrEmpty(l.CurrencyCode) ? "JPY" : l.CurrencyCode,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "MaterialOrder.Update",
                entityType: "MaterialOrder", entityId: id,
                note: $"order_no={order.OrderNo}, total=***", cancellationToken: ct);
            return order;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>発注確定 (MO-03)。Draft→Ordered、instructed_at SET。冪等。</summary>
    public async Task<bool> OrderAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, ct);
        if (order is null) return false;
        if (order.Status == MaterialOrderStatus.Cancelled)
            throw new InvalidOperationException("中止済の素材発注は確定できません (MORD-003)");
        if (order.Status == MaterialOrderStatus.Ordered) return true; // 冪等

        var now = SystemTime.Now;
        order.Status = MaterialOrderStatus.Ordered;
        order.InstructedAt = now;
        order.UpdatedAt = now;
        order.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "MaterialOrder.Order",
            entityType: "MaterialOrder", entityId: id,
            note: $"order_no={order.OrderNo}", cancellationToken: ct);
        return true;
    }

    /// <summary>中止 (MO-03)。status=Cancelled。物理削除しない。冪等。</summary>
    public async Task<bool> CancelAsync(long id, CancelMaterialOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, ct);
        if (order is null) return false;
        if (order.Status == MaterialOrderStatus.Cancelled) return true; // 冪等

        var now = SystemTime.Now;
        order.Status = MaterialOrderStatus.Cancelled;
        order.CancelledAt = now;
        order.CancelledByUserId = actorUserId;
        order.CancelReason = req.Reason;
        order.UpdatedAt = now;
        order.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "MaterialOrder.Cancel",
            entityType: "MaterialOrder", entityId: id,
            note: $"order_no={order.OrderNo}, reason={req.Reason}", cancellationToken: ct);
        return true;
    }

    /// <summary>order_no 採番 (YY-MO-NNNNN)。トランザクション内で advisory lock を取得。</summary>
    private async Task<string> GenerateOrderNoAsync(CancellationToken ct)
    {
        var year2 = SystemTime.Now.Year % 100;
        var prefix = $"{year2:D2}-MO-";
        var lockKey = $"MO-{year2:D2}";
        for (var attempt = 0; attempt < MaxNumberingRetries; attempt++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)", ct);
            var existing = await db.MaterialOrders
                .Where(o => o.OrderNo.StartsWith(prefix))
                .Select(o => o.OrderNo)
                .ToListAsync(ct);
            var maxSeq = existing
                .Select(s => s.Length > prefix.Length && int.TryParse(s.AsSpan(prefix.Length), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            var candidate = $"{prefix}{maxSeq + 1:D5}";
            if (!await db.MaterialOrders.AnyAsync(o => o.OrderNo == candidate, ct))
                return candidate;
        }
        throw new InvalidOperationException("素材発注番号の採番に失敗しました。再試行してください (MORD-004)");
    }
}
