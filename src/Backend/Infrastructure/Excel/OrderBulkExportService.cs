using System.IO.Compression;
using Akebono.Application.Common;
using Akebono.Application.Orders;
using Akebono.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 発注一括ダウンロード (#3b) の組み立て。
/// 発注書は既存 IPurchaseOrderExcelService を発注ごとに呼び出して再利用し
/// (snapshot 凍結 / order_no 採番 / export_log INSERT の副作用を保持)、
/// 管理表は IOrderManagementTableExcelService (読み取り専用) で生成する。
/// ZIP は BCL (System.IO.Compression) のみで構築 (新規 NuGet なし)。
/// </summary>
public class OrderBulkExportService(
    IAkebonoDbContext db,
    IPurchaseOrderExcelService orderExcel,
    IOrderManagementTableExcelService managementExcel,
    IAuditLogger audit,
    ILogger<OrderBulkExportService> logger)
    : IOrderBulkExportService
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string ZipContentType = "application/zip";

    public async Task<BulkExportResult> ExportAsync(
        BulkExportRequest request, long actorUserId, CancellationToken ct = default)
    {
        if (request.OrderIds is null || request.OrderIds.Count == 0)
            throw new ArgumentException("発注を 1 件以上選択してください (BULK-001)");

        var format = ParseFormat(request.Format);

        // 入力 id の重複を除去しつつ選択順を保つ。
        var requestedIds = request.OrderIds.Distinct().ToList();

        // is_deleted の発注は対象外 (skip して続行、原則4 非ブロッキング)。
        // 存在する非削除発注の id 集合を 1 クエリで取得し、選択順でフィルタする。
        var validIdSet = (await db.PurchaseOrders
                .Where(o => requestedIds.Contains(o.Id) && !o.IsDeleted)
                .Select(o => o.Id)
                .ToListAsync(ct))
            .ToHashSet();
        var targetIds = requestedIds.Where(validIdSet.Contains).ToList();

        if (targetIds.Count == 0)
            throw new ArgumentException("選択された発注に有効な (削除されていない) 発注がありません (BULK-002)");

        var now = SystemTime.Now;
        var stamp = now.ToString("yyyyMMdd");

        return format switch
        {
            BulkExportFormat.Management => await BuildManagementOnlyAsync(targetIds, actorUserId, ct),
            BulkExportFormat.Order => await BuildOrderZipAsync(targetIds, actorUserId, stamp, includeManagement: false, ct),
            BulkExportFormat.Both => await BuildOrderZipAsync(targetIds, actorUserId, stamp, includeManagement: true, ct),
            _ => throw new ArgumentException($"未対応の format です: {request.Format} (BULK-003)"),
        };
    }

    /// <summary>管理表のみ: 単一 .xlsx をそのまま返す (ZIP にしない)。</summary>
    private async Task<BulkExportResult> BuildManagementOnlyAsync(
        IReadOnlyList<long> targetIds, long actorUserId, CancellationToken ct)
    {
        var (fileName, content) = await managementExcel.ExportAsync(targetIds, actorUserId, ct);
        return new BulkExportResult(fileName, XlsxContentType, content);
    }

    /// <summary>
    /// 発注書 (+任意で管理表) を ZIP にまとめて返す。
    /// 発注書は 1 件でも一律 ZIP で返す (UI/ファイル名の一貫性のため)。
    /// 各発注の Excel 生成は既存サービス経由 = 初回出力の副作用 (凍結/採番/ログ) を発注ごとに保持する。
    /// 1 発注の生成に失敗しても他発注は続行する (原則4 非ブロッキング)。失敗は ILogger + 監査ログに記録し
    /// 握りつぶさない。発注書が 1 件も成功しなかった場合のみ全体を失敗 (BULK-004) とする。
    /// </summary>
    private async Task<BulkExportResult> BuildOrderZipAsync(
        IReadOnlyList<long> targetIds, long actorUserId, string stamp, bool includeManagement, CancellationToken ct)
    {
        using var zipStream = new MemoryStream();
        var succeeded = 0;
        var failedIds = new List<long>();
        // leaveOpen: true で ZipArchive Dispose 後も zipStream を読めるようにする。
        // using ブロックを抜けた時点で Zip の中央ディレクトリが flush される。
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 管理表 (both のみ)。読み取り専用なので発注書より先でも後でも副作用なし。
            if (includeManagement)
            {
                var (mgmtName, mgmtBytes) = await managementExcel.ExportAsync(targetIds, actorUserId, ct);
                await WriteEntryAsync(archive, mgmtName, mgmtBytes, ct);
            }

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in targetIds)
            {
                // キャンセル要求は中断扱い (部分結果も返さない)。OperationCanceledException は再スロー。
                ct.ThrowIfCancellationRequested();
                try
                {
                    // 既存サービスで発注書を生成 (初回なら order_no 採番 + snapshot 凍結 + export_log INSERT)。
                    // 返却ファイル名は採番後の order_no を含むため、ZIP エントリ名の素材に使う。
                    var (fileName, content) = await orderExcel.ExportAsync(id, actorUserId, ct);
                    var entryName = UniqueEntryName(usedNames, fileName);
                    await WriteEntryAsync(archive, entryName, content, ct);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    throw; // キャンセルは非ブロッキング対象外
                }
                catch (Exception ex)
                {
                    // 1 発注の失敗は他発注をブロックしない。記録して継続 (原則4)。
                    failedIds.Add(id);
                    logger.LogError(ex,
                        "一括ダウンロードで発注書生成に失敗しました purchase_order_id={PurchaseOrderId} actor_user_id={ActorUserId}",
                        id, actorUserId);
                }
            }
        }

        if (failedIds.Count > 0)
        {
            // 部分失敗を監査ログにも残す (運用者が次に確認すべき発注 id を示す)。success=false。
            await audit.LogAsync(actorUserId, "PurchaseOrder.BulkExport",
                entityType: "PurchaseOrder", success: false,
                note: $"format={(includeManagement ? "both" : "order")}, succeeded={succeeded}, failed_ids=[{string.Join(",", failedIds)}]",
                cancellationToken: ct);
        }

        // 発注書が 1 件も生成できなかった場合は ZIP の発注書が空になるため全体を失敗扱いとする
        // (both で管理表のみ ZIP に入って発注書ゼロ、という分かりにくい成功を避ける)。
        if (succeeded == 0)
            throw new InvalidOperationException("発注書を 1 件も生成できませんでした (BULK-004)");

        // ZipArchive Dispose 完了後にバイト列を取り出す (position を先頭に戻す必要はなく ToArray は全域を返す)。
        var fileName2 = includeManagement ? $"発注一括_{stamp}.zip" : $"発注書_{stamp}.zip";
        return new BulkExportResult(fileName2, ZipContentType, zipStream.ToArray());
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string entryName, byte[] content, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(content, ct);
    }

    /// <summary>ZIP エントリ名の重複を回避 (同名なら " (2)", " (3)" を付与)。</summary>
    private static string UniqueEntryName(HashSet<string> used, string fileName)
    {
        if (used.Add(fileName)) return fileName;
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private static BulkExportFormat ParseFormat(string? format) => (format ?? "").Trim().ToLowerInvariant() switch
    {
        "order" => BulkExportFormat.Order,
        "management" => BulkExportFormat.Management,
        "both" => BulkExportFormat.Both,
        _ => throw new ArgumentException($"format は order / management / both のいずれかです: '{format}' (BULK-003)"),
    };
}
