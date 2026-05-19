using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Common;

public interface IAkebonoDbContext
{
    DbSet<User> Users { get; }
    DbSet<AuditLog> AuditLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
