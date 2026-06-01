namespace Akebono.Domain.Orders;

/// <summary>
/// 発注書ステータス (Phase 6 簡素化、F-10/F-11 対応)。
/// 旧設計の Draft / Submitted / Revised は廃止、2 値モデルに統一。
/// Excel 出力はステータス遷移と無関係 (いつでも何度でも可)、Cancelled でも出力可能。
/// </summary>
public enum OrderStatus : short
{
    Active = 0,
    Cancelled = 1,
}
