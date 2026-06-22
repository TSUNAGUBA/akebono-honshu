-- ============================================================================
-- 06-demo-data.sql — リアルなデモ業務データ一式 (商品 / 付属情報 / 発注 / 生産)
-- ----------------------------------------------------------------------------
-- 目的: 旧 03/04/05 の最小サンプル (1 企画・1 発注・BOM/生産なし) を、業務実態に
--       近い網羅的データへ拡充する。商品マスタ(品番)・付属情報(仕入単価・素材構成
--       BOM)・完成品発注・生産指示・素材発注の各画面が、すべて DB 実データを参照して
--       表示されるようにする。
--
-- 設計方針:
--  - 既存テーブル/マスタはそのまま利用 (スキーマ変更なし、CLAUDE.md 原則7 下位互換)。
--    BOM 用に素材・色・サイズ・商品タイプ・仕入先を「不足分のみ」追加する (ON CONFLICT 冪等)。
--  - 11 桁 SKU は Domain/Products/Sku.cs の Build() と同一規則で SQL 生成する:
--      year(1)+type_conv(1)+season_conv(1)+seq(3)+factory_conv(1)+color_conv(2)+right(size_conv,2)
--    これにより、アプリが採番する SKU と完全に一致する (整合性保証)。
--  - 全 INSERT は自然キー (UNIQUE) に対し ON CONFLICT DO NOTHING で冪等 (原則2)。
--    再実行しても重複生成・既存進捗の巻き戻りは起きない。
--  - 補助関数は pg_temp スキーマに作成し、セッション終了時に自動破棄 (本番汚染なし)。
--
-- 適用: docker (db/init を docker-entrypoint-initdb.d としてマウント) では新規 DB 構築時に
--       01〜06 が番号順で自動投入される。既存 DB へは `psql -f db/init/06-demo-data.sql`
--       を実行する (RUNBOOK §動作確認 参照、冪等なので何度実行しても安全)。
-- ============================================================================

SET TIMEZONE = 'Asia/Tokyo';

-- ────────────────────────────────────────────────────────────────────────────
-- 1) マスタ拡充 (BOM・商品バリエーションに必要な分のみ、冪等)
-- ────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    owner_id     BIGINT;
    cn_id        BIGINT;
    jp_id        BIGINT;
    mc_synthetic BIGINT;
    mc_attach    BIGINT;
    mc_sub       BIGINT;
