-- ════════════════════════════════════════════════════════════════════
-- Iteration 9: 発注書 国内/海外 旧項目パリティ Phase B — purchase_orders / purchase_order_lines へ追加列
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   発注書 (purchase_orders) に旧システム由来の 国内/海外 区分と海外発注情報 8 項目
--   (発注区分 / 荷揚地 / 得意先 / 工場出荷日 / 検品所出荷日 / 海外出港日 /
--    納入倉庫2 / 納入倉庫3)、および発注明細 (purchase_order_lines) に 3 項目
--   (入数 / 見積単価 / 仮番号スナップショット) を追加する。これらは
--   db/init/04-orders.sql の CREATE TABLE に反映済だが、db/init/*.sql は
--   「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter8 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS、制約 (FK) 追加は pg_constraint を引いた DO ブロックで
--     ガードしているため、再実行しても既存データ・既存スキーマを破壊しない (追加のみ)。
--   - is_overseas は NOT NULL DEFAULT FALSE。既存行は自動的に FALSE (=国内) が入り、
--     下位互換を維持する (既存発注書は国内・海外項目 NULL で表示される)。
--   - is_overseas 以外の追加列は全て NULL 許容。既存行は NULL のまま残る。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 04-orders.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── purchase_orders 列追加 (is_overseas のみ NOT NULL DEFAULT FALSE、他は NULL 許容) ──
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS is_overseas              BOOLEAN      NOT NULL DEFAULT FALSE;  -- 発注区分 (国内=false/海外=true)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS landing_place            VARCHAR(128) NULL;                    -- 荷揚地 / Port of entry
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS customer_ref             VARCHAR(128) NULL;                    -- 得意先 / 受注先
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS factory_shipping_date    DATE         NULL;                    -- 工場出荷日
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS inspection_shipping_date DATE         NULL;                    -- 検品所出荷日
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS overseas_departure_date  DATE         NULL;                    -- 海外出港日
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS warehouse2_id            BIGINT       NULL;                    -- 納入倉庫2
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS warehouse3_id            BIGINT       NULL;                    -- 納入倉庫3

-- ── purchase_order_lines 列追加 (全 NULL 許容) ──
ALTER TABLE purchase_order_lines ADD COLUMN IF NOT EXISTS pack_quantity               INTEGER       NULL;            -- 倉庫1入数 / 入数
ALTER TABLE purchase_order_lines ADD COLUMN IF NOT EXISTS estimate_unit_price         NUMERIC(12,2) NULL;            -- 見積単価
ALTER TABLE purchase_order_lines ADD COLUMN IF NOT EXISTS provisional_number_snapshot VARCHAR(64)   NULL;            -- 仮番号 (商品 family からコピー)

-- ── 外部キー (納入倉庫2/3 → warehouses)。pg_constraint を引いて未存在のときのみ追加 (冪等)。
--    NULL 許容のため任意参照、cascade なし。 ──
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_warehouse2') THEN
        ALTER TABLE purchase_orders
            ADD CONSTRAINT fk_po_warehouse2
            FOREIGN KEY (warehouse2_id) REFERENCES warehouses(id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_warehouse3') THEN
        ALTER TABLE purchase_orders
            ADD CONSTRAINT fk_po_warehouse3
            FOREIGN KEY (warehouse3_id) REFERENCES warehouses(id);
    END IF;
END $$;

COMMIT;
