using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

public class User : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? FirebaseUid { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string LoginId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>
    /// 役職 (Iteration 33)。勤怠承認経路の承認者指定 (approver_type=title) が参照する。
    /// NULL 許容の末尾追加で下位互換 (CLAUDE.md 原則7)。既存ユーザは NULL (役職未設定)。
    /// </summary>
    public string? Title { get; set; }

    public bool IsPlanningStaff { get; set; }
    public bool IsSalesStaff { get; set; }
    public short ProductLedgerPermission { get; set; }
    public short PurchaseOrderCreatePermission { get; set; }
    public short PurchaseOrderInfoPermission { get; set; }
    public short ProcessRecordPermission { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }

    // ─────────────────────────────────────────────
    // 勤怠 (Iteration 30)。すべて末尾追加 + DB 側 DEFAULT 付きで下位互換 (CLAUDE.md 原則7)。
    // ─────────────────────────────────────────────

    /// <summary>
    /// 勤怠権限 (5 つ目の権限カテゴリ)。0=なし / 1=更新可能 / 2=参照のみ。
    /// 既存 4 権限と同じ**非単調スケール**のため、書込判定は必ず <c>== 1</c> で行う (<c>&gt;= 1</c> はバグ)。
    /// 管理系 (全員のタイムカード・承認・設定・付与) はオーナー権限
    /// (<see cref="ProcessRecordPermission"/> &gt;= 1) に集約する。
    /// 既定 1 の根拠: 勤怠は全従業員が使う機能で、移行直後から打刻できる必要があるため。
    /// </summary>
    public short AttendancePermission { get; set; } = 1;

    /// <summary>打刻対象か。役員・外注等は false。実際に打刻できるかは 権限==1 かつ本値 true。</summary>
    public bool PunchRequired { get; set; } = true;

    /// <summary>個別に割り当てた勤務体系 (attendance_rules.id)。NULL = 既定ルールを使う。</summary>
    public Guid? AttendanceRuleId { get; set; }

    /// <summary>入社日 (JST)。有給の周期自動付与の起算日。NULL = 周期自動付与の対象外。</summary>
    public DateOnly? HireDate { get; set; }

    /// <summary>週所定日数 (比例付与の判定に使う)。0〜7。</summary>
    public decimal WeeklyDays { get; set; } = 5m;

    /// <summary>週所定時間 (比例付与の判定に使う)。0〜168。</summary>
    public decimal WeeklyHours { get; set; } = 40m;
}
