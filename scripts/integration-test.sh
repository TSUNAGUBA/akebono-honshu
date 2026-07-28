#!/usr/bin/env bash
# ===========================================================================
# 結合テスト: バックエンドを「実スキーマの使い捨て PostgreSQL」に対して起動し、
# 実際に HTTP を叩いて検証する。ビルド/型チェックだけの旧ゲートでは捕捉できなかった
# 「起動不能 (スキーマ移行漏れ)」「CORS 設定崩れ」「ランタイム 500」を CI で捕捉する。
#
# 前提 (deploy.yml / ci.yml の job が用意する):
#   - サービスコンテナ postgres:16 が localhost:5432 で待受 (POSTGRES_DB=akebono_honshu,
#     POSTGRES_USER=postgres, POSTGRES_PASSWORD=postgres)。
#   - dotnet SDK 8 / psql クライアント / jq が利用可能。
#
# 検証内容:
#   1. db/init/*.sql を投入 (本番初期化と同じ経路)。akebono_app ロールもここで作られる。
#   2. backend を akebono_app 接続・テスト用 CORS Origin で起動。
#   3. /health == 200 (= 起動成功 = 起動時スキーマガード通過 = コードとスキーマの整合)。
#   4. CORS プリフライト (OPTIONS+Origin) が Access-Control-Allow-Origin を返す。
#   5. /swagger/v1/swagger.json がコミット済み docs/api/openapi.json と一致 (仕様と実装の整合)。
# ===========================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

PG_HOST="${PGHOST:-localhost}"
PG_PORT="${PGPORT:-5432}"
PG_ADMIN_USER="${PGADMIN_USER:-postgres}"
PG_ADMIN_PW="${PGADMIN_PW:-postgres}"
DB_NAME="${IT_DB_NAME:-akebono_honshu}"
APP_DB_USER="akebono_app"        # db/init/08-tenancy-rls.sql が作成 (PASSWORD 'localdev')
APP_DB_PW="localdev"
APP_PORT="${IT_APP_PORT:-5199}"
TEST_ORIGIN="https://it.frontend.example"
LOG="$(mktemp)"

cleanup() {
  if [ -n "${APP_PID:-}" ]; then
    kill "${APP_PID}" 2>/dev/null || true
  fi
  rm -f "${LOG}"
}
trap cleanup EXIT

# --- 1. db/init を投入 (プレーン SQL・番号順) ------------------------------------
echo "==> db/init/*.sql を ${DB_NAME} へ投入"
export PGPASSWORD="${PG_ADMIN_PW}"
for f in db/init/*.sql; do
  psql -v ON_ERROR_STOP=1 -q -h "${PG_HOST}" -p "${PG_PORT}" -U "${PG_ADMIN_USER}" -d "${DB_NAME}" -f "${f}"
done
unset PGPASSWORD
echo "==> db/init 投入完了"

# --- 2. backend を実スキーマに対して起動 ----------------------------------------
echo "==> backend 起動 (akebono_app 接続, port ${APP_PORT})"
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://127.0.0.1:${APP_PORT}" \
Firebase__ProjectId=integration-test \
Cors__Origins="${TEST_ORIGIN}" \
ConnectionStrings__Postgres="Host=${PG_HOST};Port=${PG_PORT};Database=${DB_NAME};Username=${APP_DB_USER};Password=${APP_DB_PW};SSL Mode=Disable" \
dotnet run --project src/Backend/Presentation --no-launch-profile > "${LOG}" 2>&1 &
APP_PID=$!

# --- 3. /health 応答待ち + 200 検証 (= 起動時スキーマガード通過) -----------------
echo "==> /health 応答待ち (最大 120 秒)"
ready=0
for _ in $(seq 1 120); do
  code="$(curl -s -o /dev/null -w '%{http_code}' --noproxy '*' "http://127.0.0.1:${APP_PORT}/health" || true)"
  if [ "${code}" = "200" ]; then ready=1; break; fi
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    echo "::error::backend の起動に失敗しました (スキーマ移行漏れ等)。ログ末尾:" >&2
    tail -50 "${LOG}" >&2
    exit 1
  fi
  sleep 1
done
if [ "${ready}" != "1" ]; then
  echo "::error::/health が 120 秒以内に 200 を返しませんでした。ログ末尾:" >&2
  tail -50 "${LOG}" >&2
  exit 1
fi
echo "✅ /health 200 (起動成功・スキーマガード通過・コードとスキーマ整合)"

# --- 4. CORS プリフライトの検証 -------------------------------------------------
# UseCors は認証より前で OPTIONS を短絡処理するため、認証不要でヘッダを検証できる。
echo "==> CORS プリフライト検証 (Origin=${TEST_ORIGIN})"
cors_hdrs="$(curl -s -i -X OPTIONS --noproxy '*' \
  -H "Origin: ${TEST_ORIGIN}" \
  -H "Access-Control-Request-Method: GET" \
  "http://127.0.0.1:${APP_PORT}/api/maker/v1/attendance/state" | tr -d '\r')"
if ! printf '%s\n' "${cors_hdrs}" | grep -iqx "access-control-allow-origin: ${TEST_ORIGIN}"; then
  echo "::error::CORS プリフライトに Access-Control-Allow-Origin: ${TEST_ORIGIN} がありません。応答ヘッダ:" >&2
  printf '%s\n' "${cors_hdrs}" | head -20 >&2
  exit 1
fi
echo "✅ CORS プリフライト OK (Access-Control-Allow-Origin: ${TEST_ORIGIN})"

# --- 5. OpenAPI 仕様と実装の一致 -------------------------------------------------
echo "==> OpenAPI 仕様の一致検証"
curl -sf --noproxy '*' "http://127.0.0.1:${APP_PORT}/swagger/v1/swagger.json" | jq -S . > /tmp/it-openapi.json
if ! diff -u docs/api/openapi.json /tmp/it-openapi.json; then
  echo "::error::docs/api/openapi.json が実装と乖離しています。scripts/generate-openapi.sh を実行してコミットしてください。" >&2
  exit 1
fi
echo "✅ OpenAPI 一致"

echo "結合テスト: ALL PASS"
