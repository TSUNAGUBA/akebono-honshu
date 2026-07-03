using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Masters;

/// <summary>
/// 仕入先マスタ (M-04) 個別サービス。
/// F-22 対応: official_name は発注書 Excel 帳票の宛名印字に使用。
/// FK 参照 (country) のネスト返却は Phase 6 F-18 対応 (api-design.md §2.3)。
/// </summary>
public class SupplierService(IAkebonoDbContext db, IAuditLogger audit)
{
    public async Task<List<SupplierListItem>> ListAsync(
        long actorUserId,
        bool includeDeleted,
        CancellationToken ct = default)
    {
        var query = db.Suppliers.Include(s => s.Country).AsQueryable();
        if (!includeDeleted) query = query.Where(s => !s.DeleteFlag);

        var items = await query.OrderBy(s => s.Code).Select(s => new SupplierListItem(
            s.Id, s.Code, s.Name, s.OfficialName, s.ItemConversionCode,
            s.CountryId, s.Country != null ? s.Country.Name : null,
            s.SupplierType, s.AlertTarget,
            s.DeleteFlag, s.CreatedAt, s.UpdatedAt,
            s.CurrencyCode, s.DrayageCost)).ToListAsync(ct);

        await audit.LogAsync(actorUserId, "Supplier.List",
            entityType: "Supplier", note: $"Returned {items.Count}",
            cancellationToken: ct);

        return items;
    }

    public async Task<Supplier?> GetAsync(long id, CancellationToken ct = default)
        => await db.Suppliers.Include(s => s.Country).FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Supplier> CreateAsync(SupplierWriteRequest req, long actorUserId, CancellationToken ct = default)
    {
        var now = SystemTime.Now;
        var entity = new Supplier
        {
            Code = req.Code,
            Name = req.Name,
            OfficialName = req.OfficialName,
            ItemConversionCode = req.ItemConversionCode,
            CountryId = req.CountryId,
            SupplierType = req.SupplierType,
            AlertTarget = req.AlertTarget,
            CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "JPY" : req.CurrencyCode.Trim().ToUpperInvariant(),
            DrayageCost = req.DrayageCost,
            CreatedAt = now,
            CreatedByUserId = actorUserId,
            UpdatedAt = now,
            UpdatedByUserId = actorUserId,
        };
        db.Suppliers.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Supplier.Create",
            entityType: "Supplier", entityId: entity.Id,
            note: $"Code={entity.Code}, OfficialName={entity.OfficialName}",
            cancellationToken: ct);

        return entity;
    }

    public async Task<Supplier?> UpdateAsync(long id, SupplierWriteRequest req, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return null;

        entity.Code = req.Code;
        entity.Name = req.Name;
        entity.OfficialName = req.OfficialName;
        entity.ItemConversionCode = req.ItemConversionCode;
        entity.CountryId = req.CountryId;
        entity.SupplierType = req.SupplierType;
        entity.AlertTarget = req.AlertTarget;
        entity.CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "JPY" : req.CurrencyCode.Trim().ToUpperInvariant();
        entity.DrayageCost = req.DrayageCost;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Supplier.Update",
            entityType: "Supplier", entityId: entity.Id,
            note: $"Code={entity.Code}", cancellationToken: ct);

        return entity;
    }

    public async Task<bool> SoftDeleteAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return false;

        entity.DeleteFlag = true;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Supplier.Delete",
            entityType: "Supplier", entityId: entity.Id,
            note: $"Code={entity.Code}", cancellationToken: ct);

        return true;
    }

    public async Task<bool> RestoreAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return false;

        entity.DeleteFlag = false;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "Supplier.Restore",
            entityType: "Supplier", entityId: entity.Id,
            cancellationToken: ct);

        return true;
    }
}
