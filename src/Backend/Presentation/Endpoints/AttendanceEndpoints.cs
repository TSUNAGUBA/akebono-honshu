using Akebono.Application.Attendance;
using Akebono.Application.Common;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 勤怠 REST endpoint (移植仕様 §5.1〜§5.3、#1〜#14)。
///
/// 権限 (移植仕様 §2):
///   - 打刻・各種申請 (書込)         : attendance_permission == 1 (AuthEndpoints.CheckAttendanceWriteAsync)
///   - 自分の勤怠参照               : attendance_permission 1 or 2 (CheckAttendanceReadAsync)
///   - 他人の勤怠参照 / 承認 / 設定  : オーナー process_record_permission >= 1 (CheckAttendanceAdminAsync)
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

        // #6 全員のタイムカード (オーナーのみ)。期間上限は AttendanceService.TimecardRangeMaxDays。
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

        // #8 修正申請の一覧。scope=all (全件) はオーナーのみ。
        group.MapGet("/fix-requests", async (HttpContext http, IAkebonoDbContext db, AttendanceService svc,
                                              string? status, string? scope, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var all = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            if (all)
            {
                var admin = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
                if (admin.ErrorResult is not null) return admin.ErrorResult;
            }

            var rows = await svc.ListFixRequestsAsync(auth.ActorId!.Value, status, all, ct);
            return ApiEnvelope.Ok(http, rows);
        });

        // #9 修正申請の承認 / 却下 (オーナーのみ)。承認は fix レコード追記 (元打刻は削除しない)。
        group.MapPost("/fix-requests/{id:guid}/decision", async (HttpContext http, IAkebonoDbContext db,
                                                                  AttendanceService svc, Guid id,
                                                                  FixDecisionRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await svc.DecideFixRequestAsync(auth.ActorId!.Value, id, req, ct);
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

        // #11 新規作成 (オーナー)
        group.MapPost("/rules", async (HttpContext http, IAkebonoDbContext db, AttendanceRuleService svc,
                                        AttendanceRuleWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"/api/maker/v1/attendance/rules/{created.Id}", created);
        });

        // #12 部分更新 (オーナー)。null のフィールドは更新しない。
        group.MapPatch("/rules/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                                   AttendanceRuleService svc, Guid id,
                                                   AttendanceRulePatchRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        // #13 論理削除 (オーナー)
        group.MapDelete("/rules/{id:guid}", async (HttpContext http, IAkebonoDbContext db,
                                                    AttendanceRuleService svc, Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // #14 論理削除の取消 (オーナー)
        group.MapPost("/rules/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db,
                                                          AttendanceRuleService svc, Guid id, CancellationToken ct) =>
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
