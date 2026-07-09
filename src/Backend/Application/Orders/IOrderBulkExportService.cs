namespace Akebono.Application.Orders;

/// <summary>一括ダウンロード形式 (#3b)。発注書 / 管理表 / 発注書+管理表。</summary>
public enum BulkExportFormat
{
    /// <summary>発注書のみ (各発注を既存 IPurchaseOrderExcelService で出力し ZIP にまとめる)。</summary>
    Order,
    /// <summary>管理表のみ (全選択発注の明細を 1 シートに展開した単一 .xlsx)。</summary>
    Management,
    /// <summary>発注書 + 管理表 (管理表 .xlsx + 各発注の発注書 .xlsx を ZIP にまとめる)。</summary>
    Both,
}

/// <summary>
/// 一括ダウンロードリクエスト (POST /api/maker/v1/orders/bulk-export、#3b)。
/// orderIds は 1 件以上必須。format は order / management / both。
/// </summary>
public record BulkExportRequest(List<long> OrderIds, string Format);

/// <summary>
/// 一括ダウンロード結果。Content-Type / FileName / バイト列をまとめて返す
/// (xlsx 単体 か ZIP かは Format と件数に依存)。
/// </summary>
public record BulkExportResult(string FileName, string ContentType, byte[] Content);

/// <summary>
/// 発注一括ダウンロード (#3b)。発注一覧でチェックした発注を
/// 発注書 (ZIP) / 管理表 (xlsx) / 発注書+管理表 (ZIP) のいずれかで束ねて返す。
///
/// 発注書部分は既存 IPurchaseOrderExcelService をそのまま再利用する
/// (初回出力の snapshot 凍結 / order_no 採番 / export_log INSERT の副作用を発注ごとに保持)。
/// 管理表部分は読み取り専用 (IOrderManagementTableExcelService、副作用なし)。
/// </summary>
public interface IOrderBulkExportService
{
    /// <summary>
    /// 一括ダウンロードを実行。is_deleted の発注は skip し、残りで処理を続行する (原則4 非ブロッキング)。
    /// </summary>
    /// <exception cref="ArgumentException">orderIds が空 / format 不正 / 有効な発注が 0 件のとき。</exception>
    Task<BulkExportResult> ExportAsync(
        BulkExportRequest request, long actorUserId, CancellationToken ct = default);
}
