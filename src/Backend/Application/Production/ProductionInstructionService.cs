using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Production;

/// <summary>
/// 生産指示書 (加工指図書) Application サービス (Phase 5 §PI-01〜04)。
/// 既存 PurchaseOrderService と同じ「ヘッダ＋明細＋snapshot凍結＋status遷移」パターン。
/// 採番 (instruction_no = YY-PI-NNNNN) は advisory lock でトランザクション内直列化 (RP-8)。
/// </summary>
public class ProductionInstructionService(IAkebonoDbContext db, IAuditLogger audit, ITenantContext tenantContext)
{
    private const int MaxNumberingRetries = 3;

    /// <summary>新規作成 (PI-01)。status=Draft。明細合計を planned_quantity に整合。</summary>
    public async Task<ProductionInstruction> CreateAsync(
        CreatePiRequest req, Guid actorUserId,
        string? idempotencyKey = null, string? idempotencyPayloadHash = null,
        CancellationToken ct = default)
    {
        // Idempotency-Key (AKB-DOC-12 §8): 同一キー・同一ペイロードの再送は初回結果を再返却。
        if (idempotencyKey is not null)
        {
            var replayed = await FindByIdempotencyKeyAsync(idempotencyKey, idempotencyPayloadHash, ct);
            if (replayed is not null) return replayed;
        }

        if (req.Lines.Count == 0)
            throw DomainException.Validation("明細を 1 件以上指定してください");
        if (req.Lines.Any(l => l.Quantity <= 0))
            throw DomainException.Validation("生産数量は正の数を指定してください");
        if (req.Lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
            throw DomainException.Validation("同一 SKU が重複しています");

        // 品番に属する SKU か検証
        var productIds = req.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id) && p.DeletedAt == null)
            .ToListAsync(ct);
        if (products.Count != productIds.Count)
            throw DomainException.Validation("指定された SKU の一部が存在しません");
        if (products.Any(p => p.ProductFamilyId != req.ProductFamilyId))
            throw DomainException.Validation("指定 SKU が品番に属していません");

        var family = await db.ProductFamilies.FirstOrDefaultAsync(f => f.Id == req.ProductFamilyId, ct)
            ?? throw DomainException.Validation("品番が存在しません");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.UtcNow;
            var instructionNo = await GenerateInstructionNoAsync(ct);

