-- ════════════════════════════════════════════════════════════════════
-- Iteration 20: 帳票出力フォーム 手入力項目 — purchase_orders へ order_date / shipping_instruction_no 追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧システムの「発注書出力」画面 (発注書出力【海外用】) では「発注日」「出荷指示番号」「発注番号」を
--   出力前に手入力していた。本アプリでも帳票 (発注書 / 管理表) の出力をフォーム経由にし、これら 3 項目を
--   手入力して Excel に反映できるようにする。
--     - 発注番号 (order_no): 既存列。従来は初回 Excel 出力時に自動採番していたが、出力フォームで手入力
--       できるようにする (手入力が無く既存採番も無い初回出力のみ自動採番にフォールバック = 後方互換)。
--     - 発注日 (order_date): 新規列。帳票の「order date」に印字する。
--     - 出荷指示番号 (shipping_instruction_no): 新規列。旧画面の手入力欄に対応。
--   これら手入力値は発注に保存し、再出力時にフォームへ初期表示する (出力ごとの再入力を不要にする)。
--
--   これらは db/init/04-orders.sql の CREATE TABLE に反映済だが、db/init/*.sql は「空 DB の初期化」
--   (run-migrations.sh action=init / docker 初回起動) でのみ適用される。既に init 済の本番 RDS には
--   反映されないため、本マイグレーション (action=migrate) で ALTER TABLE により追加適用する。
--   新規列のみ = 既存データ非破壊。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要 (iter4〜iter19 と同方式)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS でガードしているため、再実行しても既存データ・
--     既存スキーマを破壊しない (追加のみ)。
--   - 下位互換 (原則7): order_date / shipping_instruction_no は NULL 許容の新規列のため、既存発注行に
--     影響しない (既存行は NULL のまま)。既存 order_no 列・値も一切変更しない。order_no の自動採番
--     フォールバックを残すため、旧クライアント (フォーム未経由) の出力も従来どおり動作する。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない (init 側 04-orders.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── purchase_orders 帳票出力フォーム 手入力項目追加 (NULL 許容、ADD COLUMN IF NOT EXISTS で冪等)。
--    既存 order_no 列は不変。新規列のみのため既存データ非破壊。 ──
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS order_date              DATE        NULL;  -- 発注日 (帳票 order date)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS shipping_instruction_no VARCHAR(32) NULL;  -- 出荷指示番号

COMMIT;
