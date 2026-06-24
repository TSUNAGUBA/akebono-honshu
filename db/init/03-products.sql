-- Iteration 2: 商品関連 4 テーブル (Phase 5 data-design.md §4.1-4.4)
-- 前提: 01-schema.sql (users / audit_logs) + 02-masters.sql (17 マスタ) 投入済

SET TIMEZONE = 'Asia/Tokyo';

-- ─────────────────────────────────────────────────
-- §4.1 product_families — 商品企画レベル親
-- 11桁品番の上位 9 桁を確定する企画単位
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_families (
    id                    BIGSERIAL PRIMARY KEY,
    planned_year_code     CHAR(1)      NOT NULL,
    product_type_id       BIGINT       NOT NULL REFERENCES product_types(id),
    product_season_id     BIGINT       NOT NULL REFERENCES product_seasons(id),
    sequence_no           VARCHAR(3)   NOT NULL,
    factory_supplier_id   BIGINT       NOT NULL REFERENCES suppliers(id),
    brand_id              BIGINT       NOT NULL REFERENCES brands(id),
    function_id           BIGINT       NULL     REFERENCES functions(id),
    product_group_id      BIGINT       NOT NULL REFERENCES product_groups(id),
    upper_material_id     BIGINT       NOT NULL REFERENCES materials(id),
    insole_material_id    BIGINT       NOT NULL REFERENCES materials(id),
    outsole_material_id   BIGINT       NOT NULL REFERENCES materials(id),
    product_name_1        VARCHAR(255) NOT NULL,
    product_name_2        VARCHAR(255) NULL,
    -- 旧 品番台帳 項目 (Phase A、全 NULL 許容 = 既存行は NULL のまま下位互換)
    product_year          SMALLINT     NULL,                                       -- 商品年度 (9999=通年)
    management_season_id  BIGINT       NULL     REFERENCES product_seasons(id),    -- 管理季節
    planner_user_id       BIGINT       NULL     REFERENCES users(id),              -- 企画者
    provisional_number    VARCHAR(64)  NULL,                                       -- 仮番号
    sample_approval_date  DATE         NULL,                                       -- サンプル合格日
    retail_price          NUMERIC(12,2) NULL,                                      -- 小売価格
    delivery_price        NUMERIC(12,2) NULL,                                      -- 納品価格
    planning_cost         NUMERIC(12,2) NULL,                                      -- 企画費
    brand_cost            NUMERIC(12,2) NULL,                                      -- ブランド費
    royalty_target        SMALLINT     NULL,                                       -- 版権対象 (1=小売価格, 2=納品価格)
    royalty_rate          NUMERIC(5,2) NULL,                                       -- 版権料率(%)
    -- 旧 品番台帳 項目 追補 (PR1、全 NULL 許容 = 既存行は NULL のまま下位互換)
    remark                TEXT         NULL,                                       -- 商品本体 備考 (spec No.39)
    color_remark          TEXT         NULL,                                       -- 備考（色）(spec No.33、商品単位の単一テキスト)
    status                SMALLINT     NOT NULL DEFAULT 0,
    is_deleted            BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT chk_pf_planned_year_code CHECK (planned_year_code IN ('A','B','C','D','E','F','G','H','I','J','K','N','Z')),
    CONSTRAINT chk_pf_status CHECK (status BETWEEN 0 AND 2),
    CONSTRAINT chk_pf_royalty_target CHECK (royalty_target IS NULL OR royalty_target IN (1,2)),
    CONSTRAINT uq_product_families UNIQUE (planned_year_code, product_type_id, product_season_id, sequence_no, factory_supplier_id)
);
CREATE INDEX IF NOT EXISTS idx_pf_status_deleted ON product_families (status, is_deleted);
CREATE INDEX IF NOT EXISTS idx_pf_brand ON product_families (brand_id);
CREATE INDEX IF NOT EXISTS idx_pf_factory ON product_families (factory_supplier_id);

