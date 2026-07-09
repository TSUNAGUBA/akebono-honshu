-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Pre-Patch: DB スキーマ拡張 (旧データ取込前提)
-- ════════════════════════════════════════════════════════════════════
-- 目的: 既存生産管理システム CSV (1,288 行) 取込に必要な制約緩和
-- 実行: LegacyImportService (UI /admin/legacy-import) が step-01 より先に実行する
-- 冪等: 拡張済みなら完全 no-op
--
-- プラットフォーム統合改修:
--   本パッチの内容 (sku VARCHAR(16) / legacy_id 索引) は db/init に正式反映済み。
--   本ファイルは init 反映前の旧 DB への後方互換フォールバックとしてのみ残す。
--   アプリ接続ロール akebono_app はテーブル所有者ではないため、拡張済み DB では
--   ALTER TABLE 自体を実行しない (条件付き DO ブロック)。init 適用済みの DB では
--   全ステートメントが no-op になり権限エラーを起こさない。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- products.sku: 11 → 16 桁拡張 (旧 SKU "FA2071F" 等 7-8 桁の保持)。
-- 既に 16 桁なら ALTER を発行しない (所有権が不要になり akebono_app でも通る)。
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'products'
          AND column_name = 'sku' AND character_maximum_length < 16
    ) THEN
        ALTER TABLE products ALTER COLUMN sku TYPE VARCHAR(16);
        COMMENT ON COLUMN products.sku IS
            '11 桁 (新規企画) または旧 SKU (legacy import、最大 16 桁)。UNIQUE 制約 (tenant_id, sku) は維持。新旧混在可。';
    END IF;
END $$;

-- legacy_id インデックス追加 (取込後検証 SQL の高速化)。
-- 注意: CREATE INDEX IF NOT EXISTS は存在チェックの前にテーブル所有権を検査するため
-- (実測 PostgreSQL 16)、非所有者 (akebono_app) でも no-op で通るよう pg_indexes の
-- 存在確認を先に行う条件付き DO ブロックにする。init 適用済み DB では全てスキップされる。
DO $$
DECLARE
    idx RECORD;
BEGIN
    FOR idx IN
        SELECT * FROM (VALUES
            ('idx_products_legacy_id',  'products'),
            ('idx_pf_legacy_id',        'product_families'),
            ('idx_colors_legacy_id',    'colors'),
            ('idx_sizes_legacy_id',     'sizes'),
            ('idx_suppliers_legacy_id', 'suppliers')
        ) AS t(index_name, table_name)
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_indexes
            WHERE schemaname = 'public' AND indexname = idx.index_name
        ) THEN
            EXECUTE format(
                'CREATE INDEX %I ON %I (legacy_id) WHERE legacy_id IS NOT NULL',
                idx.index_name, idx.table_name);
        END IF;
    END LOOP;
END $$;

COMMIT;
