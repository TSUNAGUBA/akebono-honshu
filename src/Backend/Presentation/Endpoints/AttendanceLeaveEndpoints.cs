using Akebono.Application.Attendance;
using Akebono.Application.Common;

namespace Akebono.Api.Endpoints;

/// <summary>
/// 休暇 (有給・特別休暇) REST endpoint (移植仕様 §5.4 #15〜#27)。
///
/// 認可 (§2):
///   - 参照系 (種別一覧・サマリ・自分の申請): <see cref="AuthEndpoints.CheckAttendanceReadAsync"/>
///     (attendance_permission が 1 または 2)
///   - 申請 (本人): <see cref="AuthEndpoints.CheckAttendanceWriteAsync"/> (attendance_permission == 1)
///   - 管理系 (種別設定・承認/却下・付与・休暇管理・scope=all):
///     <see cref="AuthEndpoints.CheckAttendanceAdminAsync"/> (オーナー = process_record_permission >= 1)
///   - 他人のサマリ参照はオーナーを要求する (<see cref="EnsureCanReadOtherAsync"/>)。
///
/// 業務エラー (検証 422 / 状態違反・重複 409 / 未検出 404) は service 層の DomainException を
/// ApiExceptionMiddleware がエラー封筒へ変換する。新規エラーコードは採番しない。
/// </summary>
public static class AttendanceLeaveEndpoints
{
    private const string BasePath = "/api/maker/v1/attendance/leave";

