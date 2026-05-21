using Amazon.S3;
using Amazon.S3.Model;
using Akebono.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Storage;

/// <summary>
/// 本番環境用 (prod): AWS S3 に PutObject、配信は Pre-signed URL (TTL 15min)。
/// IAM 認証は App Runner の Instance Role で行う (環境変数不要、AWS SDK の default credentials chain)。
/// `S3:BucketName` を appsettings から、region は `AWS:Region` から (`config.GetAWSOptions()` 経由)。
///
/// **フェイルファスト (段階 B Firebase ProjectId と同規約、Iter 4 段階 C-1 reviewer 指摘 C2):**
/// `S3:BucketName` 未設定 / `__OVERRIDE_ME__` のまま起動した場合は constructor で throw する。
/// これにより本番環境で誤って placeholder のままデプロイした際に App Runner ヘルスチェックが
/// 失敗 → 自動ロールバックされる (画像 API だけ runtime 失敗する遅発故障を防止)。
/// </summary>
public sealed class S3ImageStorage : IImageStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3ImageStorage> _logger;
    private static readonly TimeSpan PreSignedUrlTtl = TimeSpan.FromMinutes(15);

    public S3ImageStorage(IAmazonS3 s3, IConfiguration config, ILogger<S3ImageStorage> logger)
    {
        _s3 = s3;
        var configuredBucket = config["S3:BucketName"];
        if (string.IsNullOrWhiteSpace(configuredBucket) || configuredBucket == "__OVERRIDE_ME__")
        {
            throw new InvalidOperationException(
                "S3:BucketName が未設定または __OVERRIDE_ME__ のままです。" +
                "本番デプロイ時は環境変数 `S3__BucketName=<bucket-name>` で必ず実値に上書きしてください " +
                "(ImageStorage:Provider=S3 時は必須)。");
        }
        _bucket = configuredBucket;
        _logger = logger;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        ValidateKey(key);
        var req = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        };
        await _s3.PutObjectAsync(req, ct);
    }

    public async Task<string> GetUrlAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        _ = ct; // SDK v4 の `GetPreSignedURLAsync(request)` は CancellationToken 引数版が未提供。
                // 引数は抽象互換のため受領のみ。署名計算自体は CPU bound だが、初回 credential
                // resolution (IMDS / AssumeRole) で I/O を伴う可能性があるため真の async を使用。
        var req = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(PreSignedUrlTtl),
            Verb = HttpVerb.GET,
        };
        return await _s3.GetPreSignedURLAsync(req);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = key,
            }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("S3 object 既に削除済 (key={Key})", key);
        }
    }

    private static void ValidateKey(string key)
    {
        // path traversal 防御 (Iter 4 段階 C-1 reviewer 指摘 M5)。
        // S3 は OS path とは異なり ".." を含む key を物理的に格納可能だが、
        // 本実装では信頼境界外からの値を受け取らない設計のため不正値を早期検知する。
        // `..` はセグメント単位 (`/` 区切り) で完全一致を判定 (ファイル名 `my..file.jpg` は許可、
        // reviewer 2 周目指摘 m-NEW-2 対応で false positive を排除)。
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("S3 key が空です", nameof(key));
        if (key.Split('/').Any(s => s == ".."))
            throw new ArgumentException($"S3 key に '..' セグメントは含められません: '{key}'", nameof(key));
    }
}
