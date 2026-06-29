using Akebono.Domain.Orders;
using Akebono.Domain.Products;

namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 発注書 / 管理表 帳票の明細行レベル算出を共有するヘルパー。
/// 旧 11 桁品番の構成 (art NO 7 桁 + カラー 2 桁 + サイズ 2 桁) と、分納×倉庫の多次元明細を
/// 旧帳票の固定 3 倉庫 (倉庫1/2/3 = 倉庫×納期の depart 列) に集約するロジックをまとめる。
/// </summary>
internal static class OrderReportLineMath
{
    /// <summary>品番 (art NO) = SKU 先頭 7 桁 (上位 7 桁が品番、8-9 桁がカラー、10-11 桁がサイズ)。</summary>
    public static string ArtNo(string? sku) => sku is { Length: >= 7 } ? sku[..7] : (sku ?? "");

    /// <summary>カラー = SKU 8-9 桁 (旧帳票の color 列は SKU に埋め込まれた 2 桁カラーコード)。</summary>
    public static string ColorCode(string? sku) => sku is { Length: >= 9 } ? sku[7..9] : "";

    /// <summary>
    /// 版権 (bland) = 有/無。商品 family の 版権対象 (RoyaltyTarget) または 版権料率 (RoyaltyRate &gt; 0) が
    /// 設定されていれば「有」、無ければ「無」。旧帳票は海外版でも 有/無 (日本語) で印字するため非ローカライズ。
    /// </summary>
    public static string Bland(ProductFamily? family) =>
        family?.RoyaltyTarget is not null || (family?.RoyaltyRate ?? 0m) > 0m ? "有" : "無";

    /// <summary>1 つの depart (倉庫) 列セル: 発注数 (Quantity) と 入数 (PackQuantity)。</summary>
    public readonly record struct DepartCell(int Quantity, int? PackQuantity)
    {
        /// <summary>箱数 = 発注数 / 入数 (入数 &gt; 0 のときのみ。割り切れない端数は切上げ)。入数未設定/0 は null。</summary>
        public int? BoxCount => PackQuantity is { } p && p > 0 ? (Quantity + p - 1) / p : null;
    }

    /// <summary>
    /// 発注明細を旧帳票の固定 3 倉庫 (倉庫1/2/3) へ集約する。
    ///   - 分納あり明細: 各分納を倉庫一致で群1/2/3 に振り分け (倉庫未指定 / 3 倉庫外は主倉庫=群1)。
    ///     発注数は群内合算、入数は群内で最初に現れた非 NULL 値を採用。Σ群 = line.Quantity (欠落なし)。
    ///   - 分納なし明細: 群1 = (line.Quantity, line.PackQuantity)、群2/3 は空。
    /// wh1 は発注ヘッダの主倉庫 (NOT NULL)、wh2/wh3 は納入倉庫2/3 (NULL 許容)。
    /// </summary>
    public static DepartCell[] Departs(PurchaseOrderLine line, long wh1, long? wh2, long? wh3)
    {
        var qty = new int[3];
        var pack = new int?[3];

        if (line.Deliveries.Count > 0)
        {
            foreach (var d in line.Deliveries)
            {
                // 群判定は wh1 → wh2 → wh3 の順 (wh2/wh3 が wh1 と同一の退化設定でも群1 を優先)。
                var idx = 0;
                if (d.WarehouseId is { } wid)
                {
                    if (wid == wh1) idx = 0;
                    else if (wh2 is { } w2 && wid == w2) idx = 1;
                    else if (wh3 is { } w3 && wid == w3) idx = 2;
                    else idx = 0; // 3 倉庫外 → 主倉庫 (群1) に寄せて欠落を防ぐ
                }
                // d.WarehouseId == null (倉庫未指定) も群1
                qty[idx] += d.Quantity;
                pack[idx] ??= d.PackQuantity;
            }
        }
        else
        {
            qty[0] = line.Quantity;
            pack[0] = line.PackQuantity;
        }

        return new[]
        {
            new DepartCell(qty[0], pack[0]),
            new DepartCell(qty[1], pack[1]),
            new DepartCell(qty[2], pack[2]),
        };
    }
}
