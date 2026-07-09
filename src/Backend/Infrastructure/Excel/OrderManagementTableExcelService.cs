using Akebono.Application.Common;
using Akebono.Application.Orders;
using Akebono.Domain.Common;
using Akebono.Domain.Orders;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 管理表 (ORDER DETAIL) Excel 出力。旧システムの管理表帳票レイアウトに合わせる
/// (ヘッダは発注書と共通: SUPPLIER / order NO. / order date / process NO. / 仕入先コード、
///  明細: art NO / color / size / product name / bland / order / depart 倉庫×3 /
///        shipping date 列 (納入日=分納の納期 delivery_date を日付ごとに動的展開) / 合計)。
/// 表記は発注区分で切替: 国内=日本語 / 海外=英語 (<see cref="OrderReportText"/>)。
///
/// 発注書 (<see cref="IPurchaseOrderExcelService"/>) と異なり <b>読み取り専用</b>:
/// snapshot 凍結 / order_no 採番 / export_log INSERT を一切行わない (集計・閲覧専用)。
/// 複数発注を渡された場合は「1 発注 = 1 ワークシート」で展開する (旧帳票は発注単位の 1 枚もの)。
/// </summary>
public class OrderManagementTableExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IOrderManagementTableExcelService
{
    public async Task<(string FileName, byte[] Content)> ExportAsync(
        IReadOnlyList<long> orderIds, long actorUserId, CancellationToken ct = default)
    {
        var orders = await db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Department)
            .Include(o => o.Warehouse)
            .Include(o => o.Warehouse2)
            .Include(o => o.Warehouse3)
            .Include(o => o.Orderer)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(ct);

        var lines = await db.PurchaseOrderLines
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Include(l => l.Product).ThenInclude(p => p!.ProductFamily)
            .Include(l => l.Deliveries)
            .Where(l => orderIds.Contains(l.PurchaseOrderId))
            .OrderBy(l => l.PurchaseOrderId).ThenBy(l => l.LineNo)
            .ToListAsync(ct);

