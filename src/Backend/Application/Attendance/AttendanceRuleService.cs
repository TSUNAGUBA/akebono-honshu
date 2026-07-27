using System.Globalization;
using Akebono.Application.Common;
using Akebono.Domain.Attendance;
using Akebono.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Attendance;

/// <summary>
/// 勤怠ルール (勤務体系マスタ) の Application サービス (移植仕様 §5.3)。
///
/// 設計方針:
///   - 参照は勤怠権限 (1 or 2)、書込はオーナーのみ (エンドポイント側で確認済み)。
///   - PATCH は**部分更新**。null のフィールドは更新しない
///     (CLAUDE.md の Zod .partial() の教訓と同趣旨: 送っていない項目を既定値で潰さない)。
///   - ただし **FlexEnabled=false のときコアタイムはサーバ側で必ず null にする**。
///     フレックス無効時のコアタイムは意味を持たない設定であり、残すと「無効化したのに
///     コアタイムが DB に残る」不整合になる (F-8)。専用のクリアフラグは設けない
///     (FlexEnabled=true のままコアタイムだけ空にする操作は Validate が禁止しており、
///      フラグを足しても到達できる状態が増えないため。原則3)。
///   - IsDefault はテナント内で高々 1 件。true で保存したら他ルールの IsDefault を落とす
///     (同一 SaveChanges = 単一トランザクションで原子的に切り替える)。
///   - 削除は論理削除 (deleted_at)。復元エンドポイントで取り消せる (原則: 操作の取消可能性)。
/// </summary>
public class AttendanceRuleService(IAkebonoDbContext db, IAuditLogger audit)
{
    private const int NameMaxLength = 128;

    /// <summary>
    /// 一覧 (name 昇順)。includeInactive=false は「有効 かつ 未削除」のみを返す。
    /// </summary>
    public async Task<IReadOnlyList<AttendanceRuleDto>> ListAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var query = db.AttendanceRules.AsNoTracking();
        if (!includeInactive) query = query.Where(r => r.DeletedAt == null && r.IsActive);

        var rows = await query.OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>新規作成。同名の有効なルールがある場合は 409 (復帰導線付き)。</summary>
    public async Task<AttendanceRuleDto> CreateAsync(
        AttendanceRuleWriteRequest req, Guid actorId, CancellationToken ct = default)
    {
        Validate(req);

        var name = req.Name.Trim();
        // 部分 UNIQUE 索引に抵触する前に弾く。新規行は未採番のため除外対象なし (Guid.Empty)。
        await EnsureNoActiveDuplicateNameAsync(name, Guid.Empty, ct);

        var now = SystemTime.UtcNow;
        var entity = new AttendanceRule
        {
            Name = name,
            WorkStart = req.WorkStart.Trim(),
            WorkEnd = req.WorkEnd.Trim(),
            BreakMinutes = req.BreakMinutes,
            FlexEnabled = req.FlexEnabled,
            // フレックス無効ならコアタイムは持たせない (F-8。更新時と同じ規則)。
            FlexCoreStart = req.FlexEnabled ? NullIfBlank(req.FlexCoreStart) : null,
            FlexCoreEnd = req.FlexEnabled ? NullIfBlank(req.FlexCoreEnd) : null,
            FlexSettlementMonths = req.FlexSettlementMonths,
            ClosingDay = req.ClosingDay,
            LegalHolidayWeekday = req.LegalHolidayWeekday,
            IsDefault = req.IsDefault,
            IsActive = req.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // 既定の付け替えは追加と同じ SaveChanges に含める (中間状態を残さない)。
        // 新規行は未採番のため、既存行はすべて「他ルール」として扱えばよい。
        if (req.IsDefault) await ClearOtherDefaultsAsync(Guid.Empty, now, ct);

        db.AttendanceRules.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceRule.Create",
            entityType: "AttendanceRule", entityId: entity.Id,
            note: $"Name={entity.Name}", cancellationToken: ct);

        return ToDto(entity);
    }

