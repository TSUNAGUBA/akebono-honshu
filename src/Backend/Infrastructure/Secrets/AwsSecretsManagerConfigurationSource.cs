using Microsoft.Extensions.Configuration;

namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// AWS Secrets Manager から取得した Secret を IConfiguration に注入する Source (Iter 4 段階 C-2)。
///
/// 使い方: `builder.Configuration.AddAkebonoAwsSecretsManager(prefix, region)` 経由で組み込む。
/// 直接 `new` する必要は無い (テスト用途を除く)。
///
/// 起動時 1 回だけ `Build()` → `Provider.Load()` が呼ばれ、AWS SDK で Secrets Manager から
/// 同期取得して `IConfigurationProvider.Data` に格納する。ランタイムでは何もしない (rotation 非対応、
/// rotation 時は App Runner 再デプロイで対応する設計選択、ヒアリング A/A/A 案)。
/// </summary>
public sealed class AwsSecretsManagerConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Secret 名の prefix (例: `akebono/prod`)。
    /// `SecretsConfigurationExtensions.AddAkebonoAwsSecretsManager` 内で Trim + TrimEnd('/') 済の
    /// **正規化された値** が格納される (N-NEW-1 reviewer 指摘の SoT)。Provider 側で再正規化しないこと。
    /// 環境変数 `Secrets__AwsPrefix` で上書き必須。`__OVERRIDE_ME__` のままだと拡張メソッドで throw。
    /// </summary>
    public required string Prefix { get; init; }

    /// <summary>取得対象の Secret マッピング表。null 不可。</summary>
    public required IReadOnlyList<SecretMapping> Mappings { get; init; }

    /// <summary>
    /// AWS region (例: `ap-northeast-1`)。null のときは AWS SDK の default resolution chain
    /// (環境変数 `AWS_REGION` / IMDS) に委ねる。App Runner では Task Role + 同 region 配置なので
    /// `AWS:Region` から渡せば十分。
    /// </summary>
    public string? RegionName { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new AwsSecretsManagerConfigurationProvider(this);
}
