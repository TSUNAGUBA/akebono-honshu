namespace Akebono.Application.Orders;

/// <summary>
/// 発注書 Excel 出力 (Phase 5 §2.5 O-06、MVP クリティカルパス) の抽象。
/// 実装は Infrastructure 層の ClosedXML 版 (Iter 3) → 本テンプレ + セルマッピング版 (Iter 4) に切替予定。
///
/// 責務:
///  1. 初回出力 (first_exported_at IS NULL) の場合に order_no 採番 + 3 件 snapshot 凍結 (F-22)
///  2. last_exported_at 更新
///  3. PurchaseOrderExportLog INSERT
///  4. ClosedXML で .xlsx を byte[] 生成
///  5. audit_logs に Excel.Export 記録 (単価はマスク)
/// </summary>
public interface IPurchaseOrderExcelService
{
    /// <summary>Excel ファイルを生成して byte[] で返却。</summary>
    /// <returns>(FileName, ContentBytes)</returns>
    Task<(string FileName, byte[] Content)> ExportAsync(
        Guid purchaseOrderId, Guid actorUserId, CancellationToken ct = default);
}
