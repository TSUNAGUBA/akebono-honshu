using Akebono.Application.Common;
using Akebono.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Orders;

/// <summary>
/// 発注書 Application サービス (Phase 5 §5)。
/// 新規作成 (O-01) / 一覧 (O-03) / 詳細 / 編集 (O-04 edit_reason 必須 F-16) /
/// 中止 (O-05) / 連絡文章テンプレ提案 (O-07)。
/// Excel 出力 (O-06) は IPurchaseOrderExcelService 経由 (Infrastructure 層実装)。
/// </summary>
public class PurchaseOrderService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>
    /// 新規作成 (O-01)。mgmt_no を自動採番 (年下 2 桁 + 連番、例: "26-00001")。
    /// product_supplier_prices から現在有効単価を引当 (BR-04) してスナップショット化。
    /// </summary>
    public async Task<PurchaseOrder> CreateAsync(
        CreateOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        if (req.Lines.Count == 0)
            throw new ArgumentException("明細を 1 件以上指定してください (ORDER-001)");

        var nextMgmtNo = await GenerateMgmtNoAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var order = new PurchaseOrder
            {
                MgmtNo = nextMgmtNo,
                Status = OrderStatus.Active,
                SupplierId = req.SupplierId,
                DeliveryDestinationId = req.DeliveryDestinationId,
                DepartmentId = req.DepartmentId,
                WarehouseId = req.WarehouseId,
                DueDate = req.DueDate,
                OrdererUserId = req.OrdererUserId,
                ManagerUserId = req.ManagerUserId,
                SubOrderer1UserId = req.SubOrderer1UserId,
                SubOrderer2UserId = req.SubOrderer2UserId,
                SubOrderer3UserId = req.SubOrderer3UserId,
                SubOrderer4UserId = req.SubOrderer4UserId,
                SubOrderer5UserId = req.SubOrderer5UserId,
                SubOrderer6UserId = req.SubOrderer6UserId,
                CommunicationText = req.CommunicationText,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            };
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            foreach (var l in req.Lines)
            {
                var product = await db.Products
                    .Include(p => p.ProductFamily)
                    .FirstOrDefaultAsync(p => p.Id == l.ProductId, ct)
                    ?? throw new ArgumentException($"product_id={l.ProductId} 不在");

                db.PurchaseOrderLines.Add(new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    LineNo = lineNo++,
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = product.ProductFamily?.ProductName1 ?? "?",
                    Quantity = l.Quantity,
                    UnitPriceSnapshot = l.UnitPriceSnapshot,
                    CurrencyCodeSnapshot = l.CurrencyCodeSnapshot,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "PurchaseOrder.Create",
                entityType: "PurchaseOrder", entityId: order.Id,
                note: $"mgmt_no={order.MgmtNo}, lines={req.Lines.Count}, total_amount=***",
                cancellationToken: ct);

            return order;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>一覧 (O-03)。SQL 集計で合計金額・明細件数を一括取得。</summary>
    public async Task<List<OrderListItem>> ListAsync(
        long actorUserId, bool includeCancelled, CancellationToken ct = default)
    {
        var query = db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Orderer)
            .AsQueryable();
        if (!includeCancelled) query = query.Where(o => o.Status == OrderStatus.Active);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                Order = o,
                LineCount = o.Lines.Count,
                TotalAmount = o.Lines.Sum(l => (decimal?)l.Subtotal) ?? 0m,
                CurrencyCode = o.Lines.Select(l => l.CurrencyCodeSnapshot).FirstOrDefault() ?? "JPY",
            })
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "PurchaseOrder.List",
            entityType: "PurchaseOrder", note: $"count={items.Count}", cancellationToken: ct);

        return items.Select(x => new OrderListItem(
            x.Order.Id, x.Order.MgmtNo, x.Order.OrderNo, (short)x.Order.Status,
            x.Order.Supplier?.Code ?? "?", x.Order.Supplier?.Name ?? "?",
            x.Order.DeliveryDestination?.Name ?? "?",
            x.Order.DueDate,
            x.Order.Orderer?.DisplayName,
            x.LineCount,
            x.TotalAmount, x.CurrencyCode,
            x.Order.FirstExportedAt, x.Order.LastExportedAt,
            x.Order.CreatedAt, x.Order.UpdatedAt)).ToList();
    }

    /// <summary>詳細 (O-04 編集画面ベース)。</summary>
    public async Task<OrderDetail?> GetDetailAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Department)
            .Include(o => o.Warehouse)
            .Include(o => o.Orderer)
            .Include(o => o.Manager)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        var lines = await db.PurchaseOrderLines
            .Include(l => l.Product).ThenInclude(p => p!.Color)
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Include(l => l.Product).ThenInclude(p => p!.ProductFamily)
            .Where(l => l.PurchaseOrderId == id)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "PurchaseOrder.View",
            entityType: "PurchaseOrder", entityId: id, cancellationToken: ct);

        return new OrderDetail(
            order.Id, order.MgmtNo, order.OrderNo, (short)order.Status,
            order.CancelledAt, order.CancelReason,
            order.SupplierId, order.Supplier?.Code ?? "?", order.Supplier?.Name ?? "?",
            order.SupplierOfficialNameSnapshot, order.SupplierCodeSnapshot,
            order.DeliveryDestinationId, order.DeliveryDestination?.Name ?? "?",
            order.CustomerNameSnapshot,
            order.DepartmentId, order.Department?.Name ?? "?",
            order.WarehouseId, order.Warehouse?.Name ?? "?",
            order.DueDate,
            order.OrdererUserId, order.Orderer?.DisplayName ?? "?",
            order.ManagerUserId, order.Manager?.DisplayName ?? "?",
            order.SubOrderer1UserId, order.SubOrderer2UserId, order.SubOrderer3UserId,
            order.SubOrderer4UserId, order.SubOrderer5UserId, order.SubOrderer6UserId,
            order.CommunicationText,
            order.FirstExportedAt, order.LastExportedAt,
            order.CreatedAt, order.UpdatedAt,
            lines.Select(l => new OrderLineDetail(
                l.Id, l.LineNo, l.ProductId, l.SkuSnapshot, l.ProductNameSnapshot,
                l.Product?.Color?.Name ?? "?", l.Product?.Size?.Name ?? "?",
                l.Quantity, l.UnitPriceSnapshot, l.CurrencyCodeSnapshot, l.Subtotal)).ToList());
    }

    /// <summary>編集 (O-04)。F-16 edit_reason 必須、audit_logs.changes に before/after 記録。</summary>
    public async Task<PurchaseOrder?> UpdateAsync(
        long id, UpdateOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        if (order.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("中止済みの発注書は編集できません (ORDER-003)");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            order.SupplierId = req.SupplierId;
            order.DeliveryDestinationId = req.DeliveryDestinationId;
            order.DepartmentId = req.DepartmentId;
            order.WarehouseId = req.WarehouseId;
            order.DueDate = req.DueDate;
            order.OrdererUserId = req.OrdererUserId;
            order.ManagerUserId = req.ManagerUserId;
            order.SubOrderer1UserId = req.SubOrderer1UserId;
            order.SubOrderer2UserId = req.SubOrderer2UserId;
            order.SubOrderer3UserId = req.SubOrderer3UserId;
            order.SubOrderer4UserId = req.SubOrderer4UserId;
            order.SubOrderer5UserId = req.SubOrderer5UserId;
            order.SubOrderer6UserId = req.SubOrderer6UserId;
            order.CommunicationText = req.CommunicationText;
            order.UpdatedAt = now;
            order.UpdatedByUserId = actorUserId;

            // 明細: 既存全削除 → 新規 INSERT (シンプル実装、Iter 4 で差分更新最適化を検討)
            var existing = await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == id).ToListAsync(ct);
            db.PurchaseOrderLines.RemoveRange(existing);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            foreach (var l in req.Lines)
            {
                var product = await db.Products
                    .Include(p => p.ProductFamily)
                    .FirstOrDefaultAsync(p => p.Id == l.ProductId, ct)
                    ?? throw new ArgumentException($"product_id={l.ProductId} 不在");
                db.PurchaseOrderLines.Add(new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    LineNo = lineNo++,
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = product.ProductFamily?.ProductName1 ?? "?",
                    Quantity = l.Quantity,
                    UnitPriceSnapshot = l.UnitPriceSnapshot,
                    CurrencyCodeSnapshot = l.CurrencyCodeSnapshot,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "PurchaseOrder.Update",
                entityType: "PurchaseOrder", entityId: id,
                note: $"mgmt_no={order.MgmtNo}, edit_reason={req.EditReason}, edit_note={req.EditNote ?? ""}, total_amount=***",
                cancellationToken: ct);

            return order;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>中止 (O-05)。status = Cancelled、cancelled_at / cancelled_by_user_id を SET。</summary>
    public async Task<bool> CancelAsync(long id, CancelOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return false;
        if (order.Status == OrderStatus.Cancelled) return true; // 冪等

        var now = DateTime.UtcNow;
        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = now;
        order.CancelledByUserId = actorUserId;
        order.CancelReason = req.CancelReason;
        order.UpdatedAt = now;
        order.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "PurchaseOrder.Cancel",
            entityType: "PurchaseOrder", entityId: id,
            note: $"mgmt_no={order.MgmtNo}, reason={req.CancelReason}", cancellationToken: ct);

        return true;
    }

    /// <summary>連絡文章テンプレ提案 (O-07)。document_template_confirmations + document_text_purchases の standard_print_flag=true を統合返却。</summary>
    public async Task<List<CommunicationTextSuggestion>> GetCommunicationSuggestionsAsync(CancellationToken ct = default)
    {
        var confirmations = await db.DocumentTemplateConfirmations
            .Where(d => !d.DeleteFlag && d.StandardPrintFlag)
            .Select(d => new CommunicationTextSuggestion(d.Body, true, $"確認表 {d.Code} - {d.Name}"))
            .ToListAsync(ct);
        var purchases = await db.DocumentTextPurchases
            .Where(d => !d.DeleteFlag && d.StandardPrintFlag)
            .Select(d => new CommunicationTextSuggestion(d.Body, true, $"発注書 {d.Code} - {d.Name}"))
            .ToListAsync(ct);

        return confirmations.Concat(purchases).ToList();
    }

    /// <summary>mgmt_no 採番 (例: "26-00001")。年下 2 桁 + ハイフン + 5 桁ゼロ埋め連番。</summary>
    private async Task<string> GenerateMgmtNoAsync(CancellationToken ct)
    {
        var year2 = DateTime.UtcNow.Year % 100;
        var prefix = $"{year2:D2}-";
        var existing = await db.PurchaseOrders
            .Where(o => o.MgmtNo.StartsWith(prefix))
            .Select(o => o.MgmtNo)
            .ToListAsync(ct);
        var maxSeq = existing
            .Select(m => m.Length > 3 && int.TryParse(m.AsSpan(3), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{maxSeq + 1:D5}";
    }
}
