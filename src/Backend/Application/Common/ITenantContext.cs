namespace Akebono.Application.Common;

/// <summary>
/// リクエストスコープの現在テナントコンテキスト。
///
/// 解決順序 (AKB-DOC-12 §10 / AKB-DOC-20):
///   1. Firebase Custom Claims の tenant_id (SoT = akebono-backoffice。プロビジョニング接続後)
///   2. users.tenant_id (MVP 暫定: backoffice 未接続の間は RDS が権威。Claims は後追いキャッシュ)
/// 確定した値は
///   - EF Core グローバルクエリフィルタ (アプリ層 WHERE)
///   - 接続オープン時の SET set_config('app.tenant_id', ...) → PostgreSQL RLS
///   - SaveChanges 時の TenantId 自動スタンプ
/// に使われる。未確定のままテナントスコープ操作を行うとフェイルクローズ
/// (クエリ 0 行 / 書込は AKB-TENANT-005) になる。
/// </summary>
public interface ITenantContext
{
    /// <summary>現在テナント。未確定 (未認証・未解決) は null。</summary>
    Guid? TenantId { get; set; }

    /// <summary>テナント必須の文脈で呼ぶ。未確定なら AKB-TENANT-005 の DomainException。</summary>
    Guid RequireTenantId();
}
