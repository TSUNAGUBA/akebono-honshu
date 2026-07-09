using System.Data.Common;
using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Tenancy;

/// <summary>
/// 接続オープン時にセッション GUC app.tenant_id を設定し、PostgreSQL RLS の
/// tenant_isolation ポリシーへ現在テナントを伝搬するインターセプタ。
///
/// AKB-DOC-20 §3.2 の標準形 (SET LOCAL / set_config(..., is_local: true)) との差分:
///   標準形はトランザクションスコープを要求するが、EF Core の読取クエリは
///   トランザクションを張らないため SET LOCAL では RLS コンテキストを伝搬できない。
///   本実装は「論理接続スコープ」の set_config(..., is_local: false) を採用し、
///   標準形が防ごうとする「プール再利用による残留越境」を次の三重で防止する
///   (逸脱と代替統制は docs/platform-integration/README.md §4 に記録。
///    プラットフォーム ADR への裁定申請は同 §9 未決事項):
///   1. 論理クローズ時に本インターセプタが GUC を明示クリア (ConnectionClosing/Async)
///   2. Npgsql が物理接続のプール返却時にセッション状態をリセットする
///      (実機検証済み 2026-07-09、Npgsql 8.0.5 + PostgreSQL 16。リセット後の
///       current_setting は空文字を返すため、RLS ポリシー側は NULLIF(..., '') で
///       NULL 化して 0 行 = フェイルクローズにしている)
///   3. テナント未確定のオープンでは設定せず (クリアのみ)、RLS は 0 行 / 書込拒否
/// </summary>
public sealed class TenantSessionInterceptor(
    ITenantContext tenantContext,
    ILogger<TenantSessionInterceptor> logger) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => ExecuteSetConfig(connection, tenantContext.TenantId);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        => await ExecuteSetConfigAsync(connection, tenantContext.TenantId, cancellationToken);

    // 論理クローズ (プール返却) 前に GUC を明示クリアする。Npgsql のリセットに依存しない
    // 残留防止の第 1 層 (AKB-DOC-20 §3.2 の趣旨 = コンテキストをスコープ外に残さない)。
    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        TryClear(connection);
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        await TryClearAsync(connection);
        return result;
    }

    private void ExecuteSetConfig(DbConnection connection, Guid? tenantId)
    {
        using var cmd = CreateSetConfigCommand(connection, tenantId);
        cmd.ExecuteNonQuery();
    }

    private async Task ExecuteSetConfigAsync(DbConnection connection, Guid? tenantId, CancellationToken ct)
    {
        await using var cmd = CreateSetConfigCommand(connection, tenantId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void TryClear(DbConnection connection)
    {
        try
        {
            ExecuteSetConfig(connection, tenantId: null);
        }
        catch (Exception ex)
        {
            // クリア失敗 (切断中等) は主フローを止めない。Npgsql のプール返却リセットと
            // RLS の NULLIF フェイルクローズが残留を防ぐ第 2・第 3 層として機能する。
            logger.LogWarning(ex, "app.tenant_id GUC のクリアに失敗しました (接続破棄時は無害)");
        }
    }

    private async Task TryClearAsync(DbConnection connection)
    {
        try
        {
            await ExecuteSetConfigAsync(connection, tenantId: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "app.tenant_id GUC のクリアに失敗しました (接続破棄時は無害)");
        }
    }

    /// <summary>tenantId=null は空文字を設定 (RLS ポリシーの NULLIF で NULL → 0 行 = フェイルクローズ)。</summary>
    private static DbCommand CreateSetConfigCommand(DbConnection connection, Guid? tenantId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "@tenant_id";
        p.Value = tenantId?.ToString() ?? string.Empty;
        cmd.Parameters.Add(p);
        return cmd;
    }
}
