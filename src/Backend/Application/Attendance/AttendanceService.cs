using System.Globalization;
using Akebono.Application.Common;
using Akebono.Domain.Attendance;
using Akebono.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Attendance;

/// <summary>
/// 勤怠 (打刻・集計・タイムカード・打刻修正申請) の Application サービス。
///
/// 設計方針 (移植仕様 §4.8 / §5):
///   - 集計はすべてサーバーサイドで実行する。**日次 API も月次経由で算出する**
///     (60h 超バケットは月内累計に依存するため、単日だけ計算すると over60Ot が常に 0 になる)。
///   - 打刻は users 行の悲観ロック (FOR UPDATE) でユーザ単位に直列化する (二重打刻防止)。
///   - punch_records は追記のみ。修正申請の承認でも元打刻は削除せず fix レコードを追記する
///     (記録系保護。有効打刻の解決は AttendanceCalc.EffectivePunches が行う)。
///   - 監査ログは主処理 commit 後に記録する (原則4: 失敗しても主フローを止めない)。
/// </summary>
public class AttendanceService(IAkebonoDbContext db, IAuditLogger audit)
{
    /// <summary>
    /// タイムカード照会期間の上限 (日)。**両端を含む**日数で数える
    /// (from=2026-01-01 / to=2026-03-03 は 62 日ちょうどで可、2026-03-04 は 63 日で不可)。
    /// **本定数がプロジェクトの SoT**。
    /// フロント (pages/attendance) は同値の定数を本定数への参照コメント付きで持ち、
    /// 同じ「両端含み」の数え方でクランプする。
    /// </summary>
    public const int TimecardRangeMaxDays = 62;

    /// <summary>
    /// タイムカードで一度に集計できる利用者数の上限。超過は 422 AKB-SYS-002 で弾く
    /// (limit / cursor を持たないエンドポイントのため、ページング不正 AKB-SYS-011 は使わない)。
    /// 利用者数 × 期間 (最大 62 日) 分の打刻をすべてメモリへ載せて集計するため、
    /// 非有界のままだと在籍者の増加に比例して必ずタイムアウトする。
    /// 超過時は q (氏名の部分一致) で絞り込ませる。
    /// </summary>
    public const int TimecardMaxUsers = 200;

    /// <summary>修正理由 (attendance_fix_requests.reason VARCHAR(512)) の最大長。</summary>
    private const int ReasonMaxLength = 512;

