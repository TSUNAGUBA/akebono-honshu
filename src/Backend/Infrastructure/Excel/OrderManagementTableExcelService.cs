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
///     金額(小計) / 通貨 / 納期 / 備考 / 倉庫
/// 備考 (発注明細 備考、spec 明細 No.26) / 倉庫 (PR5b 分納倉庫) は末尾に追加。発注数 (列15) /
/// 金額 (列19) の合計行ハードコード列番号を維持するため、既存列の手前には差し込まない。
///
/// 分納×倉庫の多次元明細 (PR5b): 分納がある明細は「1 分納 = 1 行」で展開する
/// (発注数=分納数量 / 入数=分納入数 / 納期=分納日 / 倉庫=分納倉庫、品番〜SKU〜単価等は明細値)。
/// 分納なしの明細は従来どおり 1 明細 = 1 行 (倉庫列は空)。発注数合計・金額合計は実際に出力した行から
/// 集計するため、分納展開しても合計は line.Quantity (=分納 SUM) ・ line.Subtotal と一致する。
/// </summary>
public class OrderManagementTableExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IOrderManagementTableExcelService
{
    private static readonly string[] Headers =
    {
        "管理番号", "発注番号", "発注日", "区分", "発注状態", "仕入先", "納品先", "得意先",
        "品番", "SKU", "商品名", "色", "サイズ", "仮番号", "発注数", "入数", "単価", "見積単価",
        "金額", "通貨", "納期", "備考", "倉庫",
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
            // 分納×倉庫の多次元明細 (PR5b)。倉庫名解決のため Warehouse ナビも Include。
            .Include(l => l.Deliveries).ThenInclude(d => d.Warehouse)
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

                // 分納×倉庫の多次元明細 (PR5b): 分納があれば「1 分納 = 1 行」で展開する。各行の
                // 発注数 / 入数 / 納期 / 倉庫 / 金額 は分納値を使う (品番〜単価等は明細値で共通)。
                // 分納なしは従来どおり 1 明細 = 1 行 (明細値、倉庫は空)。
                // RenderRow = (発注数, 入数, 納期文字列, 倉庫名, 金額)。各行の金額 = 発注数 * 単価 (表示用)。
                var renderRows = new List<(int Quantity, int? PackQuantity, string DateStr, string Warehouse, decimal Amount)>();
                if (line.Deliveries.Count > 0)
                {
                    foreach (var d in line.Deliveries.OrderBy(x => x.Seq).ThenBy(x => x.Id))
                    {
                        var dateStr = d.DeliveryDate?.ToString("yyyy-MM-dd") ?? order.DueDate.ToString("yyyy-MM-dd");
                        renderRows.Add((d.Quantity, d.PackQuantity, dateStr, d.Warehouse?.Name ?? "", d.Quantity * line.UnitPriceSnapshot));
                    }
                }
                else
                {
                    renderRows.Add((line.Quantity, line.PackQuantity, order.DueDate.ToString("yyyy-MM-dd"), "", line.Subtotal));
                }

                // 合計は明細単位の権威値 (line.Quantity / line.Subtotal) を 1 明細につき 1 回だけ加算する。
                // 分納展開行の発注数/金額の合算ではなく DB の line 値を使うことで、展開方法や (万一の)
                // line.Quantity ≠ 分納 SUM のドリフトに依らず合計行が DB 集計と常に一致する (reviewer 指摘対応)。
                totalQuantity += line.Quantity;
                totalAmount += line.Subtotal;

                foreach (var r in renderRows)
                {
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
                    ws.Cell(dataRow, col++).Value = r.Quantity;
                    if (r.PackQuantity is { } pack) ws.Cell(dataRow, col).Value = pack;
                    col++;
                    ws.Cell(dataRow, col).Value = line.UnitPriceSnapshot;
                    ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                    if (line.EstimateUnitPrice is { } est) ws.Cell(dataRow, col).Value = est;
                    ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(dataRow, col).Value = r.Amount;
                    ws.Cell(dataRow, col++).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(dataRow, col++).Value = line.CurrencyCodeSnapshot;
                    ws.Cell(dataRow, col++).Value = r.DateStr;
                    // 備考 (発注明細 備考、spec 明細 No.26)。未設定は空欄。
                    ws.Cell(dataRow, col++).Value = line.Remark ?? "";
                    // 倉庫 (PR5b 分納倉庫)。分納なし行は空欄。
                    ws.Cell(dataRow, col++).Value = r.Warehouse;

                    for (var c = 1; c <= Headers.Length; c++)
                        ws.Cell(dataRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    dataRow++;
                }
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
