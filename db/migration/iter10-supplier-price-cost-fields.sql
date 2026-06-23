-- ════════════════════════════════════════════════════════════════════
-- Iteration 10: 仕入コスト計算明細 旧項目パリティ Phase C — product_supplier_prices へ原価明細 9 項目を追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   マルチ仕入先単価 (product_supplier_prices) に旧システム由来の 仕入コスト計算明細 9 項目
--   (見積単価 / 見積単価受領日 / 見積原価 / 見積利益率 / 仕入原価 / 仕入利益率 /
--    ロス費 / トレー代 / 税率) を追加する。これらは db/init/03-products.sql の
--   CREATE TABLE product_supplier_prices に反映済だが、db/init/*.sql は「空 DB の初期化」
--   (run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。
--   (注: 為替レート=exchange_rate / 仕入単価=unit_price / 仕入単価決定日=decided_at は
--    既存列のため再追加しない。本 Phase C は原価明細 9 列のみを対象とする。)
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter9 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS でガードしているため、再実行しても既存データ・
--     既存スキーマを破壊しない (追加のみ)。本 Phase C は全列が plain numeric/date のため
--     外部キー・CHECK 制約は不要 (iter8/iter9 のような DO ブロックは持たない)。
--   - 全列 NULL 許容。既存行は NULL のまま残り、下位互換を維持する。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 03-products.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── product_supplier_prices 列追加 (全 NULL 許容、ADD COLUMN IF NOT EXISTS で冪等) ──
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS estimate_unit_price    NUMERIC(12,2) NULL;  -- 見積単価
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS estimate_received_date DATE          NULL;  -- 見積単価受領日
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS estimate_cost          NUMERIC(12,2) NULL;  -- 見積原価
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS estimate_margin_rate   NUMERIC(5,2)  NULL;  -- 見積利益率(%)
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS purchase_cost          NUMERIC(12,2) NULL;  -- 仕入原価
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS purchase_margin_rate   NUMERIC(5,2)  NULL;  -- 仕入利益率(%)
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS loss_cost              NUMERIC(12,2) NULL;  -- ロス費
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS tray_cost              NUMERIC(12,2) NULL;  -- トレー代
ALTER TABLE product_supplier_prices ADD COLUMN IF NOT EXISTS tax_rate               NUMERIC(5,2)  NULL;  -- 税率(%)

COMMIT;