    /// <summary>
    /// 部分更新。未指定 (null) のフィールドは現在値を保持する。Id 不在は null 返却。
    /// 同名の有効なルールがある場合は 409 (自分自身は除外。作成・復元と同じガード)。
    /// </summary>
    public async Task<AttendanceRuleDto?> UpdateAsync(
        Guid id, AttendanceRulePatchRequest req, Guid actorId, CancellationToken ct = default)
    {
        var entity = await db.AttendanceRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return null;

        // 「未指定 = 現在値」を先に確定させてから、作成時と同じ検証を通す
        // (部分更新でも組合せ検証 = 始業<終業・コアタイム整合 を必ず効かせるため)。
        var merged = new AttendanceRuleWriteRequest(
            req.Name ?? entity.Name,
            req.WorkStart ?? entity.WorkStart,
            req.WorkEnd ?? entity.WorkEnd,
            req.BreakMinutes ?? entity.BreakMinutes,
            req.FlexEnabled ?? entity.FlexEnabled,
            req.FlexCoreStart ?? entity.FlexCoreStart,
            req.FlexCoreEnd ?? entity.FlexCoreEnd,
            req.FlexSettlementMonths ?? entity.FlexSettlementMonths,
            req.ClosingDay ?? entity.ClosingDay,
            req.LegalHolidayWeekday ?? entity.LegalHolidayWeekday,
            req.IsDefault ?? entity.IsDefault,
            req.IsActive ?? entity.IsActive);
        Validate(merged);

        var name = merged.Name.Trim();
        // 名称が変わらない更新でも検査してよい (自分自身は excludeId で除外するため常に通る)。
        await EnsureNoActiveDuplicateNameAsync(name, entity.Id, ct);

        var now = SystemTime.UtcNow;
        // 既定にする場合は毎回他ルールを落とす (冪等。既に複数 default が存在する不整合も自己修復する)。
        if (merged.IsDefault) await ClearOtherDefaultsAsync(entity.Id, now, ct);

        entity.Name = name;
        entity.WorkStart = merged.WorkStart.Trim();
        entity.WorkEnd = merged.WorkEnd.Trim();
        entity.BreakMinutes = merged.BreakMinutes;
        entity.FlexEnabled = merged.FlexEnabled;
        // フレックスを無効化したらコアタイムは残さない (F-8: 無効化しても DB に残る不整合の解消)。
        entity.FlexCoreStart = merged.FlexEnabled ? NullIfBlank(merged.FlexCoreStart) : null;
        entity.FlexCoreEnd = merged.FlexEnabled ? NullIfBlank(merged.FlexCoreEnd) : null;
        entity.FlexSettlementMonths = merged.FlexSettlementMonths;
        entity.ClosingDay = merged.ClosingDay;
        entity.LegalHolidayWeekday = merged.LegalHolidayWeekday;
        entity.IsDefault = merged.IsDefault;
        entity.IsActive = merged.IsActive;
        entity.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceRule.Update",
            entityType: "AttendanceRule", entityId: entity.Id,
            note: $"Name={entity.Name}", cancellationToken: ct);

