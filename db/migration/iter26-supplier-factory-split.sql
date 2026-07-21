-- ════════════════════════════════════════════════════════════════════
-- Iteration 26: 仕入先 / 工場 マスタ分離 (Part2) — factories 追加 + 既存 suppliers 複製 + FK 付け替え
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   従来 suppliers が「仕入先 (材料の仕入れ先)」「工場 (製造)」「発注先」を兼ねていた。
--   仕入先=材料調達 / 工場=製造 の別概念のためマスタを分離する:
--     - 工場マスタ (factories) を新設。品番7桁目の工場コード (item_conversion_code) 生成元。
--       product_families / production_instructions の factory_supplier_id はこの表を参照する。
--     - 仕入先マスタ (suppliers) は材料仕入先として残す。⑤仕入単価 / 発注書 発注先 / ドレー代 / 適用通貨。
--   移行方針 (ユーザ承認 2026-07-21): 既存 suppliers を「同一 id」で factories へ複製 (非破壊)。
--   これにより既存 product_families.factory_supplier_id (= supplier id) が factories(id) と一致し、
--   FK 付け替え後も参照が壊れない。適用通貨・ドレー代は工場に複製しない (仕入先固有)。
--
--   db/init/02-masters.sql (factories 定義+シード) / 03-products.sql / 05-production.sql (FK)
--   / 08-tenancy-rls.sql (RLS) に反映済だが、init は空 DB でのみ適用されるため、既に init 済の
--   本番 RDS には本マイグレーション (action=migrate) で追加適用する。
--
-- 冪等性 (CLAUDE.md 原則 2):
--   - CREATE TABLE / INDEX / POLICY は IF EXISTS / IF NOT EXISTS でガード。
--   - 複製 INSERT は ON CONFLICT (id) DO NOTHING (再実行で二重コピーしない)。
--   - FK 付け替えは DROP CONSTRAINT IF EXISTS → ADD (存在すれば付け替え、無ければ新設)。
--
-- 下位互換 (原則 7):
--   列 factory_supplier_id は互換のため名称維持 (参照先のみ suppliers → factories)。
--   既存の全 factory_supplier_id 値は複製済 factories に存在するため FK 検証を通過する。
--
-- RLS 注意:
--   suppliers/factories は FORCE ROW LEVEL SECURITY のため、管理ユーザが RLS をバイパスしない
--   環境 (BYPASSRLS を持たない RDS master 等) では GUC 未設定だと SELECT が 0 行になり複製が空になる。
--   これを避けるため、複製 (手順3) は tenant ごとに app.tenant_id を設定してから行う
--   (バイパスする環境でも ON CONFLICT で冪等なため安全)。DDL (手順1/2/4) は RLS の影響を受けない。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- 1. 工場マスタ (factories) — init 02-masters.sql と同一定義
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

-- 2. RLS + FORCE + tenant_isolation ポリシー + アプリロール GRANT (init 08-tenancy-rls.sql と同一)
ALTER TABLE factories ENABLE ROW LEVEL SECURITY;
ALTER TABLE factories FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON factories;
CREATE POLICY tenant_isolation ON factories
    USING (tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid)
    WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid);
-- 新規テーブルには ALL TABLES の一括 GRANT が届かないため明示的に付与する。
GRANT SELECT, INSERT, UPDATE, DELETE ON factories TO akebono_app;

-- 3. 既存 suppliers を「同一 id」で複製する。FORCE RLS 下で管理ロールが RLS をバイパスしない環境でも
--    確実にコピーするため、tenant ごとに app.tenant_id を設定してから複製する (トランザクション local)。
--    バイパスする環境でも ON CONFLICT (id) により冪等 (初回全件コピー→以降スキップ) で安全。
--    tenant 表は RLS 対象外 (レジストリ、読取のみ) のため列挙可能。
DO $$
DECLARE
    t RECORD;
BEGIN
    FOR t IN SELECT tenant_id FROM tenant LOOP
        PERFORM set_config('app.tenant_id', t.tenant_id::text, true);
        INSERT INTO factories (id, tenant_id, code, name, official_name, item_conversion_code,
                               country_id, supplier_type, alert_target, deleted_at,
                               created_at, created_by_user_id, updated_at, updated_by_user_id, legacy_id)
            SELECT id, tenant_id, code, name, official_name, item_conversion_code,
                   country_id, supplier_type, alert_target, deleted_at,
                   created_at, created_by_user_id, updated_at, updated_by_user_id, legacy_id
            FROM suppliers
        ON CONFLICT (id) DO NOTHING;
    END LOOP;
END $$;

-- 4. FK 付け替え: factory_supplier_id を suppliers → factories(id) へ。
--    列名 (factory_supplier_id) は互換のため維持。複製済のため全既存値が factories に存在する。
ALTER TABLE product_families DROP CONSTRAINT IF EXISTS product_families_factory_supplier_id_fkey;
ALTER TABLE product_families
    ADD CONSTRAINT product_families_factory_supplier_id_fkey
    FOREIGN KEY (factory_supplier_id) REFERENCES factories(id);

ALTER TABLE production_instructions DROP CONSTRAINT IF EXISTS production_instructions_factory_supplier_id_fkey;
ALTER TABLE production_instructions
    ADD CONSTRAINT production_instructions_factory_supplier_id_fkey
    FOREIGN KEY (factory_supplier_id) REFERENCES factories(id);

COMMIT;
