namespace Akebono.Application.Users;

// 一覧/選択候補用 (既存の /users 消費者は id/loginId/displayName のみ参照。末尾追加で下位互換)。
public record UserListItem(
    Guid Id,
    string EmployeeNo,
    string LoginId,
    string DisplayName,
    bool IsActive,
    // 利用者マスタ (Part5) 用に権限・区分・メールを追加 (末尾 = 下位互換)。
    string? Email = null,
    bool IsPlanningStaff = false,
    bool IsSalesStaff = false,
    short ProductLedgerPermission = 0,
    short PurchaseOrderCreatePermission = 0,
    short PurchaseOrderInfoPermission = 0,
    short ProcessRecordPermission = 0,
    bool HasFirebaseLink = false,
    // 勤怠 (Iteration 30)。末尾追加 + 既定値付きで下位互換 (CLAUDE.md 原則7)。
    // AttendancePermission: 0=なし / 1=更新可能 / 2=参照のみ (非単調スケール、書込判定は ==1)。
    //
    // **すべて nullable / 既定 null**: 入社日と週所定は有給の比例付与判定に直結する労務個人情報。
    // 一覧・単票は発注/商品フォームの担当者候補にも使うため認証のみで開いており、
    // オーナー (process_record_permission >= 1) 以外にはこの 6 列を返さず null にする
    // (UserQueryService.WithoutLaborInfo)。境界は **勤怠 API と意図的に揃えない** (オーナー単独で開く)。
    // 理由は UserQueryService.WithoutLaborInfo の docstring 参照 (レダクションすると利用者マスタの
    // 全項目送信フォームが他人の勤怠設定を巻き戻すため)。
    // 受け手にとって null は「取得権限が無い」であり「0 / 未設定」ではないため、
    // 表示側は fail-close (権限は 0、打刻対象は false) で解釈すること。
    short? AttendancePermission = null,
    bool? PunchRequired = null,
    Guid? AttendanceRuleId = null,
    DateOnly? HireDate = null,
    decimal? WeeklyDays = null,
    decimal? WeeklyHours = null);

// 利用者マスタ (Part5) の作成ペイロード。権限 (閲覧/操作) を含む。
// FirebaseUid は任意 (ログイン連携のプロビジョニング用。未指定なら未連携)。
// **更新 (PATCH) は本 record ではなく UserPatchRequest を使う** (未指定項目を既定値で潰さないため)。
public record UserWriteRequest(
    string EmployeeNo,
    string LoginId,
    string DisplayName,
    string? Email,
    bool IsPlanningStaff,
    bool IsSalesStaff,
    short ProductLedgerPermission,
    short PurchaseOrderCreatePermission,
    short PurchaseOrderInfoPermission,
    short ProcessRecordPermission,
    bool IsActive,
    string? FirebaseUid = null,
    // 勤怠 (Iteration 30)。末尾追加 + 既定値付きで下位互換 (既存クライアントが送らなくても壊れない)。
    // 既定値は DB 側の DEFAULT と一致させる (勤怠は全従業員が使う機能のため既定で更新可能・打刻対象)。
    short AttendancePermission = 1,
    bool PunchRequired = true,
    Guid? AttendanceRuleId = null,
    DateOnly? HireDate = null,
    decimal WeeklyDays = 5m,
    decimal WeeklyHours = 40m);

/// <summary>
/// 利用者マスタの**部分更新 (PATCH)** ペイロード。
/// **null = 未指定 (現在値を保持)**。<see cref="UserWriteRequest"/> をそのまま更新に使うと、
/// 送っていない項目が record の既定値で上書きされ、勤怠列 (入社日・週所定日数/時間・勤務体系) が
/// 黙って初期化される。実害は「表示名を直しただけで入社日が消え、有給の周期自動付与が止まり、
/// 付与日数が法的に誤る」ため、更新は必ず本 record を使うこと
/// (CLAUDE.md の Zod <c>.partial()</c> による既定値上書き障害と同じ轍を踏まない。
///  <c>LeaveTypePatchRequest</c> / <c>AttendanceRulePatchRequest</c> と同じ手法)。
///
/// NULL 許容列 (hire_date / attendance_rule_id) は「null = 未指定」と「null = クリア」を
/// 区別できないため、明示クリアは <see cref="ClearHireDate"/> /
/// <see cref="ClearAttendanceRule"/> で行う (クリアフラグが優先)。
/// Email は「null = 未指定 (保持)」「空文字 = クリア」。
/// FirebaseUid は空/null なら既存値を保持する (連携解除は本 I/F では行わない、非破壊)。
/// </summary>
public record UserPatchRequest(
    string? EmployeeNo = null,
    string? LoginId = null,
    string? DisplayName = null,
    string? Email = null,
    bool? IsPlanningStaff = null,
    bool? IsSalesStaff = null,
    short? ProductLedgerPermission = null,
    short? PurchaseOrderCreatePermission = null,
    short? PurchaseOrderInfoPermission = null,
    short? ProcessRecordPermission = null,
    bool? IsActive = null,
    string? FirebaseUid = null,
    // 勤怠 (Iteration 30)
    short? AttendancePermission = null,
    bool? PunchRequired = null,
    Guid? AttendanceRuleId = null,
    DateOnly? HireDate = null,
    decimal? WeeklyDays = null,
    decimal? WeeklyHours = null,
    // 明示クリア用フラグ (末尾追加)。true のとき値の指定より優先して null にする。
    bool ClearAttendanceRule = false,
    bool ClearHireDate = false);