        return ToDto(entity);
    }

    /// <summary>論理削除 (deleted_at SET)。復元で取り消せる。</summary>
    public async Task<bool> SoftDeleteAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var entity = await db.AttendanceRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return false;

        var now = SystemTime.UtcNow;
        entity.DeletedAt = now;
        // 削除済みルールが「既定」として解決に混ざらないよう既定フラグを外す
        // (ResolveRule は IsActive && IsDefault を見るため、記録ではなく設定系の整合を優先)。
        entity.IsDefault = false;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceRule.Delete",
            entityType: "AttendanceRule", entityId: entity.Id,
            note: $"Name={entity.Name}", cancellationToken: ct);

        return true;
    }

    /// <summary>
    /// 論理削除の取消。既定フラグは復元しない (削除時に外れているため明示的に再設定する)。
    /// 削除後に同名のルールを作られていると復元は部分 UNIQUE 索引に抵触するため、
    /// **事前に検出して何をすれば復元できるかを返す** (汎用の 409「同一のキーを持つデータが既に存在します」
    /// では復帰導線が分からないため。休暇種別の復元 <see cref="LeaveService.RestoreTypeAsync"/> と同形)。
    /// </summary>
    public async Task<bool> RestoreAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var entity = await db.AttendanceRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return false;

        if (entity.DeletedAt != null)
            await EnsureNoActiveDuplicateNameAsync(entity.Name, id, ct);

        entity.DeletedAt = null;
        entity.UpdatedAt = SystemTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceRule.Restore",
            entityType: "AttendanceRule", entityId: entity.Id,
            note: $"Name={entity.Name}", cancellationToken: ct);

        return true;
    }

    // ── 内部ヘルパー ─────────────────────────────────────────────────────

    /// <summary>keepId 以外の既定フラグを落とす (テナント内で既定は高々 1 件)。</summary>
    private async Task ClearOtherDefaultsAsync(Guid keepId, DateTime now, CancellationToken ct)
    {
        var others = await db.AttendanceRules
            .Where(r => r.IsDefault && r.Id != keepId)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            other.IsDefault = false;
            other.UpdatedAt = now;
        }
    }

    /// <summary>
    /// 未削除の同名ルールを検出して 409 を返す
    /// (部分 UNIQUE 索引 uq_attendance_rules_tenant_name = (tenant_id, name) WHERE deleted_at IS NULL
    ///  と同じ条件。<see cref="LeaveService"/> の EnsureNoActiveDuplicateNameAsync と同形)。
    /// **作成・更新・復元の 3 経路すべてで使う** (同じ事象の応答品質を経路で二分させない)。
    /// excludeId は自分自身 (更新・復元の対象) を重複判定から除く。
    /// 新規作成は除外対象が無いため <see cref="Guid.Empty"/> を渡す (採番済み Id と一致しない)。
    /// </summary>
    private async Task EnsureNoActiveDuplicateNameAsync(string name, Guid excludeId, CancellationToken ct)
    {
        var duplicated = await db.AttendanceRules.AnyAsync(
            r => r.DeletedAt == null && r.Name == name && r.Id != excludeId, ct);
        if (duplicated)
            throw DomainException.UniqueViolation($"勤務体系「{name}」は既に登録されています",
                "別の名称を指定するか、同名の勤務体系を改名・削除してから操作をやり直してください");
    }

    private static AttendanceRuleDto ToDto(AttendanceRule r)
        => new(r.Id, r.Name, r.WorkStart, r.WorkEnd, r.BreakMinutes,
            r.FlexEnabled, r.FlexCoreStart, r.FlexCoreEnd, r.FlexSettlementMonths,
            r.ClosingDay, r.LegalHolidayWeekday, r.IsDefault, r.IsActive, r.DeletedAt);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>入力検証 (すべて 422 AKB-SYS-002)。</summary>
    private static void Validate(AttendanceRuleWriteRequest req)
    {
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw DomainException.Validation("名称を入力してください", "勤務体系の名称を入力してください");
        if (name.Length > NameMaxLength)
            throw DomainException.Validation($"名称は {NameMaxLength} 文字以内で入力してください",
                "名称を短くして再送信してください");

        var workStart = (req.WorkStart ?? string.Empty).Trim();
        var workEnd = (req.WorkEnd ?? string.Empty).Trim();
        if (!IsHhmm(workStart) || !IsHhmm(workEnd))
            throw DomainException.Validation("始業・終業は HH:mm 形式で入力してください",
                "始業・終業を 09:00 のような HH:mm 形式で入力してください");
        // 'HH:mm' はゼロ埋め固定長のため序数比較で時刻の前後を判定できる。
        if (string.CompareOrdinal(workStart, workEnd) >= 0)
            throw DomainException.Validation("終業は始業より後の時刻にしてください",
                "始業・終業のどちらかを修正して再送信してください");

        if (req.BreakMinutes is < 0 or > 240)
            throw DomainException.Validation("休憩時間は 0〜240 分で入力してください",
                "休憩時間を 0〜240 の範囲で入力してください");
        if (req.ClosingDay is < 1 or > 31)
            throw DomainException.Validation("締め日は 1〜31 で入力してください（31 = 月末）",
                "締め日を 1〜31 の範囲で入力してください");
        if (req.LegalHolidayWeekday is < 0 or > 6)
            throw DomainException.Validation("法定休日の曜日は 0〜6（0 = 日曜）で入力してください",
                "法定休日の曜日を選び直してください");
        if (req.FlexSettlementMonths is < 1 or > 3)
            throw DomainException.Validation("フレックスの清算期間は 1〜3 ヶ月で入力してください",
                "清算期間を 1〜3 ヶ月で入力してください");

        if (!req.FlexEnabled) return;

        var coreStart = (req.FlexCoreStart ?? string.Empty).Trim();
        var coreEnd = (req.FlexCoreEnd ?? string.Empty).Trim();
        if (!IsHhmm(coreStart) || !IsHhmm(coreEnd))
            throw DomainException.Validation("コアタイムは HH:mm 形式で入力してください",
                "コアタイムを入力するか、フレックスを無効にして再送信してください");
        if (string.CompareOrdinal(coreStart, coreEnd) >= 0)
            throw DomainException.Validation("コアタイムの終了は開始より後の時刻にしてください",
                "コアタイムの開始・終了のどちらかを修正して再送信してください");
    }

    /// <summary>'HH:mm' (00:00〜23:59) か。</summary>
    private static bool IsHhmm(string? value)
        => value is { Length: 5 }
           && value[2] == ':'
           && int.TryParse(value.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
           && int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
           && h is >= 0 and <= 23
           && m is >= 0 and <= 59;
}