-- ─────────────────────────────────────────────────
-- §4.2 products — SKU (11桁品番)
-- 色 × サイズの全組合せで 1 レコード (P-02 サイズ展開)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS products (
    id                    BIGSERIAL PRIMARY KEY,
    product_family_id     BIGINT       NOT NULL REFERENCES product_families(id),
    color_id              BIGINT       NOT NULL REFERENCES colors(id),
    size_id               BIGINT       NOT NULL REFERENCES sizes(id),
    sku                   VARCHAR(11)  NOT NULL UNIQUE,
    is_deleted            BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL,
    CONSTRAINT uq_products_family_color_size UNIQUE (product_family_id, color_id, size_id)
);
CREATE INDEX IF NOT EXISTS idx_products_family ON products (product_family_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_products_search ON products (sku) WHERE is_deleted = FALSE;

-- ─────────────────────────────────────────────────
-- §4.3 product_images — 商品画像 (S3 参照、Iteration 2 ではローカルファイル)
-- 企画単位で最大 5 枚 (BR-10)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_images (
    id                    BIGSERIAL PRIMARY KEY,
    product_family_id     BIGINT       NOT NULL REFERENCES product_families(id),
    s3_key                VARCHAR(512) NOT NULL,
    thumb_s3_key          VARCHAR(512) NULL,
    order_no              SMALLINT     NOT NULL,
    mime_type             VARCHAR(64)  NOT NULL,
    file_size_bytes       INTEGER      NOT NULL,
    width_px              INTEGER      NULL,
    height_px             INTEGER      NULL,
    original_filename     VARCHAR(255) NULL,
    is_deleted            BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    CONSTRAINT chk_pi_order_no CHECK (order_no BETWEEN 1 AND 5),
    CONSTRAINT chk_pi_file_size CHECK (file_size_bytes <= 5242880)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_pi_family_order
    ON product_images (product_family_id, order_no) WHERE is_deleted = FALSE;

-- ─────────────────────────────────────────────────
-- §4.4 product_supplier_prices — マルチ仕入先単価 (アイテム単位、履歴管理)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_supplier_prices (
    id                    BIGSERIAL PRIMARY KEY,
    product_family_id     BIGINT         NOT NULL REFERENCES product_families(id),
    supplier_id           BIGINT         NOT NULL REFERENCES suppliers(id),
    -- サイズ別仕入単価 (PR2、設計判断Q4=サイズ別必要)。
    --   NULL = 全サイズ共通の既定単価 (従来挙動、既存行は NULL のまま下位互換)。
    --   非NULL = そのサイズ専用単価 (既定をオーバーライド)。BR-04 有効日履歴は size 次元込みで維持。
    size_id               BIGINT         NULL     REFERENCES sizes(id),
    unit_price            NUMERIC(12,2)  NOT NULL,
    currency_code         CHAR(3)        NOT NULL DEFAULT 'JPY',
    exchange_rate         NUMERIC(10,4)  NULL,
    -- 旧 仕入コスト計算明細 項目 (Phase C、全 NULL 許容)
    estimate_unit_price   NUMERIC(12,2)  NULL,  -- 見積単価
    estimate_received_date DATE          NULL,  -- 見積単価受領日
    estimate_cost         NUMERIC(12,2)  NULL,  -- 見積原価
    estimate_margin_rate  NUMERIC(5,2)   NULL,  -- 見積利益率(%)
    purchase_cost         NUMERIC(12,2)  NULL,  -- 仕入原価
    purchase_margin_rate  NUMERIC(5,2)   NULL,  -- 仕入利益率(%)
    loss_cost             NUMERIC(12,2)  NULL,  -- ロス費
    drayage_cost          NUMERIC(12,2)  NULL,  -- ドレー代 (旧「トレー代」tray_cost、設計判断Q6 で名称統一)
    tax_rate              NUMERIC(5,2)   NULL,  -- 税率(%)
    effective_from        DATE           NOT NULL,
    effective_to          DATE           NULL,
    decided_at            DATE           NOT NULL,
    is_deleted            BOOLEAN        NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMP      NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT         NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMP      NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT         NOT NULL REFERENCES users(id),
    CONSTRAINT chk_psp_unit_price CHECK (unit_price > 0),
    CONSTRAINT chk_psp_effective_range CHECK (effective_to IS NULL OR effective_to > effective_from)
);
-- 一意制約に size を含める (PR2)。Postgres は NULL を distinct 扱いするため、size_id=NULL の
-- 既定行どうしの一意性が緩まないよう COALESCE(size_id, -1) 式インデックスを使う (移植性重視。
-- PG15+ の UNIQUE NULLS NOT DISTINCT は使わない)。sizes.id は BIGSERIAL ≥ 1 のため -1 は衝突しない。
CREATE UNIQUE INDEX IF NOT EXISTS uq_psp_family_supplier_size_from
    ON product_supplier_prices (product_family_id, supplier_id, COALESCE(size_id, -1), effective_from)
    WHERE is_deleted = FALSE;
-- 現単価解決 (family, supplier, size) の現在有効行ルックアップ + BR-04 履歴クローズ用の選択性確保 (PR2)。
CREATE INDEX IF NOT EXISTS idx_psp_family_current
    ON product_supplier_prices (product_family_id, supplier_id, COALESCE(size_id, -1), effective_from DESC)
    WHERE effective_to IS NULL AND is_deleted = FALSE;

-- ─────────────────────────────────────────────────
-- product_set_components — アソート/セット明細 (PR3、旧 spec No.37/38)
-- ある商品 (family) がアソート/セット品である場合の「子品番 + 数量」構成明細。
-- 商品の作成/更新時に全置換するコレクション (BOM / 色サイズ展開と同じパターン)。
-- child_item_number は手入力テキスト = products/product_families への FK は張らない
-- (旧システム品番/外部品番も許容するため、旧 spec の「手入力」に忠実)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_set_components (
    id                    BIGSERIAL PRIMARY KEY,
    product_family_id     BIGINT       NOT NULL REFERENCES product_families(id) ON DELETE CASCADE,
    child_item_number     VARCHAR(32)  NOT NULL,                                 -- 子品番 (手入力テキスト、spec No.37)
    quantity              INTEGER      NOT NULL,                                 -- 数量 (spec No.38)
    line_no               SMALLINT     NOT NULL DEFAULT 1,                       -- 表示順 (配列順で採番)
    created_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    CONSTRAINT chk_psc_quantity CHECK (quantity > 0)
);
CREATE INDEX IF NOT EXISTS idx_psc_family ON product_set_components (product_family_id);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 2 動作確認用に最小データ投入
-- (1 企画 × 2 色 × 3 サイズ = 6 SKU、単価 1 件)
-- ─────────────────────────────────────────────────
DO $$
DECLARE
    owner_id              BIGINT;
    pf_id                 BIGINT;
    type_id               BIGINT;
    season_id             BIGINT;
    factory_id            BIGINT;
    brand_id              BIGINT;
    func_id               BIGINT;
    group_id              BIGINT;
    material_cotton_id    BIGINT;
    color_brown_id        BIGINT;
    color_black_id        BIGINT;
    size_m_id             BIGINT;
    size_l_id             BIGINT;
    size_ll_id            BIGINT;
BEGIN
    SELECT id INTO owner_id FROM users WHERE login_id = 'owner';
    SELECT id INTO type_id  FROM product_types WHERE code = '001';
    SELECT id INTO season_id FROM product_seasons WHERE code = '001';
    SELECT id INTO factory_id FROM suppliers WHERE code = '336';
    SELECT id INTO brand_id FROM brands WHERE code = '001';
    SELECT id INTO func_id  FROM functions WHERE code = '001';
    SELECT id INTO group_id FROM product_groups WHERE code = '001';
    SELECT id INTO material_cotton_id FROM materials WHERE code = '001';
    SELECT id INTO color_brown_id FROM colors WHERE code = '040';
    SELECT id INTO color_black_id FROM colors WHERE code = '090';
    SELECT id INTO size_m_id FROM sizes WHERE code = '002';
    SELECT id INTO size_l_id FROM sizes WHERE code = '003';
    SELECT id INTO size_ll_id FROM sizes WHERE code = '004';

    -- 商品企画 1 件 (Phase 5 サンプル: 2026 年 (N) + 婦人吊込W底 (A) + 春夏 (1) + 連番 001 + 工場 A)
    INSERT INTO product_families (
        planned_year_code, product_type_id, product_season_id, sequence_no,
        factory_supplier_id, brand_id, function_id, product_group_id,
        upper_material_id, insole_material_id, outsole_material_id,
        product_name_1, product_name_2, status,
        created_by_user_id, updated_by_user_id
    ) VALUES (
        'N', type_id, season_id, '001',
        factory_id, brand_id, func_id, group_id,
        material_cotton_id, material_cotton_id, material_cotton_id,
        'デモ商品 春夏ベーシック', 'akebono プレミアム', 1,
        owner_id, owner_id
    ) ON CONFLICT DO NOTHING
    RETURNING id INTO pf_id;

    IF pf_id IS NULL THEN
        SELECT id INTO pf_id FROM product_families
        WHERE planned_year_code = 'N' AND product_type_id = type_id
          AND product_season_id = season_id AND sequence_no = '001'
          AND factory_supplier_id = factory_id;
    END IF;

    -- SKU 6 件 (ブラウン M/L/LL + ブラック M/L/LL)
    -- 11 桁品番: N(1) A(2) 1(3) 001(4-6) A(7) 40(8-9) M(10-11 ※ "110M" の末尾2桁)
    INSERT INTO products (product_family_id, color_id, size_id, sku, created_by_user_id, updated_by_user_id) VALUES
        (pf_id, color_brown_id, size_m_id,  'NA1001A4010', owner_id, owner_id),
        (pf_id, color_brown_id, size_l_id,  'NA1001A4011', owner_id, owner_id),
        (pf_id, color_brown_id, size_ll_id, 'NA1001A40X1', owner_id, owner_id),
        (pf_id, color_black_id, size_m_id,  'NA1001A9010', owner_id, owner_id),
        (pf_id, color_black_id, size_l_id,  'NA1001A9011', owner_id, owner_id),
        (pf_id, color_black_id, size_ll_id, 'NA1001A90X1', owner_id, owner_id)
    ON CONFLICT DO NOTHING;

    -- 単価 1 件 (effective_to NULL = 現在有効)
    INSERT INTO product_supplier_prices (
        product_family_id, supplier_id, unit_price, currency_code,
        effective_from, decided_at,
        created_by_user_id, updated_by_user_id
    ) VALUES (
        pf_id, factory_id, 1500.00, 'JPY',
        '2026-04-01', '2026-03-15',
        owner_id, owner_id
    ) ON CONFLICT DO NOTHING;
END $$;
