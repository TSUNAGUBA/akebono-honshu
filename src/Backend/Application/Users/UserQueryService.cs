using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Users;

public class UserQueryService(IAkebonoDbContext db, IAuditLogger audit)
{
    public async Task<List<UserListItem>> ListAsync(long actorUserId, CancellationToken ct = default)
    {
        var users = await db.Users
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.EmployeeNo)
            .Select(u => new UserListItem(u.Id, u.EmployeeNo, u.LoginId, u.DisplayName, u.IsActive))
            .ToListAsync(ct);

        await audit.LogAsync(actorUserId, "User.List", entityType: "User", note: $"Returned {users.Count} users", cancellationToken: ct);

        return users;
    }
}