BEGIN
    SELECT id INTO owner_id FROM users WHERE login_id = 'owner';
    IF owner_id IS NULL THEN RAISE EXCEPTION 'owner user not found; run 01-schema.sql first'; END IF;
    SELECT id INTO jp_id FROM countries WHERE code = '001';
    SELECT id INTO cn_id FROM countries WHERE code = '002';

    -- 付属/副資材の素材分類 (05-production で投入済の場合は何もしない)
    INSERT INTO material_classifications (code, name, delete_flag, created_by_user_id, updated_by_user_id) VALUES
        ('900', '付属',   FALSE, owner_id, owner_id),
        ('901', '副資材', FALSE, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    SELECT id INTO mc_synthetic FROM material_classifications WHERE code = '002';
    SELECT id INTO mc_attach    FROM material_classifications WHERE code = '900';
    SELECT id INTO mc_sub       FROM material_classifications WHERE code = '901';

    -- 商品タイプ (型式) — サンダル/ブーツ/コンフォート/ルームシューズ/健康
    INSERT INTO product_types (code, name, item_conversion_code, size_demographic_code, created_by_user_id, updated_by_user_id) VALUES
        ('004', 'サンダル',     'S', 'R', owner_id, owner_id),
        ('005', 'ブーツ',       'N', 'R', owner_id, owner_id),
        ('006', 'コンフォート', 'E', 'R', owner_id, owner_id),
        ('007', 'ルームシューズ','J', 'R', owner_id, owner_id),
        ('008', '健康',         'G', 'R', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- 色 (8-9桁目、item_conversion_code 2桁)
    INSERT INTO colors (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('010', 'レッド',   '10', owner_id, owner_id),
        ('020', 'グリーン', '20', owner_id, owner_id),
        ('060', 'ホワイト', '60', owner_id, owner_id),
        ('070', 'イエロー', '70', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- サイズ (10-11桁目は item_conversion_code 末尾2桁)
    INSERT INTO sizes (code, name, item_conversion_code, created_by_user_id, updated_by_user_id) VALUES
        ('006', '3L', '3L', owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- 素材 (BOM の甲皮/中底/底/付属/副資材で参照)
    INSERT INTO materials (code, name, material_classification_id, created_by_user_id, updated_by_user_id) VALUES
        ('004', 'EVA',        mc_synthetic, owner_id, owner_id),
        ('005', 'PVC',        mc_synthetic, owner_id, owner_id),
        ('006', 'ラバー',     mc_synthetic, owner_id, owner_id),
        ('007', '合成皮革',   mc_synthetic, owner_id, owner_id),
        ('008', 'ニット',     mc_synthetic, owner_id, owner_id),
        ('009', 'ウレタン',   mc_synthetic, owner_id, owner_id),
        ('010', '面ファスナー', mc_attach,  owner_id, owner_id),
        ('011', '中敷',       mc_attach,    owner_id, owner_id),
        ('012', '値札',       mc_sub,       owner_id, owner_id),
        ('013', '化粧箱',     mc_sub,       owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- 仕入先 (素材仕入先・追加工場)。supplier_type: 1=工場 / 2=素材・資材
    INSERT INTO suppliers (code, name, official_name, item_conversion_code, country_id, supplier_type, created_by_user_id, updated_by_user_id) VALUES
        ('338', '丸和資材',          'MARUWA',        'D', jp_id, 2, owner_id, owner_id),
        ('405', '寧波生地廠',        'NINGBO TEXTILE','E', cn_id, 2, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;

    -- 連絡文章 (発注書用) 追加
    INSERT INTO document_text_purchases (code, name, body, standard_print_flag, created_by_user_id, updated_by_user_id) VALUES
        ('003', '副資材手配', '値札・証紙等の副資材は発注数×5%にて手配しています。', FALSE, owner_id, owner_id)
    ON CONFLICT (code) DO NOTHING;
END $$;

-- ────────────────────────────────────────────────────────────────────────────
-- 2) 補助関数: 商品企画 + SKU(色×サイズ) + 仕入単価 + BOM をまとめて投入
--    SKU は Sku.Build() と同一規則で生成。すべて冪等。
-- ────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION pg_temp.seed_family(
    p_year char, p_type_code text, p_season_code text, p_seq text, p_factory_code text,
    p_brand_code text, p_func_code text, p_group_code text,
    p_upper_code text, p_insole_code text, p_outsole_code text,
    p_attach_code text, p_sub_code text,
    p_name1 text, p_name2 text, p_status int,
    p_colors text[], p_sizes text[],
    p_price1_supplier text, p_price1 numeric, p_price1_ccy text,
    p_price2_supplier text, p_price2 numeric, p_price2_ccy text
) RETURNS bigint
LANGUAGE plpgsql AS $func$
DECLARE
    v_owner BIGINT;
    v_type_id BIGINT;    v_type_conv TEXT;
    v_season_id BIGINT;  v_season_conv TEXT;
    v_factory_id BIGINT; v_factory_conv TEXT;
    v_brand_id BIGINT; v_func_id BIGINT; v_group_id BIGINT;
    v_upper BIGINT; v_insole BIGINT; v_outsole BIGINT; v_attach BIGINT; v_sub BIGINT;
    v_p1 BIGINT; v_p2 BIGINT;
    v_family BIGINT;
    v_cc TEXT; v_color_id BIGINT; v_color_conv TEXT;
    v_sc TEXT; v_size_id BIGINT; v_size_conv TEXT;
    v_sku TEXT;
BEGIN
    SELECT id INTO v_owner FROM users WHERE login_id = 'owner';
    SELECT id, item_conversion_code INTO v_type_id, v_type_conv     FROM product_types   WHERE code = p_type_code;
    SELECT id, item_conversion_code INTO v_season_id, v_season_conv  FROM product_seasons WHERE code = p_season_code;
    SELECT id, item_conversion_code INTO v_factory_id, v_factory_conv FROM suppliers      WHERE code = p_factory_code;
    SELECT id INTO v_brand_id FROM brands         WHERE code = p_brand_code;
    IF p_func_code IS NOT NULL THEN SELECT id INTO v_func_id FROM functions WHERE code = p_func_code; END IF;
    SELECT id INTO v_group_id FROM product_groups WHERE code = p_group_code;
    SELECT id INTO v_upper   FROM materials WHERE code = p_upper_code;
    SELECT id INTO v_insole  FROM materials WHERE code = p_insole_code;
    SELECT id INTO v_outsole FROM materials WHERE code = p_outsole_code;
    IF p_attach_code IS NOT NULL THEN SELECT id INTO v_attach FROM materials WHERE code = p_attach_code; END IF;
    IF p_sub_code    IS NOT NULL THEN SELECT id INTO v_sub    FROM materials WHERE code = p_sub_code; END IF;
    SELECT id INTO v_p1 FROM suppliers WHERE code = p_price1_supplier;
    IF p_price2_supplier IS NOT NULL THEN SELECT id INTO v_p2 FROM suppliers WHERE code = p_price2_supplier; END IF;

    -- 商品企画 (親)
    INSERT INTO product_families (
        planned_year_code, product_type_id, product_season_id, sequence_no,
        factory_supplier_id, brand_id, function_id, product_group_id,
        upper_material_id, insole_material_id, outsole_material_id,
        product_name_1, product_name_2, status, created_by_user_id, updated_by_user_id
    ) VALUES (
        p_year, v_type_id, v_season_id, p_seq, v_factory_id, v_brand_id, v_func_id, v_group_id,
        v_upper, v_insole, v_outsole, p_name1, p_name2, p_status, v_owner, v_owner
    ) ON CONFLICT ON CONSTRAINT uq_product_families DO NOTHING
    RETURNING id INTO v_family;
    IF v_family IS NULL THEN
        SELECT id INTO v_family FROM product_families
        WHERE planned_year_code = p_year AND product_type_id = v_type_id AND product_season_id = v_season_id
          AND sequence_no = p_seq AND factory_supplier_id = v_factory_id;
    END IF;

    -- SKU (色 × サイズ 全組合せ、11桁を Sku.Build と同一規則で生成)
    FOREACH v_cc IN ARRAY p_colors LOOP
        SELECT id, item_conversion_code INTO v_color_id, v_color_conv FROM colors WHERE code = v_cc;
        FOREACH v_sc IN ARRAY p_sizes LOOP
            SELECT id, item_conversion_code INTO v_size_id, v_size_conv FROM sizes WHERE code = v_sc;
            v_sku := p_year || v_type_conv || v_season_conv || p_seq || v_factory_conv
                     || v_color_conv || right(v_size_conv, 2);
            INSERT INTO products (product_family_id, color_id, size_id, sku, created_by_user_id, updated_by_user_id)
            VALUES (v_family, v_color_id, v_size_id, v_sku, v_owner, v_owner)
            ON CONFLICT DO NOTHING;
        END LOOP;
    END LOOP;

    -- 仕入単価 (アイテム単位、現在有効 = effective_to NULL)
    INSERT INTO product_supplier_prices (product_family_id, supplier_id, unit_price, currency_code, effective_from, decided_at, created_by_user_id, updated_by_user_id)
    VALUES (v_family, v_p1, p_price1, p_price1_ccy, DATE '2026-02-01', DATE '2026-01-20', v_owner, v_owner)
    ON CONFLICT DO NOTHING;
    IF v_p2 IS NOT NULL THEN
        INSERT INTO product_supplier_prices (product_family_id, supplier_id, unit_price, currency_code, effective_from, decided_at, created_by_user_id, updated_by_user_id)
        VALUES (v_family, v_p2, p_price2, p_price2_ccy, DATE '2026-02-01', DATE '2026-01-20', v_owner, v_owner)
        ON CONFLICT DO NOTHING;
    END IF;

    -- 素材構成 BOM (甲皮/中底/底 + 付属 + 副資材)。推奨仕入先は素材仕入先 (丸和資材)。
    INSERT INTO product_materials (product_family_id, material_role, material_id, required_qty_per_unit, unit, recommended_supplier_id, loss_rate, remark, created_by_user_id, updated_by_user_id) VALUES
        (v_family, 0, v_upper,   0.3000, '㎡', v_p1, 0.0500, '甲皮', v_owner, v_owner),
        (v_family, 1, v_insole,  1.0000, '組', v_p1, 0.0200, '中底', v_owner, v_owner),
        (v_family, 2, v_outsole, 1.0000, '組', v_p1, 0.0200, '底',   v_owner, v_owner)
    ON CONFLICT DO NOTHING;
    IF v_attach IS NOT NULL THEN
        INSERT INTO product_materials (product_family_id, material_role, material_id, required_qty_per_unit, unit, recommended_supplier_id, loss_rate, remark, created_by_user_id, updated_by_user_id)
        VALUES (v_family, 3, v_attach, 1.0000, '組', (SELECT id FROM suppliers WHERE code='338'), 0.0000, '付属', v_owner, v_owner)
        ON CONFLICT DO NOTHING;
    END IF;
    IF v_sub IS NOT NULL THEN
        INSERT INTO product_materials (product_family_id, material_role, material_id, required_qty_per_unit, unit, recommended_supplier_id, loss_rate, remark, created_by_user_id, updated_by_user_id)
        VALUES (v_family, 4, v_sub, 1.0000, '枚', (SELECT id FROM suppliers WHERE code='338'), 0.0000, '副資材', v_owner, v_owner)
        ON CONFLICT DO NOTHING;
    END IF;

    RETURN v_family;
END;
$func$;

-- ────────────────────────────────────────────────────────────────────────────
-- 3) 補助関数: 完成品の発注書 + 明細
-- ────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION pg_temp.seed_po(
    p_mgmt text, p_family text, p_supplier text, p_dest text, p_dept text, p_wh text,
    p_due date, p_status int, p_order_no text, p_exported_at timestamp, p_qtys int[]
) RETURNS void
LANGUAGE plpgsql AS $func$
DECLARE
    v_owner BIGINT; v_fam BIGINT; v_name TEXT; v_sup BIGINT; v_dest BIGINT; v_dept BIGINT; v_wh BIGINT;
    v_price NUMERIC; v_ccy TEXT; v_po BIGINT; v_i INT := 0; r RECORD;
BEGIN
    SELECT id INTO v_owner FROM users WHERE login_id = 'owner';
    SELECT id, product_name_1 INTO v_fam, v_name FROM product_families WHERE product_name_1 = p_family AND NOT is_deleted ORDER BY id LIMIT 1;
    SELECT id INTO v_sup  FROM suppliers WHERE code = p_supplier;
    SELECT id INTO v_dest FROM delivery_destinations WHERE code = p_dest;
    SELECT id INTO v_dept FROM departments WHERE code = p_dept;
    SELECT id INTO v_wh   FROM warehouses WHERE code = p_wh;
    IF v_fam IS NULL THEN RAISE NOTICE 'seed_po: family % not found, skipped', p_family; RETURN; END IF;

    -- 単価スナップショット: 発注先の現在有効単価、無ければ当該品番の任意の単価
    SELECT unit_price, currency_code INTO v_price, v_ccy FROM product_supplier_prices
        WHERE product_family_id = v_fam AND supplier_id = v_sup AND effective_to IS NULL AND NOT is_deleted
        ORDER BY effective_from DESC LIMIT 1;
    IF v_price IS NULL THEN
        SELECT unit_price, currency_code INTO v_price, v_ccy FROM product_supplier_prices
            WHERE product_family_id = v_fam AND NOT is_deleted ORDER BY effective_from DESC LIMIT 1;
    END IF;
    v_price := COALESCE(v_price, 1500.00); v_ccy := COALESCE(v_ccy, 'JPY');

    INSERT INTO purchase_orders (
        mgmt_no, order_no, status, cancelled_at, cancelled_by_user_id, cancel_reason,
        supplier_id, delivery_destination_id, department_id, warehouse_id, due_date,
        orderer_user_id, manager_user_id, communication_text,
        first_exported_at, last_exported_at, created_by_user_id, updated_by_user_id
    ) VALUES (
        p_mgmt, p_order_no, p_status,
        CASE WHEN p_status = 1 THEN COALESCE(p_exported_at, NOW()) ELSE NULL END,
        CASE WHEN p_status = 1 THEN v_owner ELSE NULL END,
        CASE WHEN p_status = 1 THEN '取引先都合により中止' ELSE NULL END,
        v_sup, v_dest, v_dept, v_wh, p_due,
        v_owner, v_owner, '納期厳守のほどよろしくお願いいたします。',
        p_exported_at, p_exported_at, v_owner, v_owner
    ) ON CONFLICT (mgmt_no) DO NOTHING
    RETURNING id INTO v_po;
    IF v_po IS NULL THEN SELECT id INTO v_po FROM purchase_orders WHERE mgmt_no = p_mgmt; END IF;

    FOR r IN SELECT id, sku FROM products WHERE product_family_id = v_fam AND NOT is_deleted ORDER BY id LIMIT array_length(p_qtys, 1) LOOP
        v_i := v_i + 1;
        INSERT INTO purchase_order_lines (
            purchase_order_id, line_no, product_id, sku_snapshot, product_name_snapshot,
            quantity, unit_price_snapshot, currency_code_snapshot, created_by_user_id, updated_by_user_id
        ) VALUES (v_po, v_i, r.id, r.sku, v_name, p_qtys[v_i], v_price, v_ccy, v_owner, v_owner)
        ON CONFLICT DO NOTHING;
    END LOOP;

    IF p_order_no IS NOT NULL THEN
        INSERT INTO purchase_order_export_logs (purchase_order_id, exported_at, exported_by_user_id, is_first_export, excel_template_version)
        SELECT v_po, p_exported_at, v_owner, TRUE, 'v1'
        WHERE NOT EXISTS (SELECT 1 FROM purchase_order_export_logs WHERE purchase_order_id = v_po);
    END IF;
END;
$func$;

-- ────────────────────────────────────────────────────────────────────────────
-- 4) 補助関数: 生産指示書 (加工指図書) + 明細 (色×サイズ別)
-- ────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION pg_temp.seed_pi(
    p_no text, p_family text, p_factory text, p_per_line int, p_due date, p_status int, p_exported_at timestamp
) RETURNS void
LANGUAGE plpgsql AS $func$
DECLARE
    v_owner BIGINT; v_fam BIGINT; v_name TEXT; v_fac BIGINT; v_fac_name TEXT; v_fac_code TEXT;
    v_cnt INT; v_sku9 TEXT; v_pi BIGINT; v_i INT := 0; r RECORD;
    v_instructed TIMESTAMP; v_completed TIMESTAMP; v_cancelled TIMESTAMP;
BEGIN
    SELECT id INTO v_owner FROM users WHERE login_id = 'owner';
    SELECT id, product_name_1 INTO v_fam, v_name FROM product_families WHERE product_name_1 = p_family AND NOT is_deleted ORDER BY id LIMIT 1;
    SELECT id, official_name, item_conversion_code INTO v_fac, v_fac_name, v_fac_code FROM suppliers WHERE code = p_factory;
    IF v_fam IS NULL THEN RAISE NOTICE 'seed_pi: family % not found, skipped', p_family; RETURN; END IF;

    SELECT count(*) INTO v_cnt FROM products WHERE product_family_id = v_fam AND NOT is_deleted;
    SELECT left(sku, 9) INTO v_sku9 FROM products WHERE product_family_id = v_fam AND NOT is_deleted ORDER BY sku LIMIT 1;

    v_instructed := CASE WHEN p_status IN (1, 2) THEN COALESCE(p_exported_at, NOW() - INTERVAL '5 day') END;
    v_completed  := CASE WHEN p_status = 2 THEN NOW() - INTERVAL '1 day' END;
    v_cancelled  := CASE WHEN p_status = 9 THEN NOW() - INTERVAL '2 day' END;

    INSERT INTO production_instructions (
        instruction_no, product_family_id, factory_supplier_id, planned_quantity, due_date, status,
        instructed_at, completed_at, cancelled_at, cancelled_by_user_id, cancel_reason,
        factory_official_name_snapshot, factory_code_snapshot, product_sku9_snapshot, product_name_snapshot,
        communication_text, first_exported_at, last_exported_at, created_by_user_id, updated_by_user_id
    ) VALUES (
        p_no, v_fam, v_fac, GREATEST(p_per_line * COALESCE(v_cnt, 1), 1), p_due, p_status,
        v_instructed, v_completed, v_cancelled,
        CASE WHEN p_status = 9 THEN v_owner END,
        CASE WHEN p_status = 9 THEN '計画変更により中止' END,
        CASE WHEN p_status >= 1 THEN v_fac_name END,
        CASE WHEN p_status >= 1 THEN v_fac_code END,
        CASE WHEN p_status >= 1 THEN v_sku9 END,
        CASE WHEN p_status >= 1 THEN v_name END,
        '色サイズ別の数量にて加工をお願いいたします。', p_exported_at, p_exported_at, v_owner, v_owner
    ) ON CONFLICT (instruction_no) DO NOTHING
    RETURNING id INTO v_pi;
    IF v_pi IS NULL THEN SELECT id INTO v_pi FROM production_instructions WHERE instruction_no = p_no; END IF;

    FOR r IN SELECT id, sku FROM products WHERE product_family_id = v_fam AND NOT is_deleted ORDER BY id LOOP
        v_i := v_i + 1;
        INSERT INTO production_instruction_lines (
            production_instruction_id, line_no, product_id, sku_snapshot, product_name_snapshot, quantity,
            created_by_user_id, updated_by_user_id
        ) VALUES (v_pi, v_i, r.id, r.sku, v_name, p_per_line, v_owner, v_owner)
        ON CONFLICT DO NOTHING;
    END LOOP;
END;
$func$;

-- ────────────────────────────────────────────────────────────────────────────
-- 5) 補助関数: 素材発注書 (生地材料発注) + 明細 (BOM × 生産数量で所要量展開)
-- ────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION pg_temp.seed_mo(
    p_no text, p_supplier text, p_pi_no text, p_family text, p_due date, p_status int, p_prod_qty int, p_exported_at timestamp
) RETURNS void
LANGUAGE plpgsql AS $func$
DECLARE
    v_owner BIGINT; v_sup BIGINT; v_sup_name TEXT; v_sup_code TEXT; v_pi BIGINT; v_fam BIGINT;
    v_mo BIGINT; v_i INT := 0; r RECORD; v_price NUMERIC;
BEGIN
    SELECT id INTO v_owner FROM users WHERE login_id = 'owner';
    SELECT id, official_name, item_conversion_code INTO v_sup, v_sup_name, v_sup_code FROM suppliers WHERE code = p_supplier;
    SELECT id INTO v_fam FROM product_families WHERE product_name_1 = p_family AND NOT is_deleted ORDER BY id LIMIT 1;
    IF p_pi_no IS NOT NULL THEN SELECT id INTO v_pi FROM production_instructions WHERE instruction_no = p_pi_no; END IF;
    IF v_fam IS NULL THEN RAISE NOTICE 'seed_mo: family % not found, skipped', p_family; RETURN; END IF;

    INSERT INTO material_orders (
        order_no, material_supplier_id, production_instruction_id, due_date, status,
        instructed_at, cancelled_at, cancelled_by_user_id, cancel_reason,
        supplier_official_name_snapshot, supplier_code_snapshot, communication_text,
        first_exported_at, last_exported_at, created_by_user_id, updated_by_user_id
    ) VALUES (
        p_no, v_sup, v_pi, p_due, p_status,
        CASE WHEN p_status = 1 THEN COALESCE(p_exported_at, NOW() - INTERVAL '3 day') END,
        CASE WHEN p_status = 9 THEN NOW() - INTERVAL '2 day' END,
        CASE WHEN p_status = 9 THEN v_owner END,
        CASE WHEN p_status = 9 THEN '生産計画変更により中止' END,
        v_sup_name, v_sup_code, '下記所要量にて手配をお願いいたします。',
        p_exported_at, p_exported_at, v_owner, v_owner
    ) ON CONFLICT (order_no) DO NOTHING
    RETURNING id INTO v_mo;
    IF v_mo IS NULL THEN SELECT id INTO v_mo FROM material_orders WHERE order_no = p_no; END IF;

    -- BOM の各部位を所要量展開: required = qty_per_unit × 生産数 × (1 + loss_rate)
    FOR r IN SELECT pm.material_id, m.name AS mname, pm.material_role, pm.required_qty_per_unit, pm.unit, pm.loss_rate
             FROM product_materials pm JOIN materials m ON m.id = pm.material_id
             WHERE pm.product_family_id = v_fam AND NOT pm.is_deleted
             ORDER BY pm.material_role LOOP
        v_i := v_i + 1;
        v_price := CASE r.material_role WHEN 0 THEN 850.00 WHEN 1 THEN 160.00 WHEN 2 THEN 380.00 WHEN 3 THEN 45.00 ELSE 18.00 END;
        INSERT INTO material_order_lines (
            material_order_id, line_no, material_id, material_name_snapshot, product_family_id,
            required_quantity, unit, unit_price, currency_code, created_by_user_id, updated_by_user_id
        ) VALUES (
            v_mo, v_i, r.material_id, r.mname, v_fam,
            ROUND(r.required_qty_per_unit * p_prod_qty * (1 + r.loss_rate), 4), r.unit, v_price, 'JPY', v_owner, v_owner
        ) ON CONFLICT DO NOTHING;
    END LOOP;
END;
$func$;

-- ────────────────────────────────────────────────────────────────────────────
-- 6) 商品企画 7 件 (品番 + 仕入単価 + BOM)。業務バリエーションを網羅。
--    引数: year,type,season,seq,factory, brand,func,group, upper,insole,outsole,attach,sub,
--          name1,name2,status, colors[],sizes[], price1_sup,price1,ccy, price2_sup,price2,ccy
-- ────────────────────────────────────────────────────────────────────────────
SELECT pg_temp.seed_family('F','004','001','010','404','001','001','001',
    '007','004','006','011','012','婦人コンフォートサンダル','2026春夏 新作',1,
    ARRAY['040','090','010'], ARRAY['002','003','004'],
    '404', 62.00, 'CNY', '336', 1380.00, 'JPY');

SELECT pg_temp.seed_family('F','001','003','020','336','001','001','002',
    '007','009','005','011','012','婦人スタイリッシュパンプス','通年定番',1,
    ARRAY['090','080'], ARRAY['002','003','004'],
    '336', 1680.00, 'JPY', NULL, NULL, NULL);

SELECT pg_temp.seed_family('F','004','001','011','437','001','002','001',
    '007','004','006','011','012','紳士ビジネスサンダル','2026春夏',1,
    ARRAY['040','090'], ARRAY['002','003','004'],
    '437', 58.00, 'CNY', '336', 1280.00, 'JPY');

SELECT pg_temp.seed_family('F','005','002','030','404','001','002','002',
    '008','009','005','010','013','婦人ショートブーツ','2026秋冬',1,
    ARRAY['090','040'], ARRAY['002','003','004'],
    '404', 95.00, 'CNY', '336', 2480.00, 'JPY');

SELECT pg_temp.seed_family('N','007','002','040','437','001','003','001',
    '008','001','004','011','012','ルームシューズ ボア','定番 秋冬',1,
    ARRAY['090','070'], ARRAY['002','003','004'],
    '437', 42.00, 'CNY', '336', 980.00, 'JPY');

SELECT pg_temp.seed_family('N','008','003','050','336','001','002','002',
    '007','009','006','010','013','婦人ヘルスウォーカー','定番 通年',1,
    ARRAY['080','090'], ARRAY['002','003','004','006'],
    '336', 2980.00, 'JPY', NULL, NULL, NULL);

SELECT pg_temp.seed_family('N','003','003','060','336','002','001','001',
    '002','001','002',NULL,'012','クッションマット','定番 通年',2,
    ARRAY['040','020'], ARRAY['005'],
    '336', 780.00, 'JPY', NULL, NULL, NULL);

-- ────────────────────────────────────────────────────────────────────────────
-- 7) 完成品の発注書 (Active 未出力 / Active 出力済 / 中止 を混在)
--    引数: mgmt_no, family名, 発注先, 納品先, 事業部, 倉庫, 取引先納入日, status(0/1),
--          order_no(出力済のみ), 出力日時, 明細数量[]
-- ────────────────────────────────────────────────────────────────────────────
SELECT pg_temp.seed_po('26-00101','婦人コンフォートサンダル','404','001','001','007', DATE '2026-08-20', 0, NULL, NULL, ARRAY[120,180,90]);
SELECT pg_temp.seed_po('26-00102','婦人スタイリッシュパンプス','336','002','001','007', DATE '2026-07-31', 0, 'S00021', TIMESTAMP '2026-06-10 10:30', ARRAY[100,150,80]);
SELECT pg_temp.seed_po('26-00103','婦人ショートブーツ','404','001','002','007', DATE '2026-09-30', 0, 'S00022', TIMESTAMP '2026-06-12 14:05', ARRAY[200,160,60]);
SELECT pg_temp.seed_po('26-00104','紳士ビジネスサンダル','437','002','002','007', DATE '2026-08-10', 1, NULL, NULL, ARRAY[80,60]);
SELECT pg_temp.seed_po('26-00105','婦人ヘルスウォーカー','336','001','001','007', DATE '2026-10-15', 0, NULL, NULL, ARRAY[60,90,70,40]);

-- ────────────────────────────────────────────────────────────────────────────
-- 8) 生産指示書 (Issued / Completed / Draft / Cancelled を混在)
--    引数: instruction_no, family名, 加工先, 明細1行あたり数量, 希望納期, status(0/1/2/9), 出力日時
-- ────────────────────────────────────────────────────────────────────────────
SELECT pg_temp.seed_pi('26-PI-00001','婦人コンフォートサンダル','404', 60, DATE '2026-07-31', 1, TIMESTAMP '2026-06-05 09:00');  -- 指示済
SELECT pg_temp.seed_pi('26-PI-00002','婦人スタイリッシュパンプス','336', 50, DATE '2026-07-10', 1, TIMESTAMP '2026-06-08 09:00'); -- 指示済 (素材未)
SELECT pg_temp.seed_pi('26-PI-00003','婦人ショートブーツ','404', 70, DATE '2026-09-15', 2, TIMESTAMP '2026-05-20 09:00');         -- 完了
SELECT pg_temp.seed_pi('26-PI-00004','ルームシューズ ボア','437', 80, DATE '2026-08-31', 0, NULL);                                -- 下書き (未)