    /// <summary>
    /// 修正申請の requestedAt に許可する書式。**タイムゾーン必須**
    /// (フロントは JST の "YYYY-MM-DDTHH:mm:00+09:00" で送る)。
    /// </summary>
    private static readonly string[] RequestedAtFormats =
    [
        // 'T' / 'Z' はリテラルとしてクォートする (書式指定子との衝突を避ける)。
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    // ── #1 打刻 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 打刻 (出勤/退勤/休憩開始/休憩終了)。対象は常に本人。
    /// 同一ユーザの同時打刻は users 行ロックで直列化し、状態機械で順序違反を 409 で弾く。
    /// </summary>
    public async Task<PunchResultDto> PunchAsync(Guid actorId, PunchRequest req, CancellationToken ct = default)
    {
        var kind = ParseKind(req.Kind);
        var date = SystemTime.TodayJst;   // 業務日付は JST
        var now = SystemTime.UtcNow;      // 格納は UTC

        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw DomainException.Concealed("指定されたリソースが見つかりません");
        if (!actor.PunchRequired)
            throw new DomainException(AkbErrorCodes.AuthInsufficientPermission, 403,
                "打刻対象の利用者ではありません",
                "打刻が必要な場合は管理者へ連絡してください");

        PunchResultDto result;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 同一ユーザの同時打刻を直列化する (二重打刻防止)。行ロックで十分
            // (office の pg_advisory_xact_lock('punch:'||userId) 相当)。
            // ExecuteSqlRawAsync は必ずパラメータ化する (SQL インジェクション防止)。
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM users WHERE id = {0} FOR UPDATE", new object[] { actorId }, ct);

            var rows = await LoadPunchesAsync(actorId, date, date.AddDays(1), ct);
            var state = AttendanceCalc.PunchStateOf(rows);
            if (!AttendanceCalc.IsAllowed(state, kind))
                throw new DomainException(AkbErrorCodes.SysUniqueViolation, 409,
                    $"現在の状態では「{KindLabel(kind)}」はできません",
                    "現在の状態を確認してから打刻してください");

            var entity = new PunchRecord
            {
                UserId = actorId,
                Date = date,
                Kind = kind,
                At = now,
                Source = PunchSource.Web,
                CreatedAt = now,
            };
            db.PunchRecords.Add(entity);
            await db.SaveChangesAsync(ct);

            // 打刻後の状態 (フロントのボタン活性制御に使う)。追記済みの列で再判定する。
            // 純粋な計算のため commit 前に済ませ、CommitAsync を try の最後の文にする (原則4)。
            rows.Add(entity);
            result = new PunchResultDto(entity.Id, AttendanceCalc.PunchStateOf(rows));

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // 監査ログは確定済みトランザクションの外で記録する
        // (try 内に置くと記録失敗時に commit 済みの tx を Rollback しようとする。原則4)。
        await audit.LogAsync(actorId, "Punch.Create",
            entityType: "PunchRecord", entityId: result.Id,
            note: $"kind={kind}, date={date:yyyy-MM-dd}", cancellationToken: ct);

        return result;
    }

    // ── #2 当日の打刻状態 ────────────────────────────────────────────────

    /// <summary>打刻ウィジェット用の当日状態。punches は生打刻列 (置換解決前) を返す。</summary>
    public async Task<PunchStateDto> GetStateAsync(Guid actorId, CancellationToken ct = default)
    {
        var date = SystemTime.TodayJst;
        var rows = await LoadPunchesAsync(actorId, date, date.AddDays(1), ct);
        return new PunchStateDto(
            AttendanceCalc.PunchStateOf(rows),
            rows.Select(ToPunchDto).ToList());
    }

    // ── #3 日次サマリ ────────────────────────────────────────────────────

    /// <summary>
    /// 日次サマリ。**月次経由で算出する** (60h 超バケットの月内累計を保つため。移植仕様 §5.1)。
    /// raw=true のとき修正前を含む生打刻を rawPunches に同梱する。
    /// </summary>
    public async Task<DaySummaryDto> GetDayAsync(
        Guid targetUserId, string? date, bool raw, CancellationToken ct = default)
    {
        var day = ParseDate(date, SystemTime.TodayJst, "date");
        var first = new DateOnly(day.Year, day.Month, 1);

        var rows = await LoadPunchesAsync(targetUserId, first, first.AddMonths(1), ct);
        var rule = await ResolveRuleForAsync(targetUserId, ct);
        var month = AttendanceCalc.MonthSummary(GroupByDate(rows), rule, day.Year, day.Month);
        // MonthSummary は当月全日を生成するため、当該日は必ず存在する。
        var summary = month.Days.First(d => d.Date == day);

        var rawPunches = raw
            ? rows.Where(p => p.Date == day).Select(ToPunchDto).ToList()
            : null;

        return new DaySummaryDto(
            summary.Date, summary.WorkMinutes, summary.BreakMinutes, summary.NightMinutes,
            ToBucketsDto(summary.Buckets), summary.BreakShortage,
            summary.Punches.Select(ToPunchDto).ToList(),
            rawPunches);
    }

    // ── #4 月次サマリ ────────────────────────────────────────────────────

    /// <summary>月次サマリ (日別 + 6 バケット合計 + 出勤日数)。month 既定は当月 (JST)。</summary>
    public async Task<MonthSummaryDto> GetMonthAsync(
        Guid targetUserId, string? month, CancellationToken ct = default)
    {
        var (year, monthNo) = ParseMonth(month, "month");
        var first = new DateOnly(year, monthNo, 1);

        var rows = await LoadPunchesAsync(targetUserId, first, first.AddMonths(1), ct);
        var rule = await ResolveRuleForAsync(targetUserId, ct);
        var summary = AttendanceCalc.MonthSummary(GroupByDate(rows), rule, year, monthNo);

        var days = summary.Days
            .Select(d => new DaySummaryDto(
                d.Date, d.WorkMinutes, d.BreakMinutes, d.NightMinutes,
                ToBucketsDto(d.Buckets), d.BreakShortage,
                d.Punches.Select(ToPunchDto).ToList()))
            .ToList();

        return new MonthSummaryDto(days, ToBucketsDto(summary.Total), summary.WorkDays);
    }

    // ── #5 36 協定アラート ───────────────────────────────────────────────

    /// <summary>36 協定アラート (endMonth を最終月とする直近 6 ヶ月)。endMonth 既定は当月 (JST)。</summary>
    public async Task<IReadOnlyList<Article36AlertDto>> GetAlertsAsync(
        Guid targetUserId, string? endMonth, CancellationToken ct = default)
    {
        var (year, monthNo) = ParseMonth(endMonth, "endMonth");
        var anchor = new DateOnly(year, monthNo, 1);

        // 直近 6 ヶ月 (endMonth を最終月) 分の打刻をまとめて読む。
        var rows = await LoadPunchesAsync(targetUserId, anchor.AddMonths(-5), anchor.AddMonths(1), ct);
        var rule = await ResolveRuleForAsync(targetUserId, ct);

        return AttendanceCalc.Article36Alerts(GroupByDate(rows), rule, year, monthNo)
            .Select(a => new Article36AlertDto(a.Level, a.Code, a.Message))
            .ToList();
    }

    // ── #6 全員のタイムカード ────────────────────────────────────────────

    /// <summary>
    /// 全員のタイムカード (オーナーのみ)。既定期間は当月 1 日〜今日、期間の上限は
    /// <see cref="TimecardRangeMaxDays"/> 日 (両端含み)、対象利用者数の上限は
    /// <see cref="TimecardMaxUsers"/> 人。q は氏名の部分一致。
    /// ソートは 日付降順 → 氏名昇順 → 利用者 Id 昇順 (同姓同名でも行順を確定させる)。
    /// </summary>
    public async Task<IReadOnlyList<TimecardRowDto>> GetTimecardAsync(
        string? from, string? to, string? q, CancellationToken ct = default)
    {
        var today = SystemTime.TodayJst;
        var fromDate = ParseDate(from, new DateOnly(today.Year, today.Month, 1), "from");
        var toDate = ParseDate(to, today, "to");
        if (fromDate > toDate)
            throw DomainException.Validation("期間の開始日は終了日以前にしてください",
                "日付（から）と日付（まで）を入れ替えて再検索してください");
        // 両端を含む日数で判定する (フロントのクランプと同じ数え方に揃える)。
        if (toDate.DayNumber - fromDate.DayNumber + 1 > TimecardRangeMaxDays)
            throw DomainException.Validation($"期間は最大 {TimecardRangeMaxDays} 日までです",
                "期間を狭めて再検索してください");

        var name = (q ?? string.Empty).Trim();
        var usersQuery = db.Users.AsNoTracking().Where(u => u.IsActive && u.DeletedAt == null);
        if (name.Length > 0) usersQuery = usersQuery.Where(u => u.DisplayName.Contains(name));
        // 上限 +1 件だけ取得して超過を検知する (既存のページング規約と同じ考え方)。
        var users = await usersQuery.OrderBy(u => u.EmployeeNo)
            .Take(TimecardMaxUsers + 1)
            .ToListAsync(ct);
        // 期間を短くしても利用者数は減らない (利用者クエリに期間条件が無い) ため、
        // 復帰導線は「氏名で絞り込む」のみを案内する。
        if (users.Count > TimecardMaxUsers)
            throw DomainException.Validation(
                $"対象の利用者が多すぎます（一度に集計できるのは {TimecardMaxUsers} 人までです）",
                "氏名で絞り込んで再検索してください");
        if (users.Count == 0) return [];

        var userIds = users.Select(u => u.Id).ToList();
        var punches = await db.PunchRecords
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.Date >= fromDate && p.Date <= toDate)
            .OrderBy(p => p.At).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);
        var rules = await ActiveRulesAsync(ct);
        var byUser = punches.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<TimecardRowDto>();
        foreach (var user in users)
        {
            if (!byUser.TryGetValue(user.Id, out var mine)) continue;
            var rule = AttendanceCalc.ResolveRule(user, rules);
            foreach (var group in mine.GroupBy(p => p.Date))
            {
                var summary = AttendanceCalc.DaySummary(group.ToList(), rule, group.Key);
                if (summary.Punches.Count == 0) continue;
                result.Add(new TimecardRowDto(
                    user.Id, user.DisplayName, group.Key,
                    summary.Punches.FirstOrDefault(p => p.Kind == PunchKind.In)?.At,
                    summary.Punches.LastOrDefault(p => p.Kind == PunchKind.Out)?.At,
                    summary.WorkMinutes, summary.BreakMinutes));
            }
        }

