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
#   - ACTION              init | migrate | reinit (既定: migrate)
#   - DB_HOST DB_PORT DB_NAME DB_ADMIN_USER DB_ADMIN_PASSWORD
#   - APP_DB_PASSWORD     アプリ接続ロール akebono_app のパスワード。init/reinit では必須
#                         (未設定はエラー終了)。db/init/08-tenancy-rls.sql はローカル既定値で
#                         akebono_app を作成するため、本変数で必ず上書きする (Secrets Manager
#                         由来を推奨)。migrate では任意 (設定時のみ ALTER ROLE)。
#   - CONFIRM_REINIT      reinit 時のみ必須。'yes' を明示した場合だけ実行される安全ゲート。
#
# 冪等性 (CLAUDE.md 原則 2 / 7):
#   - init  : public.users が既に存在すれば中止 (既存データ保護)。db/init/*.sql を番号順に
#             全投入後、現行スキーママイグレーションを「適用済み」として schema_migrations に
#             baseline 記録。
#             (db/init は現行スキーマを反映済 = iter4-tz-to-jst-naive.sql 等は init に内包される)
#   - migrate: schema_migrations 台帳に無いマイグレーションのみ順に適用 (再実行で二重適用しない)。
#   - reinit : プラットフォーム統合改修 (tenant_id/RLS/TIMESTAMPTZ 導入) のような破壊的
#             スキーマ再編用。public スキーマを DROP CASCADE して init と同じ手順を実行する。
#             稼働前 (保護すべき本番データが無い) 環境専用。CONFIRM_REINIT=yes が必須。
#   - MIG-3 (mig-3-*.sql) は CSV データ取込のため除外 — UI (/admin/legacy-import) から実施する
#     (db/migration/README.md の運用方針を維持)。
#
# RLS 注意 (プラットフォーム統合改修):
#   - 08-tenancy-rls.sql 以降、テナントスコープ表は FORCE ROW LEVEL SECURITY。
#     以後の migration スクリプトでデータを操作する場合は冒頭で
#     SET app.tenant_id = '<uuid>' を行うこと (管理ユーザにも RLS が適用される。
#     ただし RDS master user / superuser は PostgreSQL 仕様によりバイパスする場合がある)。
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
# 重要: -i は付けない。本関数は `while IFS= read -r f; do ... done < <(...)` ループの
# 内側から呼ばれる。`docker run -i` はホスト stdin をコンテナへ接続し、psql が読まなくても
# docker の stdin 転送がループの未処理入力 (次の移行ファイル行) を消費してしまう。結果、
# 2 件目以降のマイグレーション (例: iter5) が読まれず EOF になり適用されない。
# psql はここでは -c / -f file のみ使用し stdin を必要としないため -i は不要。
psql_run() {
  docker run --rm \
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

# アプリ接続ロール akebono_app のパスワードを APP_DB_PASSWORD で上書きする (設定時のみ)。
# 08-tenancy-rls.sql がローカル既定値 'localdev' で作成するため、本番では必須の後処理。
# 補助処理: 失敗しても本体フローは止めない (CLAUDE.md 原則 4)。パスワード内の ' は '' に
# エスケープして SQL リテラル化する。
apply_app_role_password() {
  if [ -z "${APP_DB_PASSWORD:-}" ]; then
    echo "::warning::APP_DB_PASSWORD 未設定のため akebono_app のパスワードはローカル既定値のままです。本番では必ず設定してください。"
    return 0
  fi
  local esc="${APP_DB_PASSWORD//\'/\'\'}"
  if psql_run -q -c "ALTER ROLE akebono_app WITH PASSWORD '${esc}';" >/dev/null; then
    echo "==> akebono_app パスワードを APP_DB_PASSWORD で更新しました"
  else
    echo "::warning::akebono_app パスワード更新に失敗しました (後続処理は継続)。手動で ALTER ROLE してください。"
  fi
}

# init / reinit ではアプリロールのパスワード設定を必須とする (既知の既定値 'localdev' の
# まま本番 DB が構築される事故を防ぐセキュリティゲート。migrate は既設定前提のため任意)。
require_app_db_password() {
  if [ -z "${APP_DB_PASSWORD:-}" ]; then
    echo "ERROR: APP_DB_PASSWORD が未設定です。init/reinit は akebono_app のパスワード設定が必須です。" >&2
    echo "       repository secret APP_DB_PASSWORD を設定して再実行してください (deploy/README.md §3.2)。" >&2
    exit 1
  fi
}


run_init_files() {
  # db/init の全スキーマ・シードファイルを番号順に適用 (01-schema..09-updated-at-triggers..)。
  # ハードコードせず glob することで、新規 init ファイル追加時の付け忘れを防ぐ
  # (原則1: 手動メンテを残さない。schema_migration_files と同じ find|sort パターン)。
  while IFS= read -r f; do
    [ -z "${f}" ] && continue
    echo "   applying ${f}"
    psql_run -f "/${f}"
  done < <(find db/init -maxdepth 1 -type f -name '*.sql' | sort)
  ensure_ledger
  # baseline: db/init は現行スキーマを反映済のため、現行マイグレーションは「実行せず適用済み記録」する。
  while IFS= read -r f; do
    [ -z "${f}" ] && continue
    bn="$(basename "${f}")"
    echo "   baseline (mark applied without running): ${bn}"
    record_applied "${bn}" "$(checksum "${f}")"
  done < <(schema_migration_files)
  apply_app_role_password
}

# DB 接続前提チェック。set -e 下で失敗すれば即 fail-fast し、後続の users_exists 等が
# 接続失敗を「テーブル無し」と誤判定して保護ロジックを素通りするのを防ぐ (reviewer 指摘)。
echo "==> DB 接続確認 (${DB_HOST}:${DB_PORT}/${DB_NAME})"
psql_run -q -c "SELECT 1;" >/dev/null

case "${ACTION}" in
  init)
    echo "==> action=init: 空 DB へスキーマ投入"
    require_app_db_password
    if users_exists; then
      echo "ERROR: public.users が既に存在します。初期化を中止します (既存データ保護)。" >&2
      echo "       スキーマ更新は action=migrate を、プラットフォーム統合改修のような" >&2
      echo "       破壊的再編は action=reinit (CONFIRM_REINIT=yes) を使用してください。" >&2
      exit 1
    fi
    run_init_files
    echo "==> init 完了"
    ;;

  reinit)
    echo "==> action=reinit: public スキーマを破棄して再初期化 (稼働前環境専用)"
    require_app_db_password
    if [ "${CONFIRM_REINIT:-}" != "yes" ]; then
      echo "ERROR: reinit は破壊的操作です。CONFIRM_REINIT=yes を明示してください。" >&2
      exit 1
    fi
    # 既存オブジェクトを全破棄 (テーブル・シーケンス・ポリシー含む)。ロール akebono_app は
    # スキーマ外のため残る (08-tenancy-rls.sql が冪等に再利用する)。
    psql_run -q -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;"
    run_init_files
    echo "==> reinit 完了"
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
    apply_app_role_password
    echo "==> migrate 完了 (applied=${applied}, skipped=${skipped})"
    ;;

  *)
    echo "ERROR: 不明な ACTION '${ACTION}' (init | migrate | reinit)" >&2
    exit 1
    ;;
esac
