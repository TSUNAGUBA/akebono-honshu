using Akebono.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Common;

public interface IAkebonoDbContext
{
    DbSet<User> Users { get; }
    DbSet<AuditLog> AuditLogs { get; }

    // 17 マスタ (Phase 5 §3.1-3.17)
    DbSet<Size> Sizes { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Function> Functions { get; }
    DbSet<Country> Countries { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Department> Departments { get; }
    DbSet<ProductType> ProductTypes { get; }
    DbSet<ProductSeason> ProductSeasons { get; }
    DbSet<ProductGroup> ProductGroups { get; }
    DbSet<Color> Colors { get; }
    DbSet<Material> Materials { get; }
    DbSet<MaterialClassification> MaterialClassifications { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<DeliveryDestination> DeliveryDestinations { get; }
    DbSet<DocumentTemplatePurchase> DocumentTemplatePurchases { get; }
    DbSet<DocumentTemplateConfirmation> DocumentTemplateConfirmations { get; }
    DbSet<DocumentTextPurchase> DocumentTextPurchases { get; }

    /// <summary>ジェネリック DbSet 取得 (MasterService&lt;TEntity&gt; 用)。DbContext.Set&lt;T&gt;() の暗黙実装。</summary>
    DbSet<T> Set<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
