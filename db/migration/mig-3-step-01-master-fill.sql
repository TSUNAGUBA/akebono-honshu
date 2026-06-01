-- ════════════════════════════════════════════════════════════════════
-- MIG-3 Step 1: マスタ自動補完
-- ════════════════════════════════════════════════════════════════════
-- 目的: CSV に出現する旧マスタコード (カラー 31 / サイズ 10 / 仕入先 11) を
--       新マスタに INSERT。Iter 1 Seed 既存値とは code 空間を分離 ("L" prefix) し、
--       legacy_id で旧 code を保持する。
--
-- 前提: mig-3-pre-patch.sql 実行済
-- 実行: pgAdmin4 で本ファイルを 1 回実行 (冪等、ON CONFLICT NOTHING)
--
-- 注意:
--   - カラー名/サイズ名は仮値 (業務担当者が後で UI から更新する想定)
--   - 仕入先名は code 値そのまま (取引先名は機密のため、業務担当者が UPDATE)
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ────────────────────────────────────────────────────────────────────
-- 1. カラー補完 (旧 31 種)
--    新 colors.code は "L" + 旧 2 桁 (例: "L11", "L40") で衝突回避
--    item_conversion_code (CHAR(2)) には旧 2 桁をそのまま保存 (帳票連携用)
-- ────────────────────────────────────────────────────────────────────
WITH legacy_colors(legacy_code, name_ja) AS (VALUES
  ('00','旧色 00 (未設定)'),('10','旧色 10 (ホワイト系)'),('11','旧色 11'),('12','旧色 12'),
  ('15','旧色 15'),('20','旧色 20 (ベージュ系)'),('21','旧色 21'),('22','旧色 22'),
  ('30','旧色 30 (ブラウン系)'),('31','旧色 31'),('32','旧色 32'),('35','旧色 35'),
  ('40','旧色 40 (タン系)'),('41','旧色 41'),('42','旧色 42'),('45','旧色 45'),
  ('50','旧色 50 (レッド系)'),('60','旧色 60 (オレンジ系)'),('61','旧色 61'),('62','旧色 62'),
  ('65','旧色 65 (イエロー系)'),('70','旧色 70 (グリーン系)'),('72','旧色 72'),('75','旧色 75'),
  ('80','旧色 80 (ブルー系)'),('81','旧色 81'),('82','旧色 82'),('85','旧色 85 (ネイビー)'),
  ('90','旧色 90 (ブラック系)'),('91','旧色 91'),('95','旧色 95 (グレー系)')
), owner AS (SELECT id FROM users WHERE login_id = 'owner')
INSERT INTO colors (code, name, item_conversion_code, legacy_id, created_by_user_id, updated_by_user_id)
SELECT
  'L' || lc.legacy_code,             -- "L11", "L40" (Iter 1 Seed と分離)
  lc.name_ja,
  lc.legacy_code,
  lc.legacy_code,
  o.id, o.id
FROM legacy_colors lc CROSS JOIN owner o
ON CONFLICT (code) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────
-- 2. サイズ補完 (旧 10 種)
--    name が既存 sizes と一致するもの (S/M/L/LL) は UPDATE で legacy_id 紐付け、
--    新規 (3L4L/16.0/18.0/20.0/M-L/00) は "L9xx" prefix で追加
-- ────────────────────────────────────────────────────────────────────
WITH legacy_sizes(legacy_name, conv_code, code_suffix) AS (VALUES
  ('00',    '0000', 'L90'),
  ('M-L',   '0ML0', 'L93'),
  ('3L4L',  '34L0', 'L96'),
  ('16.0',  '0160', 'L97'),
  ('18.0',  '0180', 'L98'),
  ('20.0',  '0200', 'L99')
), owner AS (SELECT id FROM users WHERE login_id = 'owner')
INSERT INTO sizes (code, name, item_conversion_code, legacy_id, created_by_user_id, updated_by_user_id)
SELECT ls.code_suffix, ls.legacy_name, ls.conv_code, ls.legacy_name, o.id, o.id
FROM legacy_sizes ls CROSS JOIN owner o
WHERE NOT EXISTS (SELECT 1 FROM sizes WHERE sizes.name = ls.legacy_name)
ON CONFLICT (code) DO NOTHING;

-- 既存 sizes (Iter 1 Seed の S/M/L/LL 等、name 一致) に legacy_id を紐付け
UPDATE sizes
   SET legacy_id = sizes.name
 WHERE sizes.name IN ('S','M','L','LL')
   AND sizes.legacy_id IS NULL;

-- ────────────────────────────────────────────────────────────────────
-- 3. 仕入先補完 (旧 11 種)
--    Iter 1 Seed の 336/404/437 は code 一致するため UPDATE で legacy_id のみ紐付け
--    残り 8 種は新規 INSERT
--    item_conversion_code (CHAR(1)) は旧 code 末尾 1 文字、衝突時は別文字
-- ────────────────────────────────────────────────────────────────────
WITH legacy_suppliers(legacy_code, name_ja, conv_char) AS (VALUES
  ('105','旧仕入先 105','5'),
  ('181','旧仕入先 181','1'),
  ('213','旧仕入先 213','3'),
  ('411','旧仕入先 411','I'),        -- 末尾 1 が "5"(105) と衝突なし? "1"(181) と衝突 → I
  ('433','旧仕入先 433','J'),
  ('434','旧仕入先 434','K'),
  ('801','旧仕入先 801','8'),
  ('888','旧仕入先 888','9')
),
owner AS (SELECT id FROM users WHERE login_id = 'owner'),
jp AS (SELECT id FROM countries WHERE code = '001')
INSERT INTO suppliers (
  code, name, official_name, item_conversion_code,
  country_id, supplier_type, alert_target,
  legacy_id, created_by_user_id, updated_by_user_id
)
SELECT
  ls.legacy_code,
  ls.name_ja,
  ls.name_ja,                        -- official_name 仮値 (業務担当者が後で更新)
  ls.conv_char,
  jp.id,
  1,                                 -- supplier_type=1 (工場、仮)
  0,
  ls.legacy_code,
  o.id, o.id
FROM legacy_suppliers ls CROSS JOIN owner o CROSS JOIN jp
ON CONFLICT (code) DO NOTHING;

-- Iter 1 Seed の 336/404/437 に legacy_id を紐付け
UPDATE suppliers
   SET legacy_id = suppliers.code
 WHERE suppliers.code IN ('336','404','437')
   AND suppliers.legacy_id IS NULL;

COMMIT;

-- ────────────────────────────────────────────────────────────────────
-- 確認クエリ
-- ────────────────────────────────────────────────────────────────────
SELECT 'colors'   AS master, COUNT(*) AS total, COUNT(legacy_id) AS with_legacy FROM colors
UNION ALL SELECT 'sizes',     COUNT(*), COUNT(legacy_id) FROM sizes
UNION ALL SELECT 'suppliers', COUNT(*), COUNT(legacy_id) FROM suppliers
ORDER BY master;
-- 期待値 (Iter 1 Seed + 補完):
--   colors    : 35+ 件 (4 + 31)、with_legacy 31 件
--   sizes     : 11+ 件 (5 Seed + 6 補完)、with_legacy 10 件
--   suppliers : 11+ 件 (3 Seed + 8 補完)、with_legacy 11 件