            var pi = new ProductionInstruction
            {
                InstructionNo = instructionNo,
                ProductFamilyId = req.ProductFamilyId,
                FactorySupplierId = req.FactorySupplierId,
                PlannedQuantity = req.Lines.Sum(l => l.Quantity),
                DueDate = req.DueDate,
                Status = ProductionInstructionStatus.Draft,
                CommunicationText = req.CommunicationText,
                CreatedAt = now, UpdatedAt = now,
                CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                IdempotencyKey = idempotencyKey,
                IdempotencyPayloadHash = idempotencyPayloadHash,
            };
            db.ProductionInstructions.Add(pi);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            var productById = products.ToDictionary(p => p.Id);
            foreach (var l in req.Lines)
            {
                var product = productById[l.ProductId];
                db.ProductionInstructionLines.Add(new ProductionInstructionLine
                {
                    ProductionInstructionId = pi.Id,
                    LineNo = lineNo++,
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = family.ProductName1,
                    Quantity = l.Quantity,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "ProductionInstruction.Create",
                entityType: "ProductionInstruction", entityId: pi.Id,
                note: $"instruction_no={pi.InstructionNo}, qty={pi.PlannedQuantity}", cancellationToken: ct);
            return pi;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>一覧 (PI-02)。キーセットページング (AKB-DOC-12 §7.1): (created_at, id) 降順の安定ソート。</summary>
    public async Task<PagedResult<PiListItem>> ListAsync(
        Guid actorUserId, bool includeCancelled, PageRequest page, CancellationToken ct = default)
    {
        var query = db.ProductionInstructions
            .Include(p => p.FactorySupplier)
            .Include(p => p.ProductFamily)
            .Where(p => p.DeletedAt == null);
        if (!includeCancelled)
            query = query.Where(p => p.Status != ProductionInstructionStatus.Cancelled);

        // キーセット: 前ページ末尾 (created_at, id) より「後ろ」(降順) の行のみ。
        // Guid.CompareTo は Npgsql EF が uuid 比較へ翻訳する (実機検証済)。
        if (page.AfterCreatedAt is { } afterCreatedAt && page.AfterId is { } afterId)
            query = query.Where(p => p.CreatedAt < afterCreatedAt
                || (p.CreatedAt == afterCreatedAt && p.Id.CompareTo(afterId) < 0));

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new
            {
                P = p,
                FirstSku = p.Lines.OrderBy(l => l.LineNo).Select(l => l.SkuSnapshot).FirstOrDefault(),
            })
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        // limit+1 件目が取れたら次ページあり。返却は limit 件に切り詰める。
        var hasMore = rows.Count > page.Limit;
        if (hasMore) rows.RemoveAt(page.Limit);

        var result = rows.Select(x => new PiListItem(
            x.P.Id, x.P.InstructionNo, Sku9(x.FirstSku), x.P.ProductFamily?.ProductName1 ?? "?",
            x.P.FactorySupplier?.Code ?? "?", x.P.FactorySupplier?.Name ?? "?",
            x.P.PlannedQuantity, x.P.DueDate, (short)x.P.Status,
            x.P.FirstExportedAt is null ? "unexported" : "exported",
            x.P.FirstExportedAt, x.P.CreatedAt, x.P.UpdatedAt)).ToList();

        await audit.LogAsync(actorUserId, "ProductionInstruction.List",
            entityType: "ProductionInstruction", note: $"count={result.Count}", cancellationToken: ct);

        var last = rows.Count > 0 ? rows[^1].P : null;
        return new PagedResult<PiListItem>(
            result, hasMore, hasMore ? last?.CreatedAt : null, hasMore ? last?.Id : null);
    }

    /// <summary>詳細 (PI-03 編集画面ベース)。</summary>
    public async Task<PiDetail?> GetDetailAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var pi = await db.ProductionInstructions
            .Include(p => p.FactorySupplier)
            .Include(p => p.ProductFamily)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pi is null) return null;

