using System.Diagnostics;
using System.Reflection;
using System.Text;
using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Migration;

/// <summary>
/// MIG-3 既存生産管理システム CSV 取込サービス。
/// Phase 7 Iteration 4 Hardening。
///
/// 処理フロー:
///   1. Pre-patch SQL (products.sku VARCHAR(11)→(16))
///   2. Master-fill SQL (旧カラー 31 / サイズ 10 / 仕入先 11 を補完)
///   3. Staging テーブル DDL (DROP &amp; CREATE)
///   4. CSV パース + Staging へバルク INSERT (Shift_JIS / UTF-8 両対応)
///   5. 主要列抽出 UPDATE (c001-c138 → 名前付きカラム)
///   6. Import SQL (Staging → product_families / products / product_supplier_prices)
///   7. 取込件数 + フォールバック件数の集計
///
/// SQL ファイルは db/migration/ が SoT、本 Service の Application.csproj に
/// EmbeddedResource として埋込済 (Akebono.Application.Migration.Sql.*.sql)。
/// </summary>
public sealed class LegacyImportService(IAkebonoDbContext db)
{
    private const int ExpectedColumns = 138;

    /// <summary>主要列の CSV カラム位置 (1-based、CLAUDE.md / mig-3-strategy.md と整合)。</summary>
    private static readonly (int Idx, string Field)[] FieldMap =
    [
        (1,  "legacy_sku"),
        (2,  "legacy_family_code"),
        (3,  "product_name_1"),
        (4,  "product_name_2"),
        (7,  "color_legacy"),
        (10, "size_legacy"),
        (17, "supplier_legacy"),
        (36, "classification_1"),
        (37, "classification_2"),
        (60, "sales_unit_price"),
        (62, "purchase_unit_price"),
        (64, "cost_unit_price"),
    ];

    public async Task<LegacyImportResult> ExecuteAsync(
        Stream csvStream, string fileName, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        // 1. CSV をエンコーディング自動判定 + パース
        var rows = ParseCsv(csvStream, fileName, warnings);
        if (rows.Count == 0)
            throw new InvalidOperationException("CSV にデータ行が含まれていません");

        // 2. SQL 4 種を順次実行 (Pre-patch → Master-fill → Staging DDL → Bulk INSERT → Import)
        var prePatchSql   = ReadSqlResource("pre-patch.sql");
        var masterFillSql = ReadSqlResource("step-01-master-fill.sql");
        var stagingDdlSql = ReadSqlResource("step-02-staging.sql");
        var importSql     = ReadSqlResource("step-03-import.sql");

        await db.Database.ExecuteSqlRawAsync(prePatchSql, ct);
        await db.Database.ExecuteSqlRawAsync(masterFillSql, ct);
        await db.Database.ExecuteSqlRawAsync(stagingDdlSql, ct);

        // 3. Staging へバルク INSERT (1000 行ずつバッチ、138 列 + row_no 自動採番)
        await BulkInsertStagingAsync(rows, ct);

        // 4. 主要列抽出 UPDATE (c001-c138 → 名前付き)
        await db.Database.ExecuteSqlRawAsync(BuildStagingExtractSql(), ct);

        // 5. Staging 件数確認
        var staging = await CountStagingAsync(ct);

        // 6. Import SQL 実行 (PL/pgSQL DO ブロック、RAISE NOTICE は console ログに出力)
        await db.Database.ExecuteSqlRawAsync(importSql, ct);

        // 7. 取込件数 + フォールバック件数を集計
        var import = await CountImportedAsync(ct);
        var fallbacks = await CountFallbacksAsync(ct);

        sw.Stop();
        return new LegacyImportResult(
            PrePatch:   new(true, "products.sku を VARCHAR(16) に拡張済"),
            MasterFill: new(true, "旧マスタ (色/サイズ/仕入先) を補完済"),
            Staging:    staging,
            Import:     import,
            Fallbacks:  fallbacks,
            Warnings:   warnings,
            Elapsed:    sw.Elapsed);
    }

    // ────────────────────────────────────────────────────────────────
    // CSV パース (Shift_JIS / UTF-8 両対応、引用符エスケープ対応)
    // ────────────────────────────────────────────────────────────────
    internal static List<string?[]> ParseCsv(Stream stream, string fileName, List<string> warnings)
    {
        var encoding = DetectEncoding(stream);
        warnings.Add($"CSV エンコーディング: {encoding.WebName} ({fileName})");

        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var rows = new List<string?[]>();
        bool isHeader = true;
        int lineNo = 0;

        while (!reader.EndOfStream)
        {
            lineNo++;
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line);

            if (isHeader)
            {
                isHeader = false;
                if (fields.Length != ExpectedColumns)
                    warnings.Add($"ヘッダ列数 {fields.Length} (期待 {ExpectedColumns}): カラム位置がずれている可能性");
                continue;
            }

            // 138 列に正規化 (不足は NULL、超過は切り捨て)
            var normalized = new string?[ExpectedColumns];
            for (int i = 0; i < ExpectedColumns; i++)
            {
                normalized[i] = i < fields.Length ? (string.IsNullOrEmpty(fields[i]) ? null : fields[i]) : null;
            }
            rows.Add(normalized);
        }

