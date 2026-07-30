using Akebono.Application.Attendance;
using Akebono.Application.Common;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 勤怠 REST endpoint (移植仕様 §5.1〜§5.3、#1〜#14)。
///
/// 権限 (移植仕様 §2):
///   - 打刻・各種申請 (書込)         : attendance_permission == 1 (AuthEndpoints.CheckAttendanceWriteAsync)
///   - 自分の勤怠参照               : attendance_permission 1 or 2 (CheckAttendanceReadAsync)
///   - 他人の勤怠参照 / 承認 / 設定  : 勤怠 1 or 2 AND オーナー process_record_permission >= 1
///                                    (CheckAttendanceAdminAsync。オーナーだけでは足りない)
///
/// 業務エラー (検証 422 / 状態機械・処理済み 409 / 未検出 404) は service 層の
/// DomainException を ApiExceptionMiddleware がエラー封筒へ変換する。
/// 休暇 (§5.4、#15〜#27) は AttendanceLeaveEndpoints が別途 map する。
/// </summary>
public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/maker/v1/attendance");

        // ── §5.1 打刻・集計 ──────────────────────────────────────────────

        // #1 打刻 (対象は常に本人)。打刻対象か (punch_required) は service 層で確認する。
        group.MapPost("/punches", async (HttpContext http, IAkebonoDbContext db,
                                          AttendanceService svc, PunchRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceWriteAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await svc.PunchAsync(auth.ActorId!.Value, req, ct);
            return ApiEnvelope.Created(http, "/api/maker/v1/attendance/state", result);
        });

        // #2 当日の打刻状態 (打刻ウィジェット用。punches は生打刻列)
        group.MapGet("/state", async (HttpContext http, IAkebonoDbContext db,
                                       AttendanceService svc, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var state = await svc.GetStateAsync(auth.ActorId!.Value, ct);
            return ApiEnvelope.Ok(http, state);
        });

        // #3 日次サマリ (?raw=1 で修正前の生打刻も同梱)。60h 超を正しく出すため月次経由で算出する。
        group.MapGet("/day", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                     Guid? userId, string? date, string? raw, CancellationToken ct) =>
        {
            var (targetId, error) = await ResolveTargetAsync(http, db, userId, ct);
            if (error is not null) return error;
            var summary = await svc.GetDayAsync(targetId, date, IsTruthy(raw), ct);
            return ApiEnvelope.Ok(http, summary);
        });

        // #4 月次サマリ (既定 = 当月 JST)
        group.MapGet("/month", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                       Guid? userId, string? month, CancellationToken ct) =>
        {
            var (targetId, error) = await ResolveTargetAsync(http, db, userId, ct);
            if (error is not null) return error;
            var summary = await svc.GetMonthAsync(targetId, month, ct);
            return ApiEnvelope.Ok(http, summary);
        });

        // #5 36 協定アラート (endMonth を最終月とする直近 6 ヶ月)
        group.MapGet("/alerts", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                        Guid? userId, string? endMonth, CancellationToken ct) =>
        {
            var (targetId, error) = await ResolveTargetAsync(http, db, userId, ct);
            if (error is not null) return error;
            var alerts = await svc.GetAlertsAsync(targetId, endMonth, ct);
            return ApiEnvelope.Ok(http, alerts);
        });

        // #6 全員のタイムカード (勤怠参照権限 AND オーナー)。期間上限は AttendanceService.TimecardRangeMaxDays。
        group.MapGet("/timecard", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                          string? from, string? to, string? q, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var rows = await svc.GetTimecardAsync(from, to, q, ct);
            return ApiEnvelope.Ok(http, rows);
        });

        // ── §5.2 打刻修正申請 ────────────────────────────────────────────

        // #7 修正申請 (対象は常に本人、理由必須)
        group.MapPost("/fix-requests", async (HttpContext http, IAkebonoDbContext db,
                                               AttendanceService svc, FixRequestCreateRequest req,
                                               CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceWriteAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateFixRequestAsync(auth.ActorId!.Value, req, ct);
            return ApiEnvelope.Created(http,
                $"/api/maker/v1/attendance/fix-requests/{created.Id}", created);
        });

        // #8 修正申請の一覧。scope=all (全件) は勤怠参照権限 AND オーナー。
        // キーセットページング (AKB-DOC-12 §7.1): ?limit=&cursor=<opaque>、不正は 400 AKB-SYS-011。
        // data は従来どおり配列のままで、続きの有無は meta.page.hasMore が示す (フロント契約は非破壊)。
        // limit 未指定時の既定は上限値 (PageRequest.MaxLimit)。フロント (useAttendance.loadFixRequests) は
        // まだ limit / cursor を送らないため、既定 50 だと申請が黙って欠落する。
        group.MapGet("/fix-requests", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                              string? status, string? scope, int? limit, string? cursor,
                                              CancellationToken ct) =>
        {
            // scope=all は管理操作 (全件参照)。CheckAttendanceAdminAsync が参照権限 (1 or 2) を内包する。
            // scope=self (既定) / assigned (自分が承認者の actionable) は参照権限のみで可。
            var all = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            var auth = all
                ? await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct)
                : await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var page = PageCursor.Read(limit ?? PageRequest.MaxLimit, cursor);
            var result = await svc.ListFixRequestsAsync(auth.ActorId!.Value, status, scope, page, ct);
            return ApiEnvelope.OkPaged(http, result, page.Limit);
        });

        // #9 修正申請の承認 / 却下 (多段承認)。
        // 権限は参照権限まで (承認可否は経路スナップショット + CanDecide で service が判定する:
        //   オーナーは常に可 / 経路未設定はオーナーのみ / 経路ありは現在ステップの承認者本人)。
        // これにより経路で委任された非オーナー承認者も承認でき、経路未設定時は従来どおりオーナー単段になる。
        // 承認が最終ステップに達したときのみ fix レコードを追記する (元打刻は削除しない)。
        group.MapPost("/fix-requests/{id:guid}/decision", async (HttpContext http, IAkebonoDbContext db,
                                                                  AttendanceService svc, Guid id,
                                                                  FixDecisionRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await svc.DecideFixRequestAsync(auth.ActorId!.Value, id, req, ct);
            return ApiEnvelope.Ok(http, result);
        });

        // ── §5.2b 直行/直帰申請 (Iteration 33) ───────────────────────────

        // #9a 直行/直帰の申請 (対象は常に本人、理由必須)
        group.MapPost("/direct-requests", async (HttpContext http, IAkebonoDbContext db,
                                                  AttendanceService svc, DirectRequestCreateRequest req,
                                                  CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceWriteAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateDirectRequestAsync(auth.ActorId!.Value, req, ct);
            return ApiEnvelope.Created(http,
                $"/api/maker/v1/attendance/direct-requests/{created.Id}", created);
        });

        // #9b 直行/直帰申請の一覧 (scope=self / all(オーナー) / assigned)。キーセットページング。
        group.MapGet("/direct-requests", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                                 string? status, string? scope, int? limit, string? cursor,
                                                 CancellationToken ct) =>
        {
            var all = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            var auth = all
                ? await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct)
                : await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var page = PageCursor.Read(limit ?? PageRequest.MaxLimit, cursor);
            var result = await svc.ListDirectRequestsAsync(auth.ActorId!.Value, status, scope, page, ct);
            return ApiEnvelope.OkPaged(http, result, page.Limit);
        });

        // #9c 直行/直帰申請の承認 / 却下 / 取下げ (多段承認)。取下げは申請者本人のみ (service で判定)。
        group.MapPost("/direct-requests/{id:guid}/actions", async (HttpContext http, IAkebonoDbContext db,
                                                                    AttendanceService svc, Guid id,
                                                                    DirectDecisionRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await svc.DecideDirectRequestAsync(auth.ActorId!.Value, id, req, ct);
            return ApiEnvelope.Ok(http, result);
        });

        // ── §5.3 勤怠ルール (勤務体系マスタ) ─────────────────────────────

        // #10 一覧 (勤怠 1 or 2)
        group.MapGet("/rules", async (HttpContext http, IAkebonoDbContext db, AttendanceRuleService svc,
                                       bool? includeInactive, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var rules = await svc.ListAsync(includeInactive ?? false, ct);
            return ApiEnvelope.Ok(http, rules);
        });

        // #11 新規作成 (勤怠参照権限 AND オーナー)
        group.MapPost("/rules", async (HttpContext http, IAkebonoDbContext db, AttendanceRuleService svc,
                                        AttendanceRuleWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/attendance/rules/{created.Id}", created);
        });

        // #12 部分更新 (勤怠参照権限 AND オーナー)。null のフィールドは更新しない。
        group.MapPatch("/rules/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                                   AttendanceRuleService svc, Guid id,
                                                   AttendanceRulePatchRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        // #13 論理削除 (勤怠参照権限 AND オーナー)
        group.MapDelete("/rules/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                                    AttendanceRuleService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // #14 論理削除の取消 (勤怠参照権限 AND オーナー)
        group.MapPost("/rules/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                          AttendanceRuleService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.RestoreAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // ── §5.5 勤怠承認経路 (Iteration 33) ─────────────────────────────
        // 参照は勤怠権限 (1 or 2)、書込は勤怠参照権限 AND オーナー (#10〜#14 の勤怠ルールと同権限)。

        // #15 経路一覧 (勤怠 1 or 2)
        group.MapGet("/approval-routes", async (HttpContext http, IAkebonoDbContext db,
                                                 AttendanceRouteService svc, bool? includeInactive,
                                                 CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var routes = await svc.ListAsync(includeInactive ?? false, ct);
            return ApiEnvelope.Ok(http, routes);
        });

        // #16 経路の新規作成 (勤怠参照権限 AND オーナー)
        group.MapPost("/approval-routes", async (HttpContext http, IAkebonoDbContext db,
                                                  AttendanceRouteService svc, AttendanceRouteWriteRequest req,
                                                  CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http,
                $"/api/maker/v1/attendance/approval-routes/{created.Id}", created);
        });

        // #17 経路の部分更新 (勤怠参照権限 AND オーナー)。Steps 指定時はステップを全置換。
        group.MapPatch("/approval-routes/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                                            AttendanceRouteService svc, Guid id,
                                                            AttendanceRoutePatchRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        // #18 経路の論理削除 (勤怠参照権限 AND オーナー)
        group.MapDelete("/approval-routes/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                                             AttendanceRouteService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // #19 経路の論理削除の取消 (勤怠参照権限 AND オーナー)
        group.MapPost("/approval-routes/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                                   AttendanceRouteService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.RestoreAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        return app;
    }

    /// <summary>
    /// 参照対象ユーザの解決 (移植仕様 §2「他人の勤怠を参照するときのガード」)。
    ///   - userId 省略時は自分。
    ///   - 自分以外を指定した場合はオーナー権限を要求し、不足なら 403 AKB-AUTH-010。
    /// エラー時は ErrorResult (第 2 要素) を返し、呼び出し側はそのまま return する。
    /// </summary>
    private static async Task<(Guid TargetId, IResult? ErrorResult)> ResolveTargetAsync(
        HttpContext http, IAkebonoDbContext db, Guid? userId, CancellationToken ct)
    {
        var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
        if (auth.ErrorResult is not null) return (Guid.Empty, auth.ErrorResult);

        var actorId = auth.ActorId!.Value;
        var targetId = userId ?? actorId;
        if (targetId == actorId) return (targetId, null);

        var admin = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
        if (admin.ErrorResult is not null)
            return (Guid.Empty, ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "他の利用者の勤怠を参照する権限がありません",
                userAction: "自分の勤怠のみ参照できます。全員分が必要な場合は管理者へ連絡してください"));

        return (targetId, null);
    }

    /// <summary>真値クエリの解釈 ("1" / "true" / "yes" を真とする。フロントは raw=1 を送る)。</summary>
    private static bool IsTruthy(string? value)
        => value is not null
           && (value == "1"
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
}
