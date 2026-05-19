# RUNBOOK: ローカル開発手順 (Phase 7 Iteration 0)

> **対象:** akebono アパレル生産管理システム MVP の **Iteration 0 (ローカル開発環境)**。AWS インフラ・Firebase 本番認証は Iteration 4 Hardening で構築、Iteration 0-3 は本手順でローカル動作確認します。
>
> **ゴール:** PostgreSQL + .NET Backend + Nuxt Frontend をローカルで起動し、ログイン → ユーザ一覧表示まで疎通

---

## 1. 前提ツール

ローカル PC に以下が必要です。

| ツール | 推奨バージョン | 確認コマンド | 備考 |
|---|---|---|---|
| .NET SDK | **8.0.x** | `dotnet --version` | Visual Studio 2022 17.8+ 同梱可 |
| Node.js | **22.x** (LTS) | `node -v` | `nvm` または `Volta` でバージョン管理推奨 |
| pnpm | **9.x** | `pnpm -v` | 後述 §1.2 参照 |
| PostgreSQL | **16.x** | `psql --version` | 後述 §1.3 で 2 つの選択肢 |
| pgAdmin4 (任意) | 8.x | – | GUI で DB 操作したい場合 |
| Docker (任意) | Docker 24+ | `docker compose version` | docker compose 経由で起動する場合のみ必要 |

### 1.1 .NET SDK の入手

- Visual Studio 2022 をインストール済の場合は同梱
- 単体: <https://dotnet.microsoft.com/download/dotnet/8.0>

### 1.2 pnpm のインストール

複数の方法があります。お手元の環境に応じて選択。

**A. corepack 経由 (Node 16.13+ 標準、推奨)**
```powershell
corepack enable
pnpm -v
```

**B. Volta 経由 (Volta 利用者)**
```powershell
npm install -g pnpm@9
# Volta が「note: using Volta to install pnpm」と表示、自動で Volta 管理下にインストール
pnpm -v
```

**C. npm でグローバルインストール (corepack 不在 / 管理者権限あり)**
```powershell
npm install -g pnpm@9
pnpm -v
```

> Windows で「Permission denied」「EACCES」が出る場合は PowerShell を **管理者として起動** して再実行。

### 1.3 PostgreSQL の選択肢

#### 選択肢 A: 既存ローカル PostgreSQL を使う (Windows/Mac で簡単)

既に PostgreSQL 16 がローカル PC にインストール済の場合 (pgAdmin4 も推奨):

1. pgAdmin4 でサーバ接続
2. `Login/Group Roles` 右クリック → `Create > Login/Group Role`
   - General タブ: Name = **`akebono-honshu`**
   - Definition タブ: Password = `localdev`
   - Privileges タブ: `Can login?` **ON**、`Create databases?` **ON**
3. `Databases` 右クリック → `Create > Database`
   - General タブ: Database = **`akebono-honshu`**、Owner = `akebono-honshu`
4. 左ツリーで `Databases > akebono-honshu` を選択 (重要)
5. `Tools > Query Tool` → リポジトリの `db/init/01-schema.sql` を貼り付け → F5 で実行
6. 動作確認:
   ```sql
   SELECT id, employee_no, login_id, display_name FROM users;
   ```
   → 3 件 (owner / planner / sales) 表示で OK

> **既存 PostgreSQL のスーパーユーザ (例: `pguser`) で接続する場合:** 後述 §2.1 を参照し、Backend の Connection String を上書きしてください。

#### 選択肢 B: docker compose で起動 (Mac/Linux 推奨、Windows でも Docker Desktop で動作)

```powershell
# ルートディレクトリで
docker compose up -d postgres
docker compose ps
docker compose exec postgres psql -U akebono-honshu -d akebono-honshu -c "SELECT id, login_id, display_name FROM users;"
```

`./db/init/01-schema.sql` が docker-compose の初期化スクリプトとして自動投入され、ロール + DB + Seed が一度に揃います。

---

## 2. 初回セットアップ

§1 のツールを揃えてから、リポジトリ clone 後に以下を実施。

### 2.1 Backend 認証情報の設定 (PostgreSQL を選択肢 A で構築した方)

選択肢 A で、`akebono-honshu` 以外のユーザ (例: `pguser`) で接続したい場合、`appsettings.json` の Connection String を書き換えるか、**`appsettings.Development.json`** に以下を追記します (こちらが推奨、共通設定を壊さない):

