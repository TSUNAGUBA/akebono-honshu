-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Step 3: Staging → 本テーブル (product_families / products / prices)
-- ════════════════════════════════════════════════════════════════════
-- 目的: staging_legacy_products から
--       product_families (686 件) / products (1,288 件) /
--       product_supplier_prices (≈ 816 件) に INSERT
--
-- 前提:
--   - step-01 完了 (マスタ補完済)
--   - step-02 完了 (staging テーブルに CSV 取込済、主要列抽出済)
--
-- 設計判断 (mig-3-strategy.md §2 で承認済):
--   1. 旧 SKU はそのまま保持 (新 11 桁形式に変換しない)
--   2. 仕入単価は「原価単価」(cost_unit_price) を採用
--   3. planned_year_code = 'Z' で旧データ識別 (新規企画 A-K/N と区別)
--   4. 商品タイプ/季節/ブランド/機能/素材は最初の値を仮割当
--      → 業務担当者が UI で後で確定 (status=Draft でフィルタ可)
--   5. 商品分類 1, 2 は legacy_id にのみ保持 (新マスタへの自動 mapping なし)
--
-- 冪等: legacy_id 一致レコードは ON CONFLICT NOTHING でスキップ
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ────────────────────────────────────────────────────────────────────
-- §A 共通変数 (owner / 仮値マスタ ID)
-- ────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    -- プラットフォーム統合 第二段階: 各テーブル id が BIGSERIAL → UUID になったため変数型を追随
    v_owner_id          UUID;
    v_default_type_id   UUID;
    v_default_season_id UUID;
    v_default_brand_id  UUID;
    v_default_group_id  UUID;
    v_default_material  UUID;
    v_inserted_families INTEGER;
    v_inserted_products INTEGER;
    v_inserted_prices   INTEGER;
