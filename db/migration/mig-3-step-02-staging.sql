-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Step 2: Staging テーブル DDL (Backend / psql 共用)
-- ════════════════════════════════════════════════════════════════════
-- 目的: 既存生産管理システム CSV (138 列) 用 Staging テーブル定義
--
-- 利用方法:
--   - Backend 画面取込 (POST /api/v1/admin/legacy-import):
--       LegacyImportService が本 SQL を ExecuteSqlRaw で実行
--       (CSV パース + INSERT は C# 側で実装)
--   - psql 手動取込 (フォールバック):
--       本ファイル実行 → 後段 \copy → mig-3-step-02-staging-load.sql
-- ════════════════════════════════════════════════════════════════════

DROP TABLE IF EXISTS staging_legacy_products CASCADE;
CREATE TABLE staging_legacy_products (
    row_no               SERIAL PRIMARY KEY,
    -- 主要 12 列 (取込時抽出後格納)
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
