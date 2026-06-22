-- ════════════════════════════════════════════════════════════════════
-- Iteration 5: 生産管理拡張 5 テーブルを「既存 DB」へ適用するマイグレーション
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   生産管理拡張 (BOM / 生産指示書 / 素材発注書 / 未済) の 5 テーブルは
--   db/init/05-production.sql に定義されている。db/init/*.sql は「空 DB の初期化」
--   でのみ適用される:
--     - ローカル: docker-compose の docker-entrypoint-initdb.d (初回起動時)
--     - RDS    : run-migrations.sh action=init (users 不在の空 DB のみ)
--   既に init 済の本番 RDS (iter-4 で初期化) には反映されないため、生産管理 API が
--   参照するテーブルが存在せず 500 (relation does not exist) となる。
--   既存 DB へは本マイグレーション (action=migrate) で追加適用する。
--
-- 単一の正規定義 (Single Source of Truth, CLAUDE.md 原則3/5):
--   スキーマ定義の二重管理を避けるため、本ファイルは正規定義
--   db/init/05-production.sql を psql の \ir で取込むだけにする。これにより
--   init 用とマイグレーション用で定義が乖離しない。
--   取込先の全 DDL は CREATE ... IF NOT EXISTS / CREATE INDEX IF NOT EXISTS /
--   INSERT ... ON CONFLICT DO NOTHING で冪等 (原則2)。再適用しても既存データ・
--   既存26テーブルを破壊しない (原則7、追加のみ)。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が postgres:16-alpine の psql で本ファイルを -f 実行し、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。
--   ※ \ir は psql のメタコマンドのため、本ファイル単体を pgAdmin 等の GUI に
--      貼り付けて実行することはできない。GUI で手動適用する場合は正規定義
--      db/init/05-production.sql を直接実行すること。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- 正規定義を取込む (\ir = include_relative: 本ファイルのあるディレクトリ基準で解決)。
-- db/migration/ から見た db/init/05-production.sql の相対パス。
\ir ../init/05-production.sql

COMMIT;
