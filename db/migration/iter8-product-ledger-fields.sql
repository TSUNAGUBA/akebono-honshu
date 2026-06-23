-- ════════════════════════════════════════════════════════════════════
-- Iteration 8: 品番台帳 旧項目パリティ Phase A — product_families へ属性/価格/版権列を追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   品番台帳 (product_families) に旧システム由来の 11 項目 (商品年度 / 管理季節 /
--   企画者 / 仮番号 / サンプル合格日 / 小売価格 / 納品価格 / 企画費 / ブランド費 /
--   版権対象 / 版権料率) を追加する。これらは db/init/03-products.sql の
--   CREATE TABLE product_families に反映済だが、db/init/*.sql は「空 DB の初期化」
--   (run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter7 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS、制約追加は pg_constraint を引いた DO ブロックで
--     ガードしているため、再実行しても既存データ・既存スキーマを破壊しない (追加のみ)。
--   - 全列 NULL 許容。既存行は NULL のまま残り、下位互換を維持する。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 03-products.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── 列追加 (全 NULL 許容、ADD COLUMN IF NOT EXISTS で冪等) ──
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS product_year         SMALLINT      NULL;  -- 商品年度 (9999=通年)
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS management_season_id BIGINT        NULL;  -- 管理季節
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS planner_user_id      BIGINT        NULL;  -- 企画者
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS provisional_number   VARCHAR(64)   NULL;  -- 仮番号
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS sample_approval_date DATE          NULL;  -- サンプル合格日
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS retail_price         NUMERIC(12,2) NULL;  -- 小売価格
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS delivery_price       NUMERIC(12,2) NULL;  -- 納品価格
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS planning_cost        NUMERIC(12,2) NULL;  -- 企画費
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS brand_cost           NUMERIC(12,2) NULL;  -- ブランド費
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS royalty_target       SMALLINT      NULL;  -- 版権対象 (1=小売価格, 2=納品価格)
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS royalty_rate         NUMERIC(5,2)  NULL;  -- 版権料率(%)

-- ── 外部キー (管理季節 → product_seasons、企画者 → users)。
--    pg_constraint を引いて未存在のときのみ追加 (冪等)。NULL 許容のため任意参照。 ──
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_pf_management_season') THEN
        ALTER TABLE product_families
            ADD CONSTRAINT fk_pf_management_season
            FOREIGN KEY (management_season_id) REFERENCES product_seasons(id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_pf_planner_user') THEN
        ALTER TABLE product_families
            ADD CONSTRAINT fk_pf_planner_user
            FOREIGN KEY (planner_user_id) REFERENCES users(id);
    END IF;

    -- ── 版権対象の CHECK 制約 (1=小売価格, 2=納品価格、NULL は許容) ──
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_pf_royalty_target') THEN
        ALTER TABLE product_families
            ADD CONSTRAINT chk_pf_royalty_target
            CHECK (royalty_target IS NULL OR royalty_target IN (1,2));
    END IF;
END $$;

COMMIT;
