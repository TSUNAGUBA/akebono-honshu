namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// `Secrets:Provider` 設定値の許容値定数 (Iter 4 段階 C-2、CR-n3 reviewer 指摘)。
/// マジックストリング typo 防止のため Program.cs / appsettings ドキュメントで本定数を参照する。
/// </summary>
public static class SecretsProviders
{
    /// <summary>環境変数 / User Secrets / appsettings 経由で値が解決される (dev/test/CI 既定)。</summary>
    public const string Environment = "Environment";

    /// <summary>AWS Secrets Manager から起動時 1 回取得 (prod)。</summary>
    public const string AwsSecretsManager = "AwsSecretsManager";
}
