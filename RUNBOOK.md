# RUNBOOK: ローカル開発手順 (Phase 7 Iteration 0 ベース + Iter 4 段階 A/B 反映)

> **対象:** akebono アパレル生産管理システム MVP の **ローカル開発環境**。
> Iter 4 段階 A (AWS RDS 接続、2026-05-20 完了) と 段階 B (Firebase Auth 切替、2026-05-20 完了) を反映済。
> 段階 C (本番 App Runner + Firebase Hosting デプロイ) は別文書。
>
> **ゴール:** PostgreSQL + .NET Backend + Nuxt Frontend をローカルで起動し、Firebase Auth でログイン → ユーザ一覧表示まで疎通

---

## 0. Iter 4 段階 B 完了 (2026-05-20) による変更点

| 項目 | 旧 (Iter 0 ダミー) | 新 (Iter 4 段階 B) |
|---|---|---|
| 認証方式 | `POST /api/v1/auth/login` に loginId/password → 独自 HMAC JWT | Firebase Auth (Email/Password) でログイン → ID Token を `Authorization: Bearer` 送信 |
| 同期 endpoint | – | `POST /api/v1/auth/sync` (Firebase UID → users.firebase_uid 引当 + 業務情報返却 + audit log) |
| ログイン UI | `owner` / `localdev` の固定フォーム | メール (Firebase) + パスワード |
| Backend ミドルウェア | なし (`ITokenService.TryValidate`) | `AddJwtBearer` + JWKS 検証 + `OnTokenValidated` で `users.firebase_uid` → `ClaimsPrincipal.akebono_user_id` 解決 |
| Backend secret | `DummyAuth:Secret` (削除済) | `Firebase:ProjectId=akebono-honshu` (appsettings.json) |
| Frontend SDK | localStorage `akebono-auth` | `firebase@^12.13.0` の `signInWithEmailAndPassword` + `onAuthStateChanged` |
| timestamp | TIMESTAMPTZ (UTC + JST 表示) | TIMESTAMP (without time zone)、JST naive 統一 + `SystemTime.Now` |

**既存ローカル環境を Iter 4 段階 B に追従する手順:**

1. `git pull` でブランチ最新化
2. **DB 移行 (既存 DB のみ):** pgAdmin4 で `db/migration/iter4-tz-to-jst-naive.sql` を実行 (全 TIMESTAMPTZ → TIMESTAMP に変換)
3. **Firebase Console での準備 (オペレーター作業、初回のみ):**
   - <https://console.firebase.google.com> でプロジェクト `akebono-honshu` (作成済) を選択
   - Authentication → Users → テストユーザ追加 (例: `owner@akebono.example` + 任意パスワード)
   - 追加されたユーザの UID 列をコピー
4. **RDS に Firebase UID を紐付け:**
   ```sql
   UPDATE users SET firebase_uid = '<コピーしたUID>' WHERE login_id = 'owner';
   ```
5. **Backend:** `cd src/Backend && dotnet restore` (Microsoft.AspNetCore.Authentication.JwtBearer 8.0.27 が追加)
6. **Frontend:** `cd src/Frontend && pnpm install` (firebase 12.13.0 が追加)
7. 通常通り Backend (`dotnet run --project Presentation`) + Frontend (`pnpm dev`) を起動
8. <http://localhost:3000> → メール + パスワードでログイン → ユーザ一覧表示
9. 検証: `SELECT occurred_at, actor_user_id, action, result FROM audit_logs WHERE action LIKE 'Login.%' ORDER BY occurred_at DESC LIMIT 5;` で `Login.Success` (result=0) が記録される

> **Firebase config 設定:** Web app 用の `firebaseConfig` (apiKey 等) は公開情報のため `nuxt.config.ts:runtimeConfig.public.firebase` に既定値として埋込済。別 project に切替える場合は `NUXT_PUBLIC_FIREBASE_*` 環境変数で上書き可能。Backend は `appsettings.json:Firebase:ProjectId` (現状 `akebono-honshu`) のみ参照、本番は環境変数 `Firebase__ProjectId` で上書き。
> **Service Account 鍵:** 段階 B では未使用。段階 C のシナリオ E (Custom Claims 同期) で使用予定のため、オペレーターのローカルに保管したまま (Git 管理外)。

