namespace Akebono.Application.Common;

/// <summary>
/// キーセット (カーソル) ページング要求 (AKB-DOC-12 §7.1)。
/// ソートは安定キー (created_at, id) の降順で確定させる。AfterCreatedAt / AfterId は
/// 前ページ末尾行のキーセットアンカー (両方 null = 先頭ページ)。
/// limit の検証と不透明カーソルの符号化/復号は Presentation 層 (PageCursor) が行う。
/// </summary>
public sealed record PageRequest(int Limit, DateTime? AfterCreatedAt, Guid? AfterId)
{
    /// <summary>limit 省略時の既定値 (AKB-DOC-12 §7.1)。</summary>
    public const int DefaultLimit = 50;

    /// <summary>limit の上限。超過は 400 AKB-SYS-011 (AKB-DOC-12 §7.1)。</summary>
    public const int MaxLimit = 200;
}

/// <summary>
/// ページング済み一覧。NextCreatedAt / NextId は次ページ要求のキーセットアンカー
/// (= 本ページ末尾行の created_at / id)。HasMore = false のとき null。
/// 一覧 DTO 自体に created_at を持たないもの (FamilyListItem) があるため、
/// アンカーは DTO ではなく本レコードで運ぶ。
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    bool HasMore,
    DateTime? NextCreatedAt,
    Guid? NextId);
