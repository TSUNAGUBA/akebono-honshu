using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Application.Orders;
using Akebono.Domain.Orders;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 発注書 Excel 出力の Iter 3 仮テンプレ版 (ClosedXML 動的生成)。
/// Iter 4 Hardening で業務担当者提供の本テンプレ (templates/purchase-order-domestic.xlsx) +
/// セルマッピング版に置換予定。本実装の I/F は同一。
///
/// レイアウト (仮):
///  - A1:G1 タイトル「発注書」
///  - A3 「<official_name> 御中 <supplier_code>」(F-22 帳票宛名)
///  - A4 「mgmt_no: ..., order_no: ..., due_date: ...」
///  - A6 表ヘッダ (line_no / sku / 商品名 / 数量 / 単価 / 小計 / 通貨)
///  - A7〜 明細
///  - 末尾「連絡文章: <communication_text>」
/// </summary>
public class PurchaseOrderExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IPurchaseOrderExcelService
{
    private const string TemplateVersion = "iter3-v1";

    public async Task<(string FileName, byte[] Content)> ExportAsync(
        long purchaseOrderId, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.DeliveryDestination)
            .Include(o => o.Department)
            .Include(o => o.Warehouse)
            .Include(o => o.Orderer)
            .Include(o => o.Manager)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct)
            ?? throw new InvalidOperationException($"発注書 id={purchaseOrderId} 不在");

        var lines = await db.PurchaseOrderLines
            // 分納×倉庫の多次元明細 (PR5b)。倉庫名解決のため Warehouse ナビも Include。
            .Include(l => l.Deliveries).ThenInclude(d => d.Warehouse)
            .Where(l => l.PurchaseOrderId == purchaseOrderId)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        var isFirstExport = order.FirstExportedAt is null;
        var now = SystemTime.Now;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 初回出力: order_no 採番 + 3 件 snapshot 凍結 (F-22)
            if (isFirstExport)
            {
                order.OrderNo = await GenerateOrderNoAsync(ct);
                order.SupplierOfficialNameSnapshot = order.Supplier?.OfficialName ?? order.Supplier?.Name ?? "";
                order.SupplierCodeSnapshot = order.Supplier?.Code;
                order.CustomerNameSnapshot = order.DeliveryDestination?.CustomerName;
                order.FirstExportedAt = now;
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

        // Excel 生成 (動的)
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("発注書");

        ws.Cell("A1").Value = "発注書";
        ws.Range("A1:G1").Merge().Style.Font.SetBold().Font.FontSize = 18;
        ws.Range("A1:G1").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        // 宛名 (F-22: <official_name> 御中 <supplier_code>)
        var officialName = order.SupplierOfficialNameSnapshot ?? order.Supplier?.OfficialName ?? order.Supplier?.Name ?? "";
        var supplierCode = order.SupplierCodeSnapshot ?? order.Supplier?.Code ?? "";
        ws.Cell("A3").Value = $"{officialName} 御中 {supplierCode}";
        ws.Range("A3:G3").Merge().Style.Font.SetBold().Font.FontSize = 14;

        ws.Cell("A4").Value = $"管理番号: {order.MgmtNo}";
        ws.Cell("C4").Value = $"発注番号: {order.OrderNo ?? "(未採番)"}";
        ws.Cell("E4").Value = $"納入日: {order.DueDate:yyyy-MM-dd}";
        ws.Cell("A5").Value = $"納品先: {order.DeliveryDestination?.Name ?? ""}";
        ws.Cell("C5").Value = $"発注事業部: {order.Department?.Name ?? ""}";
        ws.Cell("E5").Value = $"納入倉庫: {order.Warehouse?.Name ?? ""}";

        // 表ヘッダ
        var headerRow = 7;
        var headers = new[] { "No", "品番", "商品名", "数量", "単価", "通貨", "小計" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // 明細
        var dataRow = headerRow + 1;
        decimal totalAmount = 0m;
        string currency = "JPY";
        foreach (var line in lines)
        {
            ws.Cell(dataRow, 1).Value = (int)line.LineNo;
            ws.Cell(dataRow, 2).Value = line.SkuSnapshot;
            ws.Cell(dataRow, 3).Value = line.ProductNameSnapshot;
            // 数量 = line.Quantity。分納あり明細では line.Quantity = 分納数量の合計 (SUM、PR5b) のため、
            // 明細合計数量・小計は従来表示を維持する。分納内訳は下の追記行で出力する。
            ws.Cell(dataRow, 4).Value = line.Quantity;
            ws.Cell(dataRow, 5).Value = line.UnitPriceSnapshot;
            ws.Cell(dataRow, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(dataRow, 6).Value = line.CurrencyCodeSnapshot;
            ws.Cell(dataRow, 7).Value = line.Subtotal;
            ws.Cell(dataRow, 7).Style.NumberFormat.Format = "#,##0.00";
            for (var col = 1; col <= 7; col++)
                ws.Cell(dataRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            totalAmount += line.Subtotal;
            currency = line.CurrencyCodeSnapshot;
            dataRow++;

            // 分納×倉庫の多次元明細 (PR5b): 分納がある明細は、明細直下に「分納スケジュール」追記行を
            // 1 分納 = 1 行で出力する (倉庫 / 納期 / 数量 / 入数)。金額・合計には影響しない (明細行の
            // line.Subtotal が SUM ベースで既に正しいため二重計上しない)。商品名列に内訳を寄せて表示する。
            if (line.Deliveries.Count > 0)
            {
                foreach (var d in line.Deliveries.OrderBy(x => x.Seq).ThenBy(x => x.Id))
                {
                    var wh = d.Warehouse?.Name ?? "倉庫未指定";
                    var date = d.DeliveryDate?.ToString("yyyy-MM-dd") ?? "(発注明細日)";
                    var pack = d.PackQuantity is { } p ? $" 入数 {p}" : "";
                    ws.Cell(dataRow, 3).Value = $"  └ 分納: {wh} / {date}{pack}";
                    ws.Cell(dataRow, 3).Style.Font.SetItalic().Font.FontColor = XLColor.Gray;
                    ws.Cell(dataRow, 4).Value = d.Quantity;
                    for (var col = 1; col <= 7; col++)
                        ws.Cell(dataRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRow++;
                }
            }
        }

        // 合計行
        ws.Cell(dataRow, 6).Value = "合計";
        ws.Cell(dataRow, 6).Style.Font.SetBold();
        ws.Cell(dataRow, 7).Value = totalAmount;
        ws.Cell(dataRow, 7).Style.Font.SetBold().NumberFormat.Format = "#,##0.00";

        // 連絡文章
        if (!string.IsNullOrEmpty(order.CommunicationText))
        {
            var commRow = dataRow + 2;
            ws.Cell(commRow, 1).Value = "連絡文章:";
            ws.Cell(commRow, 1).Style.Font.SetBold();
            ws.Cell(commRow + 1, 1).Value = order.CommunicationText;
            ws.Range(commRow + 1, 1, commRow + 1, 7).Merge().Style.Alignment.SetWrapText(true);
        }

        // 列幅自動調整
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        await audit.LogAsync(actorUserId, "PurchaseOrder.Export",
            entityType: "PurchaseOrder", entityId: purchaseOrderId,
            note: $"mgmt_no={order.MgmtNo}, order_no={order.OrderNo}, is_first={isFirstExport}, total_amount=***",
            cancellationToken: ct);

        var fileName = $"PO_{order.OrderNo ?? order.MgmtNo}_{now:yyyyMMdd_HHmmss}.xlsx";
        return (fileName, stream.ToArray());
    }

    /// <summary>order_no 採番 (例: "S00001")。"S" + 5 桁ゼロ埋め連番、全期間通し。</summary>
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