以下の §1〜§4 は Iter 0 当時のローカル開発手順を歴史的記録として残します。**Iter 4 段階 B 以降は §0 の手順を優先してください**。

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

> **2026-05-20 命名変更:** DB 名・ロール名を `akebono-honshu` → `akebono_honshu` に変更 (アンダースコア)。PostgreSQL の通常識別子になりクォート (`""`) が不要に。
>
> **既にローカル環境を構築済の方の移行手順:**
>
> - **選択肢 A (pgAdmin / ローカル PostgreSQL):**
>   1. pgAdmin4 で `akebono-honshu` データベース右クリック → Properties → General タブで Database 名を `akebono_honshu` に変更
>   2. `Login/Group Roles > akebono-honshu` 右クリック → Properties → General タブで Name を `akebono_honshu` に変更
>   3. 接続中セッションがあるとリネーム不可 → pgAdmin の Query Tool を一旦閉じる
> - **選択肢 B (docker):** Iter 0 のデータは Seed のみ (業務データなし) のため、`docker compose down -v` でボリュームごと削除 → `docker compose up -d postgres` で再作成が最速
> - **user-secrets 更新:** ローカル接続文字列を再設定: `dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=akebono_honshu;Username=pguser;Password=<your_password>"` (pguser 利用者) または `appsettings.Development.json` のまま (`akebono_honshu / localdev`)


#### 選択肢 A: 既存ローカル PostgreSQL を使う (Windows/Mac で簡単)

既に PostgreSQL 16 がローカル PC にインストール済の場合 (pgAdmin4 も推奨):

1. pgAdmin4 でサーバ接続
2. `Login/Group Roles` 右クリック → `Create > Login/Group Role`
   - General タブ: Name = **`akebono_honshu`**
   - Definition タブ: Password = `localdev`
   - Privileges タブ: `Can login?` **ON**、`Create databases?` **ON**
3. `Databases` 右クリック → `Create > Database`
   - General タブ: Database = **`akebono_honshu`**、Owner = `akebono_honshu`
4. 左ツリーで `Databases > akebono_honshu` を選択 (重要)
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
docker compose exec postgres psql -U akebono_honshu -d akebono_honshu -c "SELECT id, login_id, display_name FROM users;"
```

`./db/init/01-schema.sql` が docker-compose の初期化スクリプトとして自動投入され、ロール + DB + Seed が一度に揃います。

#### 選択肢 C: AWS RDS PostgreSQL を使う (Iter 4 本番移行 段階 A)

> **位置付け:** `iteration4-prod-migration-plan.md` 段階 A 用。ローカル Backend / Frontend と AWS RDS の組み合わせで動作確認する。Phase 4 確定は PostgreSQL 16、現状 RDS は 14.17 だが MVP スキーマは 14 互換のため動作可能。
>
> **前提:** RDS インスタンス (`akebono1` 等) のセキュリティグループに開発 PC の IP からの 5432 接続を許可済み。

##### Step 1. データベース新規作成 (RDS マスター接続)

ローカル PC の psql から RDS マスターユーザで一度接続し、本システム用 DB を作成 (`akebono_honshu`)。他システムとは DB 単位で論理分離する。

```bash
# 接続 (パスワードは対話的に聞かれる、ユーザ手元のものを入力)
psql -h <rds-endpoint> -p 5432 -U pguser -d postgres
```

```sql
-- akebono_honshu DB を新規作成 (オーナーは pguser のまま、別途アプリ専用ユーザを作る場合は段階 C で実施)
CREATE DATABASE "akebono_honshu" OWNER pguser;
\q
```

##### Step 2. スキーマ初期化 (4 ファイルを順に投入)

```bash
# 新規 DB に接続し直し、初期化スクリプトを順に投入
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/01-schema.sql

psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/02-masters.sql

psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/03-products.sql

psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/04-orders.sql

# 動作確認
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -c "SELECT id, login_id, display_name FROM users;"
# → owner / planner / sales の 3 件が表示されれば OK
```

> パスワードは `PGPASSWORD` 環境変数で渡すと自動化しやすい (`PGPASSWORD=xxx psql ...`)。

##### Step 3. Backend の接続先を RDS に切替

```bash
cd src/Backend/Presentation

