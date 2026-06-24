using Akebono.Application.Common;
using Akebono.Application.Orders;
using Akebono.Domain.Common;
using Akebono.Domain.Orders;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 管理表 Excel 出力 (#3b)。選択発注の全明細を 1 シートに展開した社内管理用一覧表。
/// 発注書 (PurchaseOrderExcelService) と異なり <b>読み取り専用</b>:
/// snapshot 凍結 / order_no 採番 / export_log INSERT を一切行わない (集計・閲覧専用)。
///
/// レイアウト: ヘッダ行 + 明細行 (発注をまたいで 1 明細 = 1 行) + 合計行 (発注数・金額)。
/// 列: 管理番号 / 発注番号 / 発注日 / 区分 / 発注状態 / 仕入先 / 納品先 / 得意先 /
///     品番(7桁) / SKU / 商品名 / 色 / サイズ / 仮番号 / 発注数 / 入数 / 単価 / 見積単価 /
///     金額(小計) / 通貨 / 納期
/// </summary>
public class OrderManagementTableExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IOrderManagementTableExcelService
{
    private static readonly string[] Headers =
    {
        "管理番号", "発注番号", "発注日", "区分", "発注状態", "仕入先", "納品先", "得意先",
        "品番", "SKU", "商品名", "色", "サイズ", "仮番号", "発注数", "入数", "単価", "見積単価",
        "金額", "通貨", "納期",
    };

    public async Task<(string FileName, byte[] Content)> ExportAsync(
        IReadOnlyList<long> orderIds, long actorUserId, CancellationToken ct = default)
    {
        // 発注ヘッダ + 明細 (色/サイズ名は Product ナビから解決、GetDetailAsync と同じ Include 構成)。
        // 読み取り専用のためトランザクション・書込なし。並び順は安定化のため 発注 id → line_no。
        var orders = await db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(ct);

        var lines = await db.PurchaseOrderLines
            .Include(l => l.Product).ThenInclude(p => p!.Color)
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Where(l => orderIds.Contains(l.PurchaseOrderId))
            .OrderBy(l => l.PurchaseOrderId).ThenBy(l => l.LineNo)
            .ToListAsync(ct);

        var orderById = orders.ToDictionary(o => o.Id);
        // 選択順 (フロントが渡した orderIds の順) を保ちつつ、各発注の明細を line_no 順に出力する。
        var linesByOrder = lines
            .GroupBy(l => l.PurchaseOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).ToList());

        var now = SystemTime.Now;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("管理表");

        // タイトル
        ws.Cell("A1").Value = "管理表";
        ws.Range(1, 1, 1, Headers.Length).Merge().Style.Font.SetBold().Font.FontSize = 16;
        ws.Range(1, 1, 1, Headers.Length).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Cell("A2").Value = $"出力日時: {now:yyyy-MM-dd HH:mm}  /  対象発注 {orderById.Count} 件";

        // ヘッダ行
        const int headerRow = 4;
        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        // 明細行 (発注をまたいで 1 明細 = 1 行)。フロントの選択順 (orderIds) を維持。
        var dataRow = headerRow + 1;
        long totalQuantity = 0;
        decimal totalAmount = 0m;
        foreach (var oid in orderIds)
        {
            if (!orderById.TryGetValue(oid, out var order)) continue; // 削除済等で読み込めなかった id は skip
            if (!linesByOrder.TryGetValue(oid, out var orderLines)) continue;

            var region = order.IsOverseas ? "海外" : "国内";
            var state = OrderStateLabel(order);
            var supplier = order.Supplier?.Name ?? "";
            var delivery = order.DeliveryDestination?.Name ?? "";
            // 得意先: 発注時凍結スナップショット優先、未設定時は納品先マスタの取引先名で補完 (一覧と同じ規則)。
            var customer = order.CustomerNameSnapshot ?? order.DeliveryDestination?.CustomerName ?? "";

            foreach (var line in orderLines)
            {
                // 品番 = SKU 先頭 7 桁 (7 桁未満はそのまま)。
                var sku = line.SkuSnapshot ?? "";
                var partNo = sku.Length >= 7 ? sku[..7] : sku;

                var col = 1;
                ws.Cell(dataRow, col++).Value = order.MgmtNo;
                ws.Cell(dataRow, col++).Value = order.OrderNo ?? "";
                ws.Cell(dataRow, col++).Value = order.CreatedAt.ToString("yyyy-MM-dd");
                ws.Cell(dataRow, col++).Value = region;
                ws.Cell(dataRow, col++).Value = state;
                ws.Cell(dataRow, col++).Value = supplier;
                ws.Cell(dataRow, col++).Value = delivery;
                ws.Cell(dataRow, col++).Value = customer;
                ws.Cell(dataRow, col++).Value = partNo;
                ws.Cell(dataRow, col++).Value = sku;
                ws.Cell(dataRow, col++).Value = line.ProductNameSnapshot;
                ws.Cell(dataRow, col++).Value = line.Product?.Color?.Name ?? "";
                ws.Cell(dataRow, col++).Value = line.Product?.Size?.Name ?? "";
                ws.Cell(dataRow, col++).Value = line.ProvisionalNumberSnapshot ?? "";
                ws.Cell(dataRow, col++).Value = line.Quantity;
                if (line.PackQuantity is { } pack) ws.Cell(dataRow, col).Value = pack;
                col++;
                ws.Cell(dataRow, col).Value = line.UnitPriceSnapshot;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                if (line.EstimateUnitPrice is { } est) ws.Cell(dataRow, col).Value = est;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col).Value = line.Subtotal;
                ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, col++).Value = line.CurrencyCodeSnapshot;
                ws.Cell(dataRow, col++).Value = order.DueDate.ToString("yyyy-MM-dd");

                for (var c = 1; c <= Headers.Length; c++)
                    ws.Cell(dataRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                totalQuantity += line.Quantity;
                totalAmount += line.Subtotal;
                dataRow++;
            }
        }

        // 合計行 (発注数・金額)。通貨混在の可能性があるため金額は単純合算 (見出しに注記)。
        ws.Cell(dataRow, 1).Value = "合計";
        ws.Cell(dataRow, 1).Style.Font.SetBold();
        // 発注数 (列 15)
        ws.Cell(dataRow, 15).Value = totalQuantity;
        ws.Cell(dataRow, 15).Style.Font.SetBold();
        // 金額 (列 19)
        ws.Cell(dataRow, 19).Value = totalAmount;
        ws.Cell(dataRow, 19).Style.Font.SetBold().NumberFormat.Format = "#,##0.00";
        for (var c = 1; c <= Headers.Length; c++)
            ws.Cell(dataRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        // 監査ログ (読み取り専用。発注データは変更しないため Export ではなく ManagementTable として記録)。
        await audit.LogAsync(actorUserId, "PurchaseOrder.ManagementTable",
            entityType: "PurchaseOrder",
            note: $"order_count={orderById.Count}, line_count={dataRow - (headerRow + 1)}",
            cancellationToken: ct);

        var fileName = $"管理表_{now:yyyyMMdd}.xlsx";
        return (fileName, stream.ToArray());
    }

    /// <summary>
    /// 発注状態 5 値ラベル。フロント deriveOrderState (#3a) と同じ優先順位:
    /// 発注削除 &gt; 納品完了 &gt; 発注中止 &gt; 正式発注済 &gt; 発注依頼中。
    /// </summary>
    private static string OrderStateLabel(PurchaseOrder o)
    {
        if (o.IsDeleted) return "発注削除";
        if (o.DeliveredAt is not null) return "納品完了";
        if (o.Status == OrderStatus.Cancelled) return "発注中止";
        if (o.FirstExportedAt is not null) return "正式発注済";
        return "発注依頼中";
    }
}
