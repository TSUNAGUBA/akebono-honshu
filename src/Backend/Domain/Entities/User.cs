namespace Akebono.Domain.Entities;

public class User
{
    public long Id { get; set; }
    public string? FirebaseUid { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string LoginId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsPlanningStaff { get; set; }
    public bool IsSalesStaff { get; set; }
    public short ProductLedgerPermission { get; set; }
    public short PurchaseOrderCreatePermission { get; set; }
    public short PurchaseOrderInfoPermission { get; set; }
    public short ProcessRecordPermission { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? UpdatedByUserId { get; set; }
    public string? LegacyId { get; set; }
}
