-- ════════════════════════════════════════════════════════════════════
-- Iteration 11: マスタ選択肢の旧システム確定値反映 Phase D — master DATA の追加/改名 (スキーマ変更なし)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧システムの確定マスタ選択肢 (.ai-native/outputs/phase5/legacy-field-spec.md
--   §A-2 商品群/機能, §A-3 ブランド/素材, §B 倉庫/納品先) を新システムの seed マスタに
--   反映する。Phase A〜C (iter8〜iter10) は ALTER TABLE による「スキーマ追加」だったが、
--   本 Phase D は既存マスタ「データ」の追加 (不足コードの INSERT) と改名 (name の UPDATE)
--   のみで、スキーマ (列・制約) には一切触れない。
--   db/init/02-masters.sql (空 DB 初期化の SoT) にも同一の確定値を反映済だが、
--   db/init/*.sql は action=init / docker 初回起動でのみ適用されるため、既に init 済の
--   本番 RDS には届かない。本マイグレーション (action=migrate) で UPSERT により追加適用する。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter10 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ/データ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性・安全性 (CLAUDE.md 原則 2 / 7、本 Phase の最重要事項):
--   - 全 INSERT は自然キー (code UNIQUE) に対し ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name
--     とし、不足コードは追加・既存コードは name のみ確定値へ更新する (追加+改名、削除なし)。
--   - **code は不変** (products/orders/demo が FK 参照する安定識別子)。本ファイルは既存 code を
--     一切変更・削除しない。name のみを旧確定値へ揃える (FK・SKU は影響を受けない)。
--   - 改名対象は「旧確定値が存在し、かつ既存 code が demo データの意味と衝突しない」ものに限定。
--     衝突するマスタ (素材 002/004-007, 商品タイプの gender×型式, 倉庫/納品先のキー欠落) は
--     本ファイルでは触れず、設計判断として後続にフラグする (ファイル末尾の注記参照)。
--   - 再実行しても INSERT は DO UPDATE で既存行の name を確定値へ収束させるだけで、
--     新規行生成・code 変更・既存 FK の破壊は起きない (真の冪等)。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。後続のマスタ値変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 02-masters.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

DO $$
DECLARE
    owner_id BIGINT;
BEGIN
    SELECT id INTO owner_id FROM users WHERE login_id = 'owner';
    IF owner_id IS NULL THEN
        RAISE EXCEPTION 'owner user not found; run 01-schema.sql first';
    END IF;

    -- ── §A-2 商品群 product_groups ──
    -- 旧確定値: 001 第1プロパー / 002 第2プロパー / 003 第1他商品 / 004 第2他商品 /
    --           005 第1バーゲン / 999 その他
    -- 001/002 は demo (seed_family group '001'/'002') が参照。code 不変・name のみ確定値へ。
    -- planning_fee は本 Phase の対象外のため touch しない (既存値を保持)。新規行は DEFAULT 0。
    INSERT INTO product_groups (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '第1プロパー', owner_id, owner_id),
        ('002', '第2プロパー', owner_id, owner_id),
        ('003', '第1他商品',   owner_id, owner_id),
        ('004', '第2他商品',   owner_id, owner_id),
        ('005', '第1バーゲン', owner_id, owner_id),
        ('999', 'その他',       owner_id, owner_id)
    ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name, updated_at = NOW();

    -- ── §A-2 機能 functions ──
    -- 旧確定値: 001 静音 / 002 脱げにくい / 003 からだにいい岩 / 004 足つぼ /
    --           005 むじゅうりょく / 006 超軽量 / 007 メタボリッパ
    -- 001/002/003 は demo (seed_family func '001'/'002'/'003') が参照。code 不変・name のみ確定値へ。
    INSERT INTO functions (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('001', '静音',          owner_id, owner_id),
        ('002', '脱げにくい',     owner_id, owner_id),
        ('003', 'からだにいい岩', owner_id, owner_id),
        ('004', '足つぼ',         owner_id, owner_id),
        ('005', 'むじゅうりょく', owner_id, owner_id),
        ('006', '超軽量',         owner_id, owner_id),
        ('007', 'メタボリッパ',   owner_id, owner_id)
    ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name, updated_at = NOW();

    -- ── §A-3 ブランド brands ──
    -- 旧確定値: 000 ホンシュPB / 004 PLANT / 005 ディズニー / 006 リラックマ /
    --           011 ガチャピン / 013 ONE PIECE / 017 HKWL(ヒロココシノ)
    -- 既存 001 akebono / 002 プライベート は demo (seed_family brand '001'/'002') が参照し、
    -- 旧確定リストに該当 code が無い。削除・改名せずそのまま残す (本 INSERT に含めない)。
    -- 旧リストの code (000/004/005/006/011/013/017) は既存 brands と重複しないため安全に追加。
    INSERT INTO brands (code, name, created_by_user_id, updated_by_user_id) VALUES
        ('000', 'ホンシュPB',        owner_id, owner_id),
        ('004', 'PLANT',             owner_id, owner_id),
        ('005', 'ディズニー',         owner_id, owner_id),
        ('006', 'リラックマ',         owner_id, owner_id),
        ('011', 'ガチャピン',         owner_id, owner_id),
        ('013', 'ONE PIECE',         owner_id, owner_id),
        ('017', 'HKWL(ヒロココシノ)', owner_id, owner_id)
    ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name, updated_at = NOW();

    -- ── §A-3 素材 materials — 確認のみ (UPSERT は 001/003 に限定) ──
    -- 旧確定値: 001 綿 / 002 イント綿 / 003 麻 / 004 ポリエステル / 005 アクリル /
    --           006 ナイロン / 007 竹
    -- ⚠ 衝突注意: code 002/004/005/006/007 は 06-demo-data.sql の BOM が別の素材として
    --   高頻度に参照している (002 ポリエステル, 004 EVA, 005 PVC, 006 ラバー, 007 合成皮革)。
    --   ここで旧確定値へ改名すると demo BOM (甲皮/中底/底/付属/副資材) の素材意味が静かに
    --   変質し、material_order_lines への将来スナップショットも誤る。よって本ファイルでは
    --   旧名と既存 code が「一致」する 001 綿・003 麻 のみを冪等確認 (実質 no-op) に留める。
    --   002/004-007 の旧素材は code 名前空間が demo と衝突するため UPSERT せず、
    --   ファイル末尾でコード/素材モデリングの設計判断としてフラグする。
    INSERT INTO materials (code, name, material_classification_id, created_by_user_id, updated_by_user_id) VALUES
        ('001', '綿', (SELECT id FROM material_classifications WHERE code = '001'), owner_id, owner_id),
        ('003', '麻', (SELECT id FROM material_classifications WHERE code = '001'), owner_id, owner_id)
    ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name, updated_at = NOW();
    -- (material_classification_id は NOT NULL のため新規 INSERT 時のみ必要。001/003 は既存のため
    --  DO UPDATE 側では classification を touch しない = 既存分類を保持する。)

END $$;

COMMIT;

-- ════════════════════════════════════════════════════════════════════
-- 本 Phase D で「適用せず・設計判断としてフラグした」マスタ (後続課題)
-- ════════════════════════════════════════════════════════════════════
-- 安全制約 (code 不変 / demo 参照 code 保護 / スキーマ変更なし) を満たせないため、
-- 以下は本ファイルで触れず、別途モデリング/採番の意思決定を要する:
--
-- 1) 商品タイプ product_types: 旧仕様は「型式コード × 婦人/紳士/子供」で約 30 件。同一
--    単一型式コード (A, J …) が複数の型式名に重複 (A=吊込W底/吊込レザー, J=ソフト/ルームシューズ)。
--    現行 product_types は単一 code('001'..) と単一 item_conversion_code (SKU が使う型式 1 文字)
--    で構成され、11 桁 SKU は型式 1 文字のみを用いる。30 通り (型式×性別) は単一 code にも
--    単一型式文字にも 1:1 で写像できないため構造変更を要する。既存 name は型式文字が旧型式と
--    一致する範囲 (A 吊込W底 → 001/002 吊込W底婦人/紳士、J ルームシューズ → 007) で既に整合
--    しており、曖昧な改名 (003 クッション=demo参照, 004-006/008 の型式文字 S/N/E/G は旧型式集合に
--    非存在) は行わない。→ 性別 (size_demographic_code) × 型式の表現方法は別設計判断。
--
-- 2) 素材 materials の旧 002/004/005/006/007: 上記の通り demo BOM 参照 code と意味衝突。
--    旧素材 (イント綿/ポリエステル/アクリル/ナイロン/竹) を新規導入するなら、demo と衝突しない
--    別 code への採番、または demo 素材 code の再編が必要。→ code 採番方針の意思決定を要する。
--
-- 3) 倉庫 warehouses: §B は code のみ列挙 (000/001/002/005/007/008/009) で name が未提供。
--    name 不明のままでは確定値 UPSERT 不可 (推測名を投入するとデータ捏造)。007 は既存 (本社倉庫)
--    かつ旧リストに含まれる。→ 残り code の正式名称をオペレーターに確認のうえ追加する。
--
-- 4) 納品先 delivery_destinations: §B は name のみ列挙で code が未提供。一意キーは code のため
--    code 無しでは ON CONFLICT (code) 不可。001 は既存で「しまむらセンター」(旧リスト先頭) に一致。
--    残り 6 件 (しまむらセンター＆MSJ / (株)MSJ加須第2営業所 / (株)ホンシュいわきセンター /
--    (株)ホンシュ福岡支店 / ロジコ / KEYUCA商品センター) は code 採番の意思決定を要する。
--
-- 5) 企画者/社員 users: §A-3 は 社員番号 + 氏名 のみ (008 佐藤浩始 〜 115 南雄介)。一致キーは
--    employee_no だが、users は認証連携テーブルで login_id が NOT NULL UNIQUE。旧リストに login_id
--    が無く、追加には login_id の合成 (データ捏造) を要する。さらに旧 093 斉藤摂次 / 115 南雄介 は
--    既存 seed (003 sales 斉藤摂次 / 002 planner 南雄介) と同一人物が別 employee_no で重複する。
--    認証連携の機微性 + 重複人物リスク + login_id 捏造を避け、本 Phase では追加せずフラグする。
--    → 社員マスタと認証ユーザの分離 (社員番号主キーの非認証社員テーブル新設等) を要する設計判断。
-- ════════════════════════════════════════════════════════════════════
