#!/usr/bin/env bash
# ===========================================================================
# EC2 上で実行される Backend デプロイスクリプト
# (GitHub Actions deploy-backend.yml から SSH 経由で起動)。
#
# 前提 (CI が同ディレクトリに scp 済):
#   - docker-compose.prod.yml
#   - .env             (repository secrets から生成した本番設定)
#   - .ghcr_token      (GHCR pull 用の一時トークン。使用後に削除)
# 環境変数 (SSH 経由で注入):
#   - GHCR_USER        (GHCR ログインユーザ = github.actor)
#
# 設計:
#   - 原則 4 (非ブロッキング) ではなく「デプロイは失敗を明確に伝える」方針。
#     health check 失敗時は非 0 終了し、CI を赤にしてオペレーターへ通知する。
#   - トークンは login 後にファイル削除 + 終了時 docker logout で EC2 に残さない。
# ===========================================================================
set -euo pipefail
cd "$(dirname "$0")"

COMPOSE=(docker compose -f docker-compose.prod.yml)

# --- GHCR ログイン (private image の pull に必要) ---
if [ -f .ghcr_token ]; then
  : "${GHCR_USER:?GHCR_USER required}"
  docker login ghcr.io -u "${GHCR_USER}" --password-stdin < .ghcr_token
  rm -f .ghcr_token
fi

echo "==> pull image"
"${COMPOSE[@]}" pull
echo "==> up -d"
"${COMPOSE[@]}" up -d --remove-orphans

# トークンを docker 認証キャッシュ (~/.docker/config.json) に残さない。
# 再起動 (restart: unless-stopped) は local image を使うため再ログイン不要。
docker logout ghcr.io >/dev/null 2>&1 || true

# --- health check ---
PORT="$(grep -E '^BACKEND_HOST_PORT=' .env | cut -d= -f2- || true)"
PORT="${PORT:-8080}"
echo "==> health check http://localhost:${PORT}/health"
ok=0
for i in $(seq 1 40); do
  if curl --fail --silent --max-time 4 "http://localhost:${PORT}/health" >/dev/null 2>&1; then
    echo "    healthy after ~$((i * 3))s"
    ok=1
    break
  fi
  sleep 3
done

# 旧イメージを掃除 (ディスク逼迫防止)。失敗してもデプロイ結果には影響させない。
docker image prune -f >/dev/null 2>&1 || true

if [ "${ok}" != 1 ]; then
  echo "ERROR: health check failed. recent backend logs:" >&2
  "${COMPOSE[@]}" logs --tail 80 backend >&2 || true
  exit 1
fi
echo "==> deploy 完了"
