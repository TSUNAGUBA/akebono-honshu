using Akebono.Application.Common;
using Akebono.Domain.Common;
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
    /// 各明細の unit_price_snapshot はクライアント入力値をそのまま凍結保存する
    /// (サーバ側で product_supplier_prices から引当て・上書きはしない。「単価未決定」=
    /// unit_price_snapshot &lt;= 0 の状態も保持される)。入力補助の size-aware 現単価サジェストは
    /// OrderEndpoints の GET /api/v1/orders/price-suggestion で別途提供 (読取専用、PR2)。
    /// </summary>
    public async Task<PurchaseOrder> CreateAsync(
        CreateOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        if (req.Lines.Count == 0)
            throw new ArgumentException("明細を 1 件以上指定してください (ORDER-001)");

        // 分納×倉庫の多次元明細 (PR5b) を DB 書込前に一括検証 (不正は 422 で弾く)。
        // null/空は分納なし (単一明細) として許容。1 件以上あれば各行数量・合計を検証。
        foreach (var l in req.Lines)
            ValidateDeliveries(l.Deliveries);

        var nextMgmtNo = await GenerateMgmtNoAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.Now;
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
                // 旧 発注書 国内/海外 項目 (Phase B)
                IsOverseas = req.IsOverseas,
                LandingPlace = req.LandingPlace,
                CustomerRef = req.CustomerRef,
                FactoryShippingDate = req.FactoryShippingDate,
                DeliveryPlaceShippingDate = req.DeliveryPlaceShippingDate,
                OverseasDepartureDate = req.OverseasDepartureDate,
                Warehouse2Id = req.Warehouse2Id,
                Warehouse3Id = req.Warehouse3Id,
                CommunicationText = req.CommunicationText,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            };
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            // 分納がある明細について (line エンティティ, 分納入力) を控えておき、line.Id 確定後に分納を挿入する。
            var pendingDeliveries = new List<(PurchaseOrderLine Line, List<OrderLineDeliveryInput> Deliveries)>();
            foreach (var l in req.Lines)
            {
                var product = await db.Products
                    .Include(p => p.ProductFamily)
                    .FirstOrDefaultAsync(p => p.Id == l.ProductId, ct)
                    ?? throw new ArgumentException($"product_id={l.ProductId} 不在");

                // 分納 (PR5b): 1 件以上あれば line.Quantity = 分納数量の合計 (SUM)。0 件は client の quantity を
                // そのまま使う (従来挙動)。subtotal は GENERATED (quantity * unit_price_snapshot) のため書かない。
                var hasDeliveries = l.Deliveries is { Count: > 0 };
                var line = new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    LineNo = lineNo++,
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = product.ProductFamily?.ProductName1 ?? "?",
                    // 旧 発注明細 項目 (Phase B)。仮番号は商品 family.ProvisionalNumber を発注時点で
                    // 凍結コピー (ProductNameSnapshot と同じ snapshot-copy 方式)。入数・見積単価は入力値。
                    ProvisionalNumberSnapshot = product.ProductFamily?.ProvisionalNumber,
                    PackQuantity = l.PackQuantity,
                    EstimateUnitPrice = l.EstimateUnitPrice,
                    Remark = l.Remark,
                    Quantity = hasDeliveries ? SumDeliveryQuantity(l.Deliveries!) : l.Quantity,
                    UnitPriceSnapshot = l.UnitPriceSnapshot,
                    CurrencyCodeSnapshot = l.CurrencyCodeSnapshot,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                };
                db.PurchaseOrderLines.Add(line);
                if (hasDeliveries)
                    pendingDeliveries.Add((line, l.Deliveries!));
            }
            await db.SaveChangesAsync(ct);

            // 分納行の挿入 (PR5b)。line.Id は上の SaveChanges で確定済。seq は配列順で 1 から採番。
            foreach (var (line, deliveries) in pendingDeliveries)
                db.PurchaseOrderLineDeliveries.AddRange(BuildDeliveryEntities(line.Id, deliveries, now, actorUserId));
            if (pendingDeliveries.Count > 0)
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

    /// <summary>
    /// 一覧 (O-03)。SQL 集計で合計金額・明細件数・単価未決定有無を一括取得。
    /// 発注状態 5 値モデル (#3a): is_deleted 行も含めて返す (発注削除 フィルタが表示できるよう)。
    /// 既定の絞込 (発注削除を隠す) はフロント側 SPLIT フィルタで行う。
    /// includeCancelled は後方互換のため残すが、5 状態フィルタ導入後はフロントが全状態を
    /// client-side で絞り込むため、サービスは includeCancelled=true 相当の全件 (中止/納品完了/削除含む) を返す。
    /// </summary>
    public async Task<List<OrderListItem>> ListAsync(
        long actorUserId, bool includeCancelled, CancellationToken ct = default)
    {
        var query = db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Orderer)
            .AsQueryable();
        // 後方互換: includeCancelled=false の旧呼び出し時は中止を除外。さらに削除済 (is_deleted) も
        // サーバ側で除外する (論理削除は「非中止」ビューにも出さない。client 未更新でも漏れない)。
        // 発注状態 SPLIT フィルタ (#3a) を使うフロントは includeCancelled=true で呼び、全状態 (削除含む) を
        // 取得して client-side で絞り込む (発注削除 フィルタを ON にしたときだけ削除済を表示)。
        if (!includeCancelled)
            query = query.Where(o => o.Status == OrderStatus.Active && !o.IsDeleted);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                Order = o,
                LineCount = o.Lines.Count,
                TotalAmount = o.Lines.Sum(l => (decimal?)l.Subtotal) ?? 0m,
                CurrencyCode = o.Lines.Select(l => l.CurrencyCodeSnapshot).FirstOrDefault() ?? "JPY",
                // 単価未決定: 明細に unit_price_snapshot <= 0 が 1 件でも存在するか (EXISTS 相当を SQL 集計)
                HasUndecidedPrice = o.Lines.Any(l => l.UnitPriceSnapshot <= 0m),
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
            x.Order.CreatedAt, x.Order.UpdatedAt,
            // 発注区分 国内/海外 (Phase B、is_overseas)
            x.Order.IsOverseas,
            // 発注状態 5 値モデル (#3a): 納品完了/発注削除 導出フィールド + フィルタ用フィールド
            x.Order.DeliveredAt,
            x.Order.IsDeleted,
            x.Order.SupplierId,
            x.Order.OrdererUserId,
            // 得意先: 発注時凍結スナップショット優先、未設定時は納品先マスタの取引先名で補完
            x.Order.CustomerNameSnapshot ?? x.Order.DeliveryDestination?.CustomerName,
            x.HasUndecidedPrice)).ToList();
    }

    /// <summary>詳細 (O-04 編集画面ベース)。</summary>
    public async Task<OrderDetail?> GetDetailAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Department)
            .Include(o => o.Warehouse)
            // 旧 発注書 国内/海外 項目の表示名解決 (Phase B): 納入倉庫2/3
            .Include(o => o.Warehouse2)
            .Include(o => o.Warehouse3)
            .Include(o => o.Orderer)
            .Include(o => o.Manager)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        var lines = await db.PurchaseOrderLines
            .Include(l => l.Product).ThenInclude(p => p!.Color)
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Include(l => l.Product).ThenInclude(p => p!.ProductFamily)
            // 分納×倉庫の多次元明細 (PR5b)。倉庫名解決のため Warehouse ナビも Include。
            .Include(l => l.Deliveries).ThenInclude(d => d.Warehouse)
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
                l.Quantity, l.UnitPriceSnapshot, l.CurrencyCodeSnapshot, l.Subtotal,
                // 旧 発注明細 項目 (Phase B) + 発注明細 備考 (spec 明細 No.26)
                l.PackQuantity, l.EstimateUnitPrice, l.ProvisionalNumberSnapshot, l.Remark,
                // 分納×倉庫の多次元明細 (PR5b)。seq 昇順 (表示順)。分納なしの明細では空リスト。
                l.Deliveries
                    .OrderBy(d => d.Seq).ThenBy(d => d.Id)
                    .Select(d => new OrderLineDeliverySummary(
                        d.Id, d.WarehouseId, d.Warehouse?.Name, d.DeliveryDate,
                        d.Quantity, d.PackQuantity, d.Seq))
                    .ToList())).ToList(),
            // 旧 発注書 国内/海外 項目 (Phase B)。納入倉庫2/3 名は Include 済ナビから解決 (未設定時 null)。
            order.IsOverseas,
            order.LandingPlace,
            order.CustomerRef,
            order.FactoryShippingDate,
            order.DeliveryPlaceShippingDate,
            order.OverseasDepartureDate,
            order.Warehouse2Id, order.Warehouse2?.Name,
            order.Warehouse3Id, order.Warehouse3?.Name,
            // 発注状態 5 値モデル (#3a)。納品完了/発注削除 の状態表示・操作可否判定に使う。
            order.DeliveredAt,
            order.IsDeleted,
            order.DeletedAt);
    }

    /// <summary>編集 (O-04)。F-16 edit_reason 必須、audit_logs.changes に before/after 記録。</summary>
    public async Task<PurchaseOrder?> UpdateAsync(
        long id, UpdateOrderRequest req, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        if (order.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("中止済みの発注書は編集できません (ORDER-003)");
        // 発注状態 5 値モデル (#3a): 削除済/納品完了 は終端状態として編集不可。
        if (order.IsDeleted)
            throw new InvalidOperationException("削除済みの発注書は編集できません (ORDER-007)");
        if (order.DeliveredAt is not null)
            throw new InvalidOperationException("納品完了済みの発注書は編集できません (ORDER-008)");

        // 分納×倉庫の多次元明細 (PR5b) を DB 書込前に一括検証 (不正は 422 で弾く)。
        // null/空は分納なし (単一明細) として許容。1 件以上あれば各行数量・合計を検証。
        foreach (var l in req.Lines)
            ValidateDeliveries(l.Deliveries);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.Now;
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
            // 旧 発注書 国内/海外 項目 (Phase B)
            order.IsOverseas = req.IsOverseas;
            order.LandingPlace = req.LandingPlace;
            order.CustomerRef = req.CustomerRef;
            order.FactoryShippingDate = req.FactoryShippingDate;
            order.DeliveryPlaceShippingDate = req.DeliveryPlaceShippingDate;
            order.OverseasDepartureDate = req.OverseasDepartureDate;
            order.Warehouse2Id = req.Warehouse2Id;
            order.Warehouse3Id = req.Warehouse3Id;
            order.CommunicationText = req.CommunicationText;
            order.UpdatedAt = now;
            order.UpdatedByUserId = actorUserId;

            // 明細: 既存全削除 → 新規 INSERT (シンプル実装、Iter 4 で差分更新最適化を検討)。
            // 分納 (PR5b) は明細を全置換する設計のため、親明細削除に伴い分納行も DB の ON DELETE CASCADE で
            // 消える (purchase_order_line_deliveries.purchase_order_line_id FK)。再 INSERT 時に再構築する。
            var existing = await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == id).ToListAsync(ct);
            db.PurchaseOrderLines.RemoveRange(existing);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            // 分納がある明細について (line エンティティ, 分納入力) を控えておき、line.Id 確定後に分納を挿入する。
            var pendingDeliveries = new List<(PurchaseOrderLine Line, List<OrderLineDeliveryInput> Deliveries)>();
            foreach (var l in req.Lines)
            {
                var product = await db.Products
                    .Include(p => p.ProductFamily)
                    .FirstOrDefaultAsync(p => p.Id == l.ProductId, ct)
                    ?? throw new ArgumentException($"product_id={l.ProductId} 不在");

                // 分納 (PR5b): 1 件以上あれば line.Quantity = 分納数量の合計 (SUM)。0 件は client の quantity を
                // そのまま使う (従来挙動)。subtotal は GENERATED (quantity * unit_price_snapshot) のため書かない。
                var hasDeliveries = l.Deliveries is { Count: > 0 };
                var line = new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    LineNo = lineNo++,
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = product.ProductFamily?.ProductName1 ?? "?",
                    // 旧 発注明細 項目 (Phase B)。仮番号は商品 family.ProvisionalNumber を再凍結コピー
                    // (明細は全削除→再 INSERT 方式のため、編集時も最新 family 値で凍結し直す)。
                    ProvisionalNumberSnapshot = product.ProductFamily?.ProvisionalNumber,
                    PackQuantity = l.PackQuantity,
                    EstimateUnitPrice = l.EstimateUnitPrice,
                    Remark = l.Remark,
                    Quantity = hasDeliveries ? SumDeliveryQuantity(l.Deliveries!) : l.Quantity,
                    UnitPriceSnapshot = l.UnitPriceSnapshot,
                    CurrencyCodeSnapshot = l.CurrencyCodeSnapshot,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                };
                db.PurchaseOrderLines.Add(line);
                if (hasDeliveries)
                    pendingDeliveries.Add((line, l.Deliveries!));
            }
            await db.SaveChangesAsync(ct);

            // 分納行の再挿入 (PR5b、全置換)。line.Id は上の SaveChanges で確定済。seq は配列順で 1 から採番。
            foreach (var (line, deliveries) in pendingDeliveries)
                db.PurchaseOrderLineDeliveries.AddRange(BuildDeliveryEntities(line.Id, deliveries, now, actorUserId));
            if (pendingDeliveries.Count > 0)
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
        // 発注状態 5 値モデル (#3a): 削除済/納品完了 は終端状態として中止不可
        // (deriveOrderState の優先順位 発注削除 > 納品完了 > 発注中止 と整合)。
        if (order.IsDeleted)
            throw new InvalidOperationException("削除済みの発注書は中止できません (ORDER-009)");
        if (order.DeliveredAt is not null)
            throw new InvalidOperationException("納品完了済みの発注書は中止できません (ORDER-010)");

        var now = SystemTime.Now;
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

    /// <summary>
    /// 納品完了 (#3a)。delivered_at / delivered_by_user_id を SET。
    /// 「正式発注済」(status=Active かつ first_exported_at IS NOT NULL) のときのみ実行可。
    /// 既に納品完了済なら冪等に成功を返す。中止済/削除済/未出力 (発注依頼中) はエラー。
    /// </summary>
    public async Task<bool> MarkDeliveredAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return false;
        // 終端状態ガードを冪等 return より先に評価する (削除済/中止済を納品完了成功と誤報しないため)。
        if (order.IsDeleted)
            throw new InvalidOperationException("削除済みの発注書は納品完了にできません (ORDER-004)");
        if (order.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("中止済みの発注書は納品完了にできません (ORDER-005)");
        if (order.DeliveredAt is not null) return true; // 冪等 (既に納品完了)
        if (order.FirstExportedAt is null)
            throw new InvalidOperationException("正式発注済 (初回 Excel 出力済) の発注書のみ納品完了にできます (ORDER-006)");

        var now = SystemTime.Now;
        order.DeliveredAt = now;
        order.DeliveredByUserId = actorUserId;
        order.UpdatedAt = now;
        order.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "PurchaseOrder.Deliver",
            entityType: "PurchaseOrder", entityId: id,
            note: $"mgmt_no={order.MgmtNo}", cancellationToken: ct);

        return true;
    }

    /// <summary>
    /// 発注削除 (#3a)。論理削除 (is_deleted=true、deleted_at / deleted_by_user_id を SET)。
    /// 物理削除はしない (監査・履歴保持)。既に削除済なら冪等に成功を返す。
    /// </summary>
    public async Task<bool> SoftDeleteAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return false;
        if (order.IsDeleted) return true; // 冪等

        var now = SystemTime.Now;
        order.IsDeleted = true;
        order.DeletedAt = now;
        order.DeletedByUserId = actorUserId;
        order.UpdatedAt = now;
        order.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "PurchaseOrder.Delete",
            entityType: "PurchaseOrder", entityId: id,
            note: $"mgmt_no={order.MgmtNo}", cancellationToken: ct);

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

    /// <summary>
    /// 分納×倉庫の多次元明細 (PR5b) の入力検証。null/空は分納なし (単一明細) として許容 (検証なし)。
    /// 1 件以上あれば各行の数量 (正の整数) を検証し、合計 (SUM) も 0 超であることを保証する。
    /// WarehouseId / DeliveryDate は NULL 許容のため存在検証はしない (倉庫未指定 / 発注明細日未指定を許容)。
    /// </summary>
    private static void ValidateDeliveries(List<OrderLineDeliveryInput>? deliveries)
    {
        if (deliveries is null || deliveries.Count == 0) return;
        long sum = 0;
        foreach (var d in deliveries)
        {
            if (d.Quantity <= 0)
                throw new ArgumentException("分納明細の発注数は正の整数で指定してください (ORDER-LINE-DLV-001)");
            if (d.PackQuantity is { } pack && pack < 0)
                throw new ArgumentException("分納明細の入数は 0 以上で指定してください (ORDER-LINE-DLV-002)");
            sum += d.Quantity;
        }
        // 各 Quantity > 0 を満たせば sum > 0 だが、合計が int 範囲を超えないことも明示確認する。
        if (sum <= 0 || sum > int.MaxValue)
            throw new ArgumentException("分納明細の発注数合計が不正です (ORDER-LINE-DLV-003)");
    }

    /// <summary>
    /// 分納×倉庫の多次元明細 (PR5b) の Quantity 合計 (SUM)。null/空は 0。
    /// 分納が 1 件以上ある明細では line.Quantity にこの値を設定する (subtotal GENERATED 列が
    /// quantity * unit_price_snapshot で正しい金額になる)。<see cref="ValidateDeliveries"/> 後に呼ぶ。
    /// </summary>
    private static int SumDeliveryQuantity(List<OrderLineDeliveryInput> deliveries)
        => deliveries.Sum(d => d.Quantity);

    /// <summary>
    /// 分納×倉庫の多次元明細 (PR5b) の入力を永続化エンティティへ変換する。seq は配列順で 1 から採番。
    /// 親明細 (lineId) は SaveChanges 済で Id が確定していることを前提とする (FK = purchase_order_line_id)。
    /// 呼び出し前に <see cref="ValidateDeliveries"/> で検証済であることを前提とする。
    /// </summary>
    private static List<PurchaseOrderLineDelivery> BuildDeliveryEntities(
        long lineId, List<OrderLineDeliveryInput> deliveries, DateTime now, long actorUserId)
    {
        var result = new List<PurchaseOrderLineDelivery>(deliveries.Count);
        short seq = 1;
        foreach (var d in deliveries)
        {
            result.Add(new PurchaseOrderLineDelivery
            {
                PurchaseOrderLineId = lineId,
                WarehouseId = d.WarehouseId,
                DeliveryDate = d.DeliveryDate,
                Quantity = d.Quantity,
                PackQuantity = d.PackQuantity,
                Seq = seq++,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
            });
        }
        return result;
    }

    /// <summary>mgmt_no 採番 (例: "26-00001")。年下 2 桁 + ハイフン + 5 桁ゼロ埋め連番。</summary>
    private async Task<string> GenerateMgmtNoAsync(CancellationToken ct)
    {
        var year2 = SystemTime.Now.Year % 100;
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
