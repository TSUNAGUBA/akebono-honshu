using Akebono.Application.Common;
using Akebono.Application.Production;
using Akebono.Domain.Common;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 生産指示書 (加工指図書) Excel 出力の仮テンプレ版 (ClosedXML 動的生成、PI-04)。
/// 実帳票入手後にテンプレファイル方式へ差替予定 (I/F 不変、既存 Iter3 知見#5)。
/// 初回出力時に加工先名・品番のスナップショットを凍結 + first_exported_at SET。
/// </summary>
public class ProductionInstructionExcelService(IAkebonoDbContext db, IAuditLogger audit)
    : IProductionInstructionExcelService
{
    private const string TemplateVersion = "iter5-v1";

    public async Task<(string FileName, byte[] Content)> ExportAsync(
        Guid productionInstructionId, Guid actorUserId, CancellationToken ct = default)
    {
        var pi = await db.ProductionInstructions
            .Include(p => p.FactorySupplier)
            .Include(p => p.ProductFamily)
            .FirstOrDefaultAsync(p => p.Id == productionInstructionId, ct)
            ?? throw DomainException.Concealed($"生産指示 id={productionInstructionId} 不在");

        var lines = await db.ProductionInstructionLines
            .Include(l => l.Product).ThenInclude(p => p!.Color)
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Where(l => l.ProductionInstructionId == productionInstructionId)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        var isFirstExport = pi.FirstExportedAt is null;
        var now = SystemTime.UtcNow;
        var sku9 = lines.FirstOrDefault()?.SkuSnapshot is { Length: >= 9 } s ? s[..9] : (lines.FirstOrDefault()?.SkuSnapshot ?? "");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (isFirstExport)
            {
                pi.FactoryOfficialNameSnapshot = pi.FactorySupplier?.OfficialName ?? pi.FactorySupplier?.Name ?? "";
                pi.FactoryCodeSnapshot = pi.FactorySupplier?.Code;
                pi.ProductSku9Snapshot = sku9;
                pi.ProductNameSnapshot = pi.ProductFamily?.ProductName1;
                pi.FirstExportedAt = now;
            }
            pi.LastExportedAt = now;
            pi.UpdatedAt = now;
            pi.UpdatedByUserId = actorUserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("生産指示書");

        ws.Cell("A1").Value = "生産指示書";
        ws.Range("A1:F1").Merge().Style.Font.SetBold().Font.FontSize = 18;
        ws.Range("A1:F1").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var factoryName = pi.FactoryOfficialNameSnapshot ?? pi.FactorySupplier?.OfficialName ?? pi.FactorySupplier?.Name ?? "";
        var factoryCode = pi.FactoryCodeSnapshot ?? pi.FactorySupplier?.Code ?? "";
        ws.Cell("A3").Value = $"{factoryName} 御中 {factoryCode}";
        ws.Range("A3:F3").Merge().Style.Font.SetBold().Font.FontSize = 14;

        ws.Cell("A4").Value = $"生産指示番号: {pi.InstructionNo}";
        ws.Cell("D4").Value = $"希望納期: {pi.DueDate:yyyy-MM-dd}";
        ws.Cell("A5").Value = $"品番: {pi.ProductSku9Snapshot ?? sku9}";
        ws.Cell("D5").Value = $"商品名: {pi.ProductNameSnapshot ?? pi.ProductFamily?.ProductName1 ?? ""}";
        ws.Cell("A6").Value = $"生産数量(合計): {pi.PlannedQuantity}";

        var headerRow = 8;
        var headers = new[] { "No", "品番(SKU)", "色", "サイズ", "生産数量" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold();
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var dataRow = headerRow + 1;
        foreach (var line in lines)
        {
            ws.Cell(dataRow, 1).Value = (int)line.LineNo;
            ws.Cell(dataRow, 2).Value = line.SkuSnapshot;
            ws.Cell(dataRow, 3).Value = line.Product?.Color?.Name ?? "";
            ws.Cell(dataRow, 4).Value = line.Product?.Size?.Name ?? "";
            ws.Cell(dataRow, 5).Value = line.Quantity;
            for (var col = 1; col <= 5; col++)
                ws.Cell(dataRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRow++;
        }

        if (!string.IsNullOrEmpty(pi.CommunicationText))
        {
            var commRow = dataRow + 2;
            ws.Cell(commRow, 1).Value = "連絡事項:";
            ws.Cell(commRow, 1).Style.Font.SetBold();
            ws.Cell(commRow + 1, 1).Value = pi.CommunicationText;
            ws.Range(commRow + 1, 1, commRow + 1, 5).Merge().Style.Alignment.SetWrapText(true);
        }

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        wb.SaveAs(stream);

        await audit.LogAsync(actorUserId, "ProductionInstruction.Export",
            entityType: "ProductionInstruction", entityId: productionInstructionId,
            note: $"instruction_no={pi.InstructionNo}, is_first={isFirstExport}, tmpl={TemplateVersion}", cancellationToken: ct);

        var fileName = $"PI_{pi.InstructionNo}_{SystemTime.JstNow:yyyyMMdd_HHmmss}.xlsx"; // ファイル名は業務時刻 (JST)
        return (fileName, stream.ToArray());
    }
}
