namespace Akebono.Domain.Products;

/// <summary>
/// アソート/セット明細 (PR3、旧 spec 商品マスタ本体 No.37「品番：アソート／セット明細」/
/// No.38「数量：アソート／セット明細」)。
///
/// ある商品 (product_family) がアソート/セット品である場合に、その構成となる
/// 「子品番 + 数量」の明細を 1 行ずつ保持する。商品の作成/更新時に全置換するコレクション
/// (BOM = ProductMaterial の ReplaceAsync / 色サイズ展開と同じパターン)。明細が 0 件の商品は
/// 通常商品 (非アソート/セット) として正常に扱う。
///
/// 設計判断: <see cref="ChildItemNumber"/> は <b>手入力テキスト</b>であり、products / product_families への
/// FK は張らない。旧システム品番・外部品番も子品番として許容するため (旧 spec の「手入力」に忠実)。
/// </summary>
public class ProductSetComponent
{
    public long Id { get; set; }

    /// <summary>親 = アソート/セット商品 (product_families.id)。</summary>
    public long ProductFamilyId { get; set; }

    /// <summary>
    /// 子品番 (旧 spec No.37)。手入力テキスト。FK なし (旧システム品番/外部品番も許容)。
    /// </summary>
    public string ChildItemNumber { get; set; } = string.Empty;

    /// <summary>数量 (旧 spec No.38)。正の整数 (CHECK quantity &gt; 0)。</summary>
    public int Quantity { get; set; }

    /// <summary>表示順 (配列順で採番)。</summary>
    public short LineNo { get; set; }

    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    // ナビプロパティ
    public ProductFamily? ProductFamily { get; set; }
}