    public static IEndpointRouteBuilder MapAttendanceLeaveEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath);

        MapLeaveTypes(group);
        MapLeaveSummary(group);
        MapLeaveRequests(group);
        MapLeaveGrants(group);

        return app;
    }

    // ── 休暇種別 (#15〜#19) ───────────────────────────
    private static void MapLeaveTypes(RouteGroupBuilder group)
    {
        // #15 一覧。includeInactive=true で無効・論理削除済も返す (復元 UI 用)。
        group.MapGet("/types", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                       bool? includeInactive, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return ApiEnvelope.Ok(http, await svc.ListTypesAsync(includeInactive ?? false, ct));
        });

        // #16 作成 (オーナー)。IsStatutory は受け取らないため法定有給は作成できない。
        group.MapPost("/types", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                        LeaveTypeWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var created = await svc.CreateTypeAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"{BasePath}/types/{created.Id}", created);
        });

        // #17 部分更新 (オーナー)。送られていないフィールドは既存値を保持する。法定有給は 409。
        group.MapPatch("/types/{id:guid}", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                                   Guid id, LeaveTypePatchRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var updated = await svc.UpdateTypeAsync(id, req, auth.ActorId!.Value, ct);
            return updated is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, updated);
        });

        // #18 論理削除 (オーナー)。付与・申請の実績は残す。法定有給は 409。
        group.MapDelete("/types/{id:guid}", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                                    Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.SoftDeleteTypeAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });

        // #19 復元 (オーナー)。削除の取消導線 (誤操作で詰まないための戻り道)。
        group.MapPost("/types/{id:guid}/restore", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                                          Guid id, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var ok = await svc.RestoreTypeAsync(id, auth.ActorId!.Value, ct);
            return ok ? Results.NoContent() : AuthEndpoints.NotFoundError(http);
        });
    }

    // ── サマリ (#20 / #27) ────────────────────────────
    private static void MapLeaveSummary(RouteGroupBuilder group)
    {
        // #20 残数・年5日義務・履歴。userId 省略時は自分。他人の指定はオーナーのみ。
        group.MapGet("/summary", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                         Guid? userId, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var actorId = auth.ActorId!.Value;
            var targetUserId = userId ?? actorId;
            var denied = await EnsureCanReadOtherAsync(http, db, actorId, targetUserId, ct);
            if (denied is not null) return denied;

            return ApiEnvelope.Ok(http, await svc.GetSummaryAsync(targetUserId, ct));
        });

        // #27 休暇管理一覧 (オーナー)。メンバー×種別の付与/取得/残。
        group.MapGet("/admin/summary", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                               CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return ApiEnvelope.Ok(http, await svc.AdminSummaryAsync(ct));
        });
    }

    // ── 休暇申請 (#21〜#23) ───────────────────────────
    private static void MapLeaveRequests(RouteGroupBuilder group)
    {
        // #21 一覧。scope=all は全員分 (オーナーのみ)、省略時は自分の申請。
        // キーセットページング (AKB-DOC-12 §7.1): ?limit=&cursor=<opaque>、不正は 400 AKB-SYS-011。
        // data は従来どおり配列のままで、続きの有無は meta.page.hasMore が示す (フロント契約は非破壊)。
        // limit 未指定時の既定は上限値 (PageRequest.MaxLimit)。フロント (useAttendance.leaveRequests) は
        // まだ limit / cursor を送らないため、既定 50 だと申請が黙って欠落する。
        // ?from / ?to は取得日 (業務日付) の範囲絞り込み (両端含み・YYYY-MM-DD)。**両方省略可**で、
        // 省略時は従来どおり全期間 (下位互換・原則7)。月次カレンダーの休暇マーカーのように
        // 表示中の月だけが要る画面は、ページ上限で黙って切り詰められないよう範囲を指定する。
        group.MapGet("/requests", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                          string? scope, string? status, string? from, string? to,
                                          int? limit, string? cursor,
                                          CancellationToken ct) =>
        {
            var allScope = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            var auth = allScope
                ? await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct)
                : await AuthEndpoints.CheckAttendanceReadAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var page = PageCursor.Read(limit ?? PageRequest.MaxLimit, cursor);
            var result = await svc.ListRequestsAsync(auth.ActorId!.Value, allScope, status, from, to, page, ct);
            return ApiEnvelope.OkPaged(http, result, page.Limit);
        });

        // #22 申請 (勤怠==1)。対象は常に本人 (body で他人を指定させない)。
        group.MapPost("/requests", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                           LeaveRequestWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceWriteAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;

            var actorId = auth.ActorId!.Value;
            var created = await svc.CreateRequestAsync(actorId, req, actorId, ct);
            return ApiEnvelope.Created(http, $"{BasePath}/requests/{created.Id}", created);
        });

        // #23 承認/却下 (オーナー)。処理済みの再操作は 409 (トランザクション内で再確認)。
        group.MapPost("/requests/{id:guid}/decision",
            async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                   Guid id, LeaveDecisionRequest req, CancellationToken ct) =>
            {
                var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
                if (auth.ErrorResult is not null) return auth.ErrorResult;
                var decided = await svc.DecideRequestAsync(id, req, auth.ActorId!.Value, ct);
                return decided is null ? AuthEndpoints.NotFoundError(http) : ApiEnvelope.Ok(http, decided);
            });
    }

    // ── 付与 (#24〜#26) ───────────────────────────────
    private static void MapLeaveGrants(RouteGroupBuilder group)
    {
        // #24 個別付与 (オーナー)。同一 (user, 種別, 付与日) が既にあれば skipped=1 で既存 ID を返す。
        group.MapPost("/grants", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                         LeaveGrantWriteRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            var result = await svc.GrantAsync(req, auth.ActorId!.Value, ct);
            return ApiEnvelope.Created(http, $"{BasePath}/grants/{result.Id}", result);
        });

        // #25 一括付与 (オーナー)。target=all のみ。既存分は skipped。
        group.MapPost("/grants/bulk", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                              LeaveBulkGrantRequest req, CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return ApiEnvelope.Ok(http, await svc.BulkGrantAsync(req, auth.ActorId!.Value, ct));
        });

        // #26 周期自動付与の実行 (オーナー)。何度実行しても既存の付与は変更されない (原則 2)。
        group.MapPost("/periodic-grants/run", async (HttpContext http, IAkebonoDbContext db, LeaveService svc,
                                                      CancellationToken ct) =>
        {
            var auth = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
            if (auth.ErrorResult is not null) return auth.ErrorResult;
            return ApiEnvelope.Ok(http, await svc.RunPeriodicGrantsAsync(auth.ActorId!.Value, ct));
        });
    }

    /// <summary>
    /// 他人の勤怠を参照する場合のガード (§2)。自分自身なら null (参照可)、
    /// オーナーでなければ 403 AKB-AUTH-010 のエラー封筒を返す。
    /// </summary>
    private static async Task<IResult?> EnsureCanReadOtherAsync(
        HttpContext http, IAkebonoDbContext db, Guid actorId, Guid targetUserId, CancellationToken ct)
    {
        if (actorId == targetUserId) return null;

        var admin = await AuthEndpoints.CheckAttendanceAdminAsync(http, db, ct);
        return admin.ErrorResult is null
            ? null
            : ApiEnvelope.Error(http, 403, AkbErrorCodes.AuthInsufficientPermission,
                "他の利用者の勤怠を参照する権限がありません",
                "自分の勤怠のみ参照できます");
    }
}
