namespace Akebono.Domain.Orders;

/// <summary>
/// 発注書編集理由 5 値 Enum (Phase 6 F-16 解消、A 案 選択肢必須化)。
/// PATCH /api/maker/v1/orders/{id} で必須、audit_logs.changes.edit_reason に記録。
/// JSON シリアライズ時は文字列 (例: "quantity")。
/// </summary>
public enum EditReason
{
    /// <summary>数量変更 (仕入先在庫切れ、生産計画見直し等)</summary>
    Quantity,
    /// <summary>納期変更 (出荷遅延、繁忙期前倒し等)</summary>
    Deadline,
    /// <summary>仕入先変更 (品質問題、コスト見直し等)</summary>
    Supplier,
    /// <summary>誤入力修正</summary>
    Typo,
    /// <summary>その他 (edit_note 推奨)</summary>
    Other,
}