# Connection String を user-secrets に格納 (リポジトリには記録されない)
dotnet user-secrets set "ConnectionStrings:Postgres" \
  "Host=<rds-endpoint>;Port=5432;Database=akebono_honshu;Username=pguser;Password=<your-password>;SslMode=Require"

# 確認 (パスワードは出力される、Claude には共有しない)
dotnet user-secrets list
```

> **戻したいとき (ローカル PostgreSQL へロールバック):**
> `dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=akebono_honshu;Username=akebono_honshu;Password=localdev"`

##### Step 4. 動作確認

```bash
# Backend
dotnet run --project Presentation

# 別ターミナル / Frontend
cd ../../Frontend && pnpm dev
```

ブラウザで `http://localhost:3000` → ログイン (owner / 開発時パスワード) → ユーザ一覧 → 商品管理 が表示できれば、ローカル UI + AWS RDS の構成で疎通完了。

##### トラブルシュート

| 症状 | 確認ポイント |
|---|---|
| `connection refused` | RDS セキュリティグループ、開発 PC の現在 IP が許可されているか |
| `password authentication failed` | pguser のパスワードを再確認 (RDS console から再リセット可能) |
| `SSL is not enabled on the server` | RDS の `rds.force_ssl` パラメータ確認、Connection String の `SslMode=Require` |
| Backend 起動時 EF Core エラー | スキーマ未投入。Step 2 を再実行 |
| `relation "..." does not exist` | スキーマファイルの投入順序ミス (01 → 02 → 03 → 04 の順) |

---

## 2. 初回セットアップ

§1 のツールを揃えてから、リポジトリ clone 後に以下を実施。

### 2.1 Backend 認証情報の設定

設定の優先度 (低 → 高):
1. `appsettings.json` (リポジトリ管理、本番デフォルトはプレースホルダ `__OVERRIDE_ME__`)
2. `appsettings.Development.json` (リポジトリ管理、**チーム共通の開発デフォルト** = `akebono_honshu / localdev`)
3. `dotnet user-secrets` (リポジトリ外、**個人固有の機密値** = `pguser / 個人パスワード` 等)
4. 環境変数 `ConnectionStrings__Postgres` (CI / コンテナ起動時の上書き)

**§1.3.A の選択肢 A** で `akebono_honshu / localdev` ロールを作成した方は **何も設定不要**、appsettings.Development.json の値で動きます。

**`pguser` 等の別ユーザで接続する方** (Iteration 0 動作確認中のオペレーター環境など):

```powershell
cd src\Backend\Presentation

# UserSecretsId は .csproj に設定済 (akebono-honshu-iter0-dev-secrets)、init は不要
# Connection String を user-secrets に格納 (リポジトリには記録されない)
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=akebono_honshu;Username=pguser;Password=<your_password>"

# 確認
dotnet user-secrets list
```

格納先 (リポジトリ外、git 管理不要):
- Windows: `%APPDATA%\Microsoft\UserSecrets\akebono-honshu-iter0-dev-secrets\secrets.json`
- Mac/Linux: `~/.microsoft/usersecrets/akebono-honshu-iter0-dev-secrets/secrets.json`

> 不要になったら `dotnet user-secrets remove "ConnectionStrings:Postgres"` でクリア、`dotnet user-secrets clear` で全削除可能。

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
   docker compose exec postgres psql -U akebono_honshu -d akebono_honshu `
     -c "SELECT id, occurred_at, action, actor_user_id, note FROM audit_logs ORDER BY id DESC LIMIT 10;"
   ```
   期待結果: `Login.Success`, `User.List` 等が記録されている。

### 3.1 Iteration 1 追加シナリオ (マスタ管理 17 種)

> **前提:** ステップ §2.1 + §2.2 で `db/init/01-schema.sql` 投入済の前提に加え、`db/init/02-masters.sql` を pgAdmin4 で実行して 17 マスタ + Seed データを投入する。

1. ブラウザで `http://localhost:3000` → `owner` / `localdev` でログイン
2. 上部ナビ「マスタ管理」をクリック → `/masters` に 17 マスタのカードが表示される
3. **ブランド (拡張なしマスタ)** カード → `/masters/brands` で 2 件 (akebono / プライベート)
   - 「+ 新規追加」→ コード `099`、名称 `テスト` → 保存 → 3 件に増える
   - 編集 → 名称変更 → 保存
   - 削除 → 確認ダイアログ → 一覧から消える
   - 「論理削除済みを含む」チェック → 削除済みが表示 → 「復元」で復活
