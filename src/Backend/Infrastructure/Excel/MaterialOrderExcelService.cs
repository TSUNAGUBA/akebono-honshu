using Akebono.Application.Common;
using Akebono.Application.Production;
using Akebono.Domain.Common;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 素材発注書 (生地材料発注) Excel 出力の仮テンプレ版 (ClosedXML 動的生成、MO-04)。
/// 初回出力時に素材仕入先名のスナップショットを凍結 + first_exported_at SET (既存 F-22 と同方針)。
/// 素材単価を帳票には出すが監査ログには金額を残さない (機密度 中-高)。
/// </summary>
public class MaterialOrderExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IMaterialOrderExcelService
{
    private const string TemplateVersion = "iter5-v1";

    public async Task<(string FileName, byte[] Content)> ExportAsync(
        long materialOrderId, long actorUserId, CancellationToken ct = default)
    {
        var order = await db.MaterialOrders
            .Include(o => o.MaterialSupplier)
            .FirstOrDefaultAsync(o => o.Id == materialOrderId, ct)
            ?? throw DomainException.Concealed($"素材発注 id={materialOrderId} 不在");

        var lines = await db.MaterialOrderLines
            .Include(l => l.Material)
            .Where(l => l.MaterialOrderId == materialOrderId)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        var isFirstExport = order.FirstExportedAt is null;
        var now = SystemTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (isFirstExport)
            {
                order.SupplierOfficialNameSnapshot = order.MaterialSupplier?.OfficialName ?? order.MaterialSupplier?.Name ?? "";
                order.SupplierCodeSnapshot = order.MaterialSupplier?.Code;
                order.FirstExportedAt = now;
            }
            order.LastExportedAt = now;
            order.UpdatedAt = now;
            order.UpdatedByUserId = actorUserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("素材発注書");

        ws.Cell("A1").Value = "素材発注書";
        ws.Range("A1:F1").Merge().Style.Font.SetBold().Font.FontSize = 18;
        ws.Range("A1:F1").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var supplierName = order.SupplierOfficialNameSnapshot ?? order.MaterialSupplier?.OfficialName ?? order.MaterialSupplier?.Name ?? "";
        var supplierCode = order.SupplierCodeSnapshot ?? order.MaterialSupplier?.Code ?? "";
        ws.Cell("A3").Value = $"{supplierName} 御中 {supplierCode}";
        ws.Range("A3:F3").Merge().Style.Font.SetBold().Font.FontSize = 14;

        ws.Cell("A4").Value = $"発注番号: {order.OrderNo}";
        ws.Cell("D4").Value = $"納入希望日: {order.DueDate:yyyy-MM-dd}";

        var headerRow = 6;
        var headers = new[] { "No", "素材", "数量", "単位", "単価", "金額" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var dataRow = headerRow + 1;
        decimal total = 0m;
        string currency = "JPY";
        foreach (var line in lines)
        {
            ws.Cell(dataRow, 1).Value = (int)line.LineNo;
            ws.Cell(dataRow, 2).Value = line.Material?.Name ?? line.MaterialNameSnapshot;
            ws.Cell(dataRow, 3).Value = line.RequiredQuantity;
            ws.Cell(dataRow, 3).Style.NumberFormat.Format = "#,##0.0000";
            ws.Cell(dataRow, 4).Value = line.Unit;
            if (line.UnitPrice is { } up)
            {
                ws.Cell(dataRow, 5).Value = up;
                ws.Cell(dataRow, 5).Style.NumberFormat.Format = "#,##0.00";
            }
            else
            {
                ws.Cell(dataRow, 5).Value = "(未設定)";
            }
            ws.Cell(dataRow, 6).Value = line.Subtotal;
            ws.Cell(dataRow, 6).Style.NumberFormat.Format = "#,##0.00";
            for (var col = 1; col <= 6; col++)
                ws.Cell(dataRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            total += line.Subtotal;
            currency = line.CurrencyCode;
            dataRow++;
        }

        ws.Cell(dataRow, 5).Value = $"合計({currency})";
        ws.Cell(dataRow, 5).Style.Font.SetBold();
        ws.Cell(dataRow, 6).Value = total;
        ws.Cell(dataRow, 6).Style.Font.SetBold().NumberFormat.Format = "#,##0.00";

        if (!string.IsNullOrEmpty(order.CommunicationText))
        {
            var commRow = dataRow + 2;
            ws.Cell(commRow, 1).Value = "連絡事項:";
            ws.Cell(commRow, 1).Style.Font.SetBold();
            ws.Cell(commRow + 1, 1).Value = order.CommunicationText;
            ws.Range(commRow + 1, 1, commRow + 1, 6).Merge().Style.Alignment.SetWrapText(true);
        }

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        await audit.LogAsync(actorUserId, "MaterialOrder.Export",
            entityType: "MaterialOrder", entityId: materialOrderId,
            note: $"order_no={order.OrderNo}, is_first={isFirstExport}, total=***, tmpl={TemplateVersion}", cancellationToken: ct);

        var fileName = $"MO_{order.OrderNo}_{SystemTime.JstNow:yyyyMMdd_HHmmss}.xlsx"; // ファイル名は業務時刻 (JST)
        return (fileName, stream.ToArray());
    }
}
