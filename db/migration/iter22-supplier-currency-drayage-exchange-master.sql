-- ════════════════════════════════════════════════════════════════════
-- Iteration 22: 仕入先の適用通貨/ドレー代 + 為替マスタ新設 (§2f / §2i)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   商品⑤仕入単価の高度化に伴い、以下を追加する。
--     - suppliers.currency_code (§2f): 仕入先の適用通貨。商品⑤で選択した仕入先の通貨で為替マスタの
--       適用レート・円換算を解決する。既定 'JPY' (国内仕入先は円建て)。
--     - suppliers.drayage_cost (§2i): 仕入先ごとのドレー代。商品⑤仕入単価行へ自動反映する既定値。
--       「ドレー代設定マスタメンテナンス = 仕入先ごとの本項目」(壁打ち結果: ドレー代は仕入先ごと)。
--     - exchange_rates (§2f、新テーブル): 年月 (YYYY-MM) × 通貨ごとの対円レートを保持する為替マスタ。
--       code/name を持たない bespoke master (M-04 仕入先と同様の個別対応)。
--
--   これらは db/init/02-masters.sql の CREATE TABLE / ALTER に反映済だが、db/init/*.sql は「空 DB の
--   初期化」でのみ適用されるため、既に init 済の本番 RDS には本マイグレーション (action=migrate) で
--   追加適用する。新規列・新規テーブルのみ = 既存データ非破壊。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。run-migrations.sh が
--   db/migration/*.sql を find|sort で自動探索し、schema_migrations 台帳で二重適用を防止する (前進専用)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS、テーブル/索引は IF NOT EXISTS でガード。再実行しても追加のみ。
--   - 下位互換 (原則7): currency_code は NOT NULL DEFAULT 'JPY' のため既存仕入先は自動的に円建て扱い。
--     drayage_cost は NULL 許容。exchange_rates は新規テーブルのため既存データに影響しない。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── 仕入先 適用通貨 (§2f) / ドレー代 (§2i)。ADD COLUMN IF NOT EXISTS で冪等。 ──
ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS currency_code CHAR(3)       NOT NULL DEFAULT 'JPY'; -- 適用通貨
ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS drayage_cost  NUMERIC(12,2) NULL;                   -- ドレー代 (仕入先ごと)

-- ── 為替マスタ (§2f、新テーブル)。年月 × 通貨ごとの対円レート。 ──
CREATE TABLE IF NOT EXISTS exchange_rates (
    id                   BIGSERIAL PRIMARY KEY,
    year_month           CHAR(7)      NOT NULL,
    currency_code        CHAR(3)      NOT NULL,
    rate                 NUMERIC(12,4) NOT NULL,
    delete_flag          BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at           TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id   BIGINT       NOT NULL REFERENCES users(id),
    updated_at           TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id   BIGINT       NOT NULL REFERENCES users(id),
    legacy_id            VARCHAR(64)  NULL,
    CONSTRAINT chk_exr_year_month CHECK (year_month ~ '^[0-9]{4}-[0-9]{2}$'),
    CONSTRAINT chk_exr_rate CHECK (rate > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_exr_year_month_currency
    ON exchange_rates (year_month, currency_code) WHERE delete_flag = FALSE;

COMMIT;
