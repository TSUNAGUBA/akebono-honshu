using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

/// <summary>
/// 工場マスタ (Part2)。製品を製造する工場。材料の仕入れ先である「仕入先 (Supplier)」とは別マスタ。
/// 11 桁品番の 7 桁目 (工場コード = item_conversion_code) の生成元。生産指示の加工先でもある。
///
/// 分離の経緯: 従来は Supplier が「仕入先」「工場」「発注先」を兼ねていたが、
/// 仕入先=材料の仕入れ先 / 工場=製造 の別概念のためマスタを分離した (非破壊: 既存 suppliers を複製)。
/// 適用通貨・ドレー代は仕入先 (材料調達) 固有のため Factory には持たせない。
/// </summary>
public class Factory : MasterEntityBase
{
    /// <summary>法的書面用正式名 (生産指示書 Excel 帳票の宛名印字に使用)</summary>
    public string? OfficialName { get; set; }

    /// <summary>11 桁品番の 7 桁目 (工場コード)。SKU 生成元。</summary>
    public string ItemConversionCode { get; set; } = string.Empty;

    public Guid CountryId { get; set; }

    /// <summary>区分 (0=国内, 1=海外)</summary>
    public short SupplierType { get; set; }

    /// <summary>アラート対象 (0=対象外, 1=対象)</summary>
    public short AlertTarget { get; set; }

    public Country? Country { get; set; }
}
