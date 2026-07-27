using Akebono.Application.Attendance;
using Akebono.Application.Auth;
using Akebono.Application.Common;
using Akebono.Application.Masters;
using Akebono.Application.Migration;
using Akebono.Application.Orders;
using Akebono.Application.Production;
using Akebono.Application.Products;
using Akebono.Application.Users;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Akebono.Infrastructure.Excel;
using Akebono.Infrastructure.Audit;
using Akebono.Infrastructure.Persistence;
using Akebono.Infrastructure.Storage;
using Akebono.Infrastructure.Tenancy;
using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akebono.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAkebonoInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未設定");

        // テナントコンテキスト (リクエストスコープ)。TenantResolutionMiddleware が
        // 認証クレームから確定し、DbContext のクエリフィルタ / RLS GUC / TenantId スタンプに伝搬する。
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantSessionInterceptor>();

        services.AddDbContext<AkebonoDbContext>((sp, opt) => opt
            .UseNpgsql(connection)
            // 接続オープンごとに SET set_config('app.tenant_id', ...) を発行し RLS へ伝搬
            .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));
        services.AddScoped<IAkebonoDbContext>(sp => sp.GetRequiredService<AkebonoDbContext>());

        services.AddScoped<IAuditLogger, AuditLogger>();
        // audit_logs 月次パーティションの先行作成 (起動時 + 24h ごと、失敗は warning のみで継続。
        // DEFAULT パーティションが安全網 — AuditPartitionMaintenanceService 参照)。
        services.AddHostedService<AuditPartitionMaintenanceService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserQueryService>();

        // 17 マスタ共通テンプレート (拡張カラムなし or 単純なマスタ用)
        // 拡張カラムは Endpoint 側で Entity に直接 set/get する設計 (1.D で実装)
        services.AddScoped<MasterService<Brand>>();
        services.AddScoped<MasterService<Function>>();
        services.AddScoped<MasterService<Country>>();
        services.AddScoped<MasterService<Currency>>();
        services.AddScoped<MasterService<Department>>();
        services.AddScoped<MasterService<MaterialClassification>>();
        services.AddScoped<MasterService<Warehouse>>();
        services.AddScoped<MasterService<Size>>();
        services.AddScoped<MasterService<ProductType>>();
        services.AddScoped<MasterService<ProductSeason>>();
        services.AddScoped<MasterService<ProductGroup>>();
        // 税率マスタ (Part5)。
        services.AddScoped<MasterService<TaxRate>>();
        services.AddScoped<MasterService<Color>>();
        services.AddScoped<MasterService<Material>>();
        services.AddScoped<MasterService<DeliveryDestination>>();
        services.AddScoped<MasterService<DocumentTemplatePurchase>>();
        services.AddScoped<MasterService<DocumentTemplateConfirmation>>();
        services.AddScoped<MasterService<DocumentTextPurchase>>();

        // M-04 仕入先 (F-22 official_name 帳票準備のため個別 Service)
        services.AddScoped<SupplierService>();
        // 工場マスタ (Part2)。仕入先 (SupplierService) から分離した工場専用サービス。
        services.AddScoped<FactoryService>();
        // 為替マスタ (§2f、bespoke master)
        services.AddScoped<ExchangeRateService>();

        // 商品関連 (Iteration 2、Phase 5 §4)
        services.AddScoped<ProductFamilyService>();
        services.AddScoped<ProductSupplierPriceService>();

        // 発注関連 (Iteration 3、Phase 5 §5)
        services.AddScoped<PurchaseOrderService>();
        services.AddScoped<IPurchaseOrderExcelService, PurchaseOrderExcelService>();
        // 一括ダウンロード (#3b)。管理表 (読み取り専用) + 発注書 ZIP 束ね。
        services.AddScoped<IOrderManagementTableExcelService, OrderManagementTableExcelService>();
        services.AddScoped<IOrderBulkExportService, OrderBulkExportService>();

        // 生産管理拡張 (Iteration 5、data-design-production §4)
        services.AddScoped<ProductMaterialService>();
        services.AddScoped<ProductionInstructionService>();
        services.AddScoped<MaterialOrderService>();
        services.AddScoped<ProductionStatusQuery>();
        services.AddScoped<IProductionInstructionExcelService, ProductionInstructionExcelService>();
        services.AddScoped<IMaterialOrderExcelService, MaterialOrderExcelService>();

        // 勤怠・休暇 (Iteration 30)
        // AttendanceService は打刻の直列化で BeginTransaction / ExecuteSqlRawAsync を使うため
        // IAkebonoDbContext.Database ファサード経由でトランザクションを制御する。
        services.AddScoped<AttendanceService>();
        services.AddScoped<AttendanceRuleService>();
        services.AddScoped<LeaveService>();

        // MIG-3 既存 CSV 取込 (Iteration 4 Hardening)
        services.AddScoped<LegacyImportService>();

        // 画像ストレージ抽象 (Iter 4 段階 C)
        // ImageStorage:Provider = "S3" → 本番 S3 + Pre-signed URL、それ以外は dev 用 wwwroot 直保存。
        // LocalImageStorage は IHttpContextAccessor を使って absolute URL を組み立てるため
        // AddHttpContextAccessor() も必要 (default では未登録)。
        services.AddHttpContextAccessor();
        var imageProvider = config["ImageStorage:Provider"] ?? "Local";
        if (string.Equals(imageProvider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDefaultAWSOptions(config.GetAWSOptions());
            services.AddAWSService<IAmazonS3>();
            services.AddSingleton<IImageStorageService, S3ImageStorage>();
        }
        else
        {
            services.AddSingleton<IImageStorageService, LocalImageStorage>();
        }

        return services;
    }
}
