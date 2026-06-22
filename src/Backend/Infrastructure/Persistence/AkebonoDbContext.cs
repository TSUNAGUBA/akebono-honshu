using Akebono.Application.Common;
using Akebono.Domain.Common;
using Akebono.Domain.Entities;
using Akebono.Domain.Orders;
using Akebono.Domain.Production;
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

    // 発注関連 (Iteration 3)
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderExportLog> PurchaseOrderExportLogs => Set<PurchaseOrderExportLog>();

    // 生産管理拡張 (Iteration 5)
    public DbSet<ProductMaterial> ProductMaterials => Set<ProductMaterial>();
    public DbSet<ProductionInstruction> ProductionInstructions => Set<ProductionInstruction>();
    public DbSet<ProductionInstructionLine> ProductionInstructionLines => Set<ProductionInstructionLine>();
    public DbSet<MaterialOrder> MaterialOrders => Set<MaterialOrder>();
    public DbSet<MaterialOrderLine> MaterialOrderLines => Set<MaterialOrderLine>();

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

        // 発注関連 (Iteration 3、Phase 5 §5)
        modelBuilder.Entity<PurchaseOrder>(b =>
        {
            b.ToTable("purchase_orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.MgmtNo).HasColumnName("mgmt_no").IsRequired().HasMaxLength(16);
            b.Property(x => x.OrderNo).HasColumnName("order_no").HasMaxLength(16);
            b.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            b.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
            b.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(255);
            b.Property(x => x.SupplierId).HasColumnName("supplier_id");
            b.Property(x => x.SupplierOfficialNameSnapshot).HasColumnName("supplier_official_name_snapshot").HasMaxLength(255);
            b.Property(x => x.SupplierCodeSnapshot).HasColumnName("supplier_code_snapshot").HasMaxLength(3);
            b.Property(x => x.DeliveryDestinationId).HasColumnName("delivery_destination_id");
            b.Property(x => x.CustomerNameSnapshot).HasColumnName("customer_name_snapshot").HasMaxLength(255);
            b.Property(x => x.DepartmentId).HasColumnName("department_id");
            b.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            b.Property(x => x.DueDate).HasColumnName("due_date");
            b.Property(x => x.OrdererUserId).HasColumnName("orderer_user_id");
            b.Property(x => x.SubOrderer1UserId).HasColumnName("sub_orderer_1_user_id");
            b.Property(x => x.SubOrderer2UserId).HasColumnName("sub_orderer_2_user_id");
            b.Property(x => x.SubOrderer3UserId).HasColumnName("sub_orderer_3_user_id");
            b.Property(x => x.SubOrderer4UserId).HasColumnName("sub_orderer_4_user_id");
            b.Property(x => x.SubOrderer5UserId).HasColumnName("sub_orderer_5_user_id");
            b.Property(x => x.SubOrderer6UserId).HasColumnName("sub_orderer_6_user_id");
            b.Property(x => x.ManagerUserId).HasColumnName("manager_user_id");
            b.Property(x => x.CommunicationText).HasColumnName("communication_text");
            b.Property(x => x.FirstExportedAt).HasColumnName("first_exported_at");
            b.Property(x => x.LastExportedAt).HasColumnName("last_exported_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);

            b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId);
            b.HasOne(x => x.DeliveryDestination).WithMany().HasForeignKey(x => x.DeliveryDestinationId);
            b.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId);
            b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
            b.HasOne(x => x.Orderer).WithMany().HasForeignKey(x => x.OrdererUserId);
            b.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerUserId);
            b.HasMany(x => x.Lines).WithOne(l => l.PurchaseOrder!).HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.ExportLogs).WithOne(l => l.PurchaseOrder!).HasForeignKey(l => l.PurchaseOrderId);

            b.HasIndex(x => x.MgmtNo).IsUnique();
        });

        modelBuilder.Entity<PurchaseOrderLine>(b =>
        {
            b.ToTable("purchase_order_lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id");
            b.Property(x => x.LineNo).HasColumnName("line_no");
            b.Property(x => x.ProductId).HasColumnName("product_id");
            b.Property(x => x.SkuSnapshot).HasColumnName("sku_snapshot").IsRequired().HasMaxLength(11);
            b.Property(x => x.ProductNameSnapshot).HasColumnName("product_name_snapshot").IsRequired().HasMaxLength(255);
            b.Property(x => x.Quantity).HasColumnName("quantity");
            b.Property(x => x.UnitPriceSnapshot).HasColumnName("unit_price_snapshot").HasColumnType("numeric(12,2)");
            b.Property(x => x.CurrencyCodeSnapshot).HasColumnName("currency_code_snapshot").IsRequired().HasMaxLength(3).IsFixedLength();
            b.Property(x => x.Subtotal)
                .HasColumnName("subtotal")
                .HasColumnType("numeric(14,2)")
                .HasComputedColumnSql("quantity * unit_price_snapshot", stored: true)
                .ValueGeneratedOnAddOrUpdate();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");

            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            b.HasIndex(x => new { x.PurchaseOrderId, x.LineNo }).IsUnique();
        });

        modelBuilder.Entity<PurchaseOrderExportLog>(b =>
        {
            b.ToTable("purchase_order_export_logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id");
            b.Property(x => x.ExportedAt).HasColumnName("exported_at");
            b.Property(x => x.ExportedByUserId).HasColumnName("exported_by_user_id");
            b.Property(x => x.IsFirstExport).HasColumnName("is_first_export");
            b.Property(x => x.ExcelTemplateVersion).HasColumnName("excel_template_version").IsRequired().HasMaxLength(16);
        });

        // 生産管理拡張 (Iteration 5、data-design-production §4)
        modelBuilder.Entity<ProductMaterial>(b =>
        {
            b.ToTable("product_materials");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductFamilyId).HasColumnName("product_family_id");
            b.Property(x => x.MaterialRole).HasColumnName("material_role").HasConversion<short>();
            b.Property(x => x.MaterialId).HasColumnName("material_id");
            b.Property(x => x.RequiredQtyPerUnit).HasColumnName("required_qty_per_unit").HasColumnType("numeric(12,4)");
            b.Property(x => x.Unit).HasColumnName("unit").IsRequired().HasMaxLength(8);
            b.Property(x => x.RecommendedSupplierId).HasColumnName("recommended_supplier_id");
            b.Property(x => x.LossRate).HasColumnName("loss_rate").HasColumnType("numeric(5,4)");
            b.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(255);
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");

            b.HasOne(x => x.ProductFamily).WithMany().HasForeignKey(x => x.ProductFamilyId);
            b.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId);
            b.HasOne(x => x.RecommendedSupplier).WithMany().HasForeignKey(x => x.RecommendedSupplierId);
        });

        modelBuilder.Entity<ProductionInstruction>(b =>
        {
            b.ToTable("production_instructions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.InstructionNo).HasColumnName("instruction_no").IsRequired().HasMaxLength(16);
            b.Property(x => x.ProductFamilyId).HasColumnName("product_family_id");
            b.Property(x => x.FactorySupplierId).HasColumnName("factory_supplier_id");
            b.Property(x => x.PlannedQuantity).HasColumnName("planned_quantity");
            b.Property(x => x.DueDate).HasColumnName("due_date");
            b.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            b.Property(x => x.InstructedAt).HasColumnName("instructed_at");
            b.Property(x => x.CompletedAt).HasColumnName("completed_at");
            b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            b.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
            b.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(255);
            b.Property(x => x.FactoryOfficialNameSnapshot).HasColumnName("factory_official_name_snapshot").HasMaxLength(255);
            b.Property(x => x.FactoryCodeSnapshot).HasColumnName("factory_code_snapshot").HasMaxLength(3);
            b.Property(x => x.ProductSku9Snapshot).HasColumnName("product_sku9_snapshot").HasMaxLength(9);
            b.Property(x => x.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(255);
            b.Property(x => x.CommunicationText).HasColumnName("communication_text");
            b.Property(x => x.FirstExportedAt).HasColumnName("first_exported_at");
            b.Property(x => x.LastExportedAt).HasColumnName("last_exported_at");
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);

            b.HasOne(x => x.ProductFamily).WithMany().HasForeignKey(x => x.ProductFamilyId);
            b.HasOne(x => x.FactorySupplier).WithMany().HasForeignKey(x => x.FactorySupplierId);
            b.HasMany(x => x.Lines).WithOne(l => l.ProductionInstruction!).HasForeignKey(l => l.ProductionInstructionId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.InstructionNo).IsUnique();
        });

        modelBuilder.Entity<ProductionInstructionLine>(b =>
        {
            b.ToTable("production_instruction_lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductionInstructionId).HasColumnName("production_instruction_id");
            b.Property(x => x.LineNo).HasColumnName("line_no");
            b.Property(x => x.ProductId).HasColumnName("product_id");
            b.Property(x => x.SkuSnapshot).HasColumnName("sku_snapshot").IsRequired().HasMaxLength(11);
            b.Property(x => x.ProductNameSnapshot).HasColumnName("product_name_snapshot").IsRequired().HasMaxLength(255);
            b.Property(x => x.Quantity).HasColumnName("quantity");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");

            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            b.HasIndex(x => new { x.ProductionInstructionId, x.LineNo }).IsUnique();
            b.HasIndex(x => new { x.ProductionInstructionId, x.ProductId }).IsUnique();
        });

        modelBuilder.Entity<MaterialOrder>(b =>
        {
            b.ToTable("material_orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.OrderNo).HasColumnName("order_no").IsRequired().HasMaxLength(16);
            b.Property(x => x.MaterialSupplierId).HasColumnName("material_supplier_id");
            b.Property(x => x.ProductionInstructionId).HasColumnName("production_instruction_id");
            b.Property(x => x.DueDate).HasColumnName("due_date");
            b.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            b.Property(x => x.InstructedAt).HasColumnName("instructed_at");
            b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            b.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
            b.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(255);
            b.Property(x => x.SupplierOfficialNameSnapshot).HasColumnName("supplier_official_name_snapshot").HasMaxLength(255);
            b.Property(x => x.SupplierCodeSnapshot).HasColumnName("supplier_code_snapshot").HasMaxLength(3);
            b.Property(x => x.CommunicationText).HasColumnName("communication_text");
            b.Property(x => x.FirstExportedAt).HasColumnName("first_exported_at");
            b.Property(x => x.LastExportedAt).HasColumnName("last_exported_at");
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            b.Property(x => x.LegacyId).HasColumnName("legacy_id").HasMaxLength(64);

            b.HasOne(x => x.MaterialSupplier).WithMany().HasForeignKey(x => x.MaterialSupplierId);
            b.HasOne(x => x.ProductionInstruction).WithMany().HasForeignKey(x => x.ProductionInstructionId);
            b.HasMany(x => x.Lines).WithOne(l => l.MaterialOrder!).HasForeignKey(l => l.MaterialOrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.OrderNo).IsUnique();
        });

        modelBuilder.Entity<MaterialOrderLine>(b =>
        {
            b.ToTable("material_order_lines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.MaterialOrderId).HasColumnName("material_order_id");
            b.Property(x => x.LineNo).HasColumnName("line_no");
            b.Property(x => x.MaterialId).HasColumnName("material_id");
            b.Property(x => x.MaterialNameSnapshot).HasColumnName("material_name_snapshot").IsRequired().HasMaxLength(255);
            b.Property(x => x.ProductFamilyId).HasColumnName("product_family_id");
            b.Property(x => x.SourcePiLineId).HasColumnName("source_pi_line_id");
            b.Property(x => x.RequiredQuantity).HasColumnName("required_quantity").HasColumnType("numeric(14,4)");
            b.Property(x => x.Unit).HasColumnName("unit").IsRequired().HasMaxLength(8);
            b.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)");
            b.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength();
            b.Property(x => x.Subtotal)
                .HasColumnName("subtotal")
                .HasColumnType("numeric(16,2)")
                .HasComputedColumnSql("required_quantity * COALESCE(unit_price, 0)", stored: true)
                .ValueGeneratedOnAddOrUpdate();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");

            b.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId);
            b.HasOne(x => x.ProductFamily).WithMany().HasForeignKey(x => x.ProductFamilyId);
            b.HasIndex(x => new { x.MaterialOrderId, x.LineNo }).IsUnique();
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