```jsonc
// src/Backend/Presentation/appsettings.Development.json に追記
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=akebono-honshu;Username=pguser;Password=<password>"
  }
}
```

> ASP.NET Core の規約により、`ASPNETCORE_ENVIRONMENT=Development` (既定) では `appsettings.json` → `appsettings.Development.json` の順でマージされ、後者が優先されます。Iteration 1 で `User Secrets` 方式 (`dotnet user-secrets`) に移行予定。

### 2.2 ターミナル 1: PostgreSQL

選択肢 A の場合は §1.3.A 手順で起動済 (常駐サービス)。

選択肢 B の場合:
```powershell
docker compose up -d postgres
```

### 2.3 ターミナル 2: Backend (.NET 8)

#### Visual Studio で開く場合
1. `src/Backend/Akebono.sln` をダブルクリック
2. F5 でデバッグ実行 (Akebono.Api がスタートアッププロジェクト)
3. 「出力」ウィンドウに `Now listening on: http://localhost:5000` が出れば OK

#### CLI で実行する場合
```powershell
cd src\Backend
dotnet restore
dotnet run --project Presentation
```

#### 疎通確認
```powershell
# ヘルスチェック
Invoke-RestMethod http://localhost:5000/health

# ログイン API
Invoke-RestMethod -Uri http://localhost:5000/api/v1/auth/login `
  -Method POST `
  -ContentType "application/json" `
  -Body '{"loginId":"owner","password":"localdev"}'
```

期待結果: `token`, `userId`, `displayName` を含む JSON 応答。

### 2.4 ターミナル 3: Frontend (Nuxt 3)

新しい PowerShell ターミナルで:
```powershell
cd src\Frontend
pnpm install           # 初回 1-2 分
copy .env.example .env
pnpm dev
```

期待結果: `http://localhost:3000` で Nuxt が起動。

---

## 3. 動作確認シナリオ

1. ブラウザで `http://localhost:3000` にアクセス → `/login` にリダイレクト
2. ログイン ID: `owner`、パスワード: `localdev` (初期入力済) → 「ログイン」クリック
3. `/users` にリダイレクト、ユーザ一覧テーブルに 3 件 (owner / planner / sales) が表示
4. 「ログアウト」ボタン → `/login` に戻る
5. 監査ログ確認:

   選択肢 A (pgAdmin4):
   ```sql
   SELECT id, occurred_at, action, actor_user_id, note 
   FROM audit_logs 
   ORDER BY id DESC LIMIT 10;
   ```

   選択肢 B (docker):
   ```powershell
   docker compose exec postgres psql -U akebono-honshu -d akebono-honshu `
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

### Backend / Frontend
- Visual Studio: Shift+F5 で停止、F5 で再開
- CLI: Ctrl+C で停止、コマンド再実行で再開

### PostgreSQL (選択肢 A)
Windows サービスとして常駐、停止不要。データベースを完全リセットしたい場合は pgAdmin4 で `akebono-honshu` データベースを削除 → §1.3.A の手順 3〜6 で再作成。

### PostgreSQL (選択肢 B、docker)
```powershell
docker compose stop postgres                 # 停止 (データ保持)
docker compose down -v && docker compose up -d postgres  # 完全リセット
```

---

## 6. ディレクトリ構成

```
.
├── .ai-native/         AI ネイティブ開発方法論ドキュメント (Phase 0-7)
│   └── outputs/        各 Phase 成果物
├── db/init/            PostgreSQL 初期化 SQL (docker でも pgAdmin4 でも投入可)
├── docker-compose.yml  PostgreSQL ローカル起動定義 (選択肢 B のみ使用)
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
├── RUNBOOK.md (本ファイル)
└── CLAUDE.md           Claude Code 環境固有の実装ルール
```

---

## 7. トラブルシュート

