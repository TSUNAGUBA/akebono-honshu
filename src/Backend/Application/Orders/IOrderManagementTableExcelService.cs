namespace Akebono.Application.Orders;

/// <summary>
/// 管理表 Excel 出力 (#3b 一括ダウンロード)。社内管理用の一覧表。
/// 選択された発注の「明細 1 行 = 管理表 1 行」で全発注をまたいで 1 シートに展開する。
///
/// 発注書 (IPurchaseOrderExcelService) と異なり <b>読み取り専用</b>:
///  - snapshot 凍結なし / order_no 採番なし / export_log INSERT なし (F-22 の副作用を起こさない)
///  - 既存発注のデータを一切変更しない (集計・閲覧専用帳票)
/// </summary>
public interface IOrderManagementTableExcelService
{
    /// <summary>
    /// 選択発注 (orderIds) の全明細を 1 シートに展開した管理表 .xlsx を byte[] で返却。
    /// is_deleted の発注は呼び出し側 (OrderBulkExportService) で除外済みの id 集合を渡す想定。
    /// </summary>
    /// <returns>(FileName, ContentBytes)</returns>
    Task<(string FileName, byte[] Content)> ExportAsync(
        IReadOnlyList<long> orderIds, long actorUserId, CancellationToken ct = default);
}
