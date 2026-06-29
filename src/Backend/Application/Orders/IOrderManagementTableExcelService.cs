namespace Akebono.Application.Orders;

/// <summary>
/// 管理表 (ORDER DETAIL) Excel 出力。旧システムの管理表帳票レイアウトに合わせ、
/// 「1 発注 = 1 ワークシート」で出力する (発注書と共通ヘッダ + art NO/color/size/product name/bland/order/
/// depart 倉庫×3 / shipping date 列 (納入日=分納の納期を日付ごとに動的展開) / 合計)。
///
/// 発注書 (IPurchaseOrderExcelService) と異なり <b>読み取り専用</b>:
///  - snapshot 凍結なし / order_no 採番なし / export_log INSERT なし (F-22 の副作用を起こさない)
///  - 既存発注のデータを一切変更しない (集計・閲覧専用帳票)
/// </summary>
public interface IOrderManagementTableExcelService
{
    /// <summary>
    /// 選択発注 (orderIds) を「1 発注 = 1 ワークシート」で展開した管理表 .xlsx を byte[] で返却。
    /// is_deleted の発注は呼び出し側 (OrderBulkExportService) で除外済みの id 集合を渡す想定。
    /// </summary>
    /// <returns>(FileName, ContentBytes)</returns>
    Task<(string FileName, byte[] Content)> ExportAsync(
        IReadOnlyList<long> orderIds, long actorUserId, CancellationToken ct = default);
}
