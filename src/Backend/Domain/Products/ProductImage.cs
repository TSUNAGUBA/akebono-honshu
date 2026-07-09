using Akebono.Domain.Common;

namespace Akebono.Domain.Products;

/// <summary>
/// 商品画像メタデータ (Phase 5 §4.3)。実体は Iteration 2 ではローカルファイル、
/// Iteration 4 Hardening で S3 + Pre-signed URL に置換。
/// BR-10: 企画単位で最大 5 枚 (CHECK 制約 + 部分 UNIQUE で保証)。
/// </summary>
public class ProductImage : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductFamilyId { get; set; }

    /// <summary>
    /// 画像区分 (§2a)。0=企画画像 (planning)、1=本番画像 (production)。既定 0 (既存画像は企画扱いで下位互換)。
    /// 画像利用シーンの代表画像は「本番画像があれば本番の order_no 最小、無ければ企画の order_no 最小」で選択。
    /// order_no の 1〜5 上限・一意は区分ごとに独立 (企画最大5 + 本番最大5)。
    /// </summary>
    public short ImageCategory { get; set; }

    /// <summary>S3 key (Iteration 2 ではローカルファイルの相対パス相当)</summary>
    public string S3Key { get; set; } = string.Empty;
    public string? ThumbS3Key { get; set; }

    /// <summary>1〜5 (CHECK 制約、区分ごとに独立)。区分内で先頭が代表画像</summary>
    public short OrderNo { get; set; }

    public string MimeType { get; set; } = string.Empty;
    public int FileSizeBytes { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public string? OriginalFilename { get; set; }

    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }

    public ProductFamily? ProductFamily { get; set; }
}
