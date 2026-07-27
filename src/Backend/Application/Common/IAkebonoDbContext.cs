using Akebono.Domain.Attendance;
using Akebono.Domain.Entities;
using Akebono.Domain.Orders;
using Akebono.Domain.Production;
using Akebono.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Akebono.Application.Common;

public interface IAkebonoDbContext
{
    /// <summary>テナントレジストリ投影 (読取専用。SoT = akebono-backoffice)。</summary>
    DbSet<Tenant> Tenants { get; }

    DbSet<User> Users { get; }
    DbSet<AuditLog> AuditLogs { get; }

    // 17 マスタ (Phase 5 §3.1-3.17)
    DbSet<Size> Sizes { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Function> Functions { get; }
    DbSet<Country> Countries { get; }
    DbSet<Supplier> Suppliers { get; }
    // 工場マスタ (Part2)。仕入先から分離。
    DbSet<Factory> Factories { get; }
    // 税率マスタ (Part5)。
    DbSet<TaxRate> TaxRates { get; }
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
    // 通貨マスタ (標準マスタ) + 為替マスタ (bespoke master)
    DbSet<Currency> Currencies { get; }
    DbSet<ExchangeRate> ExchangeRates { get; }

    // 商品関連 (Iteration 2、Phase 5 §4)
    DbSet<ProductFamily> ProductFamilies { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductSupplierPrice> ProductSupplierPrices { get; }
    // アソート/セット明細 (PR3、旧 spec No.37/38)
    DbSet<ProductSetComponent> ProductSetComponents { get; }

    // 発注関連 (Iteration 3、Phase 5 §5)
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    // 分納×倉庫の多次元明細 (PR5b、Phase 5 §5.2b)
    DbSet<PurchaseOrderLineDelivery> PurchaseOrderLineDeliveries { get; }
    DbSet<PurchaseOrderExportLog> PurchaseOrderExportLogs { get; }

    // 生産管理拡張 (Iteration 5、data-design-production §4)
    DbSet<ProductMaterial> ProductMaterials { get; }
    DbSet<ProductionInstruction> ProductionInstructions { get; }
    DbSet<ProductionInstructionLine> ProductionInstructionLines { get; }
    DbSet<MaterialOrder> MaterialOrders { get; }
    DbSet<MaterialOrderLine> MaterialOrderLines { get; }

    // 勤怠・休暇 (Iteration 30、db/init/10-attendance.sql)
    DbSet<AttendanceRule> AttendanceRules { get; }
    // 記録系・追記のみ (UPDATE/DELETE しない。updated_at 列を持たない)
    DbSet<PunchRecord> PunchRecords { get; }
    DbSet<AttendanceFixRequest> AttendanceFixRequests { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<LeaveGrant> LeaveGrants { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }

    /// <summary>ジェネリック DbSet 取得 (MasterService&lt;TEntity&gt; 用)。DbContext.Set&lt;T&gt;() の暗黙実装。</summary>
    DbSet<T> Set<T>() where T : class;

    /// <summary>EF Core Database ファサード (トランザクション制御等で使用)。</summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
