-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Step 2: Staging テーブル + CSV COPY
-- ════════════════════════════════════════════════════════════════════
-- 目的: 既存生産管理システム CSV (1,288 行 × 138 列) を Staging テーブルに
--       取込み、本テーブル INSERT 用の元データを準備
--
-- 前提: step-01 (マスタ補完) 完了
--
-- 実行手順:
--   1. CSV を UTF-8 に変換 (元は SHIFT_JIS)
--        $ iconv -f SHIFT_JIS -t UTF-8 products.csv > /tmp/legacy_products_utf8.csv
--   2. CSV を PostgreSQL サーバから読める場所に配置
--   3. psql で本ファイルを実行
--        $ psql -h localhost -U postgres -d akebono -f mig-3-step-02-staging.sql
--      (pgAdmin4 GUI 利用時は §D の \copy を Query Tool ではなく psql で実行)
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ────────────────────────────────────────────────────────────────────
-- §A Staging テーブル定義 (138 列を全て TEXT で素直に取込)
-- ────────────────────────────────────────────────────────────────────
DROP TABLE IF EXISTS staging_legacy_products CASCADE;
CREATE TABLE staging_legacy_products (
    row_no               SERIAL PRIMARY KEY,
    -- 主要 12 列 (取込時は NULL、§C で raw から抽出)
    legacy_sku           TEXT,
    legacy_family_code   TEXT,
    product_name_1       TEXT,
    product_name_2       TEXT,
    color_legacy         TEXT,
    size_legacy          TEXT,
    supplier_legacy      TEXT,
    classification_1     TEXT,
    classification_2     TEXT,
    cost_unit_price      NUMERIC(12,2),
    purchase_unit_price  NUMERIC(12,2),
    sales_unit_price     NUMERIC(12,2),
    -- 138 列を素直に保持 (デバッグ + 業務担当者後参照用)
    c001 TEXT,c002 TEXT,c003 TEXT,c004 TEXT,c005 TEXT,c006 TEXT,c007 TEXT,c008 TEXT,c009 TEXT,c010 TEXT,
    c011 TEXT,c012 TEXT,c013 TEXT,c014 TEXT,c015 TEXT,c016 TEXT,c017 TEXT,c018 TEXT,c019 TEXT,c020 TEXT,
    c021 TEXT,c022 TEXT,c023 TEXT,c024 TEXT,c025 TEXT,c026 TEXT,c027 TEXT,c028 TEXT,c029 TEXT,c030 TEXT,
    c031 TEXT,c032 TEXT,c033 TEXT,c034 TEXT,c035 TEXT,c036 TEXT,c037 TEXT,c038 TEXT,c039 TEXT,c040 TEXT,
    c041 TEXT,c042 TEXT,c043 TEXT,c044 TEXT,c045 TEXT,c046 TEXT,c047 TEXT,c048 TEXT,c049 TEXT,c050 TEXT,
    c051 TEXT,c052 TEXT,c053 TEXT,c054 TEXT,c055 TEXT,c056 TEXT,c057 TEXT,c058 TEXT,c059 TEXT,c060 TEXT,
    c061 TEXT,c062 TEXT,c063 TEXT,c064 TEXT,c065 TEXT,c066 TEXT,c067 TEXT,c068 TEXT,c069 TEXT,c070 TEXT,
    c071 TEXT,c072 TEXT,c073 TEXT,c074 TEXT,c075 TEXT,c076 TEXT,c077 TEXT,c078 TEXT,c079 TEXT,c080 TEXT,
    c081 TEXT,c082 TEXT,c083 TEXT,c084 TEXT,c085 TEXT,c086 TEXT,c087 TEXT,c088 TEXT,c089 TEXT,c090 TEXT,
    c091 TEXT,c092 TEXT,c093 TEXT,c094 TEXT,c095 TEXT,c096 TEXT,c097 TEXT,c098 TEXT,c099 TEXT,c100 TEXT,
    c101 TEXT,c102 TEXT,c103 TEXT,c104 TEXT,c105 TEXT,c106 TEXT,c107 TEXT,c108 TEXT,c109 TEXT,c110 TEXT,
    c111 TEXT,c112 TEXT,c113 TEXT,c114 TEXT,c115 TEXT,c116 TEXT,c117 TEXT,c118 TEXT,c119 TEXT,c120 TEXT,
    c121 TEXT,c122 TEXT,c123 TEXT,c124 TEXT,c125 TEXT,c126 TEXT,c127 TEXT,c128 TEXT,c129 TEXT,c130 TEXT,
    c131 TEXT,c132 TEXT,c133 TEXT,c134 TEXT,c135 TEXT,c136 TEXT,c137 TEXT,c138 TEXT,
    imported_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMIT;

-- ────────────────────────────────────────────────────────────────────
-- §B CSV 取込 (psql から \copy を実行)
-- ────────────────────────────────────────────────────────────────────
-- 注意: \copy はクライアントサイドで実行されるため、ファイルパスは psql 実行
-- マシンから見える絶対パス。pgAdmin4 Query Tool では実行不可。

\copy staging_legacy_products(c001,c002,c003,c004,c005,c006,c007,c008,c009,c010,c011,c012,c013,c014,c015,c016,c017,c018,c019,c020,c021,c022,c023,c024,c025,c026,c027,c028,c029,c030,c031,c032,c033,c034,c035,c036,c037,c038,c039,c040,c041,c042,c043,c044,c045,c046,c047,c048,c049,c050,c051,c052,c053,c054,c055,c056,c057,c058,c059,c060,c061,c062,c063,c064,c065,c066,c067,c068,c069,c070,c071,c072,c073,c074,c075,c076,c077,c078,c079,c080,c081,c082,c083,c084,c085,c086,c087,c088,c089,c090,c091,c092,c093,c094,c095,c096,c097,c098,c099,c100,c101,c102,c103,c104,c105,c106,c107,c108,c109,c110,c111,c112,c113,c114,c115,c116,c117,c118,c119,c120,c121,c122,c123,c124,c125,c126,c127,c128,c129,c130,c131,c132,c133,c134,c135,c136,c137,c138) FROM '/tmp/legacy_products_utf8.csv' WITH (FORMAT csv, HEADER true, QUOTE '"', DELIMITER ',', ENCODING 'UTF8');

-- ────────────────────────────────────────────────────────────────────
-- §C 取込後の主要列抽出 (c001-c138 → 名前付きカラムへ)
-- ────────────────────────────────────────────────────────────────────
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

-- ────────────────────────────────────────────────────────────────────
-- §D 取込件数 + サンプル確認
-- ────────────────────────────────────────────────────────────────────
SELECT COUNT(*) AS total_rows,
       COUNT(DISTINCT legacy_family_code) AS family_count,
       COUNT(*) FILTER (WHERE cost_unit_price > 0) AS rows_with_cost
  FROM staging_legacy_products;
-- 期待: total_rows = 1288、family_count = 686、rows_with_cost ≈ 816

SELECT legacy_sku, legacy_family_code,
       LEFT(COALESCE(product_name_1,''), 20) AS name_1,
       color_legacy, size_legacy, supplier_legacy, cost_unit_price
  FROM staging_legacy_products
 ORDER BY row_no LIMIT 5;
