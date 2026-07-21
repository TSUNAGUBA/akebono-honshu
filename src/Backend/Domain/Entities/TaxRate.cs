using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

/// <summary>
/// 税率マスタ (Part5)。税区分ごとの税率(%)。商品⑤仕入単価などで参照する税率の正規管理。
/// </summary>
public class TaxRate : MasterEntityBase
{
    /// <summary>税率(%)。例: 10.00 = 10%</summary>
    public decimal Rate { get; set; }
}
