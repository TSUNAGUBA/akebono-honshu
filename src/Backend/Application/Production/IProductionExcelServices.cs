namespace Akebono.Application.Production;

/// <summary>生産指示書 Excel 出力 (PI-04)。Infrastructure 層で ClosedXML 実装。</summary>
public interface IProductionInstructionExcelService
{
    Task<(string FileName, byte[] Content)> ExportAsync(
        long productionInstructionId, long actorUserId, CancellationToken ct = default);
}

/// <summary>素材発注書 Excel 出力 (MO-04)。Infrastructure 層で ClosedXML 実装。</summary>
public interface IMaterialOrderExcelService
{
    Task<(string FileName, byte[] Content)> ExportAsync(
        long materialOrderId, long actorUserId, CancellationToken ct = default);
}
