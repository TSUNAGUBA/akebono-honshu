using Amazon.S3;
using Amazon.S3.Model;
using Akebono.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Storage;

/// <summary>
/// 本番環境用 (prod): AWS S3 に PutObject、配信は Pre-signed URL (TTL 15min)。
/// IAM 認証は App Runner の Task Role で行う (環境変数不要、AWS SDK の default credentials chain)。
/// `S3:BucketName` / `S3:Region` を appsettings から読む (Region は SDK の default credentials chain
/// の region と一致させること、Secrets Manager / RDS と同 region 推奨)。
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
        _bucket = config["S3:BucketName"]
            ?? throw new InvalidOperationException("S3:BucketName が未設定 (ImageStorage:Provider=S3 時は必須)");
        _logger = logger;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var req = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true,
        };
        await _s3.PutObjectAsync(req, ct);
    }

    public async Task<string> GetUrlAsync(string key, CancellationToken ct)
    {
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
}
