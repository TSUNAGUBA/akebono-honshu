-- Iteration 1: 17 マスタ + users 拡張
-- Phase 5 data-design.md §3.1-3.18
-- pgAdmin4 で akebono_honshu DB に対して実行 (docker 利用者は再構築で自動投入)

SET TIMEZONE = 'Asia/Tokyo';

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
    ADD COLUMN IF NOT EXISTS created_by_user_id              BIGINT       NULL,
    ADD COLUMN IF NOT EXISTS updated_by_user_id              BIGINT       NULL,
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
    id                    BIGSERIAL PRIMARY KEY,
    code                  VARCHAR(3)   NOT NULL UNIQUE,
    name                  VARCHAR(255) NOT NULL,
    item_conversion_code  VARCHAR(4)   NOT NULL,
    delete_flag           BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sizes_conv_active ON sizes (item_conversion_code) WHERE delete_flag = FALSE;

-- ─────────────────────────────────────────────────
-- §3.2 brands — ブランドマスタ (拡張カラムなし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS brands (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.3 functions — 機能マスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS functions (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.4 countries — 国マスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS countries (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.5 suppliers — 仕入先マスタ (工場兼用、F-22 official_name 帳票印字)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS suppliers (
    id                    BIGSERIAL PRIMARY KEY,
    code                  VARCHAR(3)   NOT NULL UNIQUE,
    name                  VARCHAR(255) NOT NULL,
    official_name         VARCHAR(255) NULL,
    item_conversion_code  CHAR(1)      NOT NULL,
    country_id            BIGINT       NOT NULL REFERENCES countries(id),
    supplier_type         SMALLINT     NOT NULL,
    alert_target          SMALLINT     NOT NULL DEFAULT 0,
    delete_flag           BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL
);
CREATE INDEX IF NOT EXISTS idx_suppliers_country ON suppliers (country_id);
CREATE INDEX IF NOT EXISTS idx_suppliers_active ON suppliers (delete_flag) WHERE delete_flag = FALSE;

-- ─────────────────────────────────────────────────
-- §3.6 departments — 事業部マスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS departments (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.7 product_types — 商品タイプマスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_types (
    id                       BIGSERIAL PRIMARY KEY,
    code                     VARCHAR(3)   NOT NULL UNIQUE,
    name                     VARCHAR(255) NOT NULL,
    item_conversion_code     CHAR(1)      NOT NULL,
    size_demographic_code    CHAR(1)      NOT NULL,
    delete_flag              BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id       BIGINT       NOT NULL REFERENCES users(id),
    updated_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id       BIGINT       NOT NULL REFERENCES users(id),
    legacy_id                VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.8 product_seasons — 商品季節マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_seasons (
    id                    BIGSERIAL PRIMARY KEY,
    code                  VARCHAR(3)   NOT NULL UNIQUE,
    name                  VARCHAR(255) NOT NULL,
    item_conversion_code  CHAR(1)      NOT NULL,
    conversion_order      VARCHAR(64)  NULL,
    delete_flag           BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.9 product_groups — 商品群マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS product_groups (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)    NOT NULL UNIQUE,
    name                VARCHAR(255)  NOT NULL,
    planning_fee        NUMERIC(12,2) NOT NULL DEFAULT 0,
    delete_flag         BOOLEAN       NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT        NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT        NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)   NULL
);

-- ─────────────────────────────────────────────────
-- §3.10 colors — 色マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS colors (
    id                    BIGSERIAL PRIMARY KEY,
    code                  VARCHAR(3)   NOT NULL UNIQUE,
    name                  VARCHAR(255) NOT NULL,
    item_conversion_code  CHAR(2)      NOT NULL,
    delete_flag           BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    legacy_id             VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.12 material_classifications — 素材分類マスタ (materials の FK 参照先、先に作る)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS material_classifications (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.11 materials — 素材マスタ
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS materials (
    id                              BIGSERIAL PRIMARY KEY,
    code                            VARCHAR(3)   NOT NULL UNIQUE,
    name                            VARCHAR(255) NOT NULL,
    material_classification_id      BIGINT       NOT NULL REFERENCES material_classifications(id),
    delete_flag                     BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id              BIGINT       NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id              BIGINT       NOT NULL REFERENCES users(id),
    legacy_id                       VARCHAR(64)  NULL
);
CREATE INDEX IF NOT EXISTS idx_materials_classification ON materials (material_classification_id);

-- ─────────────────────────────────────────────────
-- §3.13 warehouses — 倉庫コードマスタ (拡張なし)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouses (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.14 delivery_destinations — 納品先マスタ (F-22 customer_name は内部識別用)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS delivery_destinations (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    customer_name       VARCHAR(255) NULL,
    remark_1            VARCHAR(255) NULL,
    remark_2            VARCHAR(255) NULL,
    remark_3            VARCHAR(255) NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.15 document_template_purchases — 連絡文書定型・発注書
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_template_purchases (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    body                TEXT         NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.16 document_template_confirmations — 連絡文章・確認表
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_template_confirmations (
    id                   BIGSERIAL PRIMARY KEY,
    code                 VARCHAR(3)   NOT NULL UNIQUE,
    name                 VARCHAR(255) NOT NULL,
    body                 TEXT         NOT NULL,
    standard_print_flag  BOOLEAN      NOT NULL DEFAULT FALSE,
    delete_flag          BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id   BIGINT       NOT NULL REFERENCES users(id),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id   BIGINT       NOT NULL REFERENCES users(id),
    legacy_id            VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- §3.17 document_text_purchases — 連絡文章・発注書
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_text_purchases (
    id                   BIGSERIAL PRIMARY KEY,
    code                 VARCHAR(3)   NOT NULL UNIQUE,
    name                 VARCHAR(255) NOT NULL,
    body                 TEXT         NOT NULL,
    standard_print_flag  BOOLEAN      NOT NULL DEFAULT FALSE,
    delete_flag          BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_user_id   BIGINT       NOT NULL REFERENCES users(id),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_user_id   BIGINT       NOT NULL REFERENCES users(id),
    legacy_id            VARCHAR(64)  NULL
);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 1 動作確認用に最小データ投入
-- (FK 整合のため owner ユーザを created_by/updated_by に使用)
-- ─────────────────────────────────────────────────
DO $$
DECLARE
    owner_id BIGINT;
    jp_id    BIGINT;
    cn_id    BIGINT;
    mc_natural BIGINT;
BEGIN
    SELECT id INTO owner_id FROM users WHERE login_id = 'owner';
    IF owner_id IS NULL THEN
        RAISE EXCEPTION 'owner user not found, run 01-schema.sql first';
    END IF;

    -- countries (suppliers の FK 参照先)
    INSERT INTO countries (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '日本',     owner_id, owner_id),
        ('002', '中国',     owner_id, owner_id),
        ('003', 'ベトナム', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    SELECT id INTO jp_id FROM countries WHERE code = '001';
    SELECT id INTO cn_id FROM countries WHERE code = '002';

    -- material_classifications (materials の FK 参照先)
    INSERT INTO material_classifications (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '天然素材',   owner_id, owner_id),
        ('002', '合成素材',   owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    SELECT id INTO mc_natural FROM material_classifications WHERE code = '001';

    -- brands
    INSERT INTO brands (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'akebono',  owner_id, owner_id),
        ('002', 'プライベート', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- functions
    INSERT INTO functions (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '通常',     owner_id, owner_id),
        ('002', '防水',     owner_id, owner_id),
        ('003', '防臭',     owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- departments
    INSERT INTO departments (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '第1事業部', owner_id, owner_id),
        ('002', '第2事業部', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- product_types
    INSERT INTO product_types (code, name, item_conversion_code, size_demographic_code, created_by_user_id, updated_by_user_id) VALUES
        ('001', '吊込W底婦人', 'A', 'R', owner_id, owner_id),
        ('002', '吊込W底紳士', 'A', 'M', owner_id, owner_id),
        ('003', 'クッション',  'C', 'R', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- product_seasons
    INSERT INTO product_seasons (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('001', '春夏', '1', owner_id, owner_id),
        ('002', '秋冬', '2', owner_id, owner_id),
        ('003', '通年', '0', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- product_groups
    INSERT INTO product_groups (code, name, planning_fee, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'ベーシック', 1000.00, owner_id, owner_id),
        ('002', 'プレミアム', 2500.00, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- colors
    INSERT INTO colors (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('030', 'ブルー',  '30', owner_id, owner_id),
        ('040', 'ブラウン', '40', owner_id, owner_id),
        ('080', 'グレー',  '80', owner_id, owner_id),
        ('090', 'ブラック', '90', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- sizes
    INSERT INTO sizes (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'S',  '110S', owner_id, owner_id),
        ('002', 'M',  '110M', owner_id, owner_id),
        ('003', 'L',  '110L', owner_id, owner_id),
        ('004', 'LL', '110X', owner_id, owner_id),
        ('005', 'フリー', 'AS', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- materials
    INSERT INTO materials (code, name, material_classification_id, created_by_user_id, updated_by_user_id) VALUES
        ('001', '綿',       mc_natural, owner_id, owner_id),
        ('002', 'ポリエステル', (SELECT id FROM material_classifications WHERE code='002'), owner_id, owner_id),
        ('003', '麻',       mc_natural, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- warehouses
    INSERT INTO warehouses (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('007', '本社倉庫',     owner_id, owner_id),
        ('717', 'しまむらセンター', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- suppliers (F-22 sample: official_name=DEPARTURES, code=336)
    INSERT INTO suppliers (code, name, official_name, item_conversion_code, country_id, supplier_type, created_by_user_id, updated_by_user_id) VALUES
        ('336', 'デパーチャーズ',         'DEPARTURES',     'A', jp_id, 0, owner_id, owner_id),
        ('404', '安徽拓馳鞋業有限公司',     'AN-HUI TUO-CHI', 'B', cn_id, 1, owner_id, owner_id),
        ('437', '南通本州貿易有限公司',     'NAN-TONG HONSHU','C', cn_id, 1, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- delivery_destinations (Phase 6 サンプル準拠)
    INSERT INTO delivery_destinations (code, name, customer_name, remark_1, remark_2, remark_3, created_by_user_id, updated_by_user_id) VALUES
        ('001', 'しまむらセンター', 'しまむら', '埼玉県さいたま市', '048-XXX-XXXX', '048-XXX-XXXY', owner_id, owner_id),
        ('002', 'AEONセンター',    'AEON',    '千葉県千葉市',   '043-XXX-XXXX', '043-XXX-XXXY', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- document_template_purchases
    INSERT INTO document_template_purchases (code, name, body, created_by_user_id, updated_by_user_id) VALUES
        ('001', '標準発注', '発注書を作成いたします。よろしくお願いいたします。', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- document_template_confirmations
    INSERT INTO document_template_confirmations (code, name, body, standard_print_flag, created_by_user_id, updated_by_user_id) VALUES
        ('001', '確認表標準', 'ご確認のほどよろしくお願いいたします。', TRUE, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- document_text_purchases
    INSERT INTO document_text_purchases (code, name, body, standard_print_flag, created_by_user_id, updated_by_user_id) VALUES
        ('001', '注意書き標準', '分納、遅納、訂正等は、商品管理部宛 FAX:03-5850-4720 までご連絡ください。', TRUE, owner_id, owner_id),
        ('002', '納品書記載',   '納品書には発注番号を記載してください。発注番号がないものは支払対象になっておりません。', TRUE, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

END $$;
