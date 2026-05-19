# RUNBOOK: ローカル開発手順 (Phase 7 Iteration 0)

> **対象:** akebono アパレル生産管理システム MVP の **Iteration 0 (ローカル開発環境)**。AWS インフラ・Firebase 本番認証は Iteration 4 Hardening で構築、Iteration 0-3 は本手順でローカル動作確認します。
>
> **ゴール:** `docker compose up` + `dotnet run` + `pnpm dev` の 3 本でログイン → ユーザ一覧表示まで疎通

---

## 1. 前提ツール

ローカル PC に以下が必要です。

| ツール | 推奨バージョン | 確認コマンド |
|---|---|---|
| Docker / Docker Compose | Docker 24+ (Compose v2 同梱) | `docker compose version` |
| .NET SDK | **8.0.x** | `dotnet --version` |
| Node.js | **22.x** (LTS) | `node -v` |
| pnpm | **9.x** | `pnpm -v` |
| psql クライアント (任意) | 16.x | `psql --version` |

> Node.js は `nvm` 等で 22.x をインストールしてください。pnpm は `corepack enable` で有効化できます。

---

## 2. 初回セットアップ

リポジトリ clone 後、3 ターミナル並行で起動します。

### 2.1 ターミナル 1: PostgreSQL

```bash
# ルートディレクトリで
docker compose up -d postgres

# 起動確認 (healthcheck が "healthy" になるまで数秒)
docker compose ps

# 初期データ確認 (任意)
docker compose exec postgres psql -U akebono-honshu -d akebono-honshu -c "SELECT id, login_id, display_name FROM users;"
```

期待結果: `owner / planner / sales` の 3 ユーザが表示される。

### 2.2 ターミナル 2: Backend (.NET 8)

```bash
cd src/Backend

# 依存解決 (初回のみ、約 30 秒)
dotnet restore

# 起動
dotnet run --project Presentation
```

期待結果: `http://localhost:5000` で listening、`GET /health` が `{"status":"ok"}` を返す。

```bash
# 別ターミナルで疎通確認 (任意)
curl http://localhost:5000/health
curl -X POST http://localhost:5000/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"loginId":"owner","password":"localdev"}'
```

### 2.3 ターミナル 3: Frontend (Nuxt 3)

```bash
cd src/Frontend

# 依存解決 (初回のみ、約 1-2 分)
pnpm install

# 環境変数 (デフォルトで OK)
cp .env.example .env

# 起動
pnpm dev
```

期待結果: `http://localhost:3000` で Nuxt が起動。ブラウザでアクセスするとログイン画面が表示される。

---

## 3. 動作確認シナリオ

1. ブラウザで `http://localhost:3000` にアクセス → `/login` にリダイレクト
2. ログイン ID: `owner`、パスワード: `localdev` を入力 → 「ログイン」クリック
3. `/users` にリダイレクト、ユーザ一覧テーブルに 3 件 (owner / planner / sales) が表示される
4. 「ログアウト」ボタン → `/login` に戻る
5. 監査ログ確認:
   ```bash
   docker compose exec postgres psql -U akebono-honshu -d akebono-honshu \
     -c "SELECT id, occurred_at, action, actor_user_id, note FROM audit_logs ORDER BY id DESC LIMIT 10;"
   ```
   期待結果: `Login.Success`, `User.List` 等が記録されている。

---

## 4. 想定エンドポイント

| メソッド | パス | 概要 | 認証 |
|---|---|---|---|
| GET | `/health` | ヘルスチェック | なし |
| POST | `/api/v1/auth/login` | ダミー認証ログイン | なし |
| GET | `/api/v1/auth/me` | 現在のユーザ情報 | Bearer 必須 |
| GET | `/api/v1/users` | ユーザ一覧 | Bearer 必須 |

---

## 5. 停止 & 再起動

```bash
# ターミナル 2/3 は Ctrl+C で停止
# PostgreSQL 停止 (データは保持)
docker compose stop postgres

# 完全リセット (データ削除、初期 SQL 再実行)
docker compose down -v
docker compose up -d postgres
```

