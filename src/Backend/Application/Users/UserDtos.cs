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
    short AttendancePermission = 1,
    bool PunchRequired = true,
    Guid? AttendanceRuleId = null,
    DateOnly? HireDate = null,
    decimal WeeklyDays = 5m,
    decimal WeeklyHours = 40m);

// 利用者マスタ (Part5) の作成/更新ペイロード。権限 (閲覧/操作) を含む。
// FirebaseUid は任意 (ログイン連携のプロビジョニング用。未指定なら既存値を保持/未連携)。
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
