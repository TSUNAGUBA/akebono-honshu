using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Akebono.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akebono.Infrastructure.Persistence;

public class AkebonoDbContext(DbContextOptions<AkebonoDbContext> options)
    : DbContext(options), IAkebonoDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Size> Sizes => Set<Size>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Function> Functions => Set<Function>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<ProductSeason> ProductSeasons => Set<ProductSeason>();
    public DbSet<ProductGroup> ProductGroups => Set<ProductGroup>();
    public DbSet<Color> Colors => Set<Color>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialClassification> MaterialClassifications => Set<MaterialClassification>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<DeliveryDestination> DeliveryDestinations => Set<DeliveryDestination>();
    public DbSet<DocumentTemplatePurchase> DocumentTemplatePurchases => Set<DocumentTemplatePurchase>();
    public DbSet<DocumentTemplateConfirmation> DocumentTemplateConfirmations => Set<DocumentTemplateConfirmation>();
    public DbSet<DocumentTextPurchase> DocumentTextPurchases => Set<DocumentTextPurchase>();

    // 商品関連 (Iteration 2)
    public DbSet<ProductFamily> ProductFamilies => Set<ProductFamily>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductSupplierPrice> ProductSupplierPrices => Set<ProductSupplierPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // users (Phase 5 §3.18 全カラム反映)
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.FirebaseUid).HasColumnName("firebase_uid").HasMaxLength(128);
            b.Property(x => x.EmployeeNo).HasColumnName("employee_no").IsRequired().HasMaxLength(16);
            b.Property(x => x.LoginId).HasColumnName("login_id").IsRequired().HasMaxLength(64);
            b.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(255);
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
            b.Property(x => x.IsPlanningStaff).HasColumnName("is_planning_staff");
            b.Property(x => x.IsSalesStaff).HasColumnName("is_sales_staff");
            b.Property(x => x.ProductLedgerPermission).HasColumnName("product_ledger_permission");
            b.Property(x => x.PurchaseOrderCreatePermission).HasColumnName("purchase_order_create_permission");
            b.Property(x => x.PurchaseOrderInfoPermission).HasColumnName("purchase_order_info_permission");
            b.Property(x => x.ProcessRecordPermission).HasColumnName("process_record_permission");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);
            b.HasIndex(x => x.EmployeeNo).IsUnique();
            b.HasIndex(x => x.LoginId).IsUnique();
        });

        // audit_logs (Iteration 0 で定義済、変更なし)
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

        // 17 マスタ共通設定
        ConfigureMaster<Size>(modelBuilder, "sizes", b =>
        {
            b.Property(x => x.ItemConversionCode).HasColumnName("item_conversion_code").IsRequired().HasMaxLength(4);
        });
        ConfigureMaster<Brand>(modelBuilder, "brands");
        ConfigureMaster<Function>(modelBuilder, "functions");
        ConfigureMaster<Country>(modelBuilder, "countries");
        ConfigureMaster<Supplier>(modelBuilder, "suppliers", b =>
        {
            b.Property(x => x.OfficialName).HasColumnName("official_name").HasMaxLength(255);
            b.Property(x => x.ItemConversionCode).HasColumnName("item_conversion_code").IsRequired().HasMaxLength(1).IsFixedLength();
            b.Property(x => x.CountryId).HasColumnName("country_id");
            b.Property(x => x.SupplierType).HasColumnName("supplier_type");
            b.Property(x => x.AlertTarget).HasColumnName("alert_target");
            b.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);
        });
        ConfigureMaster<Department>(modelBuilder, "departments");
        ConfigureMaster<ProductType>(modelBuilder, "product_types", b =>
        {
            b.Property(x => x.ItemConversionCode).HasColumnName("item_conversion_code").IsRequired().HasMaxLength(1).IsFixedLength();
            b.Property(x => x.SizeDemographicCode).HasColumnName("size_demographic_code").IsRequired().HasMaxLength(1).IsFixedLength();
        });
        ConfigureMaster<ProductSeason>(modelBuilder, "product_seasons", b =>
        {
            b.Property(x => x.ItemConversionCode).HasColumnName("item_conversion_code").IsRequired().HasMaxLength(1).IsFixedLength();
            b.Property(x => x.ConversionOrder).HasColumnName("conversion_order").HasMaxLength(64);
        });
        ConfigureMaster<ProductGroup>(modelBuilder, "product_groups", b =>
        {
            b.Property(x => x.PlanningFee).HasColumnName("planning_fee").HasColumnType("numeric(12,2)");
        });
        ConfigureMaster<Color>(modelBuilder, "colors", b =>
        {
            b.Property(x => x.ItemConversionCode).HasColumnName("item_conversion_code").IsRequired().HasMaxLength(2).IsFixedLength();
        });
        ConfigureMaster<Material>(modelBuilder, "materials", b =>
        {
            b.Property(x => x.MaterialClassificationId).HasColumnName("material_classification_id");
            b.HasOne(x => x.MaterialClassification).WithMany().HasForeignKey(x => x.MaterialClassificationId);
        });
        ConfigureMaster<MaterialClassification>(modelBuilder, "material_classifications");
        ConfigureMaster<Warehouse>(modelBuilder, "warehouses");
        ConfigureMaster<DeliveryDestination>(modelBuilder, "delivery_destinations", b =>
        {
            b.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(255);
            b.Property(x => x.Remark1).HasColumnName("remark_1").HasMaxLength(255);
            b.Property(x => x.Remark2).HasColumnName("remark_2").HasMaxLength(255);
            b.Property(x => x.Remark3).HasColumnName("remark_3").HasMaxLength(255);
        });
        ConfigureMaster<DocumentTemplatePurchase>(modelBuilder, "document_template_purchases", b =>
        {
            b.Property(x => x.Body).HasColumnName("body").IsRequired();
        });
        ConfigureMaster<DocumentTemplateConfirmation>(modelBuilder, "document_template_confirmations", b =>
        {
            b.Property(x => x.Body).HasColumnName("body").IsRequired();
            b.Property(x => x.StandardPrintFlag).HasColumnName("standard_print_flag");
        });
        ConfigureMaster<DocumentTextPurchase>(modelBuilder, "document_text_purchases", b =>
        {
            b.Property(x => x.Body).HasColumnName("body").IsRequired();
            b.Property(x => x.StandardPrintFlag).HasColumnName("standard_print_flag");
        });

        // 商品関連 (Iteration 2、Phase 5 §4)
        modelBuilder.Entity<ProductFamily>(b =>
        {
            b.ToTable("product_families");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PlannedYearCode).HasColumnName("planned_year_code").IsRequired().HasMaxLength(1).IsFixedLength();
            b.Property(x => x.ProductTypeId).HasColumnName("product_type_id");
            b.Property(x => x.ProductSeasonId).HasColumnName("product_season_id");
            b.Property(x => x.SequenceNo).HasColumnName("sequence_no").IsRequired().HasMaxLength(3);
            b.Property(x => x.FactorySupplierId).HasColumnName("factory_supplier_id");
            b.Property(x => x.BrandId).HasColumnName("brand_id");
            b.Property(x => x.FunctionId).HasColumnName("function_id");
            b.Property(x => x.ProductGroupId).HasColumnName("product_group_id");
            b.Property(x => x.UpperMaterialId).HasColumnName("upper_material_id");
            b.Property(x => x.InsoleMaterialId).HasColumnName("insole_material_id");
            b.Property(x => x.OutsoleMaterialId).HasColumnName("outsole_material_id");
            b.Property(x => x.ProductName1).HasColumnName("product_name_1").IsRequired().HasMaxLength(255);
            b.Property(x => x.ProductName2).HasColumnName("product_name_2").HasMaxLength(255);
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);

            b.HasOne(x => x.ProductType).WithMany().HasForeignKey(x => x.ProductTypeId);
            b.HasOne(x => x.ProductSeason).WithMany().HasForeignKey(x => x.ProductSeasonId);
            b.HasOne(x => x.FactorySupplier).WithMany().HasForeignKey(x => x.FactorySupplierId);
            b.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId);
            b.HasOne(x => x.Function).WithMany().HasForeignKey(x => x.FunctionId);
            b.HasOne(x => x.ProductGroup).WithMany().HasForeignKey(x => x.ProductGroupId);
            b.HasOne(x => x.UpperMaterial).WithMany().HasForeignKey(x => x.UpperMaterialId);
            b.HasOne(x => x.InsoleMaterial).WithMany().HasForeignKey(x => x.InsoleMaterialId);
            b.HasOne(x => x.OutsoleMaterial).WithMany().HasForeignKey(x => x.OutsoleMaterialId);

            b.HasMany(x => x.Products).WithOne(p => p.ProductFamily!).HasForeignKey(p => p.ProductFamilyId);
            b.HasMany(x => x.Images).WithOne(i => i.ProductFamily!).HasForeignKey(i => i.ProductFamilyId);
            b.HasMany(x => x.SupplierPrices).WithOne(p => p.ProductFamily!).HasForeignKey(p => p.ProductFamilyId);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductFamilyId).HasColumnName("product_family_id");
            b.Property(x => x.ColorId).HasColumnName("color_id");
            b.Property(x => x.SizeId).HasColumnName("size_id");
            b.Property(x => x.Sku).HasColumnName("sku").IsRequired().HasMaxLength(11);
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);

            b.HasOne(x => x.Color).WithMany().HasForeignKey(x => x.ColorId);
            b.HasOne(x => x.Size).WithMany().HasForeignKey(x => x.SizeId);
            b.HasIndex(x => x.Sku).IsUnique();
        });

        modelBuilder.Entity<ProductImage>(b =>
        {
            b.ToTable("product_images");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductFamilyId).HasColumnName("product_family_id");
            b.Property(x => x.S3Key).HasColumnName("s3_key").IsRequired().HasMaxLength(512);
            b.Property(x => x.ThumbS3Key).HasColumnName("thumb_s3_key").HasMaxLength(512);
            b.Property(x => x.OrderNo).HasColumnName("order_no");
            b.Property(x => x.MimeType).HasColumnName("mime_type").IsRequired().HasMaxLength(64);
            b.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            b.Property(x => x.WidthPx).HasColumnName("width_px");
            b.Property(x => x.HeightPx).HasColumnName("height_px");
            b.Property(x => x.OriginalFilename).HasColumnName("original_filename").HasMaxLength(255);
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        });

        modelBuilder.Entity<ProductSupplierPrice>(b =>
        {
            b.ToTable("product_supplier_prices");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductFamilyId).HasColumnName("product_family_id");
            b.Property(x => x.SupplierId).HasColumnName("supplier_id");
            b.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)");
            b.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength();
            b.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").HasColumnType("numeric(10,4)");
            b.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
            b.Property(x => x.EffectiveTo).HasColumnName("effective_to");
            b.Property(x => x.DecidedAt).HasColumnName("decided_at");
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");

            b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId);
        });
    }

    /// <summary>マスタ共通基底カラム (id / code / name / delete_flag / 監査列 / legacy_id) の Fluent 設定。</summary>
    private static void ConfigureMaster<T>(
        ModelBuilder modelBuilder,
        string tableName,
        Action<EntityTypeBuilder<T>>? extension = null)
        where T : MasterEntityBase
    {
        modelBuilder.Entity<T>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(3).IsFixedLength();
            b.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(255);
            b.Property(x => x.DeleteFlag).HasColumnName("delete_flag");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);
            b.HasIndex(x => x.Code).IsUnique();
            extension?.Invoke(b);
        });
    }
}
