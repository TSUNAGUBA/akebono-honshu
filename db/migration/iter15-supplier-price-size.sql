-- ════════════════════════════════════════════════════════════════════
-- Iteration 15: サイズ別仕入単価 (PR2) — product_supplier_prices へ size_id 追加 + 一意/現単価インデックス再構成
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   現行 product_supplier_prices は family × supplier 単位で仕入単価を保持し、BR-04 で有効日
--   (effective_from) 履歴を持つ (サイズ非依存)。旧 spec No.35「仕入単価」はサイズ別であり、
--   設計判断 Q4=「サイズ別必要」。本マイグレーションは size_id を追加し、一意制約/現単価インデックスを
--   size 次元込みに再構成する。これらは db/init/03-products.sql の CREATE TABLE / CREATE INDEX に
--   反映済だが、db/init/*.sql は「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動)
--   でのみ適用される。既に init 済の本番 RDS には反映されないため、本マイグレーション
--   (action=migrate) で ALTER TABLE / インデックス再構成により追加適用する。
--
--   設計 (size_id の意味):
--     - NULL = 全サイズ共通の既定単価 (従来挙動)。既存行は NULL のまま = そのまま全サイズ既定として
--       現単価解決される (下位互換)。
--     - 非NULL = そのサイズ専用単価 (既定をオーバーライド)。
--   現単価解決のフォールバック: あるサイズについて「そのサイズ専用の有効行があればそれを、
--   無ければ NULL-size (全サイズ既定) の有効行を採用」。サイズ別単価が一切無い商品では従来と
--   同じ結果 (全サイズ既定のみ)。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要 (iter4〜iter14 と同方式)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS でガードしているため、再実行しても既存データ・
--     既存スキーマを破壊しない (追加のみ)。size_id は NULL 許容、既存行は NULL のまま下位互換。
--   - インデックス再構成は DROP INDEX IF EXISTS → CREATE UNIQUE/INDEX IF NOT EXISTS で冪等。
--     旧 uq_psp_family_supplier_from は CONSTRAINT ではなく INDEX (03-products.sql で CREATE
--     UNIQUE INDEX 定義) のため DROP INDEX で除去する。新旧でインデックス名が異なるため、
--     DROP は 2 回目以降 no-op、CREATE は IF NOT EXISTS でガードされる。
--   - 下位互換 (原則7): size_id 追加時点で既存行は全て size_id=NULL となり、COALESCE(size_id,-1)
--     式一意インデックス上は従来と同一バケット (-1) に収まるため、既存データは新一意制約を必ず満たす
--     (CREATE が既存データで失敗しない)。size_id=NULL 行の現単価解決は従来と完全に同じ。
--   注: Postgres は NULL を distinct 扱いするため、単純な (…, size_id, …) 一意インデックスでは
--      size_id=NULL の既定行どうしの一意性が緩む。これを避けるため COALESCE(size_id, -1) 式
--      一意インデックスを用いる (移植性重視。PG15+ の UNIQUE NULLS NOT DISTINCT は使わない)。
--      sizes.id は BIGSERIAL ≥ 1 のため -1 sentinel は実 size_id と衝突しない。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 03-products.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── (1) size_id 列追加 (NULL 許容 = 全サイズ既定、ADD COLUMN IF NOT EXISTS で冪等) ──
ALTER TABLE product_supplier_prices
    ADD COLUMN IF NOT EXISTS size_id BIGINT NULL REFERENCES sizes(id);  -- サイズ別仕入単価 (PR2、NULL=全サイズ共通既定)

-- ── (2) 一意インデックスを size 次元込みに再構成 (旧: family+supplier+effective_from) ──
--    旧インデックスを除去し、COALESCE(size_id,-1) を含む新インデックスを作成する。新旧で名前が
--    異なるため冪等 (2 回目以降は DROP no-op / CREATE は IF NOT EXISTS で skip)。
DROP INDEX IF EXISTS uq_psp_family_supplier_from;
CREATE UNIQUE INDEX IF NOT EXISTS uq_psp_family_supplier_size_from
    ON product_supplier_prices (product_family_id, supplier_id, COALESCE(size_id, -1), effective_from)
    WHERE is_deleted = FALSE;

-- ── (3) 現単価ルックアップ用インデックスを size 込みに再構成 (選択性確保) ──
--    現単価解決 (family, supplier, size の現在有効行) と BR-04 履歴クローズの両方が size で絞るため、
--    同名インデックスを drop→再作成して列を拡張する (非 UNIQUE のため COALESCE は選択性目的)。
DROP INDEX IF EXISTS idx_psp_family_current;
CREATE INDEX IF NOT EXISTS idx_psp_family_current
    ON product_supplier_prices (product_family_id, supplier_id, COALESCE(size_id, -1), effective_from DESC)
    WHERE effective_to IS NULL AND is_deleted = FALSE;

COMMIT;