        var orderById = orders.ToDictionary(o => o.Id);
        var linesByOrder = lines
            .GroupBy(l => l.PurchaseOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).ToList());

        var now = SystemTime.JstNow; // ファイル名スタンプは業務時刻 (JST)

        using var wb = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sheetSeq = 0;
        var totalLines = 0;

        // 選択順 (orderIds) で 1 発注 = 1 ワークシート。
        foreach (var oid in orderIds)
        {
            if (!orderById.TryGetValue(oid, out var order)) continue;
            var orderLines = linesByOrder.TryGetValue(oid, out var ls) ? ls : new List<PurchaseOrderLine>();
            var t = OrderReportText.For(order.IsOverseas);

            var baseName = order.OrderNo ?? order.MgmtNo;
            var sheetName = UniqueSheetName(usedSheetNames, baseName, ++sheetSeq);
            var ws = wb.AddWorksheet(sheetName);
            BuildOrderDetailSheet(ws, order, orderLines, t);
            totalLines += orderLines.Count;
        }

        // 選択発注が 1 件も読めなかった場合でも空ブックは作らず、案内用の 1 シートを置く。
        if (!wb.Worksheets.Any())
        {
            var ws = wb.AddWorksheet("ORDER DETAIL");
            ws.Cell("A1").Value = "対象の発注が見つかりませんでした";
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        await audit.LogAsync(actorUserId, "PurchaseOrder.ManagementTable",
            entityType: "PurchaseOrder",
            note: $"order_count={orderById.Count}, line_count={totalLines}",
            cancellationToken: ct);

        var fileName = $"管理表_{now:yyyyMMdd}.xlsx";
        return (fileName, stream.ToArray());
    }

    // ── 1 発注の ORDER DETAIL ワークシートを構築 ──
    private static void BuildOrderDetailSheet(
        IXLWorksheet ws, PurchaseOrder order, List<PurchaseOrderLine> lines, OrderReportText t)
    {
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;

        // shipping date 列 = 発注の全分納の納期 (delivery_date) の重複排除・昇順 (納入日参照)。
        var dates = lines
            .SelectMany(l => l.Deliveries)
            .Where(d => d.DeliveryDate is not null)
            .Select(d => d.DeliveryDate!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // 固定 10 列 (No, art NO, color, size, product name, bland, order, depart×3) + 納入日列。
        const int fixedCols = 10;
        var colCount = fixedCols + dates.Count;

        // ── ヘッダ部 (発注書と共通の体裁) ──
        var officialName = order.SupplierOfficialNameSnapshot ?? order.Supplier?.OfficialName ?? order.Supplier?.Name ?? "";
        var supplierCode = order.SupplierCodeSnapshot ?? order.Supplier?.Code ?? "";

        ws.Range(1, 1, 1, colCount).Merge();
        ws.Cell(1, 1).Value = $"honshu  {t.OrderDetailTitle}";
        ws.Cell(1, 1).Style.Font.SetBold().Font.FontSize = 18;
        ws.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        // SUPPLIER (左) と order NO./order date/process NO. (右) を別ブロックにマージして重なりを防ぐ。
        var metaStart = Math.Max(8, colCount - 5);
        ws.Range(2, 1, 2, metaStart - 1).Merge();
        ws.Cell(2, 1).Value = $"{t.Supplier}: {officialName}  ({supplierCode})";
        ws.Cell(2, 1).Style.Font.SetBold().Font.FontSize = 12;
        ws.Range(2, metaStart, 2, colCount).Merge();
        ws.Cell(2, metaStart).Value =
            $"{t.OrderNo} {order.OrderNo ?? ""}   {t.OrderDate} {order.OrderDate?.ToString("yyyy/MM/dd") ?? ""}   {t.ProcessNo} {order.MgmtNo}";
        ws.Cell(2, metaStart).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        // ── 明細ヘッダ行 ──
        const int headerRow = 4;
        string DepartHeader(string? code) => string.IsNullOrEmpty(code) ? t.ColDepart : $"{t.ColDepart} {code}";
        var fixedHeaders = new[]
        {
            "", t.ColArtNo, t.ColColor, t.ColSize, t.ColProductName, t.ColBland, t.ColOrder,
            DepartHeader(order.Warehouse?.Code), DepartHeader(order.Warehouse2?.Code), DepartHeader(order.Warehouse3?.Code),
        };
        for (var i = 0; i < fixedHeaders.Length; i++)
            StyleHeaderCell(ws.Cell(headerRow, i + 1), fixedHeaders[i]);
        for (var i = 0; i < dates.Count; i++)
            StyleHeaderCell(ws.Cell(headerRow, fixedCols + i + 1), dates[i].ToString("MM/dd"));
        // 納入日 (shipping date) のグループ見出し (日付列が 1 つ以上あるときのみ)。
        if (dates.Count > 0)
        {
            ws.Range(headerRow - 1, fixedCols + 1, headerRow - 1, colCount).Merge();
            ws.Cell(headerRow - 1, fixedCols + 1).Value = t.ColShippingDate;
            ws.Cell(headerRow - 1, fixedCols + 1).Style.Font.SetBold().Font.FontSize = 9;
            ws.Cell(headerRow - 1, fixedCols + 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }
        ws.Row(headerRow).Height = 26;

        var wh1 = order.WarehouseId;
        var wh2 = order.Warehouse2Id;
        var wh3 = order.Warehouse3Id;

        var dateIndex = dates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        var dataRow = headerRow + 1;
        long totalOrder = 0;
        var totalDepart = new long[3];
        var totalByDate = new long[dates.Count];

        var bodyRows = Math.Max(lines.Count, 12);
        for (var r = 0; r < bodyRows; r++)
        {
            var row = dataRow + r;
            if (r < lines.Count)
            {
                var line = lines[r];
                var family = line.Product?.ProductFamily;
                var departs = OrderReportLineMath.Departs(line, wh1, wh2, wh3);

                ws.Cell(row, 1).Value = r + 1;
                ws.Cell(row, 2).Value = OrderReportLineMath.ArtNo(line.SkuSnapshot);
                ws.Cell(row, 3).Value = OrderReportLineMath.ColorCode(line.SkuSnapshot);
                ws.Cell(row, 4).Value = line.Product?.Size?.Name ?? "";
                ws.Cell(row, 5).Value = line.ProductNameSnapshot;
                ws.Cell(row, 6).Value = OrderReportLineMath.Bland(family);
                ws.Cell(row, 7).Value = line.Quantity;
                for (var g = 0; g < 3; g++)
                {
                    if (departs[g].Quantity > 0)
                    {
                        ws.Cell(row, 8 + g).Value = departs[g].Quantity;
                        totalDepart[g] += departs[g].Quantity;
                    }
                }

                // 納入日 (shipping date) 列: 当該明細の分納を納期ごとに合算 (同一明細で同一納期の
                // 複数倉庫分納がありうるため、明細ローカルに一旦集計してからセルへ書込む)。
                var lineByDate = new long[dates.Count];
                foreach (var d in line.Deliveries)
                    if (d.DeliveryDate is { } dd && dateIndex.TryGetValue(dd, out var di))
                        lineByDate[di] += d.Quantity;
                for (var di = 0; di < dates.Count; di++)
                {
                    if (lineByDate[di] > 0) ws.Cell(row, fixedCols + di + 1).Value = lineByDate[di];
                    totalByDate[di] += lineByDate[di];
                }

                totalOrder += line.Quantity;
            }
            for (var c = 1; c <= colCount; c++)
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Row(row).Height = 15;
        }

        // ── 合計行 ──
        var totalsRow = dataRow + bodyRows;
        ws.Cell(totalsRow, 1).Value = t.Total;
        ws.Cell(totalsRow, 1).Style.Font.SetBold();
        ws.Cell(totalsRow, 7).Value = totalOrder;
        ws.Cell(totalsRow, 7).Style.Font.SetBold();
        for (var g = 0; g < 3; g++)
        {
            if (totalDepart[g] > 0) ws.Cell(totalsRow, 8 + g).Value = totalDepart[g];
            ws.Cell(totalsRow, 8 + g).Style.Font.SetBold();
        }
        for (var i = 0; i < dates.Count; i++)
        {
            if (totalByDate[i] > 0) ws.Cell(totalsRow, fixedCols + i + 1).Value = totalByDate[i];
            ws.Cell(totalsRow, fixedCols + i + 1).Style.Font.SetBold();
        }
        for (var c = 1; c <= colCount; c++)
            ws.Cell(totalsRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        // ── フッタ (各種日付) ──
        var footRow = totalsRow + 2;
        void DateLine(int col, string label, DateOnly? value)
        {
            ws.Cell(footRow, col).Value = $"{label}: {value?.ToString("yyyy/MM/dd") ?? ""}";
            ws.Cell(footRow, col).Style.Font.FontSize = 9;
        }
        DateLine(1, t.FactoryShipping, order.FactoryShippingDate);
        DateLine(4, t.Shipping, order.DeliveryPlaceShippingDate);
        DateLine(7, t.Departure, order.OverseasDepartureDate);
        DateLine(9, t.Delivery, order.DueDate);

        // ── 列幅 ──
        ws.Column(1).Width = 3.5;
        ws.Column(2).Width = 10;
        ws.Column(3).Width = 5;
        ws.Column(4).Width = 5;
        ws.Column(5).Width = 22;
        ws.Column(6).Width = 5;
        ws.Column(7).Width = 8;
        for (var c = 8; c <= 10; c++) ws.Column(c).Width = 8;
        for (var c = fixedCols + 1; c <= colCount; c++) ws.Column(c).Width = 7;

        ws.SheetView.FreezeRows(headerRow);
    }

    private static void StyleHeaderCell(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Alignment.WrapText = true;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>ワークシート名を Excel 制約 (≤31 文字 / 禁止文字除去) に整え、重複を回避する。</summary>
    private static string UniqueSheetName(HashSet<string> used, string baseName, int seq)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var cleaned = new string(baseName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrEmpty(cleaned)) cleaned = $"order{seq}";
        if (cleaned.Length > 28) cleaned = cleaned[..28];
        var name = cleaned;
        var n = 2;
        while (!used.Add(name))
        {
            var suffix = $" ({n})";
            name = cleaned.Length + suffix.Length > 31 ? cleaned[..(31 - suffix.Length)] + suffix : cleaned + suffix;
            n++;
        }
        return name;
    }
}
