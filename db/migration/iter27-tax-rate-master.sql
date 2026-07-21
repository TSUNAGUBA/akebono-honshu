-- ════════════════════════════════════════════════════════════════════
-- Iteration 27: 税率マスタ (tax_rates) 追加 (Part5)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   税区分ごとの税率(%) を正規管理する税率マスタを新設する (商品⑤仕入単価などで参照)。
--   db/init/02-masters.sql (定義+シード) / 08-tenancy-rls.sql (RLS) に反映済だが、init は空 DB
--   でのみ適用されるため、既に init 済の本番 RDS には本マイグレーションで追加適用する。
--
-- 冪等性 (CLAUDE.md 原則 2):
--   CREATE TABLE / INDEX / POLICY は IF EXISTS / IF NOT EXISTS でガード。再実行安全。
--
-- 下位互換 (原則 7): 新規テーブルのみ = 既存データ非破壊。
--
-- シード: 税率は税制・テナントにより異なるため本マイグレーションではシードしない
--   (画面から登録する)。fresh init では 02-masters が標準/軽減/非課税をシードする。
--
-- 適用方法 (自動・推奨): GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

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

ALTER TABLE tax_rates ENABLE ROW LEVEL SECURITY;
ALTER TABLE tax_rates FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tax_rates;
CREATE POLICY tenant_isolation ON tax_rates
    USING (tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid)
    WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid);
GRANT SELECT, INSERT, UPDATE, DELETE ON tax_rates TO akebono_app;

COMMIT;