---

## 6. ディレクトリ構成

```
.
├── .ai-native/         AI ネイティブ開発方法論ドキュメント (Phase 0-7)
│   └── outputs/        各 Phase 成果物
├── db/init/            PostgreSQL 初期化 SQL (docker-compose で自動投入)
├── docker-compose.yml  PostgreSQL ローカル起動定義
├── src/
│   ├── Backend/        .NET 8 Minimal API (Clean Architecture 4 層)
│   │   ├── Akebono.sln
│   │   ├── Domain/             エンティティ
│   │   ├── Application/        ビジネスロジック + 抽象
│   │   ├── Infrastructure/     EF Core + ダミー認証 + 監査
│   │   └── Presentation/       Minimal API エンドポイント
│   └── Frontend/       Nuxt 3 + Reka UI + Tailwind CSS
│       ├── pages/              ルーティング (login, users)
│       ├── composables/        useAuth (ダミー認証) / useApi
│       └── middleware/         認証ガード
└── RUNBOOK.md (本ファイル)
```

---

## 7. トラブルシュート

| 症状 | 原因 / 対処 |
|---|---|
| `docker compose up` で port 5432 already in use | ローカルに PostgreSQL が既に起動中。`brew services stop postgresql` または既存サービスを停止 |
| `dotnet restore` で NuGet エラー | `dotnet nuget locals all --clear` でキャッシュクリア後リトライ |
| Frontend で `Failed to fetch http://localhost:5000` | Backend (ターミナル 2) が起動していない、または CORS 設定不整合。`appsettings.json` の `Cors:Origins` を確認 |
| `audit_logs` が記録されない | Backend が DB に接続できていない。`appsettings.json` の `ConnectionStrings:Postgres` と docker-compose の認証情報を照合 |
| `pnpm install` で `EACCES` | Node のパーミッション問題。`sudo` ではなく nvm 経由で Node を入れ直す |
| ログイン失敗 (Invalid credentials) | パスワードは固定 `localdev`、ログイン ID は `owner` / `planner` / `sales` のいずれか |

---

## 8. Iteration 0 のスコープと制約

**実装済み (Iteration 0):**
- PostgreSQL ローカル起動 + 初期スキーマ + Seed
- .NET 8 Backend (4 層) + EF Core 8 + ダミー認証 + audit_logs 記録
- Nuxt 3 Frontend + ログイン画面 + ユーザ一覧画面 + 認証 middleware

**未実装 (後続 Iteration):**

| 項目 | 着手 Iteration | 備考 |
|---|---|---|
| マスタ 17 種 CRUD | Iteration 1 | 共通テンプレート `MasterController<TEntity, TDto>` |
| 商品マスタ (P-01〜06) | Iteration 2 | 11 桁 SKU + サイズ展開 + 画像 |
| 発注書 (O-01〜07) | Iteration 3 | Excel 出力含む MVP のクリティカルパス |
| Firebase 本番認証 | Iteration 4 | `ITokenService` 実装差替 |
| AWS インフラ + CI/CD | Iteration 4 | App Runner / RDS / S3 / Terraform / GitHub Actions |
| EF Core マイグレーション | Iteration 1 | 現在は `db/init/01-schema.sql` を docker-compose で投入 |
| TLS / セキュリティ強化 | Iteration 4 | KMS / IAM 最小権限 / audit_logs 改竄防止 |

詳細は `.ai-native/outputs/phase7/iteration-plan.md` を参照。

---

## 9. 関連ドキュメント

- Phase 7 Iteration 計画: `.ai-native/outputs/phase7/iteration-plan.md`
- Phase 7 INDEX: `.ai-native/outputs/phase7/_index.md`
- Phase 5 設計: `.ai-native/outputs/phase5/{architecture,data-design,api-design,screen-design}.md`
- Phase 3 機能要件: `.ai-native/outputs/phase3/functional-requirements.md`
- 方法論 SoT: `.ai-native/methodology/`
- 環境固有実装ルール: `CLAUDE.md`
