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
public class MaterialOrderService(IAkebonoDbContext db, IAuditLogger audit, ProductMaterialService bom, ITenantContext tenantContext)
{
    private const int MaxNumberingRetries = 3;

    /// <summary>
    /// BOM 展開→仕入先別ドラフト提案 (MO-01 prepare、副作用なし)。
    /// 生産指示 id 指定時はその品番＋生産数量を、直接指定時は family＋quantity を使用。
    /// </summary>
    public async Task<MaterialRequirements> PrepareAsync(PrepareMaterialOrderRequest req, CancellationToken ct = default)
    {
        Guid familyId;
        int quantity;
        if (req.ProductionInstructionId is { } piId)
        {
            var pi = await db.ProductionInstructions.FirstOrDefaultAsync(p => p.Id == piId && p.DeletedAt == null, ct)
                ?? throw DomainException.Validation("生産指示が存在しません");
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
            throw DomainException.Validation("生産指示 id、または品番＋数量を指定してください");
        }

        return await bom.GetRequirementsAsync(familyId, quantity, ct);
    }

    /// <summary>新規作成 (MO-01)。素材仕入先1社あて。status=Draft。素材名スナップショット。</summary>
    public async Task<MaterialOrder> CreateAsync(
        CreateMaterialOrderRequest req, Guid actorUserId,
        string? idempotencyKey = null, string? idempotencyPayloadHash = null,
        CancellationToken ct = default)
    {
        // Idempotency-Key (AKB-DOC-12 §8): 同一キー・同一ペイロードの再送は初回結果を再返却。
        if (idempotencyKey is not null)
        {
            var replayed = await FindByIdempotencyKeyAsync(idempotencyKey, idempotencyPayloadHash, ct);
            if (replayed is not null) return replayed;
        }

        if (req.Lines.Count == 0)
            throw DomainException.Validation("明細を 1 件以上指定してください");
        if (req.Lines.Any(l => l.RequiredQuantity <= 0 || (l.UnitPrice.HasValue && l.UnitPrice.Value < 0)))
            throw DomainException.Validation("数量は正、単価は 0 以上を指定してください");

        var materialIds = req.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.Materials.Where(m => materialIds.Contains(m.Id)).ToListAsync(ct);
        if (materials.Count != materialIds.Count)
            throw DomainException.Validation("指定された素材の一部が存在しません");
        var materialById = materials.ToDictionary(m => m.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.UtcNow;
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
                IdempotencyKey = idempotencyKey,
                IdempotencyPayloadHash = idempotencyPayloadHash,
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

    /// <summary>一覧 (MO-02)。キーセットページング (AKB-DOC-12 §7.1): (created_at, id) 降順の安定ソート。</summary>
    public async Task<PagedResult<MaterialOrderListItem>> ListAsync(
        Guid actorUserId, bool includeCancelled, PageRequest page, CancellationToken ct = default)
    {
        var query = db.MaterialOrders
            .Include(o => o.MaterialSupplier)
            .Where(o => o.DeletedAt == null);
        if (!includeCancelled)
            query = query.Where(o => o.Status != MaterialOrderStatus.Cancelled);

        // キーセット: 前ページ末尾 (created_at, id) より「後ろ」(降順) の行のみ。
        // Guid.CompareTo は Npgsql EF が uuid 比較へ翻訳する (実機検証済)。
        if (page.AfterCreatedAt is { } afterCreatedAt && page.AfterId is { } afterId)
            query = query.Where(o => o.CreatedAt < afterCreatedAt
                || (o.CreatedAt == afterCreatedAt && o.Id.CompareTo(afterId) < 0));

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new
            {
                O = o,
                LineCount = o.Lines.Count,
                Total = o.Lines.Sum(l => (decimal?)l.Subtotal) ?? 0m,
                Currency = o.Lines.Select(l => l.CurrencyCode).FirstOrDefault() ?? "JPY",
            })
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        // limit+1 件目が取れたら次ページあり。返却は limit 件に切り詰める。
        var hasMore = rows.Count > page.Limit;
        if (hasMore) rows.RemoveAt(page.Limit);

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

        var last = rows.Count > 0 ? rows[^1].O : null;
        return new PagedResult<MaterialOrderListItem>(
            result, hasMore, hasMore ? last?.CreatedAt : null, hasMore ? last?.Id : null);
    }

    /// <summary>詳細 (MO-03)。素材単価を含むため開示を監査 (金額マスク)。</summary>
    public async Task<MaterialOrderDetail?> GetDetailAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
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
    public async Task<MaterialOrder?> UpdateAsync(Guid id, UpdateMaterialOrderRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == id && o.DeletedAt == null, ct);
        if (order is null) return null;
        if (order.Status != MaterialOrderStatus.Draft)
            throw new DomainException(AkbErrorCodes.MakerPurchaseOrderStateViolation, 409,
                "下書きの素材発注のみ編集できます", "発注の状態を確認し許可された操作を選択してください");
        if (req.Lines.Count == 0 || req.Lines.Any(l => l.RequiredQuantity <= 0 || (l.UnitPrice.HasValue && l.UnitPrice.Value < 0)))
            throw DomainException.Validation("数量は正、単価は 0 以上を指定してください");

        var materialIds = req.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.Materials.Where(m => materialIds.Contains(m.Id)).ToListAsync(ct);
        if (materials.Count != materialIds.Count)
            throw DomainException.Validation("指定された素材の一部が存在しません");
        var materialById = materials.ToDictionary(m => m.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.UtcNow;
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
    public async Task<bool> OrderAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == id && o.DeletedAt == null, ct);
        if (order is null) return false;
        if (order.Status == MaterialOrderStatus.Cancelled)
            throw new DomainException(AkbErrorCodes.MakerPurchaseOrderStateViolation, 409,
                "中止済の素材発注は確定できません", "発注の状態を確認し許可された操作を選択してください");
        if (order.Status == MaterialOrderStatus.Ordered) return true; // 冪等

        var now = SystemTime.UtcNow;
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
    public async Task<bool> CancelAsync(Guid id, CancelMaterialOrderRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == id && o.DeletedAt == null, ct);
        if (order is null) return false;
        if (order.Status == MaterialOrderStatus.Cancelled) return true; // 冪等

        var now = SystemTime.UtcNow;
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

    /// <summary>
    /// Idempotency-Key による既存行の引当 (AKB-DOC-12 §8)。
    /// 同一キー・同一ペイロードは既存行 (初回結果) を返し、異なるペイロードは AKB-SYS-005。
    /// 同時二重送信は UNIQUE(tenant_id, idempotency_key) が最終防壁 (23505 → AKB-SYS-007)。
    /// </summary>
    private async Task<MaterialOrder?> FindByIdempotencyKeyAsync(
        string idempotencyKey, string? payloadHash, CancellationToken ct)
    {
        var existing = await db.MaterialOrders
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, ct);
        if (existing is null) return null;
        if (existing.IdempotencyPayloadHash == payloadHash) return existing;
        throw new DomainException(AkbErrorCodes.SysIdempotencyKeyConflict, 409,
            "同一の Idempotency-Key が異なる内容のリクエストで再利用されました",
            userAction: "新しい Idempotency-Key を生成して再実行してください");
    }

    /// <summary>order_no 採番 (YY-MO-NNNNN)。トランザクション内で advisory lock を取得。
    /// ロックキーは tenant_id を含む (AKB-DOC-05 採番フロー: テナント間で直列化を分離)。</summary>
    private async Task<string> GenerateOrderNoAsync(CancellationToken ct)
    {
        var year2 = SystemTime.JstNow.Year % 100; // 採番の年度判定は業務時刻 (JST)
        var prefix = $"{year2:D2}-MO-";
        var lockKey = $"{tenantContext.RequireTenantId()}:MO-{year2:D2}";
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
        throw new DomainException(AkbErrorCodes.MakerNumberingConflict, 409,
            "素材発注番号の採番に失敗しました。再試行してください", "時間をおいて再試行してください");
    }
}
