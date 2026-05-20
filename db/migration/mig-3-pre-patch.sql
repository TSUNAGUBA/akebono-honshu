-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Pre-Patch: DB スキーマ拡張 (旧データ取込前提)
-- ════════════════════════════════════════════════════════════════════
-- 目的: 既存生産管理システム CSV (1,288 行) 取込に必要な制約緩和
-- 実行: pgAdmin4 で本ファイルを step-01 より先に 1 回だけ実行
-- 冪等: ALTER COLUMN は同型再指定で no-op、IF NOT EXISTS で重複時スキップ
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- products.sku: 11 → 16 桁拡張 (旧 SKU "FA2071F" 等 7-8 桁の保持)
ALTER TABLE products ALTER COLUMN sku TYPE VARCHAR(16);
COMMENT ON COLUMN products.sku IS
  '11 桁 (新規企画) または旧 SKU (legacy import、最大 16 桁)。'
  'UNIQUE 制約は維持。新旧混在可。';

-- legacy_id インデックス追加 (取込後検証 SQL の高速化)
CREATE INDEX IF NOT EXISTS idx_products_legacy_id
  ON products (legacy_id) WHERE legacy_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_pf_legacy_id
  ON product_families (legacy_id) WHERE legacy_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_colors_legacy_id
  ON colors (legacy_id) WHERE legacy_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_sizes_legacy_id
  ON sizes (legacy_id) WHERE legacy_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_suppliers_legacy_id
  ON suppliers (legacy_id) WHERE legacy_id IS NOT NULL;

COMMIT;

-- 確認クエリ
SELECT column_name, data_type, character_maximum_length
  FROM information_schema.columns
 WHERE table_name = 'products' AND column_name = 'sku';
-- 期待: character_maximum_length = 16