BEGIN
    SELECT id INTO v_owner_id FROM users WHERE login_id = 'owner';
    SELECT id INTO v_default_type_id   FROM product_types   ORDER BY code LIMIT 1;
    SELECT id INTO v_default_season_id FROM product_seasons ORDER BY code LIMIT 1;
    SELECT id INTO v_default_brand_id  FROM brands          ORDER BY code LIMIT 1;
    SELECT id INTO v_default_group_id  FROM product_groups  ORDER BY code LIMIT 1;
    SELECT id INTO v_default_material  FROM materials       ORDER BY code LIMIT 1;

    -- ────────────────────────────────────────────────────────────
    -- §B product_families に他品番ユニーク単位で INSERT
    --    sequence_no は family ごとの ROW_NUMBER (001-686 で通し連番)
    --    factory_supplier_id は CSV の supplier_legacy を引当
    -- ────────────────────────────────────────────────────────────
    WITH family_src AS (
      SELECT DISTINCT
        sl.legacy_family_code,
        FIRST_VALUE(sl.product_name_1) OVER (
          PARTITION BY sl.legacy_family_code ORDER BY sl.row_no
        ) AS pname_1,
        FIRST_VALUE(sl.product_name_2) OVER (
          PARTITION BY sl.legacy_family_code ORDER BY sl.row_no
        ) AS pname_2,
        FIRST_VALUE(sl.supplier_legacy) OVER (
          PARTITION BY sl.legacy_family_code ORDER BY sl.row_no
        ) AS supplier_legacy_first,
        ROW_NUMBER() OVER (PARTITION BY sl.legacy_family_code ORDER BY sl.row_no) AS rn
      FROM staging_legacy_products sl
      WHERE sl.legacy_family_code IS NOT NULL
    ),
    family_unique AS (
      SELECT legacy_family_code, pname_1, pname_2, supplier_legacy_first,
             LPAD((ROW_NUMBER() OVER (ORDER BY legacy_family_code))::text, 3, '0') AS seq
      FROM family_src
      WHERE rn = 1
    )
    INSERT INTO product_families (
      planned_year_code, product_type_id, product_season_id, sequence_no,
      factory_supplier_id, brand_id, function_id, product_group_id,
      upper_material_id, insole_material_id, outsole_material_id,
      product_name_1, product_name_2, status,
      created_by_user_id, updated_by_user_id, legacy_id
    )
    SELECT
      'Z',                                  -- 旧データ識別フラグ
      v_default_type_id,
      v_default_season_id,
      fu.seq,
      COALESCE(
        (SELECT id FROM suppliers WHERE legacy_id = fu.supplier_legacy_first LIMIT 1),
        (SELECT id FROM suppliers WHERE code = '336')   -- フォールバック: Iter 1 Seed 工場 A
      ),
      v_default_brand_id,
      NULL,                                 -- function_id NULL 可
      v_default_group_id,
      v_default_material, v_default_material, v_default_material,
      COALESCE(NULLIF(fu.pname_1, ''), '【旧データ】' || fu.legacy_family_code),
      fu.pname_2,
      0,                                    -- status = Draft (業務担当者確定待ち)
      v_owner_id, v_owner_id,
      fu.legacy_family_code
    FROM family_unique fu
    ON CONFLICT (tenant_id, planned_year_code, product_type_id, product_season_id, sequence_no, factory_supplier_id)  -- プラットフォーム統合改修: UNIQUE 先頭に tenant_id
      DO NOTHING;

    GET DIAGNOSTICS v_inserted_families = ROW_COUNT;
    RAISE NOTICE 'product_families inserted: %', v_inserted_families;

    -- ────────────────────────────────────────────────────────────
    -- §C products に SKU 単位で INSERT
    --    color は colors.legacy_id 一致、size は sizes.legacy_id 一致で引当
    --
    --    旧 CSV は SKU 末尾文字違い (FX2043F / FX2043S 等) で同じ
    --    family+color+size 組合せの別 SKU を持つケースが 36 件あり、
    --    新システムの UNIQUE (family,color,size) 制約と衝突する。
    --    全 UNIQUE 制約 (sku / family+color+size) に対し ON CONFLICT
    --    DO NOTHING で先着 SKU のみ取込、重複は静かにスキップする。
    -- ────────────────────────────────────────────────────────────
    INSERT INTO products (
      product_family_id, color_id, size_id, sku, legacy_id,
      created_by_user_id, updated_by_user_id
    )
    SELECT
      pf.id,
      COALESCE(
        (SELECT id FROM colors WHERE legacy_id = sl.color_legacy LIMIT 1),
        (SELECT id FROM colors WHERE code = '090' LIMIT 1)        -- フォールバック: 黒
      ),
      COALESCE(
        (SELECT id FROM sizes WHERE legacy_id = sl.size_legacy LIMIT 1),
        (SELECT id FROM sizes WHERE name = 'M' LIMIT 1)            -- フォールバック: M
      ),
      sl.legacy_sku,
      sl.legacy_sku,
      v_owner_id, v_owner_id
    FROM staging_legacy_products sl
    JOIN product_families pf ON pf.legacy_id = sl.legacy_family_code
    WHERE sl.legacy_sku IS NOT NULL
    ON CONFLICT DO NOTHING;

    GET DIAGNOSTICS v_inserted_products = ROW_COUNT;
    RAISE NOTICE 'products inserted: %', v_inserted_products;

    -- ────────────────────────────────────────────────────────────
    -- §D product_supplier_prices に単価あり分のみ INSERT
    --    cost_unit_price > 0 のレコードを採用
    --    同一 family に複数 SKU がある場合、最初の単価を採用
    -- ────────────────────────────────────────────────────────────
    WITH price_src AS (
      SELECT DISTINCT ON (pf.id)
        pf.id AS family_id,
        sup.id AS supplier_id,
        sl.cost_unit_price
      FROM staging_legacy_products sl
      JOIN product_families pf ON pf.legacy_id = sl.legacy_family_code
      JOIN suppliers sup ON sup.legacy_id = sl.supplier_legacy
      WHERE sl.cost_unit_price IS NOT NULL AND sl.cost_unit_price > 0
      ORDER BY pf.id, sl.row_no
    )
    INSERT INTO product_supplier_prices (
      product_family_id, supplier_id, unit_price, currency_code,
      effective_from, decided_at,
      created_by_user_id, updated_by_user_id
    )
    SELECT
      family_id, supplier_id, cost_unit_price, 'JPY',
      '2024-01-01'::date,                   -- 旧データの単価有効開始日 (仮)
      '2024-01-01'::date,
      v_owner_id, v_owner_id
    FROM price_src
    ON CONFLICT DO NOTHING;

    GET DIAGNOSTICS v_inserted_prices = ROW_COUNT;
    RAISE NOTICE 'product_supplier_prices inserted: %', v_inserted_prices;

    RAISE NOTICE '─── MIG-3 取込結果 ──────────────────────────';
    RAISE NOTICE '  product_families        : % 件', v_inserted_families;
    RAISE NOTICE '  products                : % 件', v_inserted_products;
    RAISE NOTICE '  product_supplier_prices : % 件', v_inserted_prices;
END $$;

COMMIT;

-- ════════════════════════════════════════════════════════════════════
-- 検証クエリ (取込後実行)
-- ════════════════════════════════════════════════════════════════════
SELECT 'product_families (legacy)' AS table_name,
       COUNT(*) AS count,
       COUNT(legacy_id) AS with_legacy
  FROM product_families
 WHERE planned_year_code = 'Z'
UNION ALL
SELECT 'products (legacy)', COUNT(*), COUNT(legacy_id)
  FROM products WHERE legacy_id IS NOT NULL
UNION ALL
SELECT 'product_supplier_prices', COUNT(*), 0
  FROM product_supplier_prices psp
  JOIN product_families pf ON pf.id = psp.product_family_id
 WHERE pf.planned_year_code = 'Z';

-- 期待:
--   product_families        : 686
--   products                : 1288
--   product_supplier_prices : ≈ 816

-- マッピング不能件数 (フォールバック適用された行を特定)
SELECT 'color フォールバック' AS issue, COUNT(*) FROM staging_legacy_products
 WHERE color_legacy NOT IN (SELECT legacy_id FROM colors WHERE legacy_id IS NOT NULL)
UNION ALL
SELECT 'size フォールバック', COUNT(*) FROM staging_legacy_products
 WHERE size_legacy NOT IN (SELECT legacy_id FROM sizes WHERE legacy_id IS NOT NULL)
UNION ALL
SELECT 'supplier フォールバック', COUNT(*) FROM staging_legacy_products
 WHERE supplier_legacy NOT IN (SELECT legacy_id FROM suppliers WHERE legacy_id IS NOT NULL);
-- 期待: いずれも 0 件 (step-01 でマスタ補完済のため)
