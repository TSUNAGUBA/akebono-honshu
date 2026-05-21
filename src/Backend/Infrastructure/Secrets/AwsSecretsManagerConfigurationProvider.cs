using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// AWS Secrets Manager から Secret を取得して IConfiguration に注入する Provider (Iter 4 段階 C-2)。
///
/// 設計判断 (ヒアリング A/A/A 案):
/// - **取得タイミング:** `Load()` は起動時 1 回のみ呼ばれる。TTL refresh や hot-reload は実装しない
///   (rotation 時は App Runner 再デプロイで対応する設計選択)。
/// - **マッピング:** 1 Secret = 1 IConfiguration key (`SecretMapping.SecretName` → `ConfigKey`)。
///   JSON 構造 Secret は未対応 (`SecretString` を生文字列としてそのまま注入)。
/// - **エラー処理:**
///   - Optional=false の Secret が `ResourceNotFoundException` or 取得失敗 → 例外を throw。
///     `WebApplication.CreateBuilder` 〜 `Build()` の間で発生するため起動失敗となり、
///     App Runner ヘルスチェック失敗 → 自動ロールバック (段階 C-1 S3ImageStorage と同規約)。
///   - Optional=true の Secret は ResourceNotFound 時のみ欠落容認、AccessDenied 等の権限系は throw。
///
/// 設計上の制限:
/// - `Load()` は同期 API。AWS SDK の async メソッドを `GetAwaiter().GetResult()` で呼ぶが、
///   起動時 1 回のみ実行されかつ ASP.NET Core の Host build 段階は HTTP 要求のための
///   SynchronizationContext が無いためデッドロックの懸念は無い。
/// - SecretBinary は未対応 (`SecretString` のみ)。本プロジェクトの Secret はすべて文字列前提。
/// </summary>
public sealed class AwsSecretsManagerConfigurationProvider : ConfigurationProvider
{
    private readonly AwsSecretsManagerConfigurationSource _source;

    public AwsSecretsManagerConfigurationProvider(AwsSecretsManagerConfigurationSource source)
    {
        _source = source;
    }

    public override void Load()
    {
        var prefix = _source.Prefix.TrimEnd('/');
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using var client = CreateClient();

        foreach (var mapping in _source.Mappings)
        {
            var secretId = $"{prefix}/{mapping.SecretName}";
            try
            {
                var response = client.GetSecretValueAsync(new GetSecretValueRequest
                {
                    SecretId = secretId,
                }).GetAwaiter().GetResult();

                if (response.SecretString is null)
                {
                    throw new InvalidOperationException(
                        $"Secret '{secretId}' は SecretString が null です " +
                        "(SecretBinary は本実装で未対応)。");
                }

                data[mapping.ConfigKey] = response.SecretString;
            }
            catch (ResourceNotFoundException) when (mapping.Optional)
            {
                // Optional Secret の欠落は許容。将来 Secret 投入時に自動で picked up される。
                // 認識性のため stderr に診断行を 1 行 (ASP.NET Logger は config build 前で未利用化)。
                Console.Error.WriteLine(
                    $"[AwsSecretsManager] Optional secret '{secretId}' が未投入のためスキップ " +
                    $"(IConfiguration key '{mapping.ConfigKey}' は未注入)。");
            }
        }

        Data = data;
    }

    private IAmazonSecretsManager CreateClient()
    {
        // RegionName 明示時は RegionEndpoint を作成。null のときは AWS SDK の resolution chain に委ねる。
        if (!string.IsNullOrEmpty(_source.RegionName))
        {
            return new AmazonSecretsManagerClient(
                RegionEndpoint.GetBySystemName(_source.RegionName));
        }
        return new AmazonSecretsManagerClient();
    }
}
