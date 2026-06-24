-- ════════════════════════════════════════════════════════════════════
-- Iteration 19: 連絡文書 6 行 (構造化、PR6) — purchase_orders へ communication_line_1〜6 追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧 spec 発注明細 No.27-32「連絡文書01行〜06行」(選択式)。現行は発注ヘッダの単一自由テキスト
--   communication_text (TEXT) + テンプレ候補追記方式だった。これを spec の 01-06 列挙に忠実な
--   6 本の構造化行 (communication_line_1 … communication_line_6 TEXT NULL) にする。各行はテンプレート
--   選択式 (既存の連絡文書サジェスト GetCommunicationSuggestionsAsync を再利用) + 自由編集可。
--
--   設計方針 (後方互換最優先):
--     - 既存 communication_text 列は削除しない。新フローは 6 列を真実 (source of truth) とする。
--     - Excel(発注書): 連絡文書は 6 列を優先してレンダリング (line_1..6 の非 NULL を順に)。6 列が
--       全て NULL の発注 (= 旧データ) では communication_text にフォールバック (既存発注の表示を壊さない)。
--     - 編集ロード: 6 列から各スロットへ。6 列が全て空でも communication_text がある旧発注は、
--       communication_text を改行分割して 6 スロットへブリッジ表示する (フロント側、最大6行)。
--     - 保存: 6 列を書く。communication_text は新フローで書かない (旧データのみ保持)。
--
--   これらは db/init/04-orders.sql の CREATE TABLE に反映済だが、db/init/*.sql は
--   「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。新規列のみ = 既存データ非破壊。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要 (iter4〜iter18 と同方式)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS でガードしているため、再実行しても既存データ・
--     既存スキーマを破壊しない (追加のみ)。
--   - 下位互換 (原則7): communication_line_1〜6 は全て NULL 許容の新規列のため、既存発注行に
--     影響しない (既存行は 6 列とも NULL のまま)。既存 communication_text 列・値は一切変更しない。
--     旧クライアントの POST/PATCH も互換 (6 列未指定 = NULL 保存、DTO は末尾 nullable 追加)。
--     6 列が全 NULL の発注は Excel/編集ロードで communication_text にフォールバックするため、
--     既存発注の連絡文書表示は不変。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 04-orders.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── purchase_orders 連絡文書 6 行追加 (全 NULL 許容、ADD COLUMN IF NOT EXISTS で冪等)。
--    既存 communication_text 列は不変。新規列のみのため既存データ非破壊。 ──
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS communication_line_1 TEXT NULL;  -- 連絡文書01行 (spec 明細 No.27)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS communication_line_2 TEXT NULL;  -- 連絡文書02行 (spec 明細 No.28)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS communication_line_3 TEXT NULL;  -- 連絡文書03行 (spec 明細 No.29)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS communication_line_4 TEXT NULL;  -- 連絡文書04行 (spec 明細 No.30)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS communication_line_5 TEXT NULL;  -- 連絡文書05行 (spec 明細 No.31)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS communication_line_6 TEXT NULL;  -- 連絡文書06行 (spec 明細 No.32)

COMMIT;
