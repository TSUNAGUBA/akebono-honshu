#!/usr/bin/env bash
# ===========================================================================
# OpenAPI 仕様 (docs/api/openapi.json) の生成 (AKB-DOC-12 §4-1)
#
# 方式: バックエンドをスタブ環境変数で起動し、Swashbuckle が公開する
#   /swagger/v1/swagger.json を取得して整形保存する。
#   - DB 接続は起動時に張られない (EF は遅延接続、パーティション保守ジョブは
#     失敗しても warning のみで継続する非ブロッキング設計) ためダミー値でよい
#   - Firebase__ProjectId は起動時検証があるためスタブ値を与える
#
# 使い方:
#   bash scripts/generate-openapi.sh            # docs/api/openapi.json を再生成
#   CI (ci.yml openapi-check) は再生成結果と committed 版を diff し、乖離があれば fail
#   (実装と仕様の一致を CI で検証する。仕様を変えたら本スクリプトを再実行してコミットする)
# ===========================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="docs/api/openapi.json"
PORT="${OPENAPI_PORT:-5199}"
LOG="$(mktemp)"

cleanup() {
  [ -n "${APP_PID:-}" ] && kill "${APP_PID}" 2>/dev/null || true
  rm -f "${LOG}"
}
trap cleanup EXIT

echo "==> バックエンドをスタブ構成で起動 (port ${PORT})"
# --no-launch-profile: launchSettings.json の applicationUrl (localhost:5000) が
# ASPNETCORE_URLS を上書きするのを防ぐ (CI・ローカル共通で PORT に固定する)
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://127.0.0.1:${PORT}" \
Firebase__ProjectId=openapi-spec-generation \
ConnectionStrings__Postgres="Host=127.0.0.1;Port=1;Database=openapi_stub;Username=stub;Password=stub" \
dotnet run --project src/Backend/Presentation --no-launch-profile >"${LOG}" 2>&1 &
APP_PID=$!

echo "==> /swagger/v1/swagger.json の応答待ち (最大 120 秒)"
READY=0
for i in $(seq 1 120); do
  if curl -sf --noproxy '*' "http://127.0.0.1:${PORT}/swagger/v1/swagger.json" -o /dev/null 2>/dev/null; then
    READY=1
    break
  fi
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    echo "ERROR: バックエンドの起動に失敗しました" >&2
    tail -30 "${LOG}" >&2
    exit 1
  fi
  sleep 1
done
if [ "${READY}" != "1" ]; then
  # タイムアウト検出なしで最終 curl へ落ちると 0 byte の openapi.json を残しかねないため明示 fail
  echo "ERROR: ${PORT} で swagger.json が 120 秒以内に応答しませんでした" >&2
  tail -30 "${LOG}" >&2
  exit 1
fi

mkdir -p "$(dirname "${OUT}")"
# キー順を安定化して diff 可能にする (jq -S)
curl -sf --noproxy '*' "http://127.0.0.1:${PORT}/swagger/v1/swagger.json" | jq -S . > "${OUT}"
echo "==> 生成完了: ${OUT} ($(wc -c < "${OUT}") bytes)"
