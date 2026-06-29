using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Application.Orders;
using Akebono.Domain.Orders;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 発注書 (ORDER SHEET) Excel 出力。旧システムの発注書帳票レイアウトに合わせる
/// (ヘッダ: SUPPLIER / order NO. / order date / process NO. / 仕入先コード、
///  明細: art NO / color / size / product name / bland / order / assort box /
///        depart 群×3 (Q'yt of box / Q'yt in box / depart 倉庫コード) / estimate price / order price / remarks、
///  フッタ: 連絡欄 / Factory Shipping・Shipping・Departure・Delivery / 納品先・受注先・Port of entry /
///          発注者 / 責任者・管理部・担当者 捺印欄)。
/// 表記は発注区分で切替: 国内=日本語 / 海外=英語 (<see cref="OrderReportText"/>)。
///
/// 副作用 (従来どおり): 初回出力時に 3 件 snapshot 凍結 (F-22) + first_exported_at SET、
/// last_exported_at 更新、PurchaseOrderExportLog INSERT、audit_logs 記録。
/// 発注番号 (order_no) は帳票出力フォームで手入力する方式に変更したため、本サービスでは
/// order.OrderNo が未設定 (null) のときのみ自動採番にフォールバックする (後方互換)。
/// </summary>
public class PurchaseOrderExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IPurchaseOrderExcelService
{
    private const string TemplateVersion = "ordersheet-v1";
    private const int ColCount = 20; // A..T

