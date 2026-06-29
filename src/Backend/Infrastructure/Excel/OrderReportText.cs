namespace Akebono.Infrastructure.Excel;

/// <summary>
/// 発注書 (ORDER SHEET) / 管理表 (ORDER DETAIL) 帳票のラベル定義。
/// 表記は発注区分で切替える: 国内 (is_overseas=false) は日本語、海外 (is_overseas=true) は英語。
/// 旧システムの英語帳票 (発注書出力【海外用】) のレイアウト・文言に合わせている。
///
/// 注: 旧帳票の海外版でも一部の内部欄 (連絡欄 / 発注者 / 責任者・管理部・担当者 / 納品先 / 受注先) は
/// 日本語のままだが、本アプリでは i18n の一貫性のため海外版は英語に統一する (構造・桁は同一)。
/// </summary>
public sealed record OrderReportText
{
    // ── 帳票タイトル / ヘッダ ──
    public required string OrderSheetTitle { get; init; }      // 発注書 / ORDER SHEET
    public required string OrderDetailTitle { get; init; }     // 管理表 / ORDER DETAIL
    public required string Supplier { get; init; }             // 仕入先 / SUPPLIER
    public required string OrderNo { get; init; }              // 発注番号 / order NO.
    public required string OrderDate { get; init; }            // 発注日 / order date
    public required string ProcessNo { get; init; }            // 管理番号 / process NO.
    public required string ShippingInstructionNo { get; init; }// 出荷指示番号 / shipping instruction NO.

    // ── 明細テーブル 列見出し ──
    public required string ColArtNo { get; init; }             // 品番 / art NO
    public required string ColColor { get; init; }             // カラー / color
    public required string ColSize { get; init; }              // サイズ / size
    public required string ColProductName { get; init; }       // 品名 / product name
    public required string ColBland { get; init; }             // 版権 / bland
    public required string ColOrder { get; init; }             // 発注数 / order
    public required string ColAssortBox { get; init; }         // アソート箱 / assort box
    public required string ColQtyOfBox { get; init; }          // 箱数 / Q'yt of box
    public required string ColQtyInBox { get; init; }          // 入数 / Q'yt in box
    public required string ColDepart { get; init; }            // 倉庫 / depart
    public required string ColEstimatePrice { get; init; }     // 見積単価 / estimate price
    public required string ColOrderPrice { get; init; }        // 仕入単価 / order price
    public required string ColRemarks { get; init; }           // 備考 / remarks
    public required string ColShippingDate { get; init; }      // 納入日 / shipping date

    // ── フッタ ──
    public required string FactoryShipping { get; init; }      // 工場出荷日 / Factory Shipping
    public required string Shipping { get; init; }             // 出荷日 / Shipping
    public required string Departure { get; init; }            // 出港日 / Departure
    public required string Delivery { get; init; }             // 納品日 / Delivery
    public required string PortOfEntry { get; init; }          // 荷揚地 / Port of entry
    public required string DeliveryTo { get; init; }           // 納品先 / Deliver to
    public required string OrderTo { get; init; }              // 受注先 / Order from
    public required string Communication { get; init; }        // 連絡欄 / Remarks
    public required string OrderStamp { get; init; }           // 捺印 / Order Stamp
    public required string Orderer { get; init; }              // 発注者 / Orderer
    public required string Responsible { get; init; }          // 責任者 / Responsible
    public required string ManagerDept { get; init; }          // 管理部 / Manager
    public required string Staff { get; init; }                // 担当者 / Staff
    public required string Total { get; init; }                // 合計 / Total
    public required string Page { get; init; }                 // ページ書式 (1 / 1)
    public required string CompanyFooter { get; init; }        // 会社名・住所フッタ

    /// <summary>発注区分に応じてラベルセットを返す (国内=日本語 / 海外=英語)。</summary>
    public static OrderReportText For(bool isOverseas) => isOverseas ? English : Japanese;

    private const string AddressEn =
        "Honshu co., ltd  2-13-1 Minowa Taitoh-ku Tokyou, Japan. 110-0011  Tel:+81 3 5850-4710  Fax:+81 3 5850-4720";
    private const string AddressJa =
        "株式会社ホンシュ  〒110-0011 東京都台東区三ノ輪2-13-1  Tel:03-5850-4710  Fax:03-5850-4720";

    public static readonly OrderReportText Japanese = new()
    {
        OrderSheetTitle = "発注書",
        OrderDetailTitle = "管理表",
        Supplier = "仕入先",
        OrderNo = "発注番号",
        OrderDate = "発注日",
        ProcessNo = "管理番号",
        ShippingInstructionNo = "出荷指示番号",
        ColArtNo = "品番",
        ColColor = "カラー",
        ColSize = "サイズ",
        ColProductName = "品名",
        ColBland = "版権",
        ColOrder = "発注数",
        ColAssortBox = "アソート箱",
        ColQtyOfBox = "箱数",
        ColQtyInBox = "入数",
        ColDepart = "倉庫",
        ColEstimatePrice = "見積単価",
        ColOrderPrice = "仕入単価",
        ColRemarks = "備考",
        ColShippingDate = "納入日",
        FactoryShipping = "工場出荷日",
        Shipping = "出荷日",
        Departure = "出港日",
        Delivery = "納品日",
        PortOfEntry = "荷揚地",
        DeliveryTo = "納品先",
        OrderTo = "受注先",
        Communication = "連絡欄",
        OrderStamp = "捺印",
        Orderer = "発注者",
        Responsible = "責任者",
        ManagerDept = "管理部",
        Staff = "担当者",
        Total = "合計",
        Page = "{0} / {1}",
        CompanyFooter = AddressJa,
    };

    public static readonly OrderReportText English = new()
    {
        OrderSheetTitle = "ORDER SHEET",
        OrderDetailTitle = "ORDER DETAIL",
        Supplier = "SUPPLIER",
        OrderNo = "order NO.",
        OrderDate = "order date",
        ProcessNo = "process NO.",
        ShippingInstructionNo = "shipping instruction NO.",
        ColArtNo = "art NO",
        ColColor = "color",
        ColSize = "size",
        ColProductName = "product name",
        ColBland = "bland",
        ColOrder = "order",
        ColAssortBox = "assort box",
        ColQtyOfBox = "Q'yt of box",
        ColQtyInBox = "Q'yt in box",
        ColDepart = "depart",
        ColEstimatePrice = "estimate price",
        ColOrderPrice = "order price",
        ColRemarks = "remarks",
        ColShippingDate = "shipping date",
        FactoryShipping = "Factory Shipping",
        Shipping = "Shipping",
        Departure = "Departure",
        Delivery = "Delivery",
        PortOfEntry = "Port of entry",
        DeliveryTo = "Deliver to",
        OrderTo = "Order from",
        Communication = "Remarks",
        OrderStamp = "Order Stamp",
        Orderer = "Orderer",
        Responsible = "Responsible",
        ManagerDept = "Manager",
        Staff = "Staff",
        Total = "Total",
        Page = "{0} / {1}",
        CompanyFooter = AddressEn,
    };
}