-- ────────────────────────────────────────────────────────────────────────────
-- 9) 素材発注書 (Ordered / Draft / Cancelled、生産指示由来 + 単独)
--    引数: order_no, 素材仕入先, 由来PI(無ければNULL), family名, 希望納期, status(0/1/9), 生産数量, 出力日時
-- ────────────────────────────────────────────────────────────────────────────
SELECT pg_temp.seed_mo('26-MO-00001','405','26-PI-00001','婦人コンフォートサンダル', DATE '2026-06-30', 1, 540, TIMESTAMP '2026-06-06 11:00'); -- 発注済
SELECT pg_temp.seed_mo('26-MO-00002','405','26-PI-00003','婦人ショートブーツ',       DATE '2026-06-20', 1, 420, TIMESTAMP '2026-05-22 11:00'); -- 発注済
SELECT pg_temp.seed_mo('26-MO-00003','338', NULL,        'クッションマット',         DATE '2026-07-15', 1, 300, TIMESTAMP '2026-06-09 11:00'); -- 単独発注 (素材済/指示未)
SELECT pg_temp.seed_mo('26-MO-00004','405','26-PI-00002','婦人スタイリッシュパンプス', DATE '2026-07-05', 0, 300, NULL);                          -- 下書き

-- ────────────────────────────────────────────────────────────────────────────
-- 10) 既存サンプル (03/04) を業務的な表記に更新 (冪等)。
--     「デモ商品」表記を解消し、BOM 未登録の品番として残す (生産手配状況の「BOM未登録」第3状態の実例)。
-- ────────────────────────────────────────────────────────────────────────────
UPDATE product_families
   SET product_name_1 = '婦人ベーシックパンプス', product_name_2 = '定番 春夏'
 WHERE product_name_1 = 'デモ商品 春夏ベーシック';

UPDATE purchase_orders
   SET communication_text = '納期厳守のほどよろしくお願いいたします。'
 WHERE mgmt_no = '26-00001' AND communication_text LIKE '%サンプル%';

UPDATE purchase_order_lines
   SET product_name_snapshot = '婦人ベーシックパンプス'
 WHERE product_name_snapshot = 'デモ商品 春夏ベーシック';
