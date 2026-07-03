using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class Supplier : MasterEntityBase
{
    public string? OfficialName { get; set; }
    public string ItemConversionCode { get; set; } = string.Empty;
    public long CountryId { get; set; }
    public short SupplierType { get; set; }
    public short AlertTarget { get; set; }

    /// <summary>
    /// 適用通貨 (§2f)。この仕入先の仕入単価に適用する通貨コード (JPY/USD/CNY 等)。
    /// 商品⑤仕入単価では、選択した仕入先の本通貨で為替マスタの適用レート・円換算を解決する。
    /// </summary>
    public string CurrencyCode { get; set; } = "JPY";

    /// <summary>
    /// ドレー代 (§2i、仕入先ごと)。この仕入先を選んだ商品⑤仕入単価行へ自動反映する既定ドレー代。
    /// NULL = 未設定 (自動反映なし)。「ドレー代設定マスタメンテナンス = 仕入先ごとの本項目」。
    /// </summary>
    public decimal? DrayageCost { get; set; }

    public Country? Country { get; set; }
}
