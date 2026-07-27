using Akebono.Domain.Attendance;

namespace Akebono.Application.Attendance;

// ═══════════════════════════════════════════════════
// 休暇種別 (移植仕様 §5.4 #15〜#19)
// ═══════════════════════════════════════════════════

/// <summary>
/// 休暇種別 (§5.4 LeaveTypeDto)。
/// DeletedAt は末尾追加 (下位互換)。includeInactive=true の一覧で復元対象を判別するために返す。
/// </summary>
public record LeaveTypeDto(
    Guid Id,
    string Name,
    LeaveGrantMethod GrantMethod,
    int? ExpiryMonths,
    bool IsStatutory,
    string Description,
    int DisplayOrder,
    bool IsActive,
    DateTime? DeletedAt = null);

/// <summary>
/// 休暇種別の作成ペイロード (#16)。
/// IsStatutory は**受け取らない**。法定有給 (労基法 39 条) は初期シードの 1 件のみで、
/// API 経由の作成・昇格を禁止する (改竄防止、§5.4)。
/// </summary>
public record LeaveTypeWriteRequest(
    string Name,
    LeaveGrantMethod GrantMethod,
    int? ExpiryMonths,
    string? Description,
    int DisplayOrder = 1,
    bool IsActive = true);

/// <summary>
/// 休暇種別の部分更新ペイロード (#17)。
/// **null = 未指定 (既存値を保持)**。送っていない項目を既定値で潰さないため、
/// <see cref="LeaveTypeWriteRequest"/> とは別 record にして全項目を nullable にしている
/// (CLAUDE.md の Zod <c>.partial()</c> による既定値上書き障害と同じ轍を踏まない)。
/// IsStatutory は受け取らない (改竄防止)。
///
/// ExpiryMonths は「null = 未指定」と「null = 無期限に戻す」を区別できないため、
/// 無期限へ戻す場合のみ <see cref="ClearExpiryMonths"/> を true にする (ClearExpiryMonths が優先)。
/// </summary>
public record LeaveTypePatchRequest(
    string? Name = null,
    LeaveGrantMethod? GrantMethod = null,
    int? ExpiryMonths = null,
    string? Description = null,
    int? DisplayOrder = null,
    bool? IsActive = null,
    bool ClearExpiryMonths = false);

// ═══════════════════════════════════════════════════
// 残数・義務サマリ (§5.4 #20 / #27)
// ═══════════════════════════════════════════════════

/// <summary>直近の失効予定 (有給)。失効予定が無い場合は親側が null を返す。</summary>
public record LeaveNextExpireDto(DateOnly Date, decimal Days);

/// <summary>種別ごとの付与/取得/残数。</summary>
public record LeaveByTypeDto(
    Guid LeaveTypeId,
    string LeaveTypeName,
    bool IsStatutory,
    decimal Granted,
    decimal Taken,
    decimal Remaining,
    DateOnly? NextExpireDate);

/// <summary>
/// 年 5 日取得義務トラッカー (労基法 39 条 7 項)。
/// Applicable=false のとき GrantDate / Deadline は null (10 日以上の付与が無い)。
/// </summary>
public record LeaveObligationDto(
    bool Applicable,
    DateOnly? GrantDate,
    DateOnly? Deadline,
    decimal RequiredDays,
    decimal TakenDays,
    int DaysLeft,
    bool Achieved,
    bool Warn);

/// <summary>
/// 付与・取得の履歴行。
/// Kind = "grant" | "take"。Status は取得行のみ "pending"/"approved"/"rejected"、付与行は ""。
/// Days は符号や「—」を含むため**表示専用の文字列**で返す (§5.4)。
/// </summary>
public record LeaveHistoryDto(
    DateOnly Date,
    string Kind,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string Detail,
    string Status,
    string Days);

/// <summary>休暇サマリ (§5.4 LeaveSummaryDto)。Paid* は法定有給 (IsStatutory) の値。</summary>
public record LeaveSummaryDto(
    decimal PaidRemaining,
    decimal PaidUsedThisFiscalYear,
    LeaveNextExpireDto? NextExpire,
    IReadOnlyList<LeaveByTypeDto> ByType,
    LeaveObligationDto Obligation,
    IReadOnlyList<LeaveHistoryDto> History);

/// <summary>休暇管理 (#27) のメンバー×種別 1 行。実績 (付与/取得) が無い組合せは行を出さない。</summary>
public record LeaveAdminRowDto(
    Guid UserId,
    string UserName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    decimal Granted,
    decimal Taken,
    decimal Remaining,
    DateOnly? LastGrantDate);

// ═══════════════════════════════════════════════════
// 休暇申請 (§5.4 #21〜#23)
// ═══════════════════════════════════════════════════

/// <summary>休暇申請 1 件 (§5.4 LeaveRequestDto)。</summary>
public record LeaveRequestDto(
    Guid Id,
    Guid UserId,
    string UserName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    DateOnly Date,
    LeaveUnit Unit,
    LeaveRequestStatus Status,
    string Reason,
    Guid? DecidedByUserId,
    string? DecidedByUserName,
    DateTime CreatedAt);

/// <summary>休暇申請の作成ペイロード (#22)。対象は常に本人 (UserId は受け取らない)。</summary>
public record LeaveRequestWriteRequest(
    Guid LeaveTypeId,
    DateOnly Date,
    LeaveUnit Unit = LeaveUnit.Full,
    string? Reason = null);

/// <summary>承認/却下ペイロード (#23)。Action = "approved" | "rejected"。</summary>
public record LeaveDecisionRequest(string Action);

// ═══════════════════════════════════════════════════
// 付与 (§5.4 #24〜#26)
// ═══════════════════════════════════════════════════

/// <summary>個別付与ペイロード (#24)。</summary>
public record LeaveGrantWriteRequest(
    Guid UserId,
    Guid LeaveTypeId,
    DateOnly GrantDate,
    decimal Days);

/// <summary>
/// 一括付与ペイロード (#25)。Target は "all" (在籍中の全ユーザ) のみ。
/// office の雇用区分/部署指定は honshu に該当属性が無いため移植対象外 (§5.4)。
/// GrantDate 省略時は JST の当日。
/// </summary>
public record LeaveBulkGrantRequest(
    Guid LeaveTypeId,
    decimal Days,
    string Target,
    DateOnly? GrantDate = null);

/// <summary>ID のみを返す応答 (申請作成・承認/却下)。</summary>
public record LeaveIdDto(Guid Id);

/// <summary>
/// 個別付与の結果 (#24)。同一 (user, 種別, 付与日) が既にある場合は
/// **既存レコードの Id** と Skipped=1 を返す (冪等・原則 2。既存付与は更新も削除もしない)。
/// </summary>
public record LeaveGrantResultDto(Guid Id, int Skipped);

/// <summary>一括付与・周期自動付与の結果 (#25 / #26)。Skipped は既存のため挿入しなかった件数。</summary>
public record LeaveGrantBulkResultDto(int Granted, int Skipped);