4. **仕入先 (M-04 拡張ありマスタ)** → `/masters/suppliers`
   - `officialName` (DEPARTURES 等)、国 (日本/中国)、工場コードが拡張カラム表示
   - 新規追加時、「国」select に countries マスタが選択肢として表示 (FK 連携)
5. **連絡文章テンプレ (M-05)** → `/masters/document-template-confirmations`
   - 本文 textarea + 標準印字 checkbox で編集
6. **権限制御 (C-02) 確認:**
   - ログアウト → `planner` / `localdev` で再ログイン
   - `/masters/brands` 画面右上に「参照のみ (品番台帳管理権限なし)」表示
   - 各行の操作列が「—」、編集/削除ボタンなし
7. **監査ログ:**
   ```sql
   SELECT id, occurred_at, action, entity_type, entity_id, note
   FROM audit_logs
   WHERE entity_type IN ('Brand', 'Supplier', 'Color')
   ORDER BY id DESC LIMIT 20;
   ```
   期待結果: `Brand.Create`, `Brand.Update`, `Brand.Delete`, `Brand.Restore`, `Supplier.List` 等が記録

### 3.2 Iteration 2 追加シナリオ (商品マスタ P-01〜P-06)

> **前提:** `db/init/03-products.sql` を pgAdmin4 で実行して商品関連 4 テーブル + Seed 投入。Backend 再起動でローカル画像保存先 `wwwroot/uploads/product-images/` が自動作成される。

1. **商品企画一覧 (P-04):** `http://localhost:3000/products` → デモ商品 春夏ベーシック が表示される
   - テーブル / カード ボタンで切替 (lg 以上 5 列、md 3 列、sm 2 列)
   - カード表示で代表画像 (`primary_image_s3_key`) が表示される (画像未登録時はプレースホルダー)
2. **商品新規ウィザード (P-01〜P-03):** ナビ右上「+ 新規商品ウィザード」→ 4 セクションフォームで一括登録
   - 1 トランザクションで family + 色×サイズ全 SKU + 仕入単価を登録
   - 11 桁品番が自動生成される (例: NA1001A4010)
3. **詳細・修正 (P-05):** 商品カードクリック → 企画情報・SKU 一覧・仕入単価・画像のフル詳細
   - 「編集」ボタンで属性 (ブランド / 機能 / 商品群 / 素材 3 種 / 名称 / 状態) を変更
   - 「+ 新単価追加」: 旧単価の `effective_to` が自動更新 (BR-04 履歴管理)
4. **画像管理 (P-06):** 詳細画面「+ 画像追加」→ JPEG/PNG/WebP アップロード (5MB / 5 枚まで)
   - Backend の `wwwroot/uploads/product-images/{familyId}/` にファイル保存
   - DB の `s3_key` には相対パスを保存、Iteration 4 で S3 移行時に I/F 互換
5. **権限制御:** planner / sales でログイン → 編集ボタン全て非表示、Swagger で直接 POST すると 403 Forbidden
6. **監査ログ:**
   ```sql
   SELECT id, occurred_at, action, entity_type, entity_id, note
   FROM audit_logs WHERE entity_type LIKE 'Product%' ORDER BY id DESC LIMIT 10;
   ```
   期待: `ProductFamily.Create` (note に SKU 件数 + 金額マスク "***" 含む), `ProductFamily.View`, `ProductSupplierPrice.Add` 等が記録

### 3.3 Iteration 3 追加シナリオ (発注書 O-01〜O-07、Excel 出力 = MVP クリティカルパス)

> **前提:** `db/init/04-orders.sql` を pgAdmin4 で実行して発注関連 3 テーブル + Seed 投入。Backend 再起動で ClosedXML 0.105.0 が NuGet 復元される。

