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
    ///
    /// **fail-fast SoT (SA-P0-1 / CR-m1 指摘):**
    /// prefix の null / 空白 / 末尾スラッシュのみ / `__OVERRIDE_ME__` の検証は本メソッドに集約。
    /// Program.cs 側で重複チェックしないこと (二重 fail-fast はメッセージ齟齬の温床)。
    /// </summary>
    /// <param name="builder">対象 IConfigurationBuilder</param>
    /// <param name="prefix">Secret 名 prefix (例: `akebono/prod/`、末尾 `/` の有無は問わない)。null / 空白 / __OVERRIDE_ME__ で throw</param>
    /// <param name="regionName">AWS region (例: `ap-northeast-1`)。null で SDK default chain</param>
    /// <param name="mappings">マッピング表 (省略時 SecretMappings.Default)</param>
    public static IConfigurationBuilder AddAkebonoAwsSecretsManager(
        this IConfigurationBuilder builder,
        string? prefix,
        string? regionName = null,
        IReadOnlyList<SecretMapping>? mappings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // trim 後に再検証 (空白だけ / 末尾スラッシュだけのケースを救う、m-1 指摘)。
        var normalized = (prefix ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "__OVERRIDE_ME__")
        {
            throw new InvalidOperationException(
                "Secrets:AwsPrefix が未設定または __OVERRIDE_ME__ のままです。" +
                "本番デプロイ時は環境変数 Secrets__AwsPrefix=akebono/prod/ で必ず実値に上書きしてください " +
                "(Secrets:Provider=AwsSecretsManager 時は必須)。");
        }

        builder.Add(new AwsSecretsManagerConfigurationSource
        {
            Prefix = normalized,
            RegionName = regionName,
            Mappings = mappings ?? SecretMappings.Default,
        });
        return builder;
    }
}
