-- ════════════════════════════════════════════════════════════════════
-- Iteration 32: 関税率マスタ (customs_duty_rates) 追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   海外からの仕入（輸入）時に適用する関税率(%) を、原産国 × 素材分類（甲皮/中底/底）ごとに
--   正規管理する関税率マスタを新設する。商品新規ウィザード (/products/new) の⑤仕入単価で、
--   仕入先の国と選択素材から関税を自動算出（参照）するために使用する。
--   db/init/02-masters.sql (定義+シード) / 08-tenancy-rls.sql (RLS) に反映済だが、init は空 DB
--   でのみ適用されるため、既に init 済の本番 RDS には本マイグレーションで追加適用する。
--
-- 素材分類 3 列は NULL 許容 = ワイルドカード（その素材は不問）。原産国 (country_id) は必須。
-- duty_rate = 従価税率(%)、specific_duty_per_pair = 従量税(円/足, 任意。革製履物等で「高い方」を採用)。
--
-- 冪等性 (CLAUDE.md 原則 2):
--   CREATE TABLE / INDEX / POLICY は IF EXISTS / IF NOT EXISTS でガード。再実行安全。
--
-- 下位互換 (原則 7): 新規テーブルのみ = 既存データ非破壊。
--
-- シード: 税率は原産国・素材・EPA・関税割当により異なるため、本マイグレーションではシードしない
--   （画面から登録する）。fresh init では 02-masters が代表値（中国/ベトナムの目安）をシードする。
--
-- 適用方法 (自動・推奨): GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

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

ALTER TABLE customs_duty_rates ENABLE ROW LEVEL SECURITY;
ALTER TABLE customs_duty_rates FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON customs_duty_rates;
CREATE POLICY tenant_isolation ON customs_duty_rates
    USING (tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid)
    WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid);
GRANT SELECT, INSERT, UPDATE, DELETE ON customs_duty_rates TO akebono_app;

COMMIT;
