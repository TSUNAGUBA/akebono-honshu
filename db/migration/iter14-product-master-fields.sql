-- ════════════════════════════════════════════════════════════════════
-- Iteration 14: 商品マスタ 旧項目パリティ追補 (PR1) — 備考 2 列追加 + ドレー代 名称統一
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧システム項目定義を正として「商品マスタ」の差異を埋める PR1。本マイグレーションは
--   2 種類のスキーマ変更を適用する:
--     (1) product_families に 備考 2 列を追加 (全 NULL 許容):
--           - remark        (商品本体 備考、spec No.39)
--           - color_remark  (備考（色）、spec No.33、色ごとではなく商品単位の単一テキスト)
--     (2) product_supplier_prices.tray_cost を drayage_cost にリネーム
--           (旧「トレー代」→「ドレー代」、設計判断Q6 で名称統一。データは保持)。
--   登録者/最終更新者の画面表示 (spec No.27/28) は既存列 (created_by_user_id /
--   updated_by_user_id) からの導出のため、本マイグレーションでの列追加は不要。
--
--   これらは db/init/03-products.sql の CREATE TABLE に反映済だが、db/init/*.sql は
--   「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter13 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS でガードしているため、再実行しても既存データ・
--     既存スキーマを破壊しない (追加のみ)。両列は全 NULL 許容、既存行は NULL のまま下位互換。
--   - リネームは information_schema を引いた DO ブロックで「tray_cost が存在し
--     drayage_cost が無い場合のみ」実行するため冪等。再実行しても二重リネーム・
--     エラーにならない。RENAME COLUMN はデータを保持する (DROP+ADD ではない)。
--   - 下位互換 (原則7): 既存の tray_cost 値は drayage_cost にそのまま引き継がれる。
--     アプリ側 (Entity DrayageCost / DbContext HasColumnName("drayage_cost")) も同 PR で
--     更新済のため、リネーム後に列名不一致は発生しない。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 03-products.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── (1) product_families 備考 2 列追加 (全 NULL 許容、ADD COLUMN IF NOT EXISTS で冪等) ──
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS remark       TEXT NULL;  -- 商品本体 備考 (spec No.39)
ALTER TABLE product_families ADD COLUMN IF NOT EXISTS color_remark TEXT NULL;  -- 備考（色）(spec No.33、商品単位の単一テキスト)

-- ── (2) product_supplier_prices.tray_cost → drayage_cost リネーム (設計判断Q6)。
--    information_schema を引いて「旧列が存在し かつ 新列が未存在」の場合のみ実行 (冪等)。
--    RENAME COLUMN はデータを保持するため、既存 tray_cost 値は drayage_cost に引き継がれる。 ──
DO $$
BEGIN
    -- table_schema='public' で限定 (iter4-tz-to-jst-naive.sql と同じ作法)。MIG-3 等で他スキーマに
    -- 同名テーブルが存在しても本番 public スキーマの列のみを判定対象にし、誤判定を防ぐ。
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'product_supplier_prices' AND column_name = 'tray_cost'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'product_supplier_prices' AND column_name = 'drayage_cost'
    ) THEN
        ALTER TABLE product_supplier_prices RENAME COLUMN tray_cost TO drayage_cost;
    END IF;
END $$;

COMMIT;
