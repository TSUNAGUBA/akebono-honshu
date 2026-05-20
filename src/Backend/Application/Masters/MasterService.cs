using Akebono.Application.Common;
using Akebono.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Masters;

/// <summary>
/// 17 マスタ共通の CRUD ジェネリックサービス。
/// 拡張カラムがあるマスタは、本サービスを base に呼び出しつつ拡張プロパティの set/get を個別 service で行う設計。
/// Phase 5 api-design.md §2.3 マスタ CRUD (M-01 / M-02 共通テンプレート) の Application 層実装。
/// </summary>
public class MasterService<TEntity>(IAkebonoDbContext db, IAuditLogger audit)
    where TEntity : MasterEntityBase, new()
{
    public async Task<List<TEntity>> ListAsync(
        long actorUserId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        var query = db.Set<TEntity>().AsQueryable();
        if (!includeDeleted) query = query.Where(e => !e.DeleteFlag);

        var items = await query.OrderBy(e => e.Code).ToListAsync(ct);

        await audit.LogAsync(actorUserId, $"{typeof(TEntity).Name}.List",
            entityType: typeof(TEntity).Name,
            note: $"Returned {items.Count} items",
            cancellationToken: ct);

        return items;
    }

    public Task<TEntity?> GetAsync(long id, CancellationToken ct = default)
        => db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>新規作成。Code 重複は呼び出し側で事前チェック必須 (DB UNIQUE 制約違反でも検知可)。</summary>
    public async Task<TEntity> CreateAsync(TEntity entity, long actorUserId, CancellationToken ct = default)
    {
        var now = SystemTime.Now;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.CreatedByUserId = actorUserId;
        entity.UpdatedByUserId = actorUserId;
        entity.DeleteFlag = false;

        db.Set<TEntity>().Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, $"{typeof(TEntity).Name}.Create",
            entityType: typeof(TEntity).Name,
            entityId: entity.Id,
            note: $"Code={entity.Code}",
            cancellationToken: ct);

        return entity;
    }

    /// <summary>更新 (mutator で拡張プロパティも書き換え可)。Id 不在は null 返却。</summary>
    public async Task<TEntity?> UpdateAsync(
        long id,
        Action<TEntity> mutator,
        long actorUserId,
        CancellationToken ct = default)
    {
        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return null;

        mutator(entity);
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, $"{typeof(TEntity).Name}.Update",
            entityType: typeof(TEntity).Name,
            entityId: entity.Id,
            note: $"Code={entity.Code}",
            cancellationToken: ct);

        return entity;
    }

    /// <summary>論理削除 (delete_flag = true)。</summary>
    public async Task<bool> SoftDeleteAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        entity.DeleteFlag = true;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, $"{typeof(TEntity).Name}.Delete",
            entityType: typeof(TEntity).Name,
            entityId: entity.Id,
            note: $"Code={entity.Code}",
            cancellationToken: ct);

        return true;
    }

    /// <summary>論理削除取消。</summary>
    public async Task<bool> RestoreAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        entity.DeleteFlag = false;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, $"{typeof(TEntity).Name}.Restore",
            entityType: typeof(TEntity).Name,
            entityId: entity.Id,
            cancellationToken: ct);

        return true;
    }

    public static MasterListItem ToListItem(TEntity e)
        => new(e.Id, e.Code, e.Name, e.DeleteFlag, e.CreatedAt, e.UpdatedAt);
}
