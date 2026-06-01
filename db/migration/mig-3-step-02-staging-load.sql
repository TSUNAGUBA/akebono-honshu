-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Step 2 (psql 手動取込フォールバック): \copy + 主要列抽出
-- ════════════════════════════════════════════════════════════════════
-- 通常は Backend 画面 (/admin/legacy-import) から取込むため、
-- 本ファイルは psql コマンドラインからのフォールバック用。
--
-- 前提: mig-3-step-02-staging.sql で Staging テーブル作成済
-- 実行:
--   1. CSV を UTF-8 に変換: iconv -f SHIFT_JIS -t UTF-8 products.csv > /tmp/legacy.csv
--   2. psql -d akebono -f mig-3-step-02-staging-load.sql
-- ════════════════════════════════════════════════════════════════════

\copy staging_legacy_products(c001,c002,c003,c004,c005,c006,c007,c008,c009,c010,c011,c012,c013,c014,c015,c016,c017,c018,c019,c020,c021,c022,c023,c024,c025,c026,c027,c028,c029,c030,c031,c032,c033,c034,c035,c036,c037,c038,c039,c040,c041,c042,c043,c044,c045,c046,c047,c048,c049,c050,c051,c052,c053,c054,c055,c056,c057,c058,c059,c060,c061,c062,c063,c064,c065,c066,c067,c068,c069,c070,c071,c072,c073,c074,c075,c076,c077,c078,c079,c080,c081,c082,c083,c084,c085,c086,c087,c088,c089,c090,c091,c092,c093,c094,c095,c096,c097,c098,c099,c100,c101,c102,c103,c104,c105,c106,c107,c108,c109,c110,c111,c112,c113,c114,c115,c116,c117,c118,c119,c120,c121,c122,c123,c124,c125,c126,c127,c128,c129,c130,c131,c132,c133,c134,c135,c136,c137,c138) FROM '/tmp/legacy_products_utf8.csv' WITH (FORMAT csv, HEADER true, QUOTE '"', DELIMITER ',', ENCODING 'UTF8');

-- 主要列を c001-c138 から抽出
BEGIN;
UPDATE staging_legacy_products
   SET legacy_sku          = NULLIF(TRIM(c001), ''),
       legacy_family_code  = NULLIF(TRIM(c002), ''),
       product_name_1      = NULLIF(TRIM(c003), ''),
       product_name_2      = NULLIF(TRIM(c004), ''),
       color_legacy        = NULLIF(TRIM(c007), ''),
       size_legacy         = NULLIF(TRIM(c010), ''),
       supplier_legacy     = NULLIF(TRIM(c017), ''),
       classification_1    = NULLIF(TRIM(c036), ''),
       classification_2    = NULLIF(TRIM(c037), ''),
       cost_unit_price     = NULLIF(TRIM(c064), '')::NUMERIC(12,2),
       purchase_unit_price = NULLIF(TRIM(c062), '')::NUMERIC(12,2),
       sales_unit_price    = NULLIF(TRIM(c060), '')::NUMERIC(12,2)
 WHERE legacy_sku IS NULL;
COMMIT;

SELECT COUNT(*) AS total_rows,
       COUNT(DISTINCT legacy_family_code) AS family_count,
       COUNT(*) FILTER (WHERE cost_unit_price > 0) AS rows_with_cost
  FROM staging_legacy_products;