1. **発注書一覧 (O-03):** `http://localhost:3000/orders` → Seed `26-00001` が「未出力」バッジで表示
2. **新規発注書 (O-01):** ナビ「発注書」→「+ 新規発注書」→ ヘッダ + 明細 + 連絡文章テンプレ複写 (O-07) → 登録で `mgmt_no` 自動採番
3. **詳細・編集 (O-04、F-16):** 行クリック → 詳細画面で「編集」→ 数量/単価変更 → **編集理由 5 値 select 必須** + メモ任意 → 保存で `audit_logs.note` に `edit_reason=quantity` 記録
4. **Excel ダウンロード (O-06、MVP クリティカルパス):**
   - 「📥 Excel ダウンロード」→ ファイル名 `PO_S00001_YYYYMMDD_HHmmss.xlsx`
   - 初回出力で `order_no` 採番 (S00001) + 3 件 snapshot 凍結 (F-22):
     - `supplier_official_name_snapshot` = "DEPARTURES"
     - `supplier_code_snapshot` = "336"
     - `customer_name_snapshot` = "しまむら"
   - Excel を開くと「**DEPARTURES 御中 336**」宛名 (F-22 帳票表記) + 明細表 + 合計
5. **中止 (O-05):** 「中止」→ 中止理由必須入力 → 詳細画面が Cancelled (オレンジ)、編集ボタン消失。**Excel ダウンロードは引き続き可能** (Phase 6 F-11 仕様)
6. **権限制御:** planner / sales でログイン → 編集ボタン全て非表示、Swagger で直接 POST すると 403 (`purchase_order_create_permission` 必須)
7. **監査ログ:**
   ```sql
   SELECT id, occurred_at, action, entity_type, entity_id, note
   FROM audit_logs WHERE entity_type = 'PurchaseOrder' ORDER BY id DESC LIMIT 10;
   ```
   期待: `PurchaseOrder.Create / Update (edit_reason 含む) / Cancel / Export (単価 ***)`
8. **Excel 出力履歴:**
   ```sql
   SELECT purchase_order_id, exported_at, is_first_export, excel_template_version
   FROM purchase_order_export_logs ORDER BY id DESC LIMIT 10;
   ```
   期待: 初回出力は `is_first_export=true`、テンプレ版 `iter3-v1`

---

## 4. 想定エンドポイント

| メソッド | パス | 概要 | 認証 | 権限 |
|---|---|---|---|---|
| GET | `/health` | ヘルスチェック | なし | – |
| GET | `/swagger` | Swagger UI (API ドキュメント + 動作確認画面) | なし | – |
| POST | `/api/v1/auth/login` | ダミー認証ログイン | なし | – |
| GET | `/api/v1/auth/me` | 現在のユーザ情報 + 4 権限 | Bearer | – |
| GET | `/api/v1/users` | ユーザ一覧 | Bearer | – |
| GET | `/api/v1/masters/{master}` | マスタ一覧 (17 種) | Bearer | – |
| POST/PATCH/DELETE | `/api/v1/masters/{master}[/{id}]` | マスタ CRUD | Bearer | `product_ledger_permission >= 1` |
| POST | `/api/v1/masters/{master}/{id}/restore` | 論理削除取消 | Bearer | `product_ledger_permission >= 1` |
| GET | `/api/v1/products/families` | 商品企画一覧 (P-04) | Bearer | – |
| GET | `/api/v1/products/families/{id}` | 商品企画詳細 (P-05) | Bearer | – |
| POST | `/api/v1/products/families/complete` | バルク登録 (P-01〜P-03) | Bearer | `product_ledger_permission >= 1` |
| PATCH | `/api/v1/products/families/{id}` | 企画更新 (P-05) | Bearer | `product_ledger_permission >= 1` |
| DELETE | `/api/v1/products/families/{id}` | 企画論理削除 (配下 SKU 連動) | Bearer | `product_ledger_permission >= 1` |
| GET | `/api/v1/products/families/{id}/supplier-prices` | 仕入単価履歴 | Bearer | – |
| POST | `/api/v1/products/families/{id}/supplier-prices` | 新単価追加 (BR-04) | Bearer | `product_ledger_permission >= 1` |
| POST | `/api/v1/products/families/{id}/images` | 画像アップロード (P-06、IFormFile) | Bearer | `product_ledger_permission >= 1` |
| PATCH | `/api/v1/products/families/{id}/images/reorder` | 画像順序変更 | Bearer | `product_ledger_permission >= 1` |
| DELETE | `/api/v1/products/families/{id}/images/{imageId}` | 画像論理削除 | Bearer | `product_ledger_permission >= 1` |
| GET | `/uploads/product-images/{familyId}/{filename}` | 画像配信 (Static Files、Iter 4 で S3 移行予定) | なし | – |
| GET | `/api/v1/orders` | 発注書一覧 (O-03) | Bearer | – |
| GET | `/api/v1/orders/{id}` | 発注書詳細 (O-04) | Bearer | – |
| POST | `/api/v1/orders` | 新規発注書 (O-01) | Bearer | `purchase_order_create_permission >= 1` |
| PATCH | `/api/v1/orders/{id}` | 発注書編集 (O-04、`editReason` 5 値必須 F-16) | Bearer | `purchase_order_create_permission >= 1` |
| POST | `/api/v1/orders/{id}/cancel` | 中止 (O-05) | Bearer | `purchase_order_create_permission >= 1` |
| GET | `/api/v1/orders/{id}/export.xlsx` | Excel 出力 (O-06、初回 snapshot 凍結 F-22) | Bearer | `purchase_order_create_permission >= 1` |
| GET | `/api/v1/orders/communication-suggestions` | 連絡文章テンプレ (O-07) | Bearer | – |

