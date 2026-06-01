using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// AWS Secrets Manager から Secret を取得して IConfiguration に注入する Provider (Iter 4 段階 C-2)。
///
/// 設計判断 (ヒアリング A/A/A 案):
/// - **取得タイミング:** `Load()` は起動時 1 回のみ実行を意図。冪等性ガードとして `_loaded` フラグで
///   2 回目以降の呼出を no-op 化 (`IConfigurationRoot.Reload()` が誤発火しても AWS API を無駄に叩かない、
///   CR-M2 reviewer 指摘)。rotation 時の値再取得は App Runner 再デプロイで対応する設計選択。
/// - **マッピング:** 1 Secret = 1 IConfiguration key (`SecretMapping.SecretName` → `ConfigKey`)。
///   JSON 構造 Secret は未対応 (`SecretString` を生文字列としてそのまま注入)。
/// - **ConfigurationSource 優先順位 (SA-P0-3 監査指摘):**
///   `WebApplication.CreateBuilder` の default Source 追加順は JsonFile → User Secrets (Dev 環境のみ)
///   → EnvironmentVariables → CommandLine。本 Source は **その後** に追加されるため、
///   Secrets Manager 経路の値が環境変数 / appsettings を **上書き** する (最後勝ち)。
///   本番 (Production) では User Secrets Source は挟まらないため Source は 3 段 (JsonFile /
///   EnvironmentVariables / CommandLine) + Secrets Manager の 4 段構成 (P2-3 監査指摘整合)。
///   緊急時に環境変数で Secrets Manager の値を override したい場合は
///   `Secrets:Provider=Environment` に切り戻すこと (環境変数で直接 override は不可)。
///
/// **エラー処理:**
/// - Optional=false の Secret 欠落 / 取得失敗 → `InvalidOperationException` でラップして throw
///   (`secretId` を含む、ただし SecretString 値は **絶対に** 含めない、CR-C2 reviewer 指摘)。
///   `WebApplication.CreateBuilder` 〜 `Build()` の間で発生するため起動失敗 →
///   App Runner ヘルスチェック失敗 → 自動ロールバック (段階 C-1 S3ImageStorage と同規約)。
/// - Optional=true の Secret は `ResourceNotFoundException` 時のみ欠落容認。
///   AccessDenied / Throttling 等の運用系例外は Optional であっても throw する (権限不足を黙殺しない、
///   CR-M4 / SA-P1-4 指摘の typo 検知性向上)。
///
/// **秘密情報露出に関する防御規約 (SA-P0-2 / SA-P1-1 監査指摘):**
/// - 例外メッセージ・ログ出力には `SecretId` (Secret 名) のみを含め、`SecretString` (値) は
///   **絶対に** 含めないこと。本ファイル内で `response.SecretString` を引数に取る `ILogger.Log*` /
///   `Exception` コンストラクタ呼出は **禁止**。将来改修時も死守する。
/// - 本番は `ASPNETCORE_ENVIRONMENT=Production` を必須とする (`UseDeveloperExceptionPage` 等で
///   `IConfiguration.GetDebugView()` 相当の値が漏れる経路を遮断、migration-plan §4.2.3 で App Runner
///   環境変数の必須項目として明記)。
///
/// **設計上の制限:**
/// - `Load()` は同期 API。AWS SDK の async メソッドを `GetAwaiter().GetResult()` で呼ぶが、
///   起動時 1 回のみ実行 + Host build 段階は SynchronizationContext が無いためデッドロックの懸念は無い。
/// - SecretBinary は未対応 (`SecretString` のみ)。本プロジェクトの Secret はすべて文字列前提。
/// - 取得対象 Secret 数 N が増えた場合 (N>=5 目安) は AWS SDK v4 の `BatchGetSecretValueAsync`
///   への切替を検討すること (KMS Decrypt 課金最適化、コールドスタート短縮、SA-P1-2 指摘)。
///
/// **AWS SDK lifecycle (CR-C1 reviewer 指摘):**
/// `IAmazonSecretsManager` は内部に `HttpClient` を保持しており再利用前提のため、`using var` で
/// 都度生成 + 破棄するパターンは採らずクラスフィールドで保持する。Provider 自身が `IDisposable`
/// を実装し、Host 終了時に解放する (`IConfigurationRoot.Dispose()` 経由で連鎖)。
///
/// **ロガー (CR-M1 reviewer 指摘 / P1-1 監査指摘):**
/// ConfigurationProvider は config build 段階で実行されるため DI コンテナの `ILogger` を注入できない。
/// 本クラスは Bootstrap LoggerFactory を constructor で生成し、`AddSimpleConsole` で
/// **プレーンテキスト 1 行 / イベント** 形式で出力する (構造化 JSON 出力ではない)。
/// CloudWatch Logs Insights での検索は `filter @message like /AwsSecretsManager: loaded/`
/// のような **@message 部分一致 grep** で行う (個別フィールドアクセスは不可)。本プロジェクトは
/// 1 vCPU/2GB の小型 App Runner 構成で本番ログボリュームが小さいため、JsonConsole への切替は
/// 段階 D 以降で検討する。本実装で `Console.Error.WriteLine` 経路は完全撤去済。
/// </summary>
public sealed class AwsSecretsManagerConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly AwsSecretsManagerConfigurationSource _source;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private IAmazonSecretsManager? _client;
    private bool _loaded;
    private bool _disposed;

    public AwsSecretsManagerConfigurationProvider(AwsSecretsManagerConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        // Bootstrap LoggerFactory: ConfigurationProvider は DI 経由で ILogger を受け取れない。
        // SimpleConsole + SingleLine で 1 行 1 イベント (プレーンテキスト)、CloudWatch では
        // `filter @message like /.../` の部分一致 grep で検索する。
        // TimestampFormat はローカル実行 (テストハーネス) で日次跨ぎログを追えるよう日付付き
        // (P2-5 監査指摘)。CloudWatch は別途行ヘッダにタイムスタンプを付与するため本番では冗長だが
        // 害は無い。
        _loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(opts =>
        {
            opts.SingleLine = true;
            opts.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        }));
        _logger = _loggerFactory.CreateLogger<AwsSecretsManagerConfigurationProvider>();
    }

    public override void Load()
    {
        // Dispose 後の再呼出を防御 (M-NEW-1 / P2-4 reviewer/auditor 指摘)。
        // 通常 Configuration provider の Dispose は Host 終了時のみだが、IDisposable 導入の副作用として
        // disposed LoggerFactory への書込み / client リーク経路を閉じておく。
        if (_disposed) return;

        // 冪等性ガード (CR-M2): 起動時 1 回のみを意図、Reload() 経由の再呼出は no-op 化。
        if (_loaded)
        {
            _logger.LogDebug(
                "AwsSecretsManager.Load() を 2 回目以降呼ばれたためスキップ (rotation 非対応設計)。");
            return;
        }

        // Source.Prefix は SecretsConfigurationExtensions.AddAkebonoAwsSecretsManager 内で
        // 既に Trim + TrimEnd('/') + 検証済 (N-NEW-1 reviewer 指摘の SSoT)。本クラスでは再正規化しない。
        var prefix = _source.Prefix;
        // OrdinalIgnoreCase は IConfiguration の section key 大文字小文字無視規約と整合させるため (m-3/P2-6)。
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        _client ??= CreateClient();
        var regionDesc = string.IsNullOrEmpty(_source.RegionName) ? "sdk-default" : _source.RegionName!;
        _logger.LogInformation(
            "AwsSecretsManager 取得開始: prefix={Prefix}, region={Region}, mappings={Count}",
            prefix, regionDesc, _source.Mappings.Count);

        int loadedCount = 0;
        foreach (var mapping in _source.Mappings)
        {
            var secretId = $"{prefix}/{mapping.SecretName}";
            try
            {
                // ConfigureAwait(false) は ASP.NET Core host build 段階では SynchronizationContext
                // が無いため厳密には不要だが、テストハーネスで直接 Load() を呼んだ際の安全策。
                var response = _client.GetSecretValueAsync(new GetSecretValueRequest
                {
                    SecretId = secretId,
                }).ConfigureAwait(false).GetAwaiter().GetResult();

                if (response.SecretString is null)
                {
                    // 防御規約: SecretString 値は例外メッセージに含めない (SecretBinary 未対応の旨のみ)。
                    throw new InvalidOperationException(
                        $"Secret '{secretId}' は SecretString が null です " +
                        "(SecretBinary は本実装で未対応)。");
                }

                data[mapping.ConfigKey] = response.SecretString;
                loadedCount++;
            }
            catch (ResourceNotFoundException) when (mapping.Optional)
            {
                // Optional Secret の不在は許容。将来 Secret 投入時に自動 picked up。
                // 防御規約: secretId のみログ、SecretString は元々 null なので漏洩リスク無し。
                _logger.LogWarning(
                    "Optional secret '{SecretId}' が未投入のためスキップ " +
                    "(IConfiguration key '{ConfigKey}' は未注入)。",
                    secretId, mapping.ConfigKey);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // 非 ResourceNotFound 例外 (AccessDenied / Throttling / DecryptionFailure / ネットワーク断 等)。
                // CR-C2: secretId を含む InvalidOperationException でラップして起動失敗させる
                // (App Runner ヘルスチェック失敗 → 自動ロールバック)。
                // 防御規約: 例外原因は inner exception で保持。AWS SDK 例外は secretId / 権限情報のみ含み
                // SecretString 値は内包しない (AWS SDK 公式仕様)。
                throw new InvalidOperationException(
                    $"AWS Secrets Manager からの Secret 取得に失敗しました " +
                    $"(secretId='{secretId}', exception={ex.GetType().Name}): {ex.Message}",
                    ex);
            }
        }

        Data = data;
        _loaded = true;

        // 成功サマリ (SA-P1-3 / CR-M3): オペレーターが CloudWatch で
        // `AwsSecretsManager: loaded N/M secrets` を grep して Secrets Manager 経路が
        // 動作したことを後追い確認するため。N != M (Optional 欠落) も検知可能。
        _logger.LogInformation(
            "AwsSecretsManager: loaded {Loaded}/{Expected} secrets from prefix={Prefix}",
            loadedCount, _source.Mappings.Count, prefix);
    }

    /// <summary>region 明示時は RegionEndpoint で client を作成、null のときは AWS SDK default chain に委譲。</summary>
    private IAmazonSecretsManager CreateClient()
    {
        if (!string.IsNullOrEmpty(_source.RegionName))
        {
            return new AmazonSecretsManagerClient(
                RegionEndpoint.GetBySystemName(_source.RegionName));
        }
        return new AmazonSecretsManagerClient();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
        _client = null;
        _loggerFactory.Dispose();
    }
}
