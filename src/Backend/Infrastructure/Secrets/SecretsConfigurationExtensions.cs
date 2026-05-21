using Microsoft.Extensions.Configuration;

namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// IConfigurationBuilder への AWS Secrets Manager 統合エントリポイント (Iter 4 段階 C-2)。
/// Program.cs から `builder.Configuration.AddAkebonoAwsSecretsManager(...)` の形で呼ぶ。
/// </summary>
public static class SecretsConfigurationExtensions
{
    /// <summary>
    /// AWS Secrets Manager を IConfiguration の Source として登録する。
    ///
    /// 呼出条件: 上位 (Program.cs) で `Secrets:Provider=AwsSecretsManager` のときだけ呼ぶ。
    /// dev/test/CI ではこの拡張を呼ばないこと (環境変数 / User Secrets / appsettings 経由で値が解決される)。
    /// </summary>
    /// <param name="builder">対象 IConfigurationBuilder</param>
    /// <param name="prefix">Secret 名 prefix (例: `akebono/prod/`)。`__OVERRIDE_ME__` だと throw</param>
    /// <param name="regionName">AWS region (例: `ap-northeast-1`)。null で SDK default chain</param>
    /// <param name="mappings">マッピング表 (省略時 SecretMappings.Default)</param>
    public static IConfigurationBuilder AddAkebonoAwsSecretsManager(
        this IConfigurationBuilder builder,
        string prefix,
        string? regionName = null,
        IReadOnlyList<SecretMapping>? mappings = null)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "__OVERRIDE_ME__")
        {
            throw new InvalidOperationException(
                "Secrets:AwsPrefix が未設定または __OVERRIDE_ME__ のままです。" +
                "本番デプロイ時は環境変数 Secrets__AwsPrefix=akebono/prod/ で必ず実値に上書きしてください " +
                "(Secrets:Provider=AwsSecretsManager 時は必須)。");
        }

        builder.Add(new AwsSecretsManagerConfigurationSource
        {
            Prefix = prefix,
            RegionName = regionName,
            Mappings = mappings ?? SecretMappings.Default,
        });
        return builder;
    }
}
