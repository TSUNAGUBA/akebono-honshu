namespace Akebono.Domain.Entities;

public class User
{
    public long Id { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string LoginId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
