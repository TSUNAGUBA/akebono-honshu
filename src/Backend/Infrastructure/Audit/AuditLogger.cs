using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Akebono.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Audit;

/// <summary>
/// 監査ログ記録 (IAuditLogger 実装)。
/// CLAUDE.md 原則4 (非ブロッキング): 監査は補助処理であり、その失敗が主フローを止めない。
/// 主処理は呼び出し前に確定済み (Service は commit 後に LogAsync を呼ぶ) のため、
/// ここでの SaveChanges 失敗は warning に留め握り潰す (主データは保持される)。
/// note は audit_logs.note (VARCHAR 512) を超えないよう安全に切詰める。
/// </summary>
public class AuditLogger(AkebonoDbContext db, ITenantContext tenantContext, ILogger<AuditLogger> logger) : IAuditLogger
{
    private const int NoteMaxLength = 512;

    public async Task LogAsync(
        Guid? actorUserId,
        string action,
        string? entityType = null,
        Guid? entityId = null,
        bool success = true,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var entry = db.AuditLogs.Add(new AuditLog
        {
            // 複合 PK (id, occurred_at) では EF のクライアント側 Guid 自動生成規約が効かないため明示採番
            Id = Guid.NewGuid(),
            // テナント確定前のイベント (認証拒否等) は NULL のまま記録する (RLS 適用除外テーブル)
            TenantId = tenantContext.TenantId,
            OccurredAt = SystemTime.UtcNow,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Result = (short)(success ? AuditResult.Success : AuditResult.Failure),
            Note = note is { Length: > NoteMaxLength } ? note[..NoteMaxLength] : note,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 監査記録の失敗で主操作 (確定済み) を 5xx にしない。追跡用に warning を残す。
            entry.State = EntityState.Detached;
            logger.LogWarning(ex,
                "監査ログ記録に失敗しました (action={Action}, entity={EntityType}/{EntityId}, actor={Actor})",
                action, entityType, entityId, actorUserId);
        }
    }
}
