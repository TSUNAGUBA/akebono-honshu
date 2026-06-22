namespace Akebono.Domain.Production;

/// <summary>
/// 素材発注書ステータス (material_orders.status)。
/// 未/済バッジは「Ordered (1) = 済」で判定。既存 OrderStatus と同様の簡素モデル。
/// </summary>
public enum MaterialOrderStatus : short
{
    /// <summary>下書き (未発注)</summary>
    Draft = 0,
    /// <summary>発注済 (済)</summary>
    Ordered = 1,
    /// <summary>中止</summary>
    Cancelled = 9,
}
