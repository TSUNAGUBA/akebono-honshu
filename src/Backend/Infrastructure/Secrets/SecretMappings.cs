namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// 既定マッピング表 (Iter 4 段階 C-2)。
/// migration-plan.md §4.2.2 の Secret 投入対象と 1:1 対応する。
/// 新規 Secret を追加する場合は本テーブルに追記し、prod 側で `<prefix>/<SecretName>` を投入する。
/// </summary>
public static class SecretMappings
{
    public static IReadOnlyList<SecretMapping> Default { get; } = new SecretMapping[]
    {
        // RDS 接続文字列 (Username/Password 含む完全な connection string)。
        // 本番 App Runner では本 Secret 経由で注入され、appsettings の `__OVERRIDE_ME__` を上書きする。
        new("db-connection", "ConnectionStrings:Postgres"),

        // Firebase Service Account 鍵 JSON (raw text)。
        // 現状の段階 B 実装 (JwtBearer + JWKS) では未使用だが、シナリオ E (setCustomUserClaims /
        // Reconciler バッチ、将来 Iteration) で使用予定のため事前マッピング。Secret 未投入時は
        // Optional=true で起動を阻害しない (migration-plan §3.2 脚注、§4.2.2bis 参照)。
        new("firebase-sa-key", "Firebase:ServiceAccountKey", Optional: true),
    };
}
