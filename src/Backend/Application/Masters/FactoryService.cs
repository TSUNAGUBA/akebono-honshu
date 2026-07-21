using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Masters;

/// <summary>
/// 工場マスタ (Part2) 個別サービス。仕入先 (SupplierService) と同型だが通貨・ドレー代を持たない。
/// official_name は生産指示書 Excel 帳票の宛名印字に使用。FK 参照 (country) のネスト返却対応。
/// </summary>
public class FactoryService(IAkebonoDbContext db, IAuditLogger audit)
{
    public async Task<List<FactoryListItem>> ListAsync(
        Guid actorUserId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        var query = db.Factories.Include(f => f.Country).AsQueryable();
        if (!includeDeleted) query = query.Where(f => f.DeletedAt == null);

        var items = await query.OrderBy(f => f.Code).Select(f => new FactoryListItem(
            f.Id, f.Code, f.Name, f.OfficialName, f.ItemConversionCode,
            f.CountryId, f.Country != null ? f.Country.Name : null,
            f.SupplierType, f.AlertTarget,
            f.DeletedAt, f.CreatedAt, f.UpdatedAt)).ToListAsync(ct);

        await audit.LogAsync(actorUserId, "Factory.List",
            entityType: "Factory", note: $"Returned {items.Count}",
            cancellationToken: ct);

        return items;
    }

    public async Task<Factory?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.Factories.Include(f => f.Country).FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<Factory> CreateAsync(FactoryWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var now = SystemTime.UtcNow;
        var entity = new Factory
        {
            Code = req.Code,
            Name = req.Name,
            OfficialName = req.OfficialName,
            ItemConversionCode = req.ItemConversionCode,
            CountryId = req.CountryId,
            SupplierType = req.SupplierType,
            AlertTarget = req.AlertTarget,
            CreatedAt = now,
            CreatedByUserId = actorUserId,
            UpdatedAt = now,
            UpdatedByUserId = actorUserId,
        };
        db.Factories.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Factory.Create",
            entityType: "Factory", entityId: entity.Id,
            note: $"Code={entity.Code}, OfficialName={entity.OfficialName}",
            cancellationToken: ct);

        return entity;
    }

    public async Task<Factory?> UpdateAsync(Guid id, FactoryWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Factories.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (entity is null) return null;

        entity.Code = req.Code;
        entity.Name = req.Name;
        entity.OfficialName = req.OfficialName;
        entity.ItemConversionCode = req.ItemConversionCode;
        entity.CountryId = req.CountryId;
        entity.SupplierType = req.SupplierType;
        entity.AlertTarget = req.AlertTarget;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Factory.Update",
            entityType: "Factory", entityId: entity.Id,
            note: $"Code={entity.Code}", cancellationToken: ct);

        return entity;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Factories.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (entity is null) return false;

        entity.DeletedAt = SystemTime.UtcNow;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Factory.Delete",
            entityType: "Factory", entityId: entity.Id,
            note: $"Code={entity.Code}", cancellationToken: ct);

        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Factories.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (entity is null) return false;

        entity.DeletedAt = null;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Factory.Restore",
            entityType: "Factory", entityId: entity.Id,
            cancellationToken: ct);

        return true;
    }
}