        return rows;
    }

    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuote)
            {
                if (c == '"')
                {
                    // エスケープ "" → "、それ以外は引用符終了
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '"')
                {
                    inQuote = true;
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    /// <summary>BOM ありなら UTF-8、なければ Shift_JIS (旧システム CSV のデフォルト)。</summary>
    internal static Encoding DetectEncoding(Stream stream)
    {
        if (!stream.CanSeek)
        {
            // MemoryStream に複製して seek 可能化
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            // 注意: 呼出側に置換した stream を返す手段がないため、本メソッドは ms を破棄せず Position リセットして返す
            // → 呼出側は this method 呼出後、stream を引き続き読む。MemoryStream の場合は seekable
            throw new InvalidOperationException("CSV stream must be seekable (use MemoryStream)");
        }

        Span<byte> head = stackalloc byte[3];
        var origPos = stream.Position;
        var read = stream.Read(head);
        stream.Position = origPos;

        // UTF-8 BOM
        if (read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            return new UTF8Encoding(true);

        // Shift_JIS (Windows-31J, CP932)
        try { return Encoding.GetEncoding(932); }
        catch (NotSupportedException)
        {
            // CodePagesEncodingProvider 未登録時の保険 (Program.cs で RegisterProvider 必須)
            return Encoding.UTF8;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Staging テーブルへバルク INSERT
    // ────────────────────────────────────────────────────────────────
    private async Task BulkInsertStagingAsync(List<string?[]> rows, CancellationToken ct)
    {
        // 138 列のパラメータ INSERT。PostgreSQL Bind protocol の上限が 32767 パラメータのため、
        // 138 列 × 200 行 = 27600 < 32767 で安全なバッチサイズを採用。
        const int batchSize = 200;
        var cols = string.Join(",", Enumerable.Range(1, ExpectedColumns).Select(i => $"c{i:D3}"));

        for (int offset = 0; offset < rows.Count; offset += batchSize)
        {
            var batch = rows.Skip(offset).Take(batchSize).ToList();
            var sb = new StringBuilder();
            sb.Append("INSERT INTO staging_legacy_products(").Append(cols).Append(") VALUES ");

            var parameters = new List<object?>();
            for (int r = 0; r < batch.Count; r++)
            {
                if (r > 0) sb.Append(',');
                sb.Append('(');
                for (int c = 0; c < ExpectedColumns; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append('{').Append(parameters.Count).Append('}');
                    parameters.Add(batch[r][c]);
                }
                sb.Append(')');
            }

            await db.Database.ExecuteSqlRawAsync(sb.ToString(), parameters.ToArray(), ct);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 主要列抽出 SQL (Staging UPDATE)
    // ────────────────────────────────────────────────────────────────
    private static string BuildStagingExtractSql() => @"
UPDATE staging_legacy_products
   SET legacy_sku          = NULLIF(TRIM(c001), ''),
       legacy_family_code  = NULLIF(TRIM(c002), ''),
       product_name_1      = NULLIF(TRIM(c003), ''),
       product_name_2      = NULLIF(TRIM(c004), ''),
       color_legacy        = NULLIF(TRIM(c007), ''),
       size_legacy         = NULLIF(TRIM(c010), ''),
       supplier_legacy     = NULLIF(TRIM(c017), ''),
       classification_1    = NULLIF(TRIM(c036), ''),
       classification_2    = NULLIF(TRIM(c037), ''),
       cost_unit_price     = NULLIF(TRIM(c064), '')::NUMERIC(12,2),
       purchase_unit_price = NULLIF(TRIM(c062), '')::NUMERIC(12,2),
       sales_unit_price    = NULLIF(TRIM(c060), '')::NUMERIC(12,2)
 WHERE legacy_sku IS NULL;";

    // ────────────────────────────────────────────────────────────────
    // 件数集計クエリ (ScalarAsync の代替に FromSqlRaw)
    // ────────────────────────────────────────────────────────────────
    private async Task<LegacyImportStagingResult> CountStagingAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)::int,
                   COUNT(DISTINCT legacy_family_code)::int,
                   COUNT(*) FILTER (WHERE cost_unit_price > 0)::int
              FROM staging_legacy_products";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new LegacyImportStagingResult(0, 0, 0);
        return new LegacyImportStagingResult(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private async Task<LegacyImportImportResult> CountImportedAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
              (SELECT COUNT(*)::int FROM product_families WHERE planned_year_code = 'Z'),
              (SELECT COUNT(*)::int FROM products         WHERE legacy_id IS NOT NULL),
              (SELECT COUNT(*)::int FROM product_supplier_prices psp
                JOIN product_families pf ON pf.id = psp.product_family_id
                WHERE pf.planned_year_code = 'Z')";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new LegacyImportImportResult(0, 0, 0);
        return new LegacyImportImportResult(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private async Task<LegacyImportFallback> CountFallbacksAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
              (SELECT COUNT(*)::int FROM staging_legacy_products sl
                 WHERE sl.color_legacy IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM colors WHERE legacy_id = sl.color_legacy)),
              (SELECT COUNT(*)::int FROM staging_legacy_products sl
                 WHERE sl.size_legacy IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sizes WHERE legacy_id = sl.size_legacy)),
              (SELECT COUNT(*)::int FROM staging_legacy_products sl
                 WHERE sl.supplier_legacy IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM suppliers WHERE legacy_id = sl.supplier_legacy))";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new LegacyImportFallback(0, 0, 0);
        return new LegacyImportFallback(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    // ────────────────────────────────────────────────────────────────
    // Embedded Resource SQL の読込
    // ────────────────────────────────────────────────────────────────
    private static string ReadSqlResource(string name)
    {
        var assembly = typeof(LegacyImportService).Assembly;
        var fullName = $"Akebono.Application.Migration.Sql.{name}";
        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource not found: {fullName}. "
                + "Akebono.Application.csproj の EmbeddedResource 設定を確認してください。");
        using var sr = new StreamReader(stream);
        return sr.ReadToEnd();
    }
}