| 症状 | 原因 / 対処 |
|---|---|
| `corepack: command not found` | Node 同梱の corepack が無い (古い Node や独自ビルド)。`npm install -g pnpm@9` でフォールバック |
| `npm install -g pnpm` で `Permission denied` | PowerShell を管理者として起動して再実行 |
| `pnpm install` で `ERR_PNPM_NO_MATCHING_VERSION` | package.json のバージョン指定がレジストリと不一致。Claude にバージョン確認を依頼 |
| `docker compose up` で port 5432 already in use | 既存ローカル PostgreSQL が起動中。docker を使わず選択肢 A に切替推奨 |
| `dotnet restore` で NuGet エラー | `dotnet nuget locals all --clear` でキャッシュクリア後リトライ |
| Backend ビルド時に `CS0246: 型または名前空間 'XXX' が見つかりません` | class library の `using` 不足。`Microsoft.Extensions.Configuration` / `Microsoft.Extensions.DependencyInjection` 等は明示が必要 (ASP.NET Core 側の implicit usings に含まれない) |
| Frontend で `Failed to fetch http://localhost:5000` | Backend が起動していない / CORS 不整合 / Connection String が DB に届いていない (`appsettings.Development.json` で上書き確認) |
| `audit_logs` が記録されない | Backend が DB に接続できていない。`appsettings.json` または `appsettings.Development.json` の `ConnectionStrings:Postgres` を確認、pgAdmin4 で対象ロールが該当 DB へのアクセス権限を持つか確認 |
| `pnpm install` で `EACCES` | Node のパーミッション問題。Volta or nvm 経由で Node を入れ直す |
| ログイン失敗 (Invalid credentials) | パスワードは固定 `localdev`、ログイン ID は `owner` / `planner` / `sales` のいずれか |
| `pnpm dev` でポート 3000 衝突 | 別アプリ使用中、`pnpm dev --port 3001` で代替 (`.env` の NUXT_PUBLIC_API_BASE は変更不要) |

---

## 8. Iteration 0 のスコープと制約

**実装済み (Iteration 0):**
- PostgreSQL ローカル起動 + 初期スキーマ + Seed (選択肢 A pgAdmin4 / 選択肢 B docker)
- .NET 8 Backend (4 層) + EF Core 8 + ダミー認証 + audit_logs 記録
- Nuxt 3 Frontend + ログイン画面 + ユーザ一覧画面 + 認証 middleware

**Iteration 0 で得た知見 (Iteration 1 以降に適用):**
- class library で `Microsoft.Extensions.*` を使う場合は `using` を明示する必要あり (ASP.NET Core 側の implicit usings 不適用)
- 新規パッケージ追加時は npm レジストリで最新版を事前確認 (`pnpm view <package> versions` または `npm view <package>`)
- ローカル PostgreSQL を持つ環境では docker よりも既存環境 + pgAdmin4 直接利用が早い
- `appsettings.json` のローカル編集はリポジトリ衝突の元、`appsettings.Development.json` で上書きする運用が安全 (Iteration 1 で `User Secrets` に正規化予定)

**未実装 (後続 Iteration):**

| 項目 | 着手 Iteration | 備考 |
|---|---|---|
| マスタ 17 種 CRUD | Iteration 1 | 共通テンプレート `MasterController<TEntity, TDto>` |
| 商品マスタ (P-01〜06) | Iteration 2 | 11 桁 SKU + サイズ展開 + 画像 |
| 発注書 (O-01〜07) | Iteration 3 | Excel 出力含む MVP のクリティカルパス |
| Firebase 本番認証 | Iteration 4 | `ITokenService` 実装差替 |
| AWS インフラ + CI/CD | Iteration 4 | App Runner / RDS / S3 / Terraform / GitHub Actions |
| EF Core マイグレーション | Iteration 1 | 現在は `db/init/01-schema.sql` を投入 |
| TLS / セキュリティ強化 | Iteration 4 | KMS / IAM 最小権限 / audit_logs 改竄防止 |
| User Secrets / Connection String 整理 | Iteration 1 | `appsettings.Development.json` から `dotnet user-secrets` に移行 |

詳細は `.ai-native/outputs/phase7/iteration-plan.md` を参照。

---

## 9. 関連ドキュメント

- Phase 7 Iteration 計画: `.ai-native/outputs/phase7/iteration-plan.md`
- Phase 7 INDEX: `.ai-native/outputs/phase7/_index.md`
- Phase 5 設計: `.ai-native/outputs/phase5/{architecture,data-design,api-design,screen-design}.md`
- Phase 3 機能要件: `.ai-native/outputs/phase3/functional-requirements.md`
- 方法論 SoT: `.ai-native/methodology/`
- 環境固有実装ルール: `CLAUDE.md`