        // 日付降順 → 氏名昇順 (Ordinal で決定的に) → 利用者 Id 昇順。
        // List.Sort は不安定なため、同姓同名が同日に居ると最終タイブレーカーが無いと行順が非決定になる
        // (他の一覧が Id をタイブレーカーに置いているのと同じ理由)。
        // (利用者, 日付) の組は 1 行しか作られないので、Id まで見れば順序は一意に決まる。
        result.Sort((a, b) =>
        {
            var byDate = b.Date.CompareTo(a.Date);
            if (byDate != 0) return byDate;
            var byName = string.Compare(a.UserName, b.UserName, StringComparison.Ordinal);
            return byName != 0 ? byName : a.UserId.CompareTo(b.UserId);
        });
        return result;
    }

    // ── #7 打刻修正申請の作成 ────────────────────────────────────────────

    /// <summary>打刻修正申請 (対象は常に本人)。理由は必須 (客観的記録の担保)。</summary>
    public async Task<IdResultDto> CreateFixRequestAsync(
        Guid actorId, FixRequestCreateRequest req, CancellationToken ct = default)
    {
        var date = ParseDate(req.Date, null, "date");
        var kind = ParseKind(req.Kind);

        // オフセット必須で解析する。オフセット無しを許すと実行環境 TZ 依存で
        // 時刻がずれるため、書式を限定して弾く (フロントは +09:00 付きで送る)。
        if (!DateTimeOffset.TryParseExact((req.RequestedAt ?? string.Empty).Trim(), RequestedAtFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var requestedAt))
            throw DomainException.Validation(
                "修正後の時刻は YYYY-MM-DDTHH:mm:ss+09:00 形式（タイムゾーン付き）で指定してください",
                "日付と時刻を選び直して再送信してください");

        // 対象日から極端に離れた時刻を承認すると、punch_records (UPDATE/DELETE 剥奪済み = 削除で
        // 復旧できない) に異常値が残り、以後その日の集計が破綻する。JST 換算した日付が
        // 対象日 〜 翌日 (夜勤の日跨ぎを許容) に収まることを必ず検証する。
        var requestedJstDate = DateOnly.FromDateTime(SystemTime.ToJst(requestedAt.UtcDateTime));
        if (requestedJstDate < date || requestedJstDate > date.AddDays(1))
            throw DomainException.Validation(
                "修正後の時刻は対象日または翌日（夜勤の日跨ぎ）の範囲で指定してください",
                "対象日を選び直すか、時刻を対象日の範囲内へ修正して再送信してください");

        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length == 0)
            throw DomainException.Validation("修正理由を入力してください（客観的記録の担保）",
                "修正が必要になった経緯を入力してください");
        if (reason.Length > ReasonMaxLength)
            throw DomainException.Validation($"修正理由は {ReasonMaxLength} 文字以内で入力してください",
                "修正理由を短くして再送信してください");

        var now = SystemTime.UtcNow;
        var entity = new AttendanceFixRequest
        {
            UserId = actorId,
            Date = date,
            Kind = kind,
            // JST オフセット付きで受け取り、格納は UTC に統一する。
            RequestedAt = requestedAt.UtcDateTime,
            Reason = reason,
            Status = FixRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AttendanceFixRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorId, "AttendanceFixRequest.Create",
            entityType: "AttendanceFixRequest", entityId: entity.Id,
            note: $"date={date:yyyy-MM-dd}, kind={kind}", cancellationToken: ct);

        return new IdResultDto(entity.Id);
    }

    // ── #8 打刻修正申請の一覧 ────────────────────────────────────────────

    /// <summary>
    /// 打刻修正申請の一覧 (createdAt 降順)。all=false は本人の申請のみ。
    /// all=true (scope=all) はオーナーのみ許可 (エンドポイント側で権限確認済み)。
    ///
    /// **キーセットページング必須** (AKB-DOC-12 §7.1、PurchaseOrderService.ListAsync と同じ規約)。
    /// status 未指定の scope=all は運用年数に比例して全履歴を返すため、非有界のままでは必ず
    /// タイムアウトする。返却は page.Limit 件で打ち切り、続きの有無は meta.page.hasMore で示す。
    /// </summary>
    public async Task<PagedResult<FixRequestDto>> ListFixRequestsAsync(
        Guid actorId, string? status, bool all, PageRequest page, CancellationToken ct = default)
    {
        var filter = ParseStatus(status);
        var query = db.AttendanceFixRequests.AsNoTracking();
        if (!all) query = query.Where(r => r.UserId == actorId);
        if (filter is { } s) query = query.Where(r => r.Status == s);

        // キーセット: 前ページ末尾 (created_at, id) より「後ろ」(降順) の行のみ。
        if (page.AfterCreatedAt is { } afterCreatedAt && page.AfterId is { } afterId)
            query = query.Where(r => r.CreatedAt < afterCreatedAt
                || (r.CreatedAt == afterCreatedAt && r.Id.CompareTo(afterId) < 0));

        var rows = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        // limit+1 件目が取れたら次ページあり。返却は limit 件に切り詰める。
        var hasMore = rows.Count > page.Limit;
        if (hasMore) rows.RemoveAt(page.Limit);
        if (rows.Count == 0) return new PagedResult<FixRequestDto>([], false, null, null);

        // 申請者名 / 決裁者名は scalar FK (ナビ無し) のため users を 1 クエリで解決する。
        // 無効化・削除済ユーザでも表示は維持したいので DeletedAt では絞らない (監査表示の性質)。
        var userIds = rows.Select(r => r.UserId)
            .Concat(rows.Where(r => r.DecidedByUserId != null).Select(r => r.DecidedByUserId!.Value))
            .Distinct().ToList();
        var names = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = rows.Select(r =>
        {
            names.TryGetValue(r.UserId, out var userName);
            string? decidedByName = null;
            if (r.DecidedByUserId is { } decidedBy && names.TryGetValue(decidedBy, out var d))
                decidedByName = d;
            return new FixRequestDto(
                r.Id, r.UserId, userName ?? string.Empty, r.Date, r.Kind, r.RequestedAt, r.Reason,
                r.Status, r.DecidedByUserId, decidedByName, r.CreatedAt);
        }).ToList();

        var last = rows[^1];
        return new PagedResult<FixRequestDto>(
            items, hasMore, hasMore ? last.CreatedAt : null, hasMore ? last.Id : null);
    }

    // ── #9 打刻修正申請の承認 / 却下 ─────────────────────────────────────

    /// <summary>
    /// 打刻修正申請の承認 / 却下 (オーナーのみ)。
    /// トランザクション内で対象行を FOR UPDATE ロックし、Status != Pending を再確認して
    /// 二重承認を防ぐ。承認時は fix レコードを追記する (**元打刻は削除しない**)。
    /// </summary>
    public async Task<IdResultDto> DecideFixRequestAsync(
        Guid actorId, Guid id, FixDecisionRequest req, CancellationToken ct = default)
    {
        var approved = ParseDecision(req.Action);

        Guid decidedId;
        string auditNote;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 二重承認防止のため対象行を悲観ロックしてから再読込・再確認する
            // (ExecuteSqlRawAsync は必ずパラメータ化する)。
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM attendance_fix_requests WHERE id = {0} FOR UPDATE",
                new object[] { id }, ct);

            var fix = await db.AttendanceFixRequests.FirstOrDefaultAsync(r => r.Id == id, ct)
                ?? throw DomainException.Concealed("指定されたリソースが見つかりません");
            if (fix.Status != FixRequestStatus.Pending)
                throw new DomainException(AkbErrorCodes.SysUniqueViolation, 409,
                    "この申請は処理済みです",
                    "一覧を再読込して最新の状態を確認してください");

            var now = SystemTime.UtcNow;
            if (approved)
            {
                var rows = await LoadPunchesAsync(fix.UserId, fix.Date, fix.Date.AddDays(1), ct);
                // 置換対象 = 現在有効な同種打刻 (fix の連鎖でも EffectivePunches が最新のみ返す)。
                var existing = AttendanceCalc.EffectivePunches(rows)
                    .FirstOrDefault(p => p.Kind == fix.Kind);
                db.PunchRecords.Add(new PunchRecord
                {
                    UserId = fix.UserId,
                    Date = fix.Date,
                    Kind = fix.Kind,
                    At = fix.RequestedAt,
                    Source = PunchSource.Fix,
                    FixedFrom = existing?.At,
                    FixReason = fix.Reason,
                    ApprovedByUserId = actorId,
                    CreatedAt = now,
                });
            }

            fix.Status = approved ? FixRequestStatus.Approved : FixRequestStatus.Rejected;
            fix.DecidedByUserId = actorId;
            fix.UpdatedAt = now;

            await db.SaveChangesAsync(ct);

            // 監査ログの材料は commit 前に確定させ、CommitAsync を try の最後の文にする (原則4)。
            decidedId = fix.Id;
            auditNote = $"user={fix.UserId}, date={fix.Date:yyyy-MM-dd}, kind={fix.Kind}";

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // 監査ログは確定済みトランザクションの外で記録する (記録失敗で主フローを止めない。原則4)。
        await audit.LogAsync(actorId,
            approved ? "AttendanceFixRequest.Approve" : "AttendanceFixRequest.Reject",
            entityType: "AttendanceFixRequest", entityId: decidedId,
            note: auditNote, cancellationToken: ct);

        return new IdResultDto(decidedId);
    }

    // ── 内部ヘルパー ─────────────────────────────────────────────────────

    /// <summary>
    /// 打刻列の取得 (fromInclusive 以上 toExclusive 未満)。順序は追記順 = (at, created_at)。
    /// 打刻は追記のみで本経路から更新しないため参照専用 (AsNoTracking) で読む。
    /// </summary>
    private Task<List<PunchRecord>> LoadPunchesAsync(
        Guid userId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken ct)
        => db.PunchRecords
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Date >= fromInclusive && p.Date < toExclusive)
            .OrderBy(p => p.At).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);

    /// <summary>削除されていない勤怠ルール (ResolveRule のフォールバック順に合わせ name 昇順)。</summary>
    private Task<List<AttendanceRule>> ActiveRulesAsync(CancellationToken ct)
        => db.AttendanceRules
            .AsNoTracking()
            .Where(r => r.DeletedAt == null)
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .ToListAsync(ct);

    /// <summary>
    /// 利用者へ適用する勤怠ルールの解決 (個別割当 → 既定ルール → 先頭)。
    /// 対象利用者が存在しない場合は 404 (越境含む存在秘匿)。
    /// </summary>
    private async Task<AttendanceRule?> ResolveRuleForAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw DomainException.Concealed("指定されたリソースが見つかりません");
        var rules = await ActiveRulesAsync(ct);
        return AttendanceCalc.ResolveRule(user, rules);
    }

    private static Dictionary<DateOnly, IReadOnlyList<PunchRecord>> GroupByDate(
        IEnumerable<PunchRecord> rows)
        => rows.GroupBy(p => p.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PunchRecord>)g.ToList());

    private static PunchDto ToPunchDto(PunchRecord p)
        => new(p.Id, p.Kind, p.At, p.Source, p.FixedFrom, p.FixReason, p.ApprovedByUserId);

    private static BucketsDto ToBucketsDto(AttendanceBuckets b)
        => new(b.Scheduled, b.StatutoryOt, b.NonStatutoryOt, b.Over60Ot, b.Night, b.LegalHoliday);

    /// <summary>打刻種別の日本語ラベル (エラーメッセージ用)。表示用ラベルはフロント側にも持つ。</summary>
    private static string KindLabel(PunchKind kind) => kind switch
    {
        PunchKind.In => "出勤",
        PunchKind.Out => "退勤",
        PunchKind.BreakStart => "休憩開始",
        _ => "休憩終了",
    };

    private static PunchKind ParseKind(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "in" => PunchKind.In,
            "out" => PunchKind.Out,
            "breakstart" => PunchKind.BreakStart,
            "breakend" => PunchKind.BreakEnd,
            _ => throw DomainException.Validation(
                "打刻種別は in / out / breakStart / breakEnd のいずれかを指定してください",
                "打刻ボタンから操作し直してください"),
        };

    private static bool ParseDecision(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "approved" => true,
            "rejected" => false,
            _ => throw DomainException.Validation("action は approved / rejected を指定してください",
                "承認 / 却下のボタンから操作し直してください"),
        };

    private static FixRequestStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => FixRequestStatus.Pending,
            "approved" => FixRequestStatus.Approved,
            "rejected" => FixRequestStatus.Rejected,
            _ => throw DomainException.Validation(
                "status は pending / approved / rejected のいずれかを指定してください",
                "絞り込みの状態を選び直して再検索してください"),
        };
    }

    /// <summary>
    /// YYYY-MM-DD の解析。fallback が null のとき未指定は 422。
    /// 業務上ありえない日付 (0001 年等) は .NET の英語例外や非現実的な集計範囲に化けるため、
    /// <see cref="AttendanceCalc.IsBusinessDateInRange"/> の妥当範囲でも弾く。
    ///
    /// 休暇申請一覧の期間絞り込み (<see cref="LeaveService.ListRequestsAsync"/>) からも使う。
    /// 日付クエリの書式・妥当範囲・エラー文言を勤怠内で 1 箇所に保つため public にしている (原則3)。
    /// </summary>
    public static DateOnly ParseDate(string? value, DateOnly? fallback, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback ?? throw DomainException.Validation($"{label} を指定してください",
                "日付を選択して再送信してください");
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw DomainException.Validation($"{label} は YYYY-MM-DD 形式で指定してください",
                "カレンダーから日付を選び直して再送信してください");

        var today = SystemTime.TodayJst;
        if (!AttendanceCalc.IsBusinessDateInRange(parsed, today))
            throw DomainException.Validation(
                $"{label} は {AttendanceCalc.MinBusinessDate:yyyy-MM-dd} 〜 {today.AddYears(1):yyyy-MM-dd} の範囲で指定してください",
                "対象の日付を選び直して再送信してください");
        return parsed;
    }

    /// <summary>YYYY-MM の解析 (未指定は当月 JST)。不正形式・範囲外は 422。</summary>
    private static (int Year, int Month) ParseMonth(string? value, string label)
    {
        var today = SystemTime.TodayJst;
        if (string.IsNullOrWhiteSpace(value)) return (today.Year, today.Month);

        var v = value.Trim();
        if (v.Length != 7 || v[4] != '-'
            || !int.TryParse(v.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(v.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || y < 1 || y > 9999 || m < 1 || m > 12)
            throw DomainException.Validation($"{label} は YYYY-MM 形式で指定してください",
                "対象の年月を選び直して再送信してください");

        // 月初日で妥当範囲を判定する (日付側と同じ基準)。
        if (!AttendanceCalc.IsBusinessDateInRange(new DateOnly(y, m, 1), today))
            throw DomainException.Validation(
                $"{label} は {AttendanceCalc.MinBusinessDate:yyyy-MM} 〜 {today.AddYears(1):yyyy-MM} の範囲で指定してください",
                "対象の年月を選び直して再送信してください");
        return (y, m);
    }
}
