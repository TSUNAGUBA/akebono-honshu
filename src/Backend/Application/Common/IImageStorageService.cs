namespace Akebono.Application.Common;

/// <summary>
/// 画像ストレージ抽象 (Iter 4 段階 C で導入)。
/// LocalImageStorage (dev: wwwroot/uploads/) と S3ImageStorage (prod: AWS S3 + Pre-signed URL) の 2 実装を持つ。
/// DI 切替は appsettings の `ImageStorage:Provider` (Local / S3) で行う。
///
/// **設計判断 (SoT 一元化、CLAUDE.md 原則 6):** 画像配信 URL の生成責務は Backend に閉じる。
/// Frontend は `ImageSummary.Url` を直接 `img.src` に渡し、URL 組立て規則を意識しない。
/// Local 時は absolute URL (`${apiOrigin}/uploads/...`)、S3 時は Pre-signed URL (TTL 15min) を返す。
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// ストレージへの保存。`key` はストレージ内部キー (相対パス相当、例: "uploads/product-images/42/abc.jpg")。
    /// Local 実装はファイルシステム書込、S3 実装は `PutObjectAsync`。
    /// 失敗時は例外を propagate (呼出側で 5xx 応答)。
    /// </summary>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>
    /// 配信用 URL の取得。Local は absolute URL、S3 は Pre-signed URL (TTL 15min)。
    /// Pre-signed URL は実行ごとに署名が変わるため、フロントは毎回新しい URL を取得して使う前提
    /// (long-term cache 不可、ImageSummary を都度フェッチ)。
    /// </summary>
    Task<string> GetUrlAsync(string key, CancellationToken ct);

    /// <summary>
    /// 物理削除 (将来用)。現状の API は論理削除 (`DeletedAt` SET) のみで本メソッドは未呼出だが、
    /// 抽象の対称性のため定義。実装は no-op でも可。
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
