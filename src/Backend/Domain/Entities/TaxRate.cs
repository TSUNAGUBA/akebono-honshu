using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

/// <summary>
/// 税率マスタ (Part5)。税区分ごとの税率(%) を正規管理する。標準/軽減/非課税等。
/// (現状、商品⑤仕入単価の税率は自由入力。将来的に本マスタを参照させる余地を残す。)
/// </summary>
public class TaxRate : MasterEntityBase
{
    /// <summary>税率(%)。例: 10.00 = 10%</summary>
    public decimal Rate { get; set; }
}
