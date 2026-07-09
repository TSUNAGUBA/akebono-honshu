namespace Akebono.Domain.Common;

/// <summary>
/// テナントスコープエンティティのマーカー。あけぼの SCM プラットフォームの
/// マルチテナンシー規約 (AKB-DOC-13/20) に基づき、実装エンティティは
///   - DB 列 tenant_id (uuid NOT NULL) に写像される
///   - EF Core のグローバルクエリフィルタ (アプリ層 WHERE) が自動適用される
///   - SaveChanges 時に現在テナントが自動スタンプされる (未確定なら例外 = フェイルクローズ)
///   - DB 側では RLS (tenant_isolation ポリシー) が第 2 の防壁になる
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
