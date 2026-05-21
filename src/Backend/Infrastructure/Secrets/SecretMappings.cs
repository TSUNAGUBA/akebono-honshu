namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// 既定マッピング表 (Iter 4 段階 C-2)。migration-plan.md §4.2.2 の Secret 投入対象と 1:1 対応する。
///
/// **新規 Secret を追加する場合の 5 ステップ (本クラスを SoT とする、CLAUDE.md 原則 5 / 6 整合、
/// CR-m4 / SA-P2-3 / SA-P1-3 / M-NEW-2 指摘):**
/// 1. (Claude) 本クラスの `Default` リストに `SecretMapping` 行を追加 (Optional の要否を判断)
/// 2. (Claude) `.ai-native/outputs/phase7/iteration4-prod-migration-plan.md` §4.2.2 の
///    「Secret 投入対象」行に新規エントリを追加 (オペレーターへの依頼内容になる)
/// 3. (オペレーター) 本番 Secrets Manager に `&lt;prefix&gt;/&lt;SecretName&gt;` を投入 (CMK で暗号化)
/// 4. (オペレーター) `aws secretsmanager list-secrets --filters Key=name,Values=&lt;prefix&gt;/` の
///    出力と本クラスの `Default` 配列の `SecretName` 列を目視突合 (typo / 欠落検知)
/// 5. (オペレーター) App Runner サービス再デプロイ → 起動ログで
///    `AwsSecretsManager: loaded N/M secrets` の N==M (Optional 除く) を確認
///
/// **typo / ドリフト検知 (SA-P1-4):**
/// マッピングと投入名の **完全一致** が運用要件。Optional=false の Secret 名が typo されていると
/// 起動時 throw で検知される。Optional=true は起動時警告ログ + IConfiguration への未注入となり、
/// 利用側で `null` 引きに化けるため遠い失敗になりやすい。ステップ 4 の list-secrets 突合で多重防御する。
/// </summary>
public static class SecretMappings
{
    public static IReadOnlyList<SecretMapping> Default { get; } =
    [
        // RDS 接続文字列 (Username/Password 含む完全な connection string)。
        // 本番 App Runner では本 Secret 経由で注入され、appsettings の `__OVERRIDE_ME__` を上書きする。
        new("db-connection", "ConnectionStrings:Postgres"),

        // Firebase Service Account 鍵 JSON (raw text)。
        // 現状の段階 B 実装 (JwtBearer + JWKS) では未使用だが、シナリオ E (setCustomUserClaims /
        // Reconciler バッチ、将来 Iteration) で使用予定のため事前マッピング。
        // Secret 未投入時は Optional=true で起動を阻害しない (migration-plan §3.2 脚注、§4.2.2bis 参照)。
        //
        // **検証手段 (CR-M4 指摘):** 利用側コードはシナリオ E 着手時に追加されるが、それまでの間も
        // 起動時の `AwsSecretsManager: loaded N/M secrets` ログで N==M (Optional 含む) を確認すれば
        // IConfiguration["Firebase:ServiceAccountKey"] への注入は検証可能。
        new("firebase-sa-key", "Firebase:ServiceAccountKey", Optional: true),
    ];
}
