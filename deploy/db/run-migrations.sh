#!/usr/bin/env bash
# ===========================================================================
# RDS スキーマ初期化 / マイグレーション ランナー
# (GitHub Actions db-migrate.yml から EC2 上で SSH 経由で起動)。
#
# EC2 を経由する理由: RDS は通常プライベートサブネット (SG が EC2 を許可) のため、
#   GitHub ホストランナー (動的 IP) から直接到達できない。VPC 内の EC2 を踏み台にする。
#
# psql は EC2 ホストに導入不要 — 使い捨ての postgres:16-alpine コンテナで実行する。
#
# 前提 (CI が同ディレクトリに scp 済):
#   - db/                  (リポジトリの db/init, db/migration 一式)
#   - .dbenv (任意)        (DB 接続情報。CI が repository secrets から生成。読込後に削除)
# 環境変数 (.dbenv もしくは直接):
#   - ACTION              init | migrate (既定: migrate)
#   - DB_HOST DB_PORT DB_NAME DB_ADMIN_USER DB_ADMIN_PASSWORD
#
# 冪等性 (CLAUDE.md 原則 2 / 7):
#   - init  : public.users が既に存在すれば中止 (既存データ保護)。db/init/01..04 を投入後、
#             現行スキーママイグレーションを「適用済み」として schema_migrations に baseline 記録。
#             (db/init は現行スキーマを反映済 = iter4-tz-to-jst-naive.sql 等は init に内包される)
#   - migrate: schema_migrations 台帳に無いマイグレーションのみ順に適用 (再実行で二重適用しない)。
#   - MIG-3 (mig-3-*.sql) は CSV データ取込のため除外 — UI (/admin/legacy-import) から実施する
#     (db/migration/README.md の運用方針を維持)。
# ===========================================================================
set -euo pipefail
cd "$(dirname "$0")"

# DB 接続情報を .dbenv から読み込み (CI が scp)、読込後に即削除して EC2 に残さない。
if [ -f .dbenv ]; then
  set -a
  # shellcheck disable=SC1091
  . ./.dbenv
  set +a
  rm -f .dbenv
fi

ACTION="${ACTION:-migrate}"
: "${DB_HOST:?DB_HOST required}"
: "${DB_NAME:?DB_NAME required}"
: "${DB_ADMIN_USER:?DB_ADMIN_USER required}"
: "${DB_ADMIN_PASSWORD:?DB_ADMIN_PASSWORD required}"
DB_PORT="${DB_PORT:-5432}"
PG_IMAGE="postgres:16-alpine"

if [ ! -d db/init ] || [ ! -d db/migration ]; then
  echo "ERROR: db/init または db/migration が見つかりません (scp 失敗?)" >&2
  exit 1
fi

# 使い捨て psql コンテナ。db/ を read-only マウントし、PG* で接続情報を渡す。
psql_run() {
  docker run --rm -i \
    -e PGPASSWORD="${DB_ADMIN_PASSWORD}" \
    -e PGHOST="${DB_HOST}" -e PGPORT="${DB_PORT}" \
    -e PGUSER="${DB_ADMIN_USER}" -e PGDATABASE="${DB_NAME}" \
    -v "${PWD}/db:/db:ro" \
    "${PG_IMAGE}" psql -v ON_ERROR_STOP=1 "$@"
}

ensure_ledger() {
  psql_run -q -c "CREATE TABLE IF NOT EXISTS schema_migrations (
      filename   text PRIMARY KEY,
      checksum   text NOT NULL,
      applied_at timestamp NOT NULL DEFAULT now()
  );"
}

# 自動適用対象 = db/migration/*.sql のうち MIG-3 データ取込 (mig-3-*) を除いたスキーマ系。
schema_migration_files() {
  find db/migration -maxdepth 1 -type f -name '*.sql' ! -name 'mig-3-*' | sort
}

users_exists() {
  [ "$(psql_run -tA -c "SELECT to_regclass('public.users') IS NOT NULL;")" = "t" ]
}

# 適用済みなら台帳の checksum を返す。未適用なら空文字。
applied_checksum() {
  psql_run -tA -c "SELECT checksum FROM schema_migrations WHERE filename = '$1';"
}

record_applied() {
  psql_run -q -c "INSERT INTO schema_migrations (filename, checksum) VALUES ('$1', '$2')
      ON CONFLICT (filename) DO NOTHING;"
}

checksum() { sha256sum "$1" | awk '{print $1}'; }

# DB 接続前提チェック。set -e 下で失敗すれば即 fail-fast し、後続の users_exists 等が
# 接続失敗を「テーブル無し」と誤判定して保護ロジックを素通りするのを防ぐ (reviewer 指摘)。
echo "==> DB 接続確認 (${DB_HOST}:${DB_PORT}/${DB_NAME})"
psql_run -q -c "SELECT 1;" >/dev/null

case "${ACTION}" in
  init)
    echo "==> action=init: 空 DB へスキーマ投入"
    if users_exists; then
      echo "ERROR: public.users が既に存在します。初期化を中止します (既存データ保護)。" >&2
      echo "       スキーマ更新は action=migrate を使用してください。" >&2
      exit 1
    fi
    for f in db/init/01-schema.sql db/init/02-masters.sql db/init/03-products.sql db/init/04-orders.sql; do
      echo "   applying ${f}"
      psql_run -f "/${f}"
    done
    ensure_ledger
    # baseline: db/init は現行スキーマを反映済のため、現行マイグレーションは「実行せず適用済み記録」する。
    while IFS= read -r f; do
      [ -z "${f}" ] && continue
      bn="$(basename "${f}")"
      echo "   baseline (mark applied without running): ${bn}"
      record_applied "${bn}" "$(checksum "${f}")"
    done < <(schema_migration_files)
    echo "==> init 完了"
    ;;

  migrate)
    echo "==> action=migrate: 未適用スキーママイグレーション適用 (MIG-3 データ取込は除外)"
    if ! users_exists; then
      echo "ERROR: public.users がありません。先に action=init を実行してください。" >&2
      exit 1
    fi
    ensure_ledger
    applied=0
    skipped=0
    while IFS= read -r f; do
      [ -z "${f}" ] && continue
      bn="$(basename "${f}")"
      cur="$(checksum "${f}")"
      stored="$(applied_checksum "${bn}")"
      if [ -n "${stored}" ]; then
        # 適用済み。前進専用方針のため再適用はしないが、内容が変化していれば警告 (ドリフト検知)。
        if [ "${stored}" != "${cur}" ]; then
          echo "::warning::${bn} は適用済みだが内容が台帳 checksum と不一致 (stored=${stored:0:12}.. current=${cur:0:12}..)。前進専用のため skip。要確認。"
        else
          echo "   skip (applied): ${bn}"
        fi
        skipped=$((skipped + 1))
        continue
      fi
      echo "   applying: ${bn}"
      psql_run -f "/${f}"
      record_applied "${bn}" "${cur}"
      applied=$((applied + 1))
    done < <(schema_migration_files)
    echo "==> migrate 完了 (applied=${applied}, skipped=${skipped})"
    ;;

  *)
    echo "ERROR: 不明な ACTION '${ACTION}' (init | migrate)" >&2
    exit 1
    ;;
esac
