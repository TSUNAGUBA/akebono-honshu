using Akebono.Application.Common;
using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Infrastructure.Persistence;

public class AkebonoDbContext(DbContextOptions<AkebonoDbContext> options)
    : DbContext(options), IAkebonoDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.EmployeeNo).HasColumnName("employee_no").IsRequired().HasMaxLength(16);
            b.Property(x => x.LoginId).HasColumnName("login_id").IsRequired().HasMaxLength(64);
            b.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(255);
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => x.EmployeeNo).IsUnique();
            b.HasIndex(x => x.LoginId).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            b.Property(x => x.Action).HasColumnName("action").IsRequired().HasMaxLength(64);
            b.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64);
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.Result).HasColumnName("result");
            b.Property(x => x.Note).HasColumnName("note").HasMaxLength(512);
        });
    }
}
