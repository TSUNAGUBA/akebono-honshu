using Akebono.Application.Auth;
using Akebono.Application.Common;
using Akebono.Application.Masters;
using Akebono.Application.Orders;
using Akebono.Application.Products;
using Akebono.Application.Users;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Akebono.Infrastructure.Excel;
using Akebono.Infrastructure.Audit;
using Akebono.Infrastructure.Auth;
using Akebono.Infrastructure.Persistence;
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

        services.AddDbContext<AkebonoDbContext>(opt => opt.UseNpgsql(connection));
        services.AddScoped<IAkebonoDbContext>(sp => sp.GetRequiredService<AkebonoDbContext>());

        services.AddSingleton<ITokenService, DummyTokenService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserQueryService>();

        // 17 マスタ共通テンプレート (拡張カラムなし or 単純なマスタ用)
        // 拡張カラムは Endpoint 側で Entity に直接 set/get する設計 (1.D で実装)
        services.AddScoped<MasterService<Brand>>();
        services.AddScoped<MasterService<Function>>();
        services.AddScoped<MasterService<Country>>();
        services.AddScoped<MasterService<Department>>();
        services.AddScoped<MasterService<MaterialClassification>>();
        services.AddScoped<MasterService<Warehouse>>();
        services.AddScoped<MasterService<Size>>();
        services.AddScoped<MasterService<ProductType>>();
        services.AddScoped<MasterService<ProductSeason>>();
        services.AddScoped<MasterService<ProductGroup>>();
        services.AddScoped<MasterService<Color>>();
        services.AddScoped<MasterService<Material>>();
        services.AddScoped<MasterService<DeliveryDestination>>();
        services.AddScoped<MasterService<DocumentTemplatePurchase>>();
        services.AddScoped<MasterService<DocumentTemplateConfirmation>>();
        services.AddScoped<MasterService<DocumentTextPurchase>>();

        // M-04 仕入先 (F-22 official_name 帳票準備のため個別 Service)
        services.AddScoped<SupplierService>();

        // 商品関連 (Iteration 2、Phase 5 §4)
        services.AddScoped<ProductFamilyService>();
        services.AddScoped<ProductSupplierPriceService>();

        // 発注関連 (Iteration 3、Phase 5 §5)
        services.AddScoped<PurchaseOrderService>();
        services.AddScoped<IPurchaseOrderExcelService, PurchaseOrderExcelService>();

        return services;
    }
}
