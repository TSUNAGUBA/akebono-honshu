-- Iteration 1: 17 マスタ + users 拡張
-- Phase 5 data-design.md §3.1-3.18
-- pgAdmin4 で akebono_honshu DB に対して実行 (docker 利用者は再構築で自動投入)
-- プラットフォーム統合改修: tenant_id (uuid) 導入・UNIQUE を (tenant_id, ...) へ差替・TIMESTAMPTZ(UTC) 化
-- プラットフォーム統合 第二段階: uuid PK / deleted_at 統一 / 監査パーティション

SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (tenant_id 列の DEFAULT がこの GUC から解決される)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- ─────────────────────────────────────────────────
-- §3.18 users テーブル拡張 (Iteration 0 最小スキーマからの拡張)
-- Phase 5 設計の全カラムを満たす
-- ─────────────────────────────────────────────────
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS firebase_uid                    VARCHAR(128) NULL,
    ADD COLUMN IF NOT EXISTS email                           VARCHAR(255) NULL,
    ADD COLUMN IF NOT EXISTS is_planning_staff               BOOLEAN      NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS is_sales_staff                  BOOLEAN      NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS product_ledger_permission       SMALLINT     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS purchase_order_create_permission SMALLINT    NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS purchase_order_info_permission  SMALLINT     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS process_record_permission       SMALLINT     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS created_by_user_id              UUID         NULL,
    ADD COLUMN IF NOT EXISTS updated_by_user_id              UUID         NULL,
    ADD COLUMN IF NOT EXISTS legacy_id                       VARCHAR(64)  NULL;

-- UNIQUE 制約 (Iteration 0 で employee_no / login_id は付与済)
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'users_firebase_uid_key') THEN
        CREATE UNIQUE INDEX users_firebase_uid_key ON users (firebase_uid) WHERE firebase_uid IS NOT NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'users_email_key') THEN
        CREATE UNIQUE INDEX users_email_key ON users (email) WHERE email IS NOT NULL;
    END IF;
END $$;

-- Iteration 0 seed ユーザを Iteration 1 用に拡張 (owner に全権限付与)
UPDATE users SET
    email = login_id || '@example.local',
    product_ledger_permission = 1,
    purchase_order_create_permission = 1,
    purchase_order_info_permission = 1,
    process_record_permission = 1
WHERE login_id = 'owner';

-- ─────────────────────────────────────────────────
-- 共通基底ヘルパー関数 (各マスタテーブル作成を簡素化)
-- ※ DDL は関数化できないため、各テーブルで明示的に列定義
-- ─────────────────────────────────────────────────

