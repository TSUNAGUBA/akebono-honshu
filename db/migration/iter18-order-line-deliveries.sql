-- ════════════════════════════════════════════════════════════════════
-- Iteration 18: 分納×倉庫の多次元明細 (PR5b) — purchase_order_line_deliveries 新規テーブル追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧 spec 発注明細 No.6「発注明細日1-11」/ No.7-17「発注明細数1〜11」/ No.18-23「倉庫1〜3 入数/
--   発注数」。現行は「1 明細 = 1 SKU = 1 数量 (quantity) = 1 単価」のフラット構造で、分納 (複数納期×
--   数量) も倉庫別数量も持てなかった (本バッチ最大ギャップ)。設計判断 Q1=可変の分納テーブル /
--   Q2=明細を倉庫×納期で多次元化 に基づき、発注明細 (purchase_order_lines) に可変の分納子テーブルを
--   新設し、1 明細を「(倉庫 × 納期) の分納行」の集合で多次元化する。
--
--   設計 (warehouse_id / delivery_date は NULL 許容):
--     - purchase_order_line_id は ON DELETE CASCADE。親明細の物理削除時に分納行も消える
--       (発注の編集は明細全削除→再 INSERT 方式のため、編集時も CASCADE で分納が消えてから再構築される)。
--     - warehouse_id は warehouses への任意参照 FK (NULL=倉庫未指定)。delivery_date NULL=発注明細日未指定。
--     - quantity は CHECK (quantity > 0)。pack_quantity (倉庫別入数) は NULL 許容。seq は表示順 (配列順採番)。
--     - 発注の作成/更新時に全置換するコレクション (アソート/セット明細 = product_set_components や
--       BOM = product_materials と同じ全置換パターン)。明細が分納 0 件の場合は従来どおり単一明細
--       (purchase_order_lines.quantity を単一数量として使う) として正常に扱う。
--
--   下位互換 (最重要):
--     - 分納は明細あたり任意 (0 件可)。分納 0 件 = 従来どおりの単一明細 (既存発注・既存フローは完全に不変)。
--     - 分納 N 件 = その明細は分納で構成。purchase_order_lines.quantity には分納数量の合計 (SUM) を
--       設定する (既存の subtotal GENERATED 列 = quantity * unit_price_snapshot がそのまま正しい金額)。
--     - 既存 purchase_order_lines.quantity / pack_quantity 列は削除しない (既存データ・GENERATED subtotal・
--       既存フローの後方互換のため)。本マイグレーションは新規テーブル追加のみで既存テーブル・列・
--       データは一切変更しない (quantity / pack_quantity も変えない)。
--
--   本テーブルは db/init/04-orders.sql の CREATE TABLE / CREATE INDEX に反映済だが、
--   db/init/*.sql は「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ
--   適用される。既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate) で
--   追加適用する。CREATE TABLE / CREATE INDEX は init 側 (04-orders.sql) と byte 等価。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要 (iter4〜iter17 と同方式)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS でガードしているため、再実行しても
--     既存データ・既存スキーマを破壊しない (追加のみ)。新規テーブルのため既存データへの影響なし。
--   - 下位互換 (原則7): 新規テーブル追加のみで既存テーブル・既存行に変更を加えない。既存の発注明細
--     は分納 0 件 = 単一明細として扱われ、挙動は従来と完全に同一。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 04-orders.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── purchase_order_line_deliveries 新規テーブル (init 04-orders.sql と byte 等価) ──
CREATE TABLE IF NOT EXISTS purchase_order_line_deliveries (
    id                       BIGSERIAL PRIMARY KEY,
    purchase_order_line_id   BIGINT       NOT NULL REFERENCES purchase_order_lines(id) ON DELETE CASCADE,
    warehouse_id             BIGINT       NULL REFERENCES warehouses(id),          -- 倉庫 (NULL=倉庫未指定)
    delivery_date            DATE         NULL,                                     -- 納期 / 発注明細日 (NULL=発注明細日未指定)
    quantity                 INTEGER      NOT NULL,                                 -- 発注明細数 (分納数量)
    pack_quantity            INTEGER      NULL,                                     -- 倉庫別入数
    seq                      SMALLINT     NOT NULL DEFAULT 1,                       -- 表示順 (配列順で採番)
    created_at               TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id       BIGINT       NOT NULL REFERENCES users(id),
    updated_at               TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id       BIGINT       NOT NULL REFERENCES users(id),
    CONSTRAINT chk_pold_quantity CHECK (quantity > 0)
);
CREATE INDEX IF NOT EXISTS idx_pold_line ON purchase_order_line_deliveries (purchase_order_line_id);

COMMIT;
