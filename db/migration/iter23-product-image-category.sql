-- ════════════════════════════════════════════════════════════════════
-- Iteration 23: 商品画像の企画/本番区分 — product_images へ image_category 追加 (§2a)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   商品画像を「企画画像 (image_category=0)」「本番画像 (image_category=1)」の 2 区分で登録できるようにする。
--   画像利用シーンの代表画像は「本番画像があれば本番の order_no 最小、無ければ企画の order_no 最小」で選ぶ。
--   order_no の 1〜5 上限・一意は区分ごとに独立させるため、一意索引に image_category を加える。
--
--   これは db/init/03-products.sql の CREATE TABLE に反映済だが、db/init/*.sql は「空 DB の初期化」でのみ
--   適用されるため、既に init 済の本番 RDS には本マイグレーション (action=migrate) で追加適用する。
--   新規列 + 索引の張り替えのみ = 既存データ非破壊 (既存画像は image_category=0 = 企画扱いで下位互換)。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。run-migrations.sh が
--   db/migration/*.sql を find|sort で自動探索し、schema_migrations 台帳で二重適用を防止する (前進専用)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS、索引は DROP/CREATE ... IF (NOT) EXISTS、CHECK は pg_constraint
--     を確認してから追加するためすべて再実行可能。
--   - 下位互換 (原則7): image_category は NOT NULL DEFAULT 0 のため既存画像は企画扱い。既存の
--     order_no はすべて image_category=0 のため、新旧索引で一意制約に矛盾は生じない。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── 画像区分 (§2a)。ADD COLUMN IF NOT EXISTS で冪等。 ──
ALTER TABLE product_images ADD COLUMN IF NOT EXISTS image_category SMALLINT NOT NULL DEFAULT 0;

-- ── CHECK (image_category IN (0,1)) を未追加のときだけ追加。 ──
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_pi_image_category') THEN
        ALTER TABLE product_images ADD CONSTRAINT chk_pi_image_category CHECK (image_category IN (0, 1));
    END IF;
END $$;

-- ── 一意索引を区分別へ張り替え。旧 (family, order_no) を落とし (family, category, order_no) を作る。 ──
DROP INDEX IF EXISTS uq_pi_family_order;
CREATE UNIQUE INDEX IF NOT EXISTS uq_pi_family_category_order
    ON product_images (product_family_id, image_category, order_no) WHERE is_deleted = FALSE;

COMMIT;
