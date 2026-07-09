using Akebono.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Audit;

/// <summary>
/// audit_logs 月次パーティションの先行作成 (第二段階規約: (id, occurred_at) 複合 PK の
/// 月次 RANGE パーティション、AKB-DOC-14)。
/// 起動時 + 24 時間ごとに DB 関数 <c>ensure_audit_log_partitions(3)</c> を呼び、
/// 当月から 3 か月先までの月次パーティションを冪等に確保する (存在済みはスキップ)。
///
/// CLAUDE.md 原則 4 (非ブロッキング): パーティション先行作成は補助処理であり、
/// 失敗しても LogWarning のみで継続する (アプリ起動・業務フローは止めない)。
/// DEFAULT パーティション (audit_logs_default) が安全網のため、月次パーティションが
/// 未作成でも監査ログの INSERT は失敗しない (行は DEFAULT に落ちる)。
/// </summary>
public sealed class AuditPartitionMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditPartitionMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EnsurePartitionsAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return; // シャットダウン
            }
        }
    }

    private async Task EnsurePartitionsAsync(CancellationToken ct)
    {
        try
        {
            // BackgroundService は root スコープのため、scoped な AkebonoDbContext は
            // IServiceScopeFactory で明示的にスコープを作って解決する。
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AkebonoDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT ensure_audit_log_partitions(3)", ct);
            logger.LogInformation("audit_logs 月次パーティションを確保しました (3 か月先まで)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // シャットダウン中のキャンセルは正常系 (呼び出し元 ExecuteAsync のループが終了する)
        }
        catch (Exception ex)
        {
            // 原則 4: 補助処理の失敗で主要フロー (起動・業務 API) を止めない。
            // DEFAULT パーティションが安全網のため、監査 INSERT は失敗しない。次周期に再試行する。
            logger.LogWarning(ex, "audit_logs パーティション先行作成に失敗しました (次周期に再試行します)");
        }
    }
}
