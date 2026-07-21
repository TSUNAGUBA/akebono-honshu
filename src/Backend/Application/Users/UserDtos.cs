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
    bool HasFirebaseLink = false);

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
    string? FirebaseUid = null);
