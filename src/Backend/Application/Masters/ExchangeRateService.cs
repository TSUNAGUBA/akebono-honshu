using System.Text.RegularExpressions;
using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Masters;

/// <summary>
/// 為替マスタ (§2f) 個別サービス。年月 (YYYY-MM) × 通貨ごとの対円レートを CRUD する。
/// code/name を持たない bespoke master のため MasterService&lt;T&gt; ではなく個別実装 (M-04 仕入先と同様)。
/// </summary>
public partial class ExchangeRateService(IAkebonoDbContext db, IAuditLogger audit)
{
    [GeneratedRegex(@"^\d{4}-\d{2}$")]
    private static partial Regex YearMonthPattern();

    public async Task<List<ExchangeRateListItem>> ListAsync(
        long actorUserId, bool includeDeleted, CancellationToken ct = default)
    {
        var query = db.ExchangeRates.AsQueryable();
        if (!includeDeleted) query = query.Where(e => !e.DeleteFlag);

        // 新しい年月 → 通貨コード順。
        var items = await query
            .OrderByDescending(e => e.YearMonth).ThenBy(e => e.CurrencyCode)
            .Select(e => new ExchangeRateListItem(
                e.Id, e.YearMonth, e.CurrencyCode, e.Rate, e.DeleteFlag, e.CreatedAt, e.UpdatedAt))
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.List",
            entityType: "ExchangeRate", note: $"Returned {items.Count}", cancellationToken: ct);

        return items;
    }

    public async Task<ExchangeRate?> GetAsync(long id, CancellationToken ct = default)
        => await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<ExchangeRate> CreateAsync(ExchangeRateWriteRequest req, long actorUserId, CancellationToken ct = default)
    {
        var (yearMonth, currency) = Validate(req);

        var now = SystemTime.Now;
        var entity = new ExchangeRate
        {
            YearMonth = yearMonth,
            CurrencyCode = currency,
            Rate = req.Rate,
            CreatedAt = now,
            CreatedByUserId = actorUserId,
            UpdatedAt = now,
            UpdatedByUserId = actorUserId,
        };
        db.ExchangeRates.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.Create",
            entityType: "ExchangeRate", entityId: entity.Id,
            note: $"{yearMonth} {currency}", cancellationToken: ct);

        return entity;
    }

    public async Task<ExchangeRate?> UpdateAsync(long id, ExchangeRateWriteRequest req, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return null;

        var (yearMonth, currency) = Validate(req);
        entity.YearMonth = yearMonth;
        entity.CurrencyCode = currency;
        entity.Rate = req.Rate;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.Update",
            entityType: "ExchangeRate", entityId: entity.Id,
            note: $"{yearMonth} {currency}", cancellationToken: ct);

        return entity;
    }

    public async Task<bool> SoftDeleteAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        entity.DeleteFlag = true;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.Delete",
            entityType: "ExchangeRate", entityId: entity.Id, cancellationToken: ct);
        return true;
    }

    public async Task<bool> RestoreAsync(long id, long actorUserId, CancellationToken ct = default)
    {
        var entity = await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        entity.DeleteFlag = false;
        entity.UpdatedAt = SystemTime.Now;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.Restore",
            entityType: "ExchangeRate", entityId: entity.Id, cancellationToken: ct);
        return true;
    }

    // 入力の年月フォーマット (YYYY-MM) と通貨コード (3 桁) を検証・正規化する。
    private static (string YearMonth, string CurrencyCode) Validate(ExchangeRateWriteRequest req)
    {
        var yearMonth = (req.YearMonth ?? "").Trim();
        if (!YearMonthPattern().IsMatch(yearMonth))
            throw new ArgumentException("年月は YYYY-MM 形式で指定してください (EXR-001)");

        var currency = (req.CurrencyCode ?? "").Trim().ToUpperInvariant();
        if (currency.Length != 3)
            throw new ArgumentException("通貨コードは 3 桁で指定してください (EXR-002)");

        if (req.Rate <= 0)
            throw new ArgumentException("レートは正の数で指定してください (EXR-003)");

        return (yearMonth, currency);
    }
}
