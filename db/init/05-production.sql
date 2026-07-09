-- Iteration 5: 生産管理拡張 5 テーブル (Phase 5 data-design-production.md §4)
-- 前提: 04-orders.sql まで投入済 (masters / products / orders)
-- 既存26テーブルはスキーマ変更なし (下位互換、CLAUDE.md 原則7)
-- プラットフォーム統合改修: tenant_id (uuid) 導入・UNIQUE を (tenant_id, ...) へ差替・TIMESTAMPTZ(UTC) 化
-- プラットフォーム統合 第二段階: uuid PK / deleted_at 統一 / 監査パーティション

SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (tenant_id 列の DEFAULT がこの GUC から解決される)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- ─────────────────────────────────────────────────
-- §4.1 product_materials — BOM (素材構成・所要量)
-- 既存 product_families.upper/insole/outsole_material_id は変更せず存置 (表示用)
-- BOM が所要量 SoT、3FK は疎結合 (書戻しなし、初期シードのみ)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_materials (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    product_family_id       UUID          NOT NULL REFERENCES product_families(id),
    material_role           SMALLINT      NOT NULL,   -- 0甲皮/1中底/2底/3付属/4副資材
    material_id             UUID          NOT NULL REFERENCES materials(id),
    required_qty_per_unit   NUMERIC(12,4) NOT NULL,   -- 1足あたり所要量
    unit                    VARCHAR(8)    NOT NULL,    -- 足/組/枚/個/m/㎡/cm/本
    recommended_supplier_id UUID          NULL REFERENCES suppliers(id),
    loss_rate               NUMERIC(5,4)  NOT NULL DEFAULT 0,  -- 0〜1未満
    remark                  VARCHAR(255)  NULL,
    deleted_at              TIMESTAMPTZ   NULL,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    created_by_user_id      UUID          NOT NULL REFERENCES users(id),
    updated_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by_user_id      UUID          NOT NULL REFERENCES users(id),

    CONSTRAINT chk_pm_role  CHECK (material_role BETWEEN 0 AND 4),
    CONSTRAINT chk_pm_qty   CHECK (required_qty_per_unit > 0),
    CONSTRAINT chk_pm_loss  CHECK (loss_rate >= 0 AND loss_rate < 1)
);
-- 同一品番・同一部位・同一素材の重複防止 (同一部位に複数素材＝多層は許容)
CREATE UNIQUE INDEX IF NOT EXISTS uq_pm_family_role_material
    ON product_materials (tenant_id, product_family_id, material_role, material_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_pm_family   ON product_materials (product_family_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_pm_material ON product_materials (material_id);
CREATE INDEX IF NOT EXISTS idx_pm_supplier ON product_materials (recommended_supplier_id) WHERE recommended_supplier_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_product_materials_tenant ON product_materials (tenant_id);

-- ─────────────────────────────────────────────────
-- §4.2 production_instructions — 生産指示書 (加工指図書) ヘッダ
-- status: 0=Draft 1=Issued(指示済) 2=Completed 9=Cancelled
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS production_instructions (
    id                              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                       UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    -- Idempotency-Key (AKB-DOC-12 §8: 作成系 POST の冪等キー。ヘッダ値と要求ペイロードの SHA-256)
    idempotency_key                 VARCHAR(128) NULL,
    idempotency_payload_hash        VARCHAR(64)  NULL,
    instruction_no                  VARCHAR(16)  NOT NULL,
    product_family_id               UUID         NOT NULL REFERENCES product_families(id),
    factory_supplier_id             UUID         NOT NULL REFERENCES suppliers(id),
    planned_quantity                INTEGER      NOT NULL,
    due_date                        DATE         NOT NULL,
    status                          SMALLINT     NOT NULL DEFAULT 0,
    instructed_at                   TIMESTAMPTZ  NULL,
    completed_at                    TIMESTAMPTZ  NULL,
    cancelled_at                    TIMESTAMPTZ  NULL,
    cancelled_by_user_id            UUID         NULL REFERENCES users(id),
    cancel_reason                   VARCHAR(255) NULL,
    factory_official_name_snapshot  VARCHAR(255) NULL,
    factory_code_snapshot           VARCHAR(3)   NULL,
    product_sku9_snapshot           VARCHAR(9)   NULL,
    product_name_snapshot           VARCHAR(255) NULL,
    communication_text              TEXT         NULL,
    first_exported_at               TIMESTAMPTZ  NULL,
    last_exported_at                TIMESTAMPTZ  NULL,
    deleted_at                      TIMESTAMPTZ  NULL,
    created_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id              UUID         NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id              UUID         NOT NULL REFERENCES users(id),
    legacy_id                       VARCHAR(64)  NULL,

    CONSTRAINT chk_pi_status            CHECK (status IN (0,1,2,9)),
    CONSTRAINT chk_pi_qty               CHECK (planned_quantity > 0),
    CONSTRAINT chk_pi_last_after_first  CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL),
    CONSTRAINT chk_pi_cancelled         CHECK ((status = 9) = (cancelled_at IS NOT NULL)),
    CONSTRAINT uq_production_instructions_tenant_instruction_no UNIQUE (tenant_id, instruction_no)
);
CREATE INDEX IF NOT EXISTS idx_pi_instruction_no ON production_instructions (instruction_no);
CREATE INDEX IF NOT EXISTS idx_pi_family   ON production_instructions (product_family_id);
CREATE INDEX IF NOT EXISTS idx_pi_factory  ON production_instructions (factory_supplier_id);
CREATE INDEX IF NOT EXISTS idx_pi_status   ON production_instructions (status, due_date);
-- 「生産指示=済」判定の部分インデックス (status 1=Issued / 2=Completed のみ)
CREATE INDEX IF NOT EXISTS idx_pi_family_active
    ON production_instructions (product_family_id) WHERE status IN (1,2) AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_pi_dates    ON production_instructions (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_production_instructions_tenant ON production_instructions (tenant_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_production_instructions_tenant_idem ON production_instructions (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §4.3 production_instruction_lines — 生産指示明細 (色×サイズ別)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS production_instruction_lines (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                   UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    production_instruction_id   UUID         NOT NULL REFERENCES production_instructions(id) ON DELETE CASCADE,
    line_no                     SMALLINT     NOT NULL,
    product_id                  UUID         NOT NULL REFERENCES products(id),
    sku_snapshot                VARCHAR(11)  NOT NULL,
    product_name_snapshot       VARCHAR(255) NOT NULL,
    quantity                    INTEGER      NOT NULL,
    created_at                  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id          UUID         NOT NULL REFERENCES users(id),
    updated_at                  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id          UUID         NOT NULL REFERENCES users(id),

    CONSTRAINT chk_pil_qty           CHECK (quantity > 0),
    CONSTRAINT uq_pil_instruction_line    UNIQUE (tenant_id, production_instruction_id, line_no),
    CONSTRAINT uq_pil_instruction_product UNIQUE (tenant_id, production_instruction_id, product_id)
);
CREATE INDEX IF NOT EXISTS idx_pil_instruction ON production_instruction_lines (production_instruction_id);
CREATE INDEX IF NOT EXISTS idx_pil_product     ON production_instruction_lines (product_id);
CREATE INDEX IF NOT EXISTS idx_production_instruction_lines_tenant ON production_instruction_lines (tenant_id);

-- ─────────────────────────────────────────────────
-- §4.4 material_orders — 素材発注書 (生地材料発注) ヘッダ
-- status: 0=Draft 1=Ordered(発注済) 9=Cancelled
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS material_orders (
    id                              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                       UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    -- Idempotency-Key (AKB-DOC-12 §8: 作成系 POST の冪等キー。ヘッダ値と要求ペイロードの SHA-256)
    idempotency_key                 VARCHAR(128) NULL,
    idempotency_payload_hash        VARCHAR(64)  NULL,
    order_no                        VARCHAR(16)  NOT NULL,
    material_supplier_id            UUID         NOT NULL REFERENCES suppliers(id),
    production_instruction_id       UUID         NULL REFERENCES production_instructions(id),
    due_date                        DATE         NOT NULL,
    status                          SMALLINT     NOT NULL DEFAULT 0,
    instructed_at                   TIMESTAMPTZ  NULL,
    cancelled_at                    TIMESTAMPTZ  NULL,
    cancelled_by_user_id            UUID         NULL REFERENCES users(id),
    cancel_reason                   VARCHAR(255) NULL,
    supplier_official_name_snapshot VARCHAR(255) NULL,
    supplier_code_snapshot          VARCHAR(3)   NULL,
    communication_text              TEXT         NULL,
    first_exported_at               TIMESTAMPTZ  NULL,
    last_exported_at                TIMESTAMPTZ  NULL,
    deleted_at                      TIMESTAMPTZ  NULL,
    created_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id              UUID         NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id              UUID         NOT NULL REFERENCES users(id),
    legacy_id                       VARCHAR(64)  NULL,

    CONSTRAINT chk_mo_status            CHECK (status IN (0,1,9)),
    CONSTRAINT chk_mo_last_after_first  CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL),
    CONSTRAINT chk_mo_cancelled         CHECK ((status = 9) = (cancelled_at IS NOT NULL)),
    CONSTRAINT uq_material_orders_tenant_order_no UNIQUE (tenant_id, order_no)
);
CREATE INDEX IF NOT EXISTS idx_mo_order_no    ON material_orders (order_no);
CREATE INDEX IF NOT EXISTS idx_mo_supplier    ON material_orders (material_supplier_id);
CREATE INDEX IF NOT EXISTS idx_mo_instruction ON material_orders (production_instruction_id) WHERE production_instruction_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_mo_status      ON material_orders (status, due_date);
CREATE INDEX IF NOT EXISTS idx_mo_dates       ON material_orders (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_material_orders_tenant ON material_orders (tenant_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_material_orders_tenant_idem ON material_orders (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §4.5 material_order_lines — 素材発注明細 (所要量展開)
-- subtotal は GENERATED で DB 側計算 (既存 purchase_order_lines と同方針)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS material_order_lines (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    material_order_id       UUID          NOT NULL REFERENCES material_orders(id) ON DELETE CASCADE,
    line_no                 SMALLINT      NOT NULL,
    material_id             UUID          NOT NULL REFERENCES materials(id),
    material_name_snapshot  VARCHAR(255)  NOT NULL,
    product_family_id       UUID          NULL REFERENCES product_families(id),  -- 由来品番 (未/済ロールアップ)
    source_pi_line_id       UUID          NULL REFERENCES production_instruction_lines(id),
    required_quantity       NUMERIC(14,4) NOT NULL,
    unit                    VARCHAR(8)    NOT NULL,
    unit_price              NUMERIC(12,2) NULL,   -- 機密度 中-高 (既存仕入単価と同等保護)
    currency_code           CHAR(3)       NOT NULL DEFAULT 'JPY',
    subtotal                NUMERIC(16,2) GENERATED ALWAYS AS (required_quantity * COALESCE(unit_price, 0)) STORED,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    created_by_user_id      UUID          NOT NULL REFERENCES users(id),
    updated_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by_user_id      UUID          NOT NULL REFERENCES users(id),

    CONSTRAINT chk_mol_qty        CHECK (required_quantity > 0),
    CONSTRAINT chk_mol_price      CHECK (unit_price IS NULL OR unit_price >= 0),
    CONSTRAINT uq_mol_order_line  UNIQUE (tenant_id, material_order_id, line_no)
);
CREATE INDEX IF NOT EXISTS idx_mol_order    ON material_order_lines (material_order_id);
CREATE INDEX IF NOT EXISTS idx_mol_material ON material_order_lines (material_id);
-- 「素材発注=済」の品番ロールアップ判定に使用
CREATE INDEX IF NOT EXISTS idx_mol_family   ON material_order_lines (product_family_id) WHERE product_family_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_material_order_lines_tenant ON material_order_lines (tenant_id);

-- ─────────────────────────────────────────────────
-- 付属・副資材の material_classifications 追加 (BOM の role 3/4 用)
-- 冪等: code 重複時は何もしない
-- ─────────────────────────────────────────────────
DO $$
DECLARE owner_id UUID;
BEGIN
    SELECT id INTO owner_id FROM users WHERE login_id = 'owner';
    IF owner_id IS NULL THEN RETURN; END IF;

    INSERT INTO material_classifications (code, name, deleted_at, created_by_user_id, updated_by_user_id)
    VALUES ('900', '付属', NULL, owner_id, owner_id),
           ('901', '副資材', NULL, owner_id, owner_id)
    ON CONFLICT (tenant_id, code) DO NOTHING;
END $$;