        var lines = await db.ProductionInstructionLines
            .Include(l => l.Product).ThenInclude(p => p!.Color)
            .Include(l => l.Product).ThenInclude(p => p!.Size)
            .Where(l => l.ProductionInstructionId == id)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);

        var sku9 = Sku9(lines.FirstOrDefault()?.SkuSnapshot);
        await audit.LogAsync(actorUserId, "ProductionInstruction.View",
            entityType: "ProductionInstruction", entityId: id, note: $"instruction_no={pi.InstructionNo}", cancellationToken: ct);

        return new PiDetail(
            pi.Id, pi.InstructionNo, pi.ProductFamilyId, sku9, pi.ProductFamily?.ProductName1 ?? "?",
            pi.FactorySupplierId, pi.FactorySupplier?.Code ?? "?", pi.FactorySupplier?.Name ?? "?",
            pi.PlannedQuantity, pi.DueDate, (short)pi.Status,
            pi.InstructedAt, pi.CompletedAt, pi.CancelledAt, pi.CancelReason,
            pi.CommunicationText, pi.FirstExportedAt, pi.LastExportedAt, pi.CreatedAt, pi.UpdatedAt,
            lines.Select(l => new PiLineDetail(
                l.Id, l.LineNo, l.ProductId, l.SkuSnapshot, l.ProductNameSnapshot,
                l.Product?.Color?.Name ?? "?", l.Product?.Size?.Name ?? "?", l.Quantity)).ToList());
    }

    /// <summary>編集 (PI-03)。Draft/Issued のみ。明細は全削除→再INSERT。</summary>
    public async Task<ProductionInstruction?> UpdateAsync(Guid id, UpdatePiRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var pi = await db.ProductionInstructions.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (pi is null) return null;
        if (pi.Status is ProductionInstructionStatus.Cancelled or ProductionInstructionStatus.Completed)
            throw new DomainException(AkbErrorCodes.MakerProductionStateViolation, 409,
                "中止済/完了済の生産指示は編集できません", "現在の生産指示状態を確認し許可された操作を選択してください");
        if (req.Lines.Count == 0 || req.Lines.Any(l => l.Quantity <= 0))
            throw DomainException.Validation("生産数量は正の数を指定してください");
        if (req.Lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
            throw DomainException.Validation("同一 SKU が重複しています");

        var productIds = req.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id) && p.DeletedAt == null).ToListAsync(ct);
        if (products.Count != productIds.Count || products.Any(p => p.ProductFamilyId != pi.ProductFamilyId))
            throw DomainException.Validation("指定 SKU が品番に属していません");
        var family = await db.ProductFamilies.FirstAsync(f => f.Id == pi.ProductFamilyId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = SystemTime.UtcNow;
            pi.FactorySupplierId = req.FactorySupplierId;
            pi.DueDate = req.DueDate;
            pi.CommunicationText = req.CommunicationText;
            pi.PlannedQuantity = req.Lines.Sum(l => l.Quantity);
            pi.UpdatedAt = now;
            pi.UpdatedByUserId = actorUserId;

            var existing = await db.ProductionInstructionLines.Where(l => l.ProductionInstructionId == id).ToListAsync(ct);
            db.ProductionInstructionLines.RemoveRange(existing);
            await db.SaveChangesAsync(ct);

            short lineNo = 1;
            var productById = products.ToDictionary(p => p.Id);
            foreach (var l in req.Lines)
            {
                var product = productById[l.ProductId];
                db.ProductionInstructionLines.Add(new ProductionInstructionLine
                {
                    ProductionInstructionId = pi.Id,
                    LineNo = lineNo++,
                    ProductId = product.Id,
                    SkuSnapshot = product.Sku,
                    ProductNameSnapshot = family.ProductName1,
                    Quantity = l.Quantity,
                    CreatedAt = now, UpdatedAt = now,
                    CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await audit.LogAsync(actorUserId, "ProductionInstruction.Update",
                entityType: "ProductionInstruction", entityId: id,
                note: $"instruction_no={pi.InstructionNo}, qty={pi.PlannedQuantity}", cancellationToken: ct);
            return pi;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>発行 (PI-03)。Draft→Issued、instructed_at SET。冪等。</summary>
    public async Task<bool> IssueAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var pi = await db.ProductionInstructions.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (pi is null) return false;
        if (pi.Status == ProductionInstructionStatus.Cancelled)
            throw new DomainException(AkbErrorCodes.MakerProductionStateViolation, 409,
                "中止済の生産指示は発行できません", "現在の生産指示状態を確認し許可された操作を選択してください");
        if (pi.Status is ProductionInstructionStatus.Issued or ProductionInstructionStatus.Completed)
            return true; // 冪等

        var now = SystemTime.UtcNow;
        pi.Status = ProductionInstructionStatus.Issued;
        pi.InstructedAt = now;
        pi.UpdatedAt = now;
        pi.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ProductionInstruction.Issue",
            entityType: "ProductionInstruction", entityId: id,
            note: $"instruction_no={pi.InstructionNo}", cancellationToken: ct);
        return true;
    }

    /// <summary>生産完了 (PI-03)。Issued→Completed。</summary>
    public async Task<bool> CompleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var pi = await db.ProductionInstructions.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (pi is null) return false;
        if (pi.Status != ProductionInstructionStatus.Issued)
            throw new DomainException(AkbErrorCodes.MakerProductionStateViolation, 409,
                "発行済の生産指示のみ完了にできます", "現在の生産指示状態を確認し許可された操作を選択してください");

        var now = SystemTime.UtcNow;
        pi.Status = ProductionInstructionStatus.Completed;
        pi.CompletedAt = now;
        pi.UpdatedAt = now;
        pi.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ProductionInstruction.Complete",
            entityType: "ProductionInstruction", entityId: id,
            note: $"instruction_no={pi.InstructionNo}", cancellationToken: ct);
        return true;
    }

    /// <summary>中止 (PI-03)。status=Cancelled。物理削除しない。冪等。</summary>
    public async Task<bool> CancelAsync(Guid id, CancelPiRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var pi = await db.ProductionInstructions.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (pi is null) return false;
        if (pi.Status == ProductionInstructionStatus.Cancelled) return true; // 冪等

        var now = SystemTime.UtcNow;
        pi.Status = ProductionInstructionStatus.Cancelled;
        pi.CancelledAt = now;
        pi.CancelledByUserId = actorUserId;
        pi.CancelReason = req.Reason;
        pi.UpdatedAt = now;
        pi.UpdatedByUserId = actorUserId;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, "ProductionInstruction.Cancel",
            entityType: "ProductionInstruction", entityId: id,
            note: $"instruction_no={pi.InstructionNo}, reason={req.Reason}", cancellationToken: ct);
        return true;
    }

    /// <summary>11桁品番の上位9桁を取り出す (色×サイズを除いた品番)。</summary>
    private static string Sku9(string? sku)
        => string.IsNullOrEmpty(sku) ? "" : (sku.Length >= 9 ? sku[..9] : sku);

    /// <summary>
    /// Idempotency-Key による既存行の引当 (AKB-DOC-12 §8)。
    /// 同一キー・同一ペイロードは既存行 (初回結果) を返し、異なるペイロードは AKB-SYS-005。
    /// 同時二重送信は UNIQUE(tenant_id, idempotency_key) が最終防壁 (23505 → AKB-SYS-007)。
    /// </summary>
    private async Task<ProductionInstruction?> FindByIdempotencyKeyAsync(
        string idempotencyKey, string? payloadHash, CancellationToken ct)
    {
        var existing = await db.ProductionInstructions
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, ct);
        if (existing is null) return null;
        if (existing.IdempotencyPayloadHash == payloadHash) return existing;
        throw new DomainException(AkbErrorCodes.SysIdempotencyKeyConflict, 409,
            "同一の Idempotency-Key が異なる内容のリクエストで再利用されました",
            userAction: "新しい Idempotency-Key を生成して再実行してください");
    }

    /// <summary>instruction_no 採番 (YY-PI-NNNNN)。呼び出し元のトランザクション内で advisory lock を取得。</summary>
    private async Task<string> GenerateInstructionNoAsync(CancellationToken ct)
    {
        var year2 = SystemTime.JstNow.Year % 100; // 採番の年度判定は業務時刻 (JST)
        var prefix = $"{year2:D2}-PI-";
        var lockKey = $"{tenantContext.RequireTenantId()}:PI-{year2:D2}";
        for (var attempt = 0; attempt < MaxNumberingRetries; attempt++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)", ct);
            var existing = await db.ProductionInstructions
                .Where(p => p.InstructionNo.StartsWith(prefix))
                .Select(p => p.InstructionNo)
                .ToListAsync(ct);
            var maxSeq = existing
                .Select(s => s.Length > prefix.Length && int.TryParse(s.AsSpan(prefix.Length), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            var candidate = $"{prefix}{maxSeq + 1:D5}";
            if (!await db.ProductionInstructions.AnyAsync(p => p.InstructionNo == candidate, ct))
                return candidate;
        }
        throw new DomainException(AkbErrorCodes.MakerNumberingConflict, 409,
            "生産指示番号の採番に失敗しました。再試行してください", "時間をおいて再試行してください");
    }
}
