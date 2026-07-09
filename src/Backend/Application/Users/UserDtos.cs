namespace Akebono.Application.Users;

public record UserListItem(Guid Id, string EmployeeNo, string LoginId, string DisplayName, bool IsActive);