-- ─────────────────────────────────────────────────
-- §3.1 sizes — サイズマスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS sizes (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                  VARCHAR(3)   NOT NULL,
    name                  VARCHAR(255) NOT NULL,
    item_conversion_code  VARCHAR(4)   NOT NULL,
    deleted_at            TIMESTAMPTZ  NULL,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    UUID         NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    UUID         NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT uq_sizes_tenant_code UNIQUE (tenant_id, code)
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sizes_conv_active ON sizes (tenant_id, item_conversion_code) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_sizes_tenant ON sizes (tenant_id);
CREATE INDEX IF NOT EXISTS idx_sizes_legacy_id ON sizes (legacy_id) WHERE legacy_id IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §3.2 brands — ブランドマスタ (拡張カラムなし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS brands (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_brands_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_brands_tenant ON brands (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.3 functions — 機能マスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS functions (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_functions_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_functions_tenant ON functions (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.4 countries — 国マスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS countries (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_countries_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_countries_tenant ON countries (tenant_id);

-- 通貨マスタ (標準マスタ)。code = ISO 4217 3 文字コード (JPY/USD/CNY 等)。
-- 為替マスタの対象通貨・仕入先の適用通貨が本 code を参照する (文字列一致の整合性を集中管理)。
CREATE TABLE IF NOT EXISTS currencies (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_currencies_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_currencies_tenant ON currencies (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.5 suppliers — 仕入先マスタ (工場兼用、F-22 official_name 帳票印字)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS suppliers (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                  VARCHAR(3)   NOT NULL,
    name                  VARCHAR(255) NOT NULL,
    official_name         VARCHAR(255) NULL,
    item_conversion_code  CHAR(1)      NOT NULL,
    country_id            UUID         NOT NULL REFERENCES countries(id),
    supplier_type         SMALLINT     NOT NULL,
    alert_target          SMALLINT     NOT NULL DEFAULT 0,
    currency_code         CHAR(3)      NOT NULL DEFAULT 'JPY',   -- 適用通貨 (§2f)
    drayage_cost          NUMERIC(12,2) NULL,                    -- ドレー代 (§2i、仕入先ごと)
    deleted_at            TIMESTAMPTZ  NULL,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    UUID         NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    UUID         NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT uq_suppliers_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_suppliers_country ON suppliers (country_id);
CREATE INDEX IF NOT EXISTS idx_suppliers_active ON suppliers (deleted_at) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_suppliers_tenant ON suppliers (tenant_id);
CREATE INDEX IF NOT EXISTS idx_suppliers_legacy_id ON suppliers (legacy_id) WHERE legacy_id IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §3.5b factories — 工場マスタ (Part2)。仕入先 (suppliers) から分離。
--   仕入先=材料の仕入れ先 / 工場=製造 の別概念。品番7桁目の工場コード (item_conversion_code) 生成元。
--   通貨・ドレー代は仕入先固有のため持たない。product_families / production_instructions の
--   factory_supplier_id はこの表を参照する。既存 suppliers を複製してシードする (下の DO ブロック)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS factories (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                  VARCHAR(3)   NOT NULL,
    name                  VARCHAR(255) NOT NULL,
    official_name         VARCHAR(255) NULL,
    item_conversion_code  CHAR(1)      NOT NULL,
    country_id            UUID         NOT NULL REFERENCES countries(id),
    supplier_type         SMALLINT     NOT NULL,
    alert_target          SMALLINT     NOT NULL DEFAULT 0,
    deleted_at            TIMESTAMPTZ  NULL,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    UUID         NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    UUID         NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT uq_factories_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_factories_country ON factories (country_id);
CREATE INDEX IF NOT EXISTS idx_factories_active ON factories (deleted_at) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_factories_tenant ON factories (tenant_id);
CREATE INDEX IF NOT EXISTS idx_factories_legacy_id ON factories (legacy_id) WHERE legacy_id IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §3.6 departments — 事業部マスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS departments (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_departments_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_departments_tenant ON departments (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.7 product_types — 商品タイプマスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_types (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                     VARCHAR(3)   NOT NULL,
    name                     VARCHAR(255) NOT NULL,
    item_conversion_code     CHAR(1)      NOT NULL,
    size_demographic_code    CHAR(1)      NOT NULL,
    deleted_at               TIMESTAMPTZ  NULL,
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id       UUID         NOT NULL REFERENCES users(id),
    updated_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id       UUID         NOT NULL REFERENCES users(id),
    legacy_id                VARCHAR(64)  NULL,
    CONSTRAINT uq_product_types_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_product_types_tenant ON product_types (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.8 product_seasons — 商品季節マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_seasons (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                  VARCHAR(3)   NOT NULL,
    name                  VARCHAR(255) NOT NULL,
    item_conversion_code  CHAR(1)      NOT NULL,
    conversion_order      VARCHAR(64)  NULL,
    deleted_at            TIMESTAMPTZ  NULL,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    UUID         NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    UUID         NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT uq_product_seasons_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_product_seasons_tenant ON product_seasons (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.9 product_groups — 商品群マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_groups (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)    NOT NULL,
    name                VARCHAR(255)  NOT NULL,
    planning_fee        NUMERIC(12,2) NOT NULL DEFAULT 0,
    deleted_at          TIMESTAMPTZ   NULL,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID          NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID          NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)   NULL,
    CONSTRAINT uq_product_groups_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_product_groups_tenant ON product_groups (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.9b tax_rates — 税率マスタ (Part5)。税区分ごとの税率(%)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tax_rates (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    rate                NUMERIC(5,2) NOT NULL DEFAULT 0,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_tax_rates_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_tax_rates_tenant ON tax_rates (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.10 colors — 色マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS colors (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                  VARCHAR(3)   NOT NULL,
    name                  VARCHAR(255) NOT NULL,
    item_conversion_code  CHAR(2)      NOT NULL,
    deleted_at            TIMESTAMPTZ  NULL,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    UUID         NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    UUID         NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT uq_colors_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_colors_tenant ON colors (tenant_id);
CREATE INDEX IF NOT EXISTS idx_colors_legacy_id ON colors (legacy_id) WHERE legacy_id IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §3.12 material_classifications — 素材分類マスタ (materials の FK 参照先、先に作る)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS material_classifications (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_material_classifications_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_material_classifications_tenant ON material_classifications (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.11 materials — 素材マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS materials (
    id                              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                       UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                            VARCHAR(3)   NOT NULL,
    name                            VARCHAR(255) NOT NULL,
    material_classification_id      UUID         NOT NULL REFERENCES material_classifications(id),
    deleted_at                      TIMESTAMPTZ  NULL,
    created_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id              UUID         NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id              UUID         NOT NULL REFERENCES users(id),
    legacy_id                       VARCHAR(64)  NULL,
    CONSTRAINT uq_materials_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_materials_classification ON materials (material_classification_id);
CREATE INDEX IF NOT EXISTS idx_materials_tenant ON materials (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.11b customs_duty_rates — 関税率マスタ。海外仕入（輸入）時の関税率(%)。
--   原産国（必須）× 素材分類 3 列（NULL=ワイルドカード）で解決。従量税(円/足)は任意。
--   material_classifications を FK 参照するため、必ずその CREATE 後に置くこと（db/init は逐次実行）。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS customs_duty_rates (
    id                                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                          UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                               VARCHAR(3)    NOT NULL,
    name                               VARCHAR(255)  NOT NULL,
    country_id                         UUID          NOT NULL REFERENCES countries(id),
    upper_material_classification_id   UUID          NULL REFERENCES material_classifications(id),
    insole_material_classification_id  UUID          NULL REFERENCES material_classifications(id),
    outsole_material_classification_id UUID          NULL REFERENCES material_classifications(id),
    duty_rate                          NUMERIC(5,2)  NOT NULL DEFAULT 0,
    specific_duty_per_pair             NUMERIC(12,2) NULL,
    deleted_at                         TIMESTAMPTZ   NULL,
    created_at                         TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    created_by_user_id                 UUID          NOT NULL REFERENCES users(id),
    updated_at                         TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by_user_id                 UUID          NOT NULL REFERENCES users(id),
    legacy_id                          VARCHAR(64)   NULL,
    CONSTRAINT uq_customs_duty_rates_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT chk_customs_duty_rate_nonneg CHECK (duty_rate >= 0),
    CONSTRAINT chk_customs_specific_duty_nonneg CHECK (specific_duty_per_pair IS NULL OR specific_duty_per_pair >= 0)
);
CREATE INDEX IF NOT EXISTS idx_customs_duty_rates_tenant ON customs_duty_rates (tenant_id);
CREATE INDEX IF NOT EXISTS idx_customs_duty_rates_country ON customs_duty_rates (country_id);

-- ─────────────────────────────────────────────────
-- §3.13 warehouses — 倉庫コードマスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouses (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_warehouses_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_warehouses_tenant ON warehouses (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.14 delivery_destinations — 納品先マスタ (F-22 customer_name は内部識別用)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS delivery_destinations (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    customer_name       VARCHAR(255) NULL,
    remark_1            VARCHAR(255) NULL,
    remark_2            VARCHAR(255) NULL,
    remark_3            VARCHAR(255) NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_delivery_destinations_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_delivery_destinations_tenant ON delivery_destinations (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.15 document_template_purchases — 連絡文書定型・発注書
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_template_purchases (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                VARCHAR(3)   NOT NULL,
    name                VARCHAR(255) NOT NULL,
    body                TEXT         NOT NULL,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  UUID         NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  UUID         NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL,
    CONSTRAINT uq_document_template_purchases_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_document_template_purchases_tenant ON document_template_purchases (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.16 document_template_confirmations — 連絡文章・確認表
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_template_confirmations (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                 VARCHAR(3)   NOT NULL,
    name                 VARCHAR(255) NOT NULL,
    body                 TEXT         NOT NULL,
    standard_print_flag  BOOLEAN      NOT NULL DEFAULT FALSE,
    deleted_at           TIMESTAMPTZ  NULL,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id   UUID         NOT NULL REFERENCES users(id),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id   UUID         NOT NULL REFERENCES users(id),
    legacy_id            VARCHAR(64)  NULL,
    CONSTRAINT uq_document_template_confirmations_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_document_template_confirmations_tenant ON document_template_confirmations (tenant_id);

-- ─────────────────────────────────────────────────
-- §3.17 document_text_purchases — 連絡文章・発注書
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_text_purchases (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code                 VARCHAR(3)   NOT NULL,
    name                 VARCHAR(255) NOT NULL,
    body                 TEXT         NOT NULL,
    standard_print_flag  BOOLEAN      NOT NULL DEFAULT FALSE,
    deleted_at           TIMESTAMPTZ  NULL,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id   UUID         NOT NULL REFERENCES users(id),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id   UUID         NOT NULL REFERENCES users(id),
    legacy_id            VARCHAR(64)  NULL,
    CONSTRAINT uq_document_text_purchases_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_document_text_purchases_tenant ON document_text_purchases (tenant_id);

-- ─────────────────────────────────────────────────
-- 為替マスタ (§2f、bespoke master)。年月 (YYYY-MM) × 通貨ごとの対円レート。
-- code/name を持たず (year_month, currency_code) が業務キー。有効行の一意は部分 UNIQUE で保証。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS exchange_rates (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    year_month           CHAR(7)      NOT NULL,                -- 'YYYY-MM'
    currency_code        CHAR(3)      NOT NULL,                -- USD/CNY 等
    rate                 NUMERIC(12,4) NOT NULL,               -- 1 通貨単位 = rate 円
    tax_rate             NUMERIC(5,2) NULL,                    -- 税率(%) (Part5)。商品⑤仕入単価で通貨×年月から自動反映する。
    deleted_at           TIMESTAMPTZ  NULL,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id   UUID         NOT NULL REFERENCES users(id),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id   UUID         NOT NULL REFERENCES users(id),
    legacy_id            VARCHAR(64)  NULL,
    CONSTRAINT chk_exr_year_month CHECK (year_month ~ '^[0-9]{4}-[0-9]{2}$'),
    CONSTRAINT chk_exr_rate CHECK (rate > 0),
    CONSTRAINT chk_exr_tax_rate CHECK (tax_rate IS NULL OR tax_rate >= 0)
);
-- 有効行 (未削除) の (年月, 通貨) 一意。削除済みは重複を許容 (再登録可)。
CREATE UNIQUE INDEX IF NOT EXISTS uq_exr_year_month_currency
    ON exchange_rates (tenant_id, year_month, currency_code) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_exchange_rates_tenant ON exchange_rates (tenant_id);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 1 動作確認用に最小データ投入
-- (FK 整合のため owner ユーザを created_by/updated_by に使用)
-- ─────────────────────────────────────────────────
DO $$
DECLARE
    owner_id UUID;
    jp_id    UUID;
    cn_id    UUID;
    unset_country_id UUID;   -- 工場マスタ (Part2) 用の「未設定」国。添付データに国情報が無いため暫定割当。
    mc_natural UUID;
BEGIN
    SELECT id INTO owner_id FROM users WHERE login_id = 'owner';
    IF owner_id IS NULL THEN
        RAISE EXCEPTION 'owner user not found, run 01-schema.sql first';
    END IF;

    -- countries (suppliers の FK 参照先)
    INSERT INTO countries (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '日本',     owner_id, owner_id),
        ('002', '中国',     owner_id, owner_id),
        ('003', 'ベトナム', owner_id, owner_id),
        -- 工場マスタ (Part2) の暫定国。添付データに国情報が無い工場に割り当てる (マスタ画面で個別修正可)。
        ('999', '未設定',   owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- currencies (為替マスタ / 仕入先 適用通貨 の参照先)。code = ISO 4217 3 文字。
    INSERT INTO currencies (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('JPY', '日本円',   owner_id, owner_id),
        ('USD', '米ドル',   owner_id, owner_id),
        ('CNY', '人民元',   owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    SELECT id INTO jp_id FROM countries WHERE code = '001';
    SELECT id INTO cn_id FROM countries WHERE code = '002';
    SELECT id INTO unset_country_id FROM countries WHERE code = '999';

    -- material_classifications (materials の FK 参照先)
    INSERT INTO material_classifications (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '天然素材',   owner_id, owner_id),
        ('002', '合成素材',   owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    SELECT id INTO mc_natural FROM material_classifications WHERE code = '001';

    -- brands (旧確定値 §A-3 を反映。既存 001/002 は demo 参照のため残置し、旧 code を追加)
    INSERT INTO brands (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'akebono',  owner_id, owner_id),
        ('002', 'プライベート', owner_id, owner_id),
        ('000', 'ホンシュPB',        owner_id, owner_id),
        ('004', 'PLANT',             owner_id, owner_id),
        ('005', 'ディズニー',         owner_id, owner_id),
        ('006', 'リラックマ',         owner_id, owner_id),
        ('011', 'ガチャピン',         owner_id, owner_id),
        ('013', 'ONE PIECE',         owner_id, owner_id),
        ('017', 'HKWL(ヒロココシノ)', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- functions (旧確定値 §A-2 を反映)
    INSERT INTO functions (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '静音',          owner_id, owner_id),
        ('002', '脱げにくい',     owner_id, owner_id),
        ('003', 'からだにいい岩', owner_id, owner_id),
        ('004', '足つぼ',         owner_id, owner_id),
        ('005', 'むじゅうりょく', owner_id, owner_id),
        ('006', '超軽量',         owner_id, owner_id),
        ('007', 'メタボリッパ',   owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- departments
    INSERT INTO departments (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '第1事業部', owner_id, owner_id),
        ('002', '第2事業部', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- product_types
    INSERT INTO product_types (code, name, item_conversion_code, size_demographic_code, created_by_user_id, updated_by_user_id) VALUES
        ('001', '吊込W底婦人', 'A', 'R', owner_id, owner_id),
        ('002', '吊込W底紳士', 'A', 'M', owner_id, owner_id),
        ('003', 'クッション',  'C', 'R', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- product_seasons
    INSERT INTO product_seasons (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('001', '春夏', '1', owner_id, owner_id),
        ('002', '秋冬', '2', owner_id, owner_id),
        ('003', '通年', '0', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- product_groups (旧確定値 §A-2 を反映。planning_fee は本確定値に含まれないため
    --   既存サンプル額を踏襲し、追加分は DEFAULT 0)
    INSERT INTO product_groups (code, name, planning_fee, created_by_user_id, updated_by_user_id) VALUES
        ('001', '第1プロパー', 1000.00, owner_id, owner_id),
        ('002', '第2プロパー', 2500.00, owner_id, owner_id),
        ('003', '第1他商品',   0.00,    owner_id, owner_id),
        ('004', '第2他商品',   0.00,    owner_id, owner_id),
        ('005', '第1バーゲン', 0.00,    owner_id, owner_id),
        ('999', 'その他',       0.00,    owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- tax_rates (Part5)。標準税率 10% / 軽減税率 8% / 非課税 0%。
    INSERT INTO tax_rates (code, name, rate, created_by_user_id, updated_by_user_id) VALUES
        ('010', '標準税率', 10.00, owner_id, owner_id),
        ('008', '軽減税率', 8.00,  owner_id, owner_id),
        ('000', '非課税',   0.00,  owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- customs_duty_rates (関税率マスタ)。海外仕入時の輸入関税率(%) の代表値（目安）。
    --   ※ 実際の税率は 実行関税率表（税関 第64類 履物）・EPA/特恵の適用可否・関税割当の一次/二次税率で
    --     変動する。ここでは「甲皮素材の分類（天然/合成）で税率が変わる」代表例をシードする。
    --     天然素材の甲皮 = 革製履物相当（高率＋従量税、目安 27%/4,300円/足）、合成素材の甲皮 = ゴム/繊維甲相当
    --     （低率、目安 6.7%）とし、素材分類が一致しない場合は国ごとの既定（ワイルドカード行）を適用する。
    --     運用者は本マスタで各テナントの実態（原産国・素材・EPA）に合わせて登録・更新すること。
    --   国内（日本）・未設定国はシードしない（＝関税対象外／未登録扱い）。
    INSERT INTO customs_duty_rates
        (code, name, country_id, upper_material_classification_id, duty_rate, specific_duty_per_pair, created_by_user_id, updated_by_user_id) VALUES
        ('201', '中国・甲皮天然（革製履物 目安）',
            (SELECT id FROM countries WHERE code='002'),
            (SELECT id FROM material_classifications WHERE code='001'), 27.00, 4300.00, owner_id, owner_id),
        ('202', '中国・甲皮合成（ゴム/繊維甲 目安）',
            (SELECT id FROM countries WHERE code='002'),
            (SELECT id FROM material_classifications WHERE code='002'), 6.70, NULL, owner_id, owner_id),
        ('209', '中国・既定（その他履物 目安）',
            (SELECT id FROM countries WHERE code='002'),
            NULL, 8.00, NULL, owner_id, owner_id),
        ('301', 'ベトナム・甲皮天然（EPA 目安）',
            (SELECT id FROM countries WHERE code='003'),
            (SELECT id FROM material_classifications WHERE code='001'), 21.00, 4300.00, owner_id, owner_id),
        ('302', 'ベトナム・甲皮合成（EPA 目安）',
            (SELECT id FROM countries WHERE code='003'),
            (SELECT id FROM material_classifications WHERE code='002'), 0.00, NULL, owner_id, owner_id),
        ('309', 'ベトナム・既定（EPA 目安）',
            (SELECT id FROM countries WHERE code='003'),
            NULL, 4.00, NULL, owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- colors
    INSERT INTO colors (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('030', 'ブルー',  '30', owner_id, owner_id),
        ('040', 'ブラウン', '40', owner_id, owner_id),
        ('080', 'グレー',  '80', owner_id, owner_id),
        ('090', 'ブラック', '90', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- sizes
    INSERT INTO sizes (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'S',  '110S', owner_id, owner_id),
        ('002', 'M',  '110M', owner_id, owner_id),
        ('003', 'L',  '110L', owner_id, owner_id),
        ('004', 'LL', '110X', owner_id, owner_id),
        ('005', 'フリー', 'AS', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- materials
    INSERT INTO materials (code, name, material_classification_id, created_by_user_id, updated_by_user_id) VALUES
        ('001', '綿',       mc_natural, owner_id, owner_id),
        ('002', 'ポリエステル', (SELECT id FROM material_classifications WHERE code='002'), owner_id, owner_id),
        ('003', '麻',       mc_natural, owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- warehouses
    INSERT INTO warehouses (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('007', '本社倉庫',     owner_id, owner_id),
        ('717', 'しまむらセンター', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- suppliers (F-22 sample: official_name=DEPARTURES, code=336)
    INSERT INTO suppliers (code, name, official_name, item_conversion_code, country_id, supplier_type, created_by_user_id, updated_by_user_id) VALUES
        ('336', 'デパーチャーズ',         'DEPARTURES',     'A', jp_id, 0, owner_id, owner_id),
        ('404', '安徽拓馳鞋業有限公司',     'AN-HUI TUO-CHI', 'B', cn_id, 1, owner_id, owner_id),
        ('437', '南通本州貿易有限公司',     'NAN-TONG HONSHU','C', cn_id, 1, owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- factories (Part2): 既存 suppliers を「同一 id」で複製して工場マスタをシードする (非破壊分離)。
    -- product_families / production_instructions の factory_supplier_id は suppliers.id と同値の
    -- factories.id を参照するため、id を明示コピーして FK 整合を保つ。通貨・ドレー代は工場に持たない。
    INSERT INTO factories (id, tenant_id, code, name, official_name, item_conversion_code,
                           country_id, supplier_type, alert_target, deleted_at,
                           created_at, created_by_user_id, updated_at, updated_by_user_id, legacy_id)
        SELECT id, tenant_id, code, name, official_name, item_conversion_code,
               country_id, supplier_type, alert_target, deleted_at,
               created_at, created_by_user_id, updated_at, updated_by_user_id, legacy_id
        FROM suppliers
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- factories (Part2 添付データ): 品番7桁目の工場コード (A〜Z) と工場名称。code=item_conversion_code=英字1桁。
    --   国情報が添付データに無いため country は '未設定' (国内その他のみ日本)。区分は 国内その他=0(国内)/他=1(海外)。
    --   添付で取り消し線の工場 (B/M/S/T/U/Y) は「廃止」とみなし deleted_at をセットして論理削除で投入する
    --   (品番の履歴解決・年度分析には残し、新規登録の工場選択肢には出さない)。'－' の I/O は工場が無いためスキップ。
    --   ON CONFLICT (tenant_id, code) DO NOTHING で冪等 (既存デモ工場 336/404/437 とは code が異なるため独立)。
    INSERT INTO factories (code, name, item_conversion_code, country_id, supplier_type, alert_target,
                           deleted_at, created_by_user_id, updated_by_user_id) VALUES
        ('A', '海外その他',     'A', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('B', '南通本州',       'B', unset_country_id, 1, 0, NOW(),  owner_id, owner_id),  -- 取消線=廃止 (論理削除)
        ('C', '固安',           'C', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('D', '正書',           'D', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('E', '天宇',           'E', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('F', '藤東',           'F', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('G', '新岡',           'G', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('H', '環球',           'H', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('J', '拓馳',           'J', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('K', '春源',           'K', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('L', '五星・柬埔寨',   'L', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('M', 'Ixora',          'M', unset_country_id, 1, 0, NOW(),  owner_id, owner_id),  -- 取消線=廃止 (論理削除)
        ('N', '蘭奥',           'N', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('P', '三和繊維',       'P', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('Q', '居美',           'Q', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('R', 'ラッキー産業',   'R', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('S', 'ストロング',     'S', unset_country_id, 1, 0, NOW(),  owner_id, owner_id),  -- 取消線=廃止 (論理削除)
        ('T', '小澤重',         'T', unset_country_id, 1, 0, NOW(),  owner_id, owner_id),  -- 取消線=廃止 (論理削除)
        ('U', 'スタル',         'U', unset_country_id, 1, 0, NOW(),  owner_id, owner_id),  -- 取消線=廃止 (論理削除)
        ('V', '湯誠',           'V', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('W', '金森',           'W', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('X', '評価減',         'X', unset_country_id, 1, 0, NULL,   owner_id, owner_id),
        ('Y', '夢華達',         'Y', unset_country_id, 1, 0, NOW(),  owner_id, owner_id),  -- 取消線=廃止 (論理削除)
        ('Z', '国内その他',     'Z', jp_id,            0, 0, NULL,   owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- delivery_destinations (Phase 6 サンプル準拠)
    INSERT INTO delivery_destinations (code, name, customer_name, remark_1, remark_2, remark_3, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'しまむらセンター', 'しまむら', '埼玉県さいたま市', '048-XXX-XXXX', '048-XXX-XXXY', owner_id, owner_id),
        ('002', 'AEONセンター',    'AEON',    '千葉県千葉市',   '043-XXX-XXXX', '043-XXX-XXXY', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- document_template_purchases
    INSERT INTO document_template_purchases (code, name, body, created_by_user_id, updated_by_user_id) VALUES
        ('001', '標準発注', '発注書を作成いたします。よろしくお願いいたします。', owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- document_template_confirmations
    INSERT INTO document_template_confirmations (code, name, body, standard_print_flag, created_by_user_id, updated_by_user_id) VALUES
        ('001', '確認表標準', 'ご確認のほどよろしくお願いいたします。', TRUE, owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

    -- document_text_purchases
    INSERT INTO document_text_purchases (code, name, body, standard_print_flag, created_by_user_id, updated_by_user_id) VALUES
        ('001', '注意書き標準', '分納、遅納、訂正等は、商品管理部宛 FAX:03-5850-4720 までご連絡ください。', TRUE, owner_id, owner_id),
        ('002', '納品書記載',   '納品書には発注番号を記載してください。発注番号がないものは支払対象になっておりません。', TRUE, owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;

END $$;
