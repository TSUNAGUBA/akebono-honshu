-- ════════════════════════════════════════════════════════════════════
-- Iteration 24: 通貨マスタ新設 — currencies (為替/仕入先 適用通貨 の参照先)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   為替マスタの対象通貨、仕入先の適用通貨がこれまで自由入力文字列で、文字列一致の整合性が
--   人依存になっていた。通貨マスタ (currencies) を新設し、code = ISO 4217 3 文字コードを
--   一元管理する。為替マスタ・仕入先の適用通貨は本マスタの code をドロップダウンから選択する。
--   標準マスタ (code/name のみ) のため汎用 MasterService/CRUD をそのまま利用する。
--
--   db/init/02-masters.sql の CREATE TABLE + seed に反映済だが、db/init/*.sql は「空 DB の初期化」
--   でのみ適用されるため、既に init 済の本番 RDS には本マイグレーション (action=migrate) で追加適用する。
--   新規テーブル + seed のみ = 既存データ非破壊。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。run-migrations.sh が
--   db/migration/*.sql を find|sort で自動探索し、schema_migrations 台帳で二重適用を防止する (前進専用)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - テーブルは CREATE TABLE IF NOT EXISTS、seed は ON CONFLICT (code) DO NOTHING でガード。再実行可能。
--   - 下位互換 (原則7): 新規テーブル。既存の suppliers.currency_code / exchange_rates.currency_code は
--     文字列 code のままで、本マスタはその選択肢を提供する参照先 (soft reference、FK 制約は張らない)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

CREATE TABLE IF NOT EXISTS currencies (
    id                  BIGSERIAL PRIMARY KEY,
    code                VARCHAR(3)   NOT NULL UNIQUE,
    name                VARCHAR(255) NOT NULL,
    delete_flag         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    updated_at          TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id  BIGINT       NOT NULL REFERENCES users(id),
    legacy_id           VARCHAR(64)  NULL
);

-- seed (JPY/USD/CNY)。created_by/updated_by は既存の最小 user id を使う (FK 整合)。
INSERT INTO currencies (code, name, created_by_user_id, updated_by_user_id)
SELECT c.code, c.name, u.id, u.id
FROM (VALUES ('JPY', '日本円'), ('USD', '米ドル'), ('CNY', '人民元')) AS c(code, name)
CROSS JOIN (SELECT MIN(id) AS id FROM users) AS u
WHERE u.id IS NOT NULL
ON CONFLICT (code) DO NOTHING;

COMMIT;
