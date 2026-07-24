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
        Guid actorUserId, bool includeDeleted, CancellationToken ct = default)
    {
        var query = db.ExchangeRates.AsQueryable();
        if (!includeDeleted) query = query.Where(e => e.DeletedAt == null);

        // 新しい年月 → 通貨コード順。
        var items = await query
            .OrderByDescending(e => e.YearMonth).ThenBy(e => e.CurrencyCode)
            .Select(e => new ExchangeRateListItem(
                e.Id, e.YearMonth, e.CurrencyCode, e.Rate, e.TaxRate, e.DeletedAt, e.CreatedAt, e.UpdatedAt))
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.List",
            entityType: "ExchangeRate", note: $"Returned {items.Count}", cancellationToken: ct);

        return items;
    }

    public async Task<ExchangeRate?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);

    // 有効行 (未削除) の (年月, 通貨) 重複を検出して DomainException (AKB-SYS-007 / 409) を投げる。
    // 部分 UNIQUE 索引による DB 例外に頼らず、アプリ層で分かりやすいメッセージにする (review #1 対応)。
    private async Task EnsureNoActiveDuplicateAsync(string yearMonth, string currency, Guid? excludeId, CancellationToken ct)
    {
        var dup = await db.ExchangeRates.AnyAsync(
            e => e.DeletedAt == null && e.YearMonth == yearMonth && e.CurrencyCode == currency
                 && (excludeId == null || e.Id != excludeId), ct);
        if (dup)
            throw DomainException.UniqueViolation($"{yearMonth} {currency} の為替レートは既に登録されています");
    }

    public async Task<ExchangeRate> CreateAsync(ExchangeRateWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var (yearMonth, currency) = Validate(req);
        await EnsureNoActiveDuplicateAsync(yearMonth, currency, null, ct);

        var now = SystemTime.UtcNow;
        var entity = new ExchangeRate
        {
            YearMonth = yearMonth,
            CurrencyCode = currency,
            Rate = req.Rate,
            TaxRate = req.TaxRate,
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

    public async Task<ExchangeRate?> UpdateAsync(Guid id, ExchangeRateWriteRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return null;

        var (yearMonth, currency) = Validate(req);
        await EnsureNoActiveDuplicateAsync(yearMonth, currency, id, ct);
        entity.YearMonth = yearMonth;
        entity.CurrencyCode = currency;
        entity.Rate = req.Rate;
        entity.TaxRate = req.TaxRate;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.Update",
            entityType: "ExchangeRate", entityId: entity.Id,
            note: $"{yearMonth} {currency}", cancellationToken: ct);

        return entity;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        entity.DeletedAt = SystemTime.UtcNow;
        entity.UpdatedAt = SystemTime.UtcNow;
        entity.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ExchangeRate.Delete",
            entityType: "ExchangeRate", entityId: entity.Id, cancellationToken: ct);
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await db.ExchangeRates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        if (entity.DeletedAt != null) // 既に有効なら何もしない。復元時は同一 (年月,通貨) の有効行が無いことを保証する。
            await EnsureNoActiveDuplicateAsync(entity.YearMonth, entity.CurrencyCode, id, ct);
        entity.DeletedAt = null;
        entity.UpdatedAt = SystemTime.UtcNow;
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
            throw DomainException.Validation("年月は YYYY-MM 形式で指定してください");

        var currency = (req.CurrencyCode ?? "").Trim().ToUpperInvariant();
        if (currency.Length != 3)
            throw DomainException.Validation("通貨コードは 3 桁で指定してください");

        if (req.Rate <= 0)
            throw DomainException.Validation("レートは正の数で指定してください");

        // 税率(%) (Part5) は任意 (NULL 許容)。指定時は 0 以上を要求する (DB の CHECK と整合)。
        if (req.TaxRate is < 0)
            throw DomainException.Validation("税率は 0 以上で指定してください");

        return (yearMonth, currency);
    }
}
