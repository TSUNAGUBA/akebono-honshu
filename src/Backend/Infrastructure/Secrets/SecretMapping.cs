namespace Akebono.Infrastructure.Secrets;

/// <summary>
/// AWS Secrets Manager の Secret 名と IConfiguration key の 1:1 マッピング (Iter 4 段階 C-2)。
///
/// - `SecretName`: prefix を除く末尾セグメント (例: `db-connection`、`firebase-sa-key`)。
///   実際の Secret フル名は `Secrets:AwsPrefix` + `/` + `SecretName` で組立てる
///   (例: `akebono/prod/db-connection`)。
/// - `ConfigKey`: IConfiguration の階層 key (例: `ConnectionStrings:Postgres`)。
///   `:` 区切りは .NET 標準の section delimiter。`__` 区切りの環境変数表記は使わない。
/// - `Optional`: `true` のとき Secret 不在 (ResourceNotFound) を許容する。
///   将来用 Secret (`firebase-sa-key` 等、現状未使用) を事前にマッピングしておくため。
///   `false` の Secret が欠落すると起動時 throw → App Runner ヘルスチェック失敗 → 自動ロールバック。
/// </summary>
public sealed record SecretMapping(string SecretName, string ConfigKey, bool Optional = false);
