#!/usr/bin/env bash
# ===========================================================================
# EC2 上で実行される Backend デプロイスクリプト
# (GitHub Actions deploy-backend.yml から SSH 経由で起動)。
#
# 既存 nginx-proxy への相乗り構成: backend はホストポートを公開せず、
# 共有ネットワーク経由で nginx-proxy からのみ到達する。よってヘルスチェックは
# localhost のポート curl ではなく **コンテナの health 状態** で判定する。
#
# 前提 (CI が同ディレクトリに scp 済):
#   - docker-compose.prod.yml
#   - .env             (repository secrets から生成した本番設定 + VIRTUAL_HOST 等)
#   - .ghcr_token      (GHCR pull 用の一時トークン。使用後に削除)
# 環境変数 (SSH 経由で注入):
#   - GHCR_USER        (GHCR ログインユーザ = github.actor)
#
# 設計:
#   - health check (主要フロー) は fail-fast: 失敗時は非 0 終了し CI を赤にして通知する。
#   - 補助処理 (image prune / docker logout) は `|| true` で非ブロッキング化 (原則 4 遵守)。
#   - トークンは login 後にファイル削除 + 終了時 docker logout で EC2 に残さない。
# ===========================================================================
set -euo pipefail
cd "$(dirname "$0")"

SERVICE="backend"                      # docker-compose.prod.yml のサービス名
CONTAINER_NAME="akebono-honshu-api"    # 同 container_name と一致させること
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
docker logout ghcr.io >/dev/null 2>&1 || true

# --- health check (ポート非公開のためコンテナの health 状態で判定) ---
echo "==> health check (container=${CONTAINER_NAME})"
ok=0
for i in $(seq 1 40); do
  st="$(docker inspect -f '{{ if .State.Health }}{{ .State.Health.Status }}{{ else }}none{{ end }}' "${CONTAINER_NAME}" 2>/dev/null || echo "")"
  case "${st}" in
    healthy)   ok=1; echo "    healthy after ~$((i * 3))s"; break ;;
    unhealthy) echo "    unhealthy"; break ;;
    none)      ok=1; echo "    (healthcheck 未定義: 起動のみ確認)"; break ;;
    *)         sleep 3 ;;   # starting / 空文字 → 待機
  esac
done

# 旧イメージを掃除 (ディスク逼迫防止)。失敗してもデプロイ結果には影響させない。
docker image prune -f >/dev/null 2>&1 || true

if [ "${ok}" != 1 ]; then
  echo "ERROR: health check failed. recent backend logs:" >&2
  "${COMPOSE[@]}" logs --tail 80 "${SERVICE}" >&2 || true
  exit 1
fi
echo "==> deploy 完了 (nginx-proxy 経由で https://<VIRTUAL_HOST>/ から到達可能)"