`{master}` は: brands / sizes / functions / countries / suppliers / departments / product-types / product-seasons / product-groups / colors / materials / material-classifications / warehouses / delivery-destinations / document-template-purchases / document-template-confirmations / document-text-purchases

---

## 5. 停止 & 再起動

### Backend / Frontend
- Visual Studio: Shift+F5 で停止、F5 で再開
- CLI: Ctrl+C で停止、コマンド再実行で再開

### PostgreSQL (選択肢 A)
Windows サービスとして常駐、停止不要。データベースを完全リセットしたい場合は pgAdmin4 で `akebono_honshu` データベースを削除 → §1.3.A の手順 3〜6 で再作成。

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
| マスタ 17 種 CRUD | ✅ Iteration 1 完了 | 共通テンプレート `MasterService<TEntity>` |
| 商品マスタ (P-01〜06) | ✅ Iteration 2 完了 | 11 桁 SKU + サイズ展開 + 画像 |
| 発注書 (O-01〜07) | ✅ Iteration 3 完了 | Excel 出力含む MVP のクリティカルパス |
| Firebase 本番認証 | ✅ Iteration 4 段階 B 完了 (2026-05-20) | JwtBearer + JWKS 検証、`POST /auth/sync` で users.firebase_uid 引当 |
| AWS RDS 接続 | ✅ Iteration 4 段階 A 完了 (2026-05-20) | dotnet user-secrets で接続文字列管理 |
| AWS App Runner + Firebase Hosting + S3 | Iteration 4 段階 C | 本番デプロイ (Dockerfile / ECR / Secrets Manager) |
| CI/CD (GitHub Actions) | Iteration 4 段階 D | main push で自動デプロイ |
| EF Core マイグレーション | Iteration 1 | 現在は `db/init/01-schema.sql` + `db/migration/*.sql` を投入 |
| TLS / セキュリティ強化 | Iteration 4 段階 C | KMS / IAM 最小権限 / audit_logs 改竄防止 |
| User Secrets / Connection String 整理 | ✅ Iteration 4 段階 A 完了 | `dotnet user-secrets` 運用に移行済 |
| Firebase Custom Claims 同期 (シナリオ E) | Iteration 4 段階 C | 権限変更時の RDS → Firebase 後追い同期 + Reconciler バッチ |

詳細は `.ai-native/outputs/phase7/iteration-plan.md` を参照。

---

## 9. 関連ドキュメント

- Phase 7 Iteration 計画: `.ai-native/outputs/phase7/iteration-plan.md`
- Phase 7 INDEX: `.ai-native/outputs/phase7/_index.md`
- Phase 5 設計: `.ai-native/outputs/phase5/{architecture,data-design,api-design,screen-design}.md`
- Phase 3 機能要件: `.ai-native/outputs/phase3/functional-requirements.md`
- 方法論 SoT: `.ai-native/methodology/`
- 環境固有実装ルール: `CLAUDE.md`
