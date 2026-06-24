-- ════════════════════════════════════════════════════════════════════
-- Iteration 16: アソート/セット明細 (PR3) — product_set_components 新規テーブル追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   旧 spec 商品マスタ本体 No.37「品番：アソート／セット明細」(手入力)・No.38「数量：アソート／
--   セット明細」(手入力)。ある商品 (product_family) がアソート/セット品である場合に、その構成と
--   なる「子品番 + 数量」の明細コレクションを保持する。現行に該当機能・テーブルは無かったため新設する。
--
--   設計 (child_item_number は手入力テキスト・FK なし):
--     - child_item_number は手入力テキスト。products / product_families への FK は張らない
--       (旧システム品番/外部品番も子品番として許容するため、旧 spec の「手入力」に忠実)。
--     - 商品の作成/更新時に全置換するコレクション (BOM = product_materials / 色サイズ展開と同じ
--       パターン)。明細が 0 件の商品は通常商品 (非アソート/セット) として正常に扱う。
--     - product_family_id は ON DELETE CASCADE。親 family の物理削除時に明細も消える
--       (通常運用の論理削除では family 行が残るため明細も保持される)。
--
--   本テーブルは db/init/03-products.sql の CREATE TABLE / CREATE INDEX に反映済だが、
--   db/init/*.sql は「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ
--   適用される。既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate) で
--   追加適用する。CREATE TABLE / CREATE INDEX は init 側 (03-products.sql) と byte 等価。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要 (iter4〜iter15 と同方式)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS でガードしているため、再実行しても
--     既存データ・既存スキーマを破壊しない (追加のみ)。新規テーブルのため既存データへの影響なし。
--   - 下位互換 (原則7): 新規テーブル追加のみで既存テーブル・既存行に変更を加えない。既存の商品
--     (family) は明細 0 件 = 通常商品として扱われ、挙動は従来と完全に同一。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 03-products.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── product_set_components 新規テーブル (init 03-products.sql と byte 等価) ──
CREATE TABLE IF NOT EXISTS product_set_components (
    id                    BIGSERIAL PRIMARY KEY,
    product_family_id     BIGINT       NOT NULL REFERENCES product_families(id) ON DELETE CASCADE,
    child_item_number     VARCHAR(32)  NOT NULL,                                 -- 子品番 (手入力テキスト、spec No.37)
    quantity              INTEGER      NOT NULL,                                 -- 数量 (spec No.38)
    line_no               SMALLINT     NOT NULL DEFAULT 1,                       -- 表示順 (配列順で採番)
    created_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    updated_at            TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id    BIGINT       NOT NULL REFERENCES users(id),
    CONSTRAINT chk_psc_quantity CHECK (quantity > 0)
);
CREATE INDEX IF NOT EXISTS idx_psc_family ON product_set_components (product_family_id);

COMMIT;
