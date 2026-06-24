-- ════════════════════════════════════════════════════════════════════
-- Iteration 17: 発注 旧項目パリティ追補 (PR5a) — 納品所出荷日 名称統一 + 発注明細 備考 追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧システム項目定義を正として「発注」の差異を埋める PR5a (PR5 の小粒部分)。本マイグレーションは
--   2 種類のスキーマ変更を適用する:
--     (1) purchase_orders.inspection_shipping_date を delivery_place_shipping_date にリネーム
--           (旧 spec 発注ヘッダ No.8「納品所出荷日」に対し現行実装は「検品所出荷日」だったため、
--            設計判断Q6 で名称統一。データは保持)。
--     (2) purchase_order_lines に remark 列を追加 (発注明細 備考、行レベル、spec 明細 No.26、NULL 許容)。
--
--   これらは db/init/04-orders.sql の CREATE TABLE に反映済だが、db/init/*.sql は
--   「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter16 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - リネーム (1) は information_schema を引いた DO ブロックで「inspection_shipping_date が存在し
--     delivery_place_shipping_date が無い場合のみ」実行するため冪等。再実行しても二重リネーム・
--     エラーにならない。RENAME COLUMN はデータを保持する (DROP+ADD ではない、iter14 と同作法)。
--   - 列追加 (2) は ADD COLUMN IF NOT EXISTS でガードしているため、再実行しても既存データ・
--     既存スキーマを破壊しない (追加のみ)。remark は NULL 許容、既存行は NULL のまま下位互換。
--   - 下位互換 (原則7):
--       (1) 既存の inspection_shipping_date 値は delivery_place_shipping_date にそのまま引き継がれる。
--           アプリ側 (Entity DeliveryPlaceShippingDate / DbContext HasColumnName("delivery_place_shipping_date") /
--           DTO / フロント bind deliveryPlaceShippingDate) も同 PR で更新済のため、リネーム後に
--           列名不一致は発生しない。
--       (2) remark は新規 NULL 許容列のため既存発注明細行に影響なし。DTO は末尾追加 (既定値 null)
--           のため旧クライアントの POST/PATCH も互換 (remark 未指定 = NULL 保存)。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 04-orders.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── (1) purchase_orders.inspection_shipping_date → delivery_place_shipping_date リネーム (設計判断Q6)。
--    information_schema を引いて「旧列が存在し かつ 新列が未存在」の場合のみ実行 (冪等)。
--    RENAME COLUMN はデータを保持するため、既存 inspection_shipping_date 値は引き継がれる。 ──
DO $$
BEGIN
    -- table_schema='public' で限定 (iter4-tz-to-jst-naive.sql / iter14 と同じ作法)。MIG-3 等で
    -- 他スキーマに同名テーブルが存在しても本番 public スキーマの列のみを判定対象にし、誤判定を防ぐ。
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'purchase_orders' AND column_name = 'inspection_shipping_date'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'purchase_orders' AND column_name = 'delivery_place_shipping_date'
    ) THEN
        ALTER TABLE purchase_orders RENAME COLUMN inspection_shipping_date TO delivery_place_shipping_date;
    END IF;
END $$;

-- ── (2) purchase_order_lines.remark 追加 (発注明細 備考、NULL 許容、ADD COLUMN IF NOT EXISTS で冪等) ──
ALTER TABLE purchase_order_lines ADD COLUMN IF NOT EXISTS remark TEXT NULL;  -- 発注明細 備考 (行レベル、spec 明細 No.26)

COMMIT;
