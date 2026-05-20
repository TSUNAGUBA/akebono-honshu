namespace Akebono.Domain.Products;

/// <summary>
/// 商品画像メタデータ (Phase 5 §4.3)。実体は Iteration 2 ではローカルファイル、
/// Iteration 4 Hardening で S3 + Pre-signed URL に置換。
/// BR-10: 企画単位で最大 5 枚 (CHECK 制約 + 部分 UNIQUE で保証)。
/// </summary>
public class ProductImage
{
    public long Id { get; set; }
    public long ProductFamilyId { get; set; }

    /// <summary>S3 key (Iteration 2 ではローカルファイルの相対パス相当)</summary>
    public string S3Key { get; set; } = string.Empty;
    public string? ThumbS3Key { get; set; }

    /// <summary>1〜5 (CHECK 制約)。先頭が代表画像</summary>
    public short OrderNo { get; set; }

    public string MimeType { get; set; } = string.Empty;
    public int FileSizeBytes { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public string? OriginalFilename { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long UpdatedByUserId { get; set; }

    public ProductFamily? ProductFamily { get; set; }
}