    public async Task<(string FileName, byte[] Content)> ExportAsync(
        long purchaseOrderId, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Department)
            .Include(o => o.Warehouse)
            .Include(o => o.Warehouse2)
            .Include(o => o.Warehouse3)
            .Include(o => o.Orderer)
            .Include(o => o.Manager)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct)
            ?? throw new InvalidOperationException($"発注書 id={purchaseOrderId} 不在");

        var lines = await db.PurchaseOrderLines
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Include(l => l.Product).ThenInclude(p => p!.ProductFamily)
            .Include(l => l.Deliveries)
            .Where(l => l.PurchaseOrderId == purchaseOrderId)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        var isFirstExport = order.FirstExportedAt is null;
        var now = SystemTime.Now;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 初回出力: 3 件 snapshot 凍結 (F-22)。発注番号は手入力フォーム方式へ移行したため、
            // 既に order.OrderNo が設定済 (フォーム入力 or 過去採番) ならそれを使い、未設定なら自動採番する。
            if (isFirstExport)
            {
                order.OrderNo ??= await GenerateOrderNoAsync(ct);
                order.SupplierOfficialNameSnapshot = order.Supplier?.OfficialName ?? order.Supplier?.Name ?? "";
                order.SupplierCodeSnapshot = order.Supplier?.Code;
                order.CustomerNameSnapshot = order.DeliveryDestination?.CustomerName;
                order.FirstExportedAt = now;
            }
            else
            {
                // 2 回目以降でも、未採番のまま (旧データ) なら採番しておく (order_no 列の一意性を維持)。
                order.OrderNo ??= await GenerateOrderNoAsync(ct);
            }
            order.LastExportedAt = now;
            order.UpdatedAt = now;
            order.UpdatedByUserId = actorUserId;

            db.PurchaseOrderExportLogs.Add(new PurchaseOrderExportLog
            {
                PurchaseOrderId = order.Id,
                ExportedAt = now,
                ExportedByUserId = actorUserId,
                IsFirstExport = isFirstExport,
                ExcelTemplateVersion = TemplateVersion,
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        var t = OrderReportText.For(order.IsOverseas);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(t.OrderSheetTitle);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;

        BuildHeaderBlock(ws, order, t);
        var totalsRow = BuildLineTable(ws, order, lines, t);
        BuildFooterBlock(ws, order, t, totalsRow);

        SetColumnWidths(ws);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        await audit.LogAsync(actorUserId, "PurchaseOrder.Export",
            entityType: "PurchaseOrder", entityId: purchaseOrderId,
            note: $"mgmt_no={order.MgmtNo}, order_no={order.OrderNo}, is_first={isFirstExport}, region={(order.IsOverseas ? "overseas" : "domestic")}, total_amount=***",
            cancellationToken: ct);

        var fileName = $"{t.OrderSheetTitle}_{order.OrderNo ?? order.MgmtNo}_{now:yyyyMMdd_HHmmss}.xlsx";
        return (fileName, stream.ToArray());
    }

    // ── ヘッダ部 (タイトル / SUPPLIER / 仕入先コード / order NO. / order date / process NO. / 出荷指示番号) ──
    private static void BuildHeaderBlock(IXLWorksheet ws, PurchaseOrder order, OrderReportText t)
    {
        ws.Range(1, 1, 1, ColCount).Merge();
        var title = ws.Cell(1, 1);
        title.Value = $"honshu  {t.OrderSheetTitle}";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 20;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var officialName = order.SupplierOfficialNameSnapshot ?? order.Supplier?.OfficialName ?? order.Supplier?.Name ?? "";
        var supplierCode = order.SupplierCodeSnapshot ?? order.Supplier?.Code ?? "";

        // SUPPLIER (左)
        ws.Range(2, 1, 2, 8).Merge();
        var sup = ws.Cell(2, 1);
        sup.Value = $"{t.Supplier}: {officialName}";
        sup.Style.Font.Bold = true;
        sup.Style.Font.FontSize = 13;

        // 仕入先コード (中央、旧帳票の "434" 相当)
        ws.Range(3, 7, 3, 9).Merge();
        var code = ws.Cell(3, 7);
        code.Value = supplierCode;
        code.Style.Font.Bold = true;
        code.Style.Font.FontSize = 13;
        code.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // order NO. (右、大きく) + ページ
        var onLabel = ws.Cell(2, 14);
        onLabel.Value = t.OrderNo;
        onLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(2, 15, 2, 18).Merge();
        var onVal = ws.Cell(2, 15);
        onVal.Value = order.OrderNo ?? "";
        onVal.Style.Font.Bold = true;
        onVal.Style.Font.FontSize = 16;
        ws.Range(2, 19, 2, 20).Merge();
        var page = ws.Cell(2, 19);
        page.Value = string.Format(t.Page, 1, 1);
        page.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        // order date + process NO. (右、order NO. の下)
        var odLabel = ws.Cell(3, 14);
        odLabel.Value = t.OrderDate;
        odLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(3, 15, 3, 16).Merge();
        ws.Cell(3, 15).Value = order.OrderDate?.ToString("yyyy/MM/dd") ?? "";
        var pnLabel = ws.Cell(3, 17);
        pnLabel.Value = t.ProcessNo;
        pnLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(3, 18, 3, 20).Merge();
        ws.Cell(3, 18).Value = order.MgmtNo;

        // 出荷指示番号 (右、process NO. の下)。未入力なら空欄。
        var siLabel = ws.Cell(4, 14);
        siLabel.Value = t.ShippingInstructionNo;
        siLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(4, 15, 4, 18).Merge();
        ws.Cell(4, 15).Value = order.ShippingInstructionNo ?? "";
    }

    // ── 明細テーブル。戻り値 = 合計行 (フッタ配置に使う)。 ──
    private static int BuildLineTable(
        IXLWorksheet ws, PurchaseOrder order, List<PurchaseOrderLine> lines, OrderReportText t)
    {
        const int headerRow = 6;

        string DepartHeader(string? c) => string.IsNullOrEmpty(c) ? t.ColDepart : $"{t.ColDepart} {c}";
        var headers = new[]
        {
            "", t.ColArtNo, t.ColColor, t.ColSize, t.ColProductName, t.ColBland, t.ColOrder, t.ColAssortBox,
            t.ColQtyOfBox, t.ColQtyInBox, DepartHeader(order.Warehouse?.Code),
            t.ColQtyOfBox, t.ColQtyInBox, DepartHeader(order.Warehouse2?.Code),
            t.ColQtyOfBox, t.ColQtyInBox, DepartHeader(order.Warehouse3?.Code),
            t.ColEstimatePrice, t.ColOrderPrice, t.ColRemarks,
        };
        for (var i = 0; i < headers.Length; i++)
            StyleHeaderCell(ws.Cell(headerRow, i + 1), headers[i]);
        ws.Row(headerRow).Height = 30;

        var wh1 = order.WarehouseId;
        var wh2 = order.Warehouse2Id;
        var wh3 = order.Warehouse3Id;

        var bodyRows = Math.Max(lines.Count, 14);
        var dataRow = headerRow + 1;
        long totalOrder = 0;
        var totalBox = new long[3];
        var totalDepart = new long[3];

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
                // col 8 assort box: per-line アソート箱情報が現データに無いため空欄 (旧帳票も大半は空欄)。

                var col = 9;
                for (var g = 0; g < 3; g++)
                {
                    var d = departs[g];
                    if (d.Quantity > 0)
                    {
                        if (d.BoxCount is { } bc) { ws.Cell(row, col).Value = bc; totalBox[g] += bc; }
                        if (d.PackQuantity is { } pk) ws.Cell(row, col + 1).Value = pk;
                        ws.Cell(row, col + 2).Value = d.Quantity;
                        totalDepart[g] += d.Quantity;
                    }
                    col += 3;
                }

                if (line.EstimateUnitPrice is { } est) ws.Cell(row, 18).Value = est;
                ws.Cell(row, 18).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 19).Value = line.UnitPriceSnapshot;
                ws.Cell(row, 19).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 20).Value = line.Remark ?? "";

                totalOrder += line.Quantity;
            }
            for (var c = 1; c <= ColCount; c++)
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Row(row).Height = 16;
        }

        // 合計行
        var totalsRow = dataRow + bodyRows;
        ws.Cell(totalsRow, 7).Value = totalOrder;
        ws.Cell(totalsRow, 7).Style.Font.Bold = true;
        var tcol = 9;
        for (var g = 0; g < 3; g++)
        {
            if (totalBox[g] > 0) ws.Cell(totalsRow, tcol).Value = totalBox[g];
            if (totalDepart[g] > 0) ws.Cell(totalsRow, tcol + 2).Value = totalDepart[g];
            ws.Cell(totalsRow, tcol).Style.Font.Bold = true;
            ws.Cell(totalsRow, tcol + 2).Style.Font.Bold = true;
            tcol += 3;
        }
        for (var c = 1; c <= ColCount; c++)
            ws.Cell(totalsRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        return totalsRow;
    }

    // ── フッタ部 (Order Stamp / 連絡欄 / 各種日付 / 納品先・受注先・Port of entry / 発注者 / 責任者・管理部・担当者) ──
    private static void BuildFooterBlock(IXLWorksheet ws, PurchaseOrder order, OrderReportText t, int totalsRow)
    {
        // 発注者: 事業部 + 発注担当者 (合計行の右側に印字)。
        ws.Range(totalsRow, 15, totalsRow, 20).Merge();
        var orderer = ws.Cell(totalsRow, 15);
        orderer.Value = $"{t.Orderer}: {order.Department?.Name ?? ""}  {order.Orderer?.DisplayName ?? ""}";
        orderer.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var top = totalsRow + 2;

        // Order Stamp (左上の捺印枠)
        var stamp = ws.Cell(top, 1);
        stamp.Value = t.OrderStamp;
        stamp.Style.Font.FontSize = 9;
        ws.Range(top, 1, top + 4, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        // 連絡欄 (中央、連絡文書 6 行 or communication_text フォールバック)。
        var comm = ws.Cell(top, 5);
        comm.Value = t.Communication;
        comm.Style.Font.Bold = true;
        comm.Style.Font.FontSize = 9;
        var commLines = new[]
        {
            order.CommunicationLine1, order.CommunicationLine2, order.CommunicationLine3,
            order.CommunicationLine4, order.CommunicationLine5, order.CommunicationLine6,
        }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        var commBody = commLines.Count > 0 ? string.Join("\n", commLines) : (order.CommunicationText ?? "");
        ws.Range(top + 1, 5, top + 7, 13).Merge();
        var commCell = ws.Cell(top + 1, 5);
        commCell.Value = commBody;
        commCell.Style.Alignment.WrapText = true;
        commCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Range(top, 5, top + 7, 13).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        // 責任者 / 管理部 / 担当者 (右、3 段の捺印枠)
        var stampLabels = new[] { t.Responsible, t.ManagerDept, t.Staff };
        for (var i = 0; i < 3; i++)
        {
            var r1 = top + i * 3;
            ws.Range(r1, 14, r1 + 2, 20).Merge();
            ws.Range(r1, 14, r1 + 2, 20).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            var lbl = ws.Cell(r1, 14);
            lbl.Value = stampLabels[i];
            lbl.Style.Font.FontSize = 9;
            lbl.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }

        // 各種日付 (左下)
        var dateRow = top + 9;
        void DateLine(int row, string label, DateOnly? value)
        {
            var lc = ws.Cell(row, 1);
            lc.Value = label;
            lc.Style.Font.FontSize = 9;
            ws.Range(row, 2, row, 4).Merge();
            var vc = ws.Cell(row, 2);
            vc.Value = value?.ToString("yyyy/MM/dd") ?? "";
            vc.Style.Font.FontSize = 9;
            vc.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        DateLine(dateRow, t.FactoryShipping, order.FactoryShippingDate);
        DateLine(dateRow + 1, t.Shipping, order.DeliveryPlaceShippingDate);
        DateLine(dateRow + 2, t.Departure, order.OverseasDepartureDate);
        DateLine(dateRow + 3, t.Delivery, order.DueDate);

        // Port of entry / 納品先 / 受注先 (中央下)
        void RefLine(int row, string label, string? value)
        {
            var lc = ws.Cell(row, 5);
            lc.Value = $"{label}:";
            lc.Style.Font.FontSize = 9;
            ws.Range(row, 6, row, 10).Merge();
            var vc = ws.Cell(row, 6);
            vc.Value = value ?? "";
            vc.Style.Font.FontSize = 9;
        }
        RefLine(dateRow, t.PortOfEntry, order.LandingPlace);
        RefLine(dateRow + 1, t.DeliveryTo, order.DeliveryDestination?.Name);
        RefLine(dateRow + 2, t.OrderTo, order.CustomerRef);

        // 会社フッタ (最下部、中央〜右)
        var footRow = dateRow + 4;
        ws.Range(footRow, 5, footRow, 20).Merge();
        var foot = ws.Cell(footRow, 5);
        foot.Value = t.CompanyFooter;
        foot.Style.Font.FontSize = 8;
        foot.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
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

    private static void SetColumnWidths(IXLWorksheet ws)
    {
        ws.Column(1).Width = 3.5;
        ws.Column(2).Width = 10;
        ws.Column(3).Width = 5;
        ws.Column(4).Width = 5;
        ws.Column(5).Width = 22;
        ws.Column(6).Width = 5;
        ws.Column(7).Width = 7;
        ws.Column(8).Width = 6;
        for (var c = 9; c <= 17; c++) ws.Column(c).Width = 7;
        ws.Column(18).Width = 9;
        ws.Column(19).Width = 9;
        ws.Column(20).Width = 18;
    }

    /// <summary>order_no 採番 (例: "S00001")。"S" + 5 桁ゼロ埋め連番、全期間通し。手入力が無いときのフォールバック。</summary>
    private async Task<string> GenerateOrderNoAsync(CancellationToken ct)
    {
        var existing = await db.PurchaseOrders
            .Where(o => o.OrderNo != null && o.OrderNo!.StartsWith("S"))
            .Select(o => o.OrderNo!)
            .ToListAsync(ct);
        var maxSeq = existing
            .Select(o => o.Length > 1 && int.TryParse(o.AsSpan(1), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"S{maxSeq + 1:D5}";
    }
}
