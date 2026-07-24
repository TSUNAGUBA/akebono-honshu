-- ════════════════════════════════════════════════════════════════════
-- Iteration 29: 工場マスタ (factories) に添付データ (A〜Z) を投入 (Part2)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   品番7桁目の工場コード (item_conversion_code = 英字1桁) と工場名称を、添付の二段組データ
--   (アルファベット → 工場名称) に基づき工場マスタへ投入する。db/init/02-masters.sql の DO ブロック
--   に反映済だが、init は空 DB でのみ適用されるため、既に init 済の本番 RDS には本マイグレーション
--   (action=migrate) で追加適用する。
--
-- データ方針 (添付の見た目を業務的に解釈):
--   - code = item_conversion_code = 英字1桁 (A〜Z)。名称は添付右列。
--   - 国情報が添付に無いため country は「未設定」(code 999) に暫定割当。国内その他 (Z) のみ日本 (code 001)。
--     区分 (supplier_type) は 国内その他=0(国内) / それ以外=1(海外)。国はマスタ画面で個別修正可能。
--   - 取消線の工場 (B 南通本州 / M Ixora / S ストロング / T 小澤重 / U スタル / Y 夢華達) は「廃止」と解釈し、
--     deleted_at をセットして論理削除で投入する (品番の履歴解決・年度分析には残し、新規登録の
--     工場選択肢には出さない)。'－' 表記の I / O は工場が無いため投入しない。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 国・工場とも ON CONFLICT (tenant_id, code) DO NOTHING。再実行しても追加のみ (既存行は不変)。
--   - 既存デモ工場 (code 336/404/437) とは code が異なるため独立 (競合しない)。
--   - 論理削除済みで投入した行を再実行で復活させない (DO NOTHING のため既存 deleted_at を保持)。
--
-- RLS 注意 (iter26 と同じ):
--   factories/countries は FORCE ROW LEVEL SECURITY のため、tenant ごとに app.tenant_id を設定してから
--   投入する (tenant_id 列の DEFAULT もこの GUC から解決)。tenant 表は RLS 対象外 (レジストリ、読取のみ)。
--   監査ユーザ (created_by/updated_by) は当該テナントに確実に存在する既存 factories→suppliers→users から流用する。
--
-- 適用方法 (自動・推奨): GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

DO $$
DECLARE
    t        RECORD;
    v_owner  UUID;
    v_jp     UUID;
    v_unset  UUID;
BEGIN
    FOR t IN SELECT tenant_id FROM tenant LOOP
        PERFORM set_config('app.tenant_id', t.tenant_id::text, true);

        -- 監査ユーザ: 当該テナントに確実に存在するものを流用 (factories → suppliers → users の順)。
        SELECT created_by_user_id INTO v_owner FROM factories WHERE tenant_id = t.tenant_id LIMIT 1;
        IF v_owner IS NULL THEN
            SELECT created_by_user_id INTO v_owner FROM suppliers WHERE tenant_id = t.tenant_id LIMIT 1;
        END IF;
        IF v_owner IS NULL THEN
            SELECT id INTO v_owner FROM users WHERE tenant_id = t.tenant_id LIMIT 1;
        END IF;
        IF v_owner IS NULL THEN
            CONTINUE; -- ユーザ不在のテナントは投入不可のためスキップ
        END IF;

        -- 未設定国 (code 999) を確保。
        INSERT INTO countries (code, name, created_by_user_id, updated_by_user_id)
            VALUES ('999', '未設定', v_owner, v_owner)
        ON CONFLICT (tenant_id, code) DO NOTHING;

        SELECT id INTO v_unset FROM countries WHERE tenant_id = t.tenant_id AND code = '999';
        SELECT id INTO v_jp    FROM countries WHERE tenant_id = t.tenant_id AND code = '001';
        IF v_jp IS NULL THEN v_jp := v_unset; END IF; -- 日本国が無ければ未設定にフォールバック

        -- 工場 A〜Z (I/O はスキップ、取消線は deleted_at=NOW で論理削除投入)。
        INSERT INTO factories (code, name, item_conversion_code, country_id, supplier_type, alert_target,
                               deleted_at, created_by_user_id, updated_by_user_id) VALUES
            ('A', '海外その他',     'A', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('B', '南通本州',       'B', v_unset, 1, 0, NOW(),  v_owner, v_owner),
            ('C', '固安',           'C', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('D', '正書',           'D', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('E', '天宇',           'E', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('F', '藤東',           'F', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('G', '新岡',           'G', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('H', '環球',           'H', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('J', '拓馳',           'J', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('K', '春源',           'K', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('L', '五星・柬埔寨',   'L', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('M', 'Ixora',          'M', v_unset, 1, 0, NOW(),  v_owner, v_owner),
            ('N', '蘭奥',           'N', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('P', '三和繊維',       'P', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('Q', '居美',           'Q', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('R', 'ラッキー産業',   'R', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('S', 'ストロング',     'S', v_unset, 1, 0, NOW(),  v_owner, v_owner),
            ('T', '小澤重',         'T', v_unset, 1, 0, NOW(),  v_owner, v_owner),
            ('U', 'スタル',         'U', v_unset, 1, 0, NOW(),  v_owner, v_owner),
            ('V', '湯誠',           'V', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('W', '金森',           'W', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('X', '評価減',         'X', v_unset, 1, 0, NULL,   v_owner, v_owner),
            ('Y', '夢華達',         'Y', v_unset, 1, 0, NOW(),  v_owner, v_owner),
            ('Z', '国内その他',     'Z', v_jp,    0, 0, NULL,   v_owner, v_owner)
        ON CONFLICT (tenant_id, code) DO NOTHING;
    END LOOP;
END $$;

COMMIT;
