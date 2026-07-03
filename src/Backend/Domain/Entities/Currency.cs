using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

/// <summary>
/// 通貨マスタ。code = ISO 4217 の 3 文字コード (JPY/USD/CNY 等)、name = 表示名 (日本円/米ドル 等)。
/// 為替マスタの対象通貨、仕入先の適用通貨は本マスタの code を参照する (文字列一致の整合性を集中管理する)。
/// 拡張列を持たない標準マスタ (M-01 共通テンプレート)。
/// </summary>
public class Currency : MasterEntityBase
{
}
