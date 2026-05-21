# syntax=docker/dockerfile:1.7
# ===========================================================================
# Akebono Honshu Backend Dockerfile (Iter 4 段階 C-3)
#
# **対象:** AWS App Runner デプロイ用 (.NET 8 / ASP.NET Core)。
# **build context:** `src/Backend/` (このファイルが置かれている階層)。
# **配置の意図:** Frontend (src/Frontend) と無関係のため context をリポジトリ root にせず、
# Backend ソリューションディレクトリ直下に Dockerfile を置く。
#
# **設計判断 (ヒアリング A/A/A 案、Iter 4 段階 C-3):**
# - Base image: `mcr.microsoft.com/dotnet/{sdk,aspnet}:8.0-bookworm-slim`
#   (glibc + ICU 完全対応。MIG-3 Shift_JIS 取込 `CodePagesEncodingProvider` の動作実績あり)
# - Multi-stage: SDK で restore → publish → runtime image にコピー (build キャッシュ + サイズ削減)
# - Non-root user (UID 1000、`appuser`、Defense in Depth)。App Runner は強制しないがベストプラクティス
# - HEALTHCHECK: curl で `/health` endpoint (`Program.cs` 内 `app.MapGet("/health", ...)`)
#   - Dockerfile HEALTHCHECK は docker-compose / `docker run` 用。App Runner は無視するため
#     App Runner Console / IaC 側でも `/health` を Health check path に設定する (migration-plan §4.2.3)
# - Port 8080 (App Runner default)
# ===========================================================================

# ===== Build stage =====
# csproj を先にコピーして `dotnet restore` のレイヤーキャッシュを効かせる
# (ソース変更だけでは restore step を再実行しない)。
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

COPY Domain/Akebono.Domain.csproj Domain/
COPY Application/Akebono.Application.csproj Application/
COPY Infrastructure/Akebono.Infrastructure.csproj Infrastructure/
COPY Presentation/Akebono.Api.csproj Presentation/
RUN dotnet restore Presentation/Akebono.Api.csproj

# ソースをコピーして publish (Release config、UseAppHost=false で native exe を生成しない)。
COPY . .
RUN dotnet publish Presentation/Akebono.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ===== Runtime stage =====
# ASP.NET Core 8 runtime + 健康診断用 curl のみ。
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
WORKDIR /app

# HEALTHCHECK で /health を叩くために curl を追加 (+~3MB)。
# `--no-install-recommends` で依存パッケージ抑制、apt cache クリーンアップで image size 最小化。
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Non-root user (UID 1000、Defense in Depth)。
# `--no-create-home` でホームディレクトリ作成省略、`--shell /usr/sbin/nologin` でログイン無効化。
RUN groupadd --gid 1000 appuser \
    && useradd --uid 1000 --gid 1000 --no-create-home --shell /usr/sbin/nologin appuser \
    && chown -R appuser:appuser /app
USER appuser

# App Runner default port = 8080。ASPNETCORE_URLS で listen を統一 (デフォルトは 80/8080 が環境依存)。
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# HEALTHCHECK は docker-compose / `docker run` 用。App Runner では無視されるため
# App Runner サービス側でも `/health` を Health check path に設定すること (migration-plan §4.2.3)。
# `--start-period=15s` は .NET 起動 + IConfiguration Build (Secrets Manager 取得) の余裕。
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent --max-time 4 http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Akebono.Api.dll"]
