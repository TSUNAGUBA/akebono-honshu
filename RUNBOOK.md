# RUNBOOK: ローカル開発手順 (Phase 7 Iteration 0 ベース + Iter 4 段階 A/B 反映)

> **対象:** akebono アパレル生産管理システム MVP の **ローカル開発環境**。
> Iter 4 段階 A (AWS RDS 接続、2026-05-20 完了) と 段階 B (Firebase Auth 切替、2026-05-20 完了) を反映済。
> 段階 C/D (本番デプロイ + CI/CD) は **`deploy/README.md`** が SoT。実装はオペレーター判断で
> EC2(ubuntu) + GHCR + repository secrets 構成 (当初計画の App Runner/ECR から変更)。
>
> **ゴール:** PostgreSQL + .NET Backend + Nuxt Frontend をローカルで起動し、Firebase Auth でログイン → ホーム（ポータル：業務メニュー）表示まで疎通

---

## 0-P. プラットフォーム統合改修 (2026-07-09) による変更点

> **SoT:** 改修の全体像・設計判断は [docs/platform-integration/README.md](./docs/platform-integration/README.md)。
> 本節はローカル開発者向けの差分手順のみ。

| 項目 | 旧 | 新 (プラットフォーム統合) |
|---|---|---|
| API ベースパス | `/api/v1` | **`/api/maker/v1`** (あけぼの SCM プラットフォーム AKB-DOC-12) |
| レスポンス封筒 | 一覧のみ `{data}` / 単一は素の JSON / エラー RFC7807 | 成功 `{data, meta}` / エラー `{error: {code, message, userAction?, traceId, details[]}}` |
| エラーコード | `AUTH-001` 等をメッセージ末尾に埋め込み | `AKB-<AREA>-<NNN>` を `error.code` で返す |
| DB スキーマ | 単一テナント (tenant_id なし)・TIMESTAMP (JST naive) | **tenant_id uuid + RLS**・**TIMESTAMPTZ (UTC)** |
| Backend DB 接続ユーザ | `akebono_honshu` (docker スーパーユーザ) | **`akebono_app`** (RLS 適用の一般ロール。08-tenancy-rls.sql が作成) |
| 現在時刻 | `SystemTime.Now` (JST naive) | 格納 `SystemTime.UtcNow` / 表示・採番年度 `SystemTime.JstNow` |
| 作成系 API | – | `Idempotency-Key` ヘッダ必須 (orders / production-instructions / material-orders / products/families/complete の POST。第二段階で families を追加) |
| テナントヘッダ | – | 業務 API は `X-Tenant-Id` **必須** (欠如 400 AKB-SYS-003。第二段階で必須化、`/auth/*` は適用除外)。Frontend が自動付与 (auth/sync の応答 `tenantId` を使用) |
| 一覧 API | 全件返却 | **カーソルページング** (第二段階)。`?limit=` 既定 50・上限 200、`meta.page = {nextCursor, limit, hasMore}`。curl 疎通時は `nextCursor` を辿る |
| PK / 論理削除 | BIGSERIAL / delete_flag・is_deleted | **uuid PK / deleted_at** (第二段階。エンティティ ID は API/FE で uuid 文字列) |

**既存ローカル環境の追従手順 (破壊的変更・データ再作成):**

1. `git pull` でブランチ最新化
2. **DB 再作成** (tenant_id/RLS/TIMESTAMPTZ は既存ボリュームへ追従適用しない。稼働前 MVP のため再作成):
   ```bash
   docker compose down -v   # akebono-postgres-data ボリューム破棄
   docker compose up -d     # db/init 01〜10 が番号順に自動適用される
   ```
3. **Firebase UID 再紐付け** (§0 手順 4 と同じ):
   ```sql
   UPDATE users SET firebase_uid = '<UID>' WHERE login_id = 'owner';
   ```
4. **Backend:** 接続文字列は `appsettings.Development.json` が `Username=akebono_app;Password=localdev` に更新済。`dotnet run --project Presentation` で起動
5. **Frontend:** `.env` の `NUXT_PUBLIC_API_BASE` を `http://localhost:5000/api/maker/v1` へ更新 (`.env.example` 参照) → `pnpm dev`
6. **RLS 検証 (推奨):**
   ```bash
   psql "host=localhost dbname=akebono_honshu user=akebono_app password=localdev" -f db/verify/rls-smoke.sql
   # 期待: RLS smoke test: ALL PASSED
   ```

> **注意:** pgAdmin 等でスーパーユーザ (akebono_honshu) として覗くと RLS を素通しして全行見えます
> (PostgreSQL 仕様)。分離の確認は必ず `akebono_app` で接続してください。
> 手動 SQL でテナントスコープ表へ INSERT する場合は先に `SET app.tenant_id = '<uuid>';`
> (Honshu 既定テナント: `00000000-0000-4000-8000-000000000001`)。

---

## 0. Iter 4 段階 B 完了 (2026-05-20) による変更点

> **注:** 本節の「新」列は 2026-05-20 時点。API パス (`/api/v1` → `/api/maker/v1`) と
> timestamp 方針 (JST naive → TIMESTAMPTZ/UTC) は **§0-P のプラットフォーム統合改修で更新済み**。

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
2. **DB 移行 (既存 DB のみ、Backend 停止中に実行):**
   - 起動中の Backend を停止 (`ALTER COLUMN ... TYPE` が ACCESS EXCLUSIVE LOCK を取るため、in-flight トランザクションがあると待ちが発生する)
   - pgAdmin4 で `db/migration/iter4-tz-to-jst-naive.sql` を実行 (全 TIMESTAMPTZ → TIMESTAMP に変換)
   - 完了後 Backend を再起動
3. **Firebase Console での準備 (オペレーター作業、初回のみ):**
   - <https://console.firebase.google.com> でプロジェクト `akebono-honshu` (作成済) を選択
   - Authentication → Users → テストユーザ追加 (例: `owner@akebono.example` + 任意パスワード)
   - 追加されたユーザの UID 列をコピー
4. **RDS に Firebase UID を紐付け:**
   ```sql
   UPDATE users SET firebase_uid = '<コピーしたUID>' WHERE login_id = 'owner';
   ```
5. **Backend:** `cd src/Backend && dotnet restore` (`Microsoft.AspNetCore.Authentication.JwtBearer 8.0.27` が追加)
6. **Frontend:** `cd src/Frontend && pnpm install` (`firebase 12.13.0` が追加) → `cp .env.example .env` (Firebase config を含む)
7. 通常通り Backend (`dotnet run --project Presentation`) + Frontend (`pnpm dev`) を起動
8. <http://localhost:3000> → メール + パスワードでログイン → ホーム（ポータル：業務メニュー）表示
9. 検証: 以下 SQL で `Login.Success` (result=0) が記録される
   ```sql
   SELECT occurred_at, actor_user_id, action, result
   FROM audit_logs
   WHERE action LIKE 'Login.%' OR action LIKE 'Auth.%'
   ORDER BY occurred_at DESC LIMIT 10;
   ```
   - `Login.Success` (result=0、actor_user_id 付き): 認証成功
   - `Auth.LoginRejected.Inactive` (result=1、actor_user_id 付き): inactive ユーザ拒否
   - `Auth.UidUnboundProbe` (result=1、actor_user_id=NULL): 未紐付け UID 偵察試行
   > **5 分 de-dup 注意:** 同一 UID の拒否は 5 分に 1 回しか記録されない (audit_logs DoS 増幅対策)。連続テスト時は **5 分待つか Backend を再起動** して cache を flush してください。
   > **`is_active` 変更後の反映遅延:** ユーザを `is_active=false → true` (または逆) に切り替えた際、OnTokenValidated の `IMemoryCache` が最大 60 秒間古い状態を保持します (P-12 admin UI 着手後は自動 `cache.Remove` を呼ぶ前提)。即時反映が必要な場合は **Backend を再起動** してください。`fb_uid_resolve:{uid}` と `audit_logged:{uid}` の 2 種類の cache を同時に flush するのと等価です。

> **Firebase config 設定 (レビュー指摘 SA P0-1 反映):**
> - **Backend:** `appsettings.json:Firebase:ProjectId` は `__OVERRIDE_ME__` プレースホルダー。dev は `appsettings.Development.json` で `akebono-honshu` に上書き済。本番は環境変数 `Firebase__ProjectId=akebono-honshu-prod` 等で上書きする (起動時に Program.cs が値を検証し、プレースホルダーのままなら throw)
> - **Frontend:** `nuxt.config.ts:runtimeConfig.public.firebase` に default 値は持たない。dev は `.env.example` を `.env` にコピー、prod は CI/CD 環境で `NUXT_PUBLIC_FIREBASE_*` を本番値に注入する。未設定の場合 `plugins/firebase.client.ts` が起動時に throw
>
> **Service Account 鍵:** 段階 B では未使用。段階 C のシナリオ E (Custom Claims 同期) で使用予定のため、オペレーターのローカルに保管したまま (Git 管理外)。

> **§0 の手順は実際の最新仕様です。以下の §1〜§7 は Iter 0 当時のローカル開発手順を歴史的記録として残しますが、認証関連 (`/auth/login`、`owner / localdev` 等) は削除済の旧仕様です。新規セットアップでは §0 の手順を優先し、§1〜§7 はインフラ準備 (PostgreSQL / docker / .NET / pnpm セットアップ等の非認証部分) のリファレンスとしてのみ参照してください。**

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

`docker-compose.yml` は `./db/init` を**ディレクトリごと** `/docker-entrypoint-initdb.d` にマウントするため、`01-schema.sql` だけでなく **`01`〜`10` の全ファイルが番号順に自動投入され**、ロール + DB + Seed + 勤怠テーブルまでが一度に揃います（初期化は**初回のみ**＝空ボリュームのときだけ実行されます）。

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

##### Step 2. スキーマ初期化 (db/init/*.sql を番号順に投入)

```bash
# 新規 DB に接続し直し、初期化スクリプトを番号順に投入 (01..10)
# ※プラットフォーム統合改修後は 08-tenancy-rls.sql (テナント分離 RLS + アプリロール
#   akebono_app) と 09-updated-at-triggers.sql (updated_at トリガ汎用配線) まで
#   必ず適用すること。アプリの接続ユーザは akebono_app (§0-P 参照)。
# ※Iteration 30 (2026-07-27) で 10-attendance.sql (勤怠 6 テーブル + users への勤怠列追加) を
#   追加。08/09 より後に流れるため RLS と updated_at トリガは 10 が自ら配線する (ファイル冒頭の
#   コメント参照)。**投入し忘れると勤怠画面が全滅する**ので必ず 10 まで流すこと。
#
# ★★ 重要 (Iteration 30 の下位互換・原則7): 勤怠列は **users** テーブルにも追加される。
#    User エンティティが勤怠 6 列を map するため、**列が無いと users を引く全経路 (とりわけ
#    ログイン直後の /auth/sync) が「column does not exist」で失敗し、全利用者がログイン不能になる**。
#    - 新規デプロイ: 上記の 10-attendance.sql まで流せば列も付く。
#    - 既存 DB を更新する場合: **コードより先に** マイグレーションを適用すること。
#        既存 DB へは冪等マイグレーション db/migration/iter30-attendance.sql を当てる:
#          deploy/db/run-migrations.sh を ACTION=migrate で実行 (推奨。schema_migrations で二重適用防止)
#          または直接: psql "<接続先>" -f db/migration/iter30-attendance.sql
#        既存 users は attendance_permission=1 / punch_required=true 等の DEFAULT で backfill される
#        (既存利用者はそのまま打刻可能・ログイン可能)。冪等なので二重適用しても安全。
#    - 起動時ガード: バックエンドは起動時に「このコード版が要求する勤怠スキーマ」を検査し、
#        不足があれば「AKB-SCHEMA-GUARD ... 未適用の db/migration/*.sql を適用してください」を出して
#        **起動を中断**する (SchemaGuardHostedService)。cryptic なログイン失敗ではなく起動失敗として気づける。
#        検査対象は users の勤怠 6 列 (iter30) と attendance_fix_requests.target_punch_id (iter31、下記) の両方。
#
# ※Iteration 31 (2026-07-28 / C-2) で attendance_fix_requests に target_punch_id UUID NULL を追加。
#    打刻修正申請で「どの打刻を直すか」を指定できるようにする列 (NULL=同種の先頭 1 件へフォールバック)。
#    - 新規デプロイ: 10-attendance.sql が本列を含む (末尾に定義) ため追加作業は不要。
#    - 既存 DB を更新する場合: **コードより先に** db/migration/iter31-fix-target-punch.sql を適用する
#        (ADD COLUMN IF NOT EXISTS で冪等。ACTION=migrate deploy/db/run-migrations.sh が iter30/iter31 を
#        まとめて前進適用する)。未適用のまま起動すると打刻修正申請の一覧・作成・承認が column does not exist で
#        失敗する (ログインは通るが機能単位で沈黙的に壊れる) ため、上記の起動時ガードで起動失敗として検知する。
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/01-schema.sql

psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/02-masters.sql

psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/03-products.sql

psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/04-orders.sql

# 生産管理拡張 (BOM/生産指示書/素材発注書)。Iter 5 で追加
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/05-production.sql

# リアルなデモ業務データ一式 (商品/付属情報/発注/生産)。冪等 (ON CONFLICT) なので再実行可
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/06-demo-data.sql

# 業務拡張モジュール (販売管理/出荷/在庫管理) のテーブル + サンプルデータ。冪等 (再実行可)
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/07-ops-data.sql

# テナント分離 RLS + アプリロール akebono_app (プラットフォーム統合改修)。冪等 (再実行可)
# 適用後、akebono_app のパスワードを必ず変更する:
#   ALTER ROLE akebono_app WITH PASSWORD '<強固なパスワード>';
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/08-tenancy-rls.sql

# updated_at トリガの汎用配線 (プラットフォーム統合 第二段階)。冪等 (再実行可)
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/09-updated-at-triggers.sql

# 勤怠管理・タイムカード 6 テーブル + users への勤怠列追加 (Iteration 30)。冪等 (再実行可)
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -f db/init/10-attendance.sql

# 動作確認
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -c "SELECT id, login_id, display_name FROM users;"
# → owner / planner / sales の 3 件が表示されれば OK

# 勤怠 (10-attendance.sql) の投入確認
psql "host=<rds-endpoint> port=5432 dbname=akebono_honshu user=pguser sslmode=require" \
  -c "SELECT login_id, attendance_permission, punch_required FROM users;"
# → 3 件とも attendance_permission=1 / punch_required=true (DEFAULT) なら OK
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

ブラウザで `http://localhost:3000` → ログイン (owner / 開発時パスワード) → ホーム（ポータル） → 商品一覧 が表示できれば、ローカル UI + AWS RDS の構成で疎通完了。

##### トラブルシュート

| 症状 | 確認ポイント |
|---|---|
| `connection refused` | RDS セキュリティグループ、開発 PC の現在 IP が許可されているか |
| `password authentication failed` | pguser のパスワードを再確認 (RDS console から再リセット可能) |
| `SSL is not enabled on the server` | RDS の `rds.force_ssl` パラメータ確認、Connection String の `SslMode=Require` |
| Backend 起動時 EF Core エラー | スキーマ未投入。Step 2 を再実行 |
| `relation "..." does not exist` | スキーマファイルの投入順序ミス (01 → 02 → … → 09 の番号順) |
| 起動ログに `audit_logs パーティション先行作成に失敗しました` が毎回出る | audit_logs の DEFAULT パーティションに行が落ちている (長期停止後の再開直後等)。DEFAULT に対象月の行があると `CREATE TABLE ... PARTITION OF` が失敗し続けるため、下記の回復 SQL で行を月次パーティションへ移送する |

**audit_logs_default からの回復 SQL** (スーパーユーザで実行。`<YYYY>` `<MM>` は default に落ちた行の月):

```sql
BEGIN;
-- 1. 対象月の行を一時退避して default から削除
CREATE TEMP TABLE audit_default_moved AS
  SELECT * FROM audit_logs_default
  WHERE occurred_at >= DATE '<YYYY>-<MM>-01'
    AND occurred_at <  DATE '<YYYY>-<MM>-01' + INTERVAL '1 month';
DELETE FROM audit_logs_default
  WHERE occurred_at >= DATE '<YYYY>-<MM>-01'
    AND occurred_at <  DATE '<YYYY>-<MM>-01' + INTERVAL '1 month';
-- 2. 月次パーティションを作成 (default が空になったので成功する)
SELECT ensure_audit_log_partitions(3);
-- 3. 退避した行を戻す (ルーティングで新パーティションに入る)
INSERT INTO audit_logs SELECT * FROM audit_default_moved;
COMMIT;
```

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
# ヘルスチェック (Iter 4 段階 B 以降も同様)
Invoke-RestMethod http://localhost:5000/health
```

> **Iter 4 段階 B (2026-05-20) で `/auth/login` は削除されました。** 認証は Firebase JS SDK 経由 (`signInWithEmailAndPassword` → ID Token を `Authorization: Bearer` 送信) のみ。CLI で `/auth/sync` を直接叩く場合は Firebase REST API でメール+パスワードから ID Token を取得してから `Authorization: Bearer <idToken>` ヘッダ付きで POST する必要があり手間。**動作確認はブラウザ経由 (§0 step 8) で行ってください**。

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

> **⚠️ Iter 4 段階 B (2026-05-20) でログイン方式が変更されています。** 以下の `owner / localdev` 記述は旧 Iter 0 ダミー認証時代の手順です。**新しい手順は §0 (Iter 4 段階 B 完了による変更点) を参照してください**。要点: メール (例: `owner@akebono.example`) + Firebase Console で設定したパスワードを使用。

1. ブラウザで `http://localhost:3000` にアクセス → `/login` にリダイレクト
2. メール + パスワードを入力 → 「ログイン」クリック (旧: `owner / localdev`)
3. `/`（ポータルホーム）に着地。「システム管理」＞「ユーザー管理」（`/users`）でユーザ一覧 3 件 (owner / planner / sales) を確認
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

1. ブラウザで `http://localhost:3000` → メール (Firebase で `owner` 行に紐付け済アカウント) + パスワードでログイン (Iter 4 段階 B 以降、旧 `owner / localdev` 形式は廃止)
2. ホーム（ポータル）の「マスタ」カード → `/masters` に 17 マスタのカードが表示される
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

> **前提:** `db/init/03-products.sql` を pgAdmin4 で実行して商品関連 4 テーブル + Seed 投入。さらに `db/init/06-demo-data.sql` でリアルなデモ商品・仕入単価・BOM が投入される（冪等、再実行可）。Backend 再起動でローカル画像保存先 `wwwroot/uploads/product-images/` が自動作成される。

1. **商品企画一覧 (P-04):** `http://localhost:3000/products` → 商品企画が一覧表示される（`06-demo-data.sql` 投入時は「婦人コンフォートサンダル」等 8 企画・49 SKU）
   - テーブル / カード ボタンで切替 (lg 以上 5 列、md 3 列、sm 2 列)
   - カード表示で代表画像 (`primary_image_s3_key`) が表示される (画像未登録時はプレースホルダー)
2. **商品新規ウィザード (P-01〜P-03):** 商品一覧 (`/products`) 右上「+ 新規商品ウィザード」→ 4 セクションフォームで一括登録
   - 1 トランザクションで family + 色×サイズ全 SKU + 仕入単価を登録
   - 11 桁品番が自動生成される (例: NA1001A4010)
3. **詳細・修正 (P-05):** 商品カードクリック → 企画情報・SKU 一覧・仕入単価・画像のフル詳細
   - 「編集」ボタンで属性 (ブランド / 機能 / 商品群 / 素材 3 種 / 名称 / 状態) を変更
   - 「+ 新単価追加」: 旧単価の `effective_to` が自動更新 (BR-04 履歴管理)
4. **画像管理 (P-06):** 詳細画面「+ 画像追加」→ JPEG/PNG/WebP アップロード (5MB / 5 枚まで)
   - Iter 4 段階 C-1 で `IImageStorageService` 抽象化済 (appsettings `ImageStorage:Provider` で切替)
   - **Local モード (dev default):** Backend の `wwwroot/uploads/product-images/{familyId}/` にファイル保存
   - **S3 モード (prod):** AWS S3 PutObject + Pre-signed URL (TTL 15min) で配信
   - DB の `s3_key` は両モード共通 (相対パス形式)、`ImageSummary.url` フィールドで配信 URL を Backend が組立てて返す (Frontend に URL 組立てロジック無し)
5. **権限制御:** planner / sales でログイン → 編集ボタン全て非表示、Swagger で直接 POST すると 403 Forbidden
6. **監査ログ:**
   ```sql
   SELECT id, occurred_at, action, entity_type, entity_id, note
   FROM audit_logs WHERE entity_type LIKE 'Product%' ORDER BY id DESC LIMIT 10;
   ```
   期待: `ProductFamily.Create` (note に SKU 件数 + 金額マスク "***" 含む), `ProductFamily.View`, `ProductSupplierPrice.Add` 等が記録

### 3.3 Iteration 3 追加シナリオ (発注書 O-01〜O-07、Excel 出力 = MVP クリティカルパス)

> **前提:** `db/init/04-orders.sql` を pgAdmin4 で実行して発注関連 3 テーブル + Seed 投入。Backend 再起動で ClosedXML 0.105.0 が NuGet 復元される。

1. **発注書一覧 (O-03):** `http://localhost:3000/orders` → 発注書が一覧表示される（`26-00001` ほか、`06-demo-data.sql` 投入時は未出力／出力済（`S00021` 等）／中止が混在）
2. **新規発注書 (O-01):** ホーム（ポータル）の「発注」カード → `/orders` →「+ 新規発注書」→ ヘッダ + 明細 + 連絡文章テンプレ複写 (O-07) → 登録で `mgmt_no` 自動採番
3. **詳細・編集 (O-04、F-16):** 行クリック → 詳細画面で「編集」→ 数量/単価変更 → **編集理由 5 値 select 必須** + メモ任意 → 保存で `audit_logs.note` に `edit_reason=quantity` 記録
4. **帳票出力 (O-06、MVP クリティカルパス。旧システム「発注書出力」画面相当):**
   - 「📥 帳票出力」→ **出力フォーム**が開く。「発注日」「出荷指示番号」「発注番号」を手入力し、
     「出力帳票選択」(発注書のみ / 管理表のみ / 発注書+管理表) を選んで「出力」。
   - 入力した 3 項目は発注に保存され (`order_date` / `shipping_instruction_no` / `order_no`)、再出力時に初期表示される。
   - 発注書 (ORDER SHEET) / 管理表 (ORDER DETAIL) は旧帳票レイアウトに準拠。**国内は日本語・海外は英語**表記。
   - 初回出力で 3 件 snapshot 凍結 (F-22): `supplier_official_name_snapshot` / `supplier_code_snapshot` / `customer_name_snapshot`。
     発注番号はフォーム手入力が優先、未入力かつ未採番の初回出力のみ自動採番 (`S00001`) にフォールバック。
   - 発注書の管理表は分納の納期 (`delivery_date`) を「shipping date (納入日)」列として日付ごとに動的展開する。
5. **中止 (O-05):** 「中止」→ 中止理由必須入力 → 詳細画面が Cancelled (オレンジ)、編集ボタン消失。**帳票出力は引き続き可能** (Phase 6 F-11 仕様)
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
   期待: 初回出力は `is_first_export=true`、テンプレ版 `ordersheet-v1`

### 3.4 Iteration 30 追加シナリオ (勤怠・タイムカード)

> **前提:** `db/init/10-attendance.sql`（新規初期化）または `db/migration/iter30-attendance.sql` ＋ `iter31-fix-target-punch.sql`（既存 DB）を投入済みであること。
> **投入し忘れると勤怠画面が全滅する**（§2 の警告参照）。利用者の `attendance_permission` は DDL 既定が `1`（更新可能）。
> iter31（C-2）は打刻修正の対象打刻列 `attendance_fix_requests.target_punch_id` を足す（未適用だと打刻修正申請だけが沈黙的に壊れる。起動時ガードが検知）。

1. **打刻 (T-01):** `http://localhost:3000/attendance/timecard` → 「出勤」→ 状態バッジが「勤務中」へ変わり、出退勤一覧に当日の行が出る
   → 「休憩開始」→「休憩終了」→「退勤」。**同じ打刻を続けて押すと 409 `AKB-SYS-007`**（状態機械が拒否）
2. **日次集計:** `/attendance?tab=daily` → 左に打刻タイムライン、右に実労働 / 休憩 / 深夜の KPI 3 枚と 6 区分の内訳
   → 6 時間超で休憩なしなら**休憩不足警告**（労基法 34 条）が出る
3. **週次・月次:** `?tab=weekly`（週の起点は日曜、週 40 時間のプログレスバー）／`?tab=monthly`
   （カレンダー + 36 協定の月 45 時間ゲージ。超過が無ければ緑帯「現時点で 36 協定に関するアラートはありません。」）
4. **打刻修正の申請 → 承認:** 日次タブの「打刻修正を申請」→ 日付・種別・修正後時刻（`+09:00` 付き）・理由を入力して申請
   → **オーナー**（`process_record_permission >= 1`）で `?tab=requests` →「承認待ち」に出る → 「承認」
   → 日次タブで**修正前打刻に取消線 +「修正前」バッジ**、修正打刻に「修正反映」バッジが付く
   （**承認は取り消せない**。誤承認はあらためて修正申請で直す → §3.14 の注記）
   → **対象打刻の指定 (C-2):** 休憩を複数回とった日で、日次タイムラインの各打刻の「この打刻を修正」から申請すると
   モーダルに対象打刻（例「休憩開始 15:00」）が表示され、その打刻だけが置換される（2 回目の休憩開始を直しても 1 回目は残る）。
   汎用の「打刻修正を申請」ボタンや、モーダルで種別を変えた場合は対象指定が外れ、同種の先頭 1 件が対象になる（下位互換）
5. **休暇:** `?tab=leave` で有給残数・年 5 日取得義務トラッカー →「休暇を申請」→ オーナーが `?tab=requests` で承認
   → 月次カレンダーに「休暇」バッジが付く
6. **オーナー専用 3 タブ:** `?tab=timecard`（全員のタイムカード、既定 = 直近 7 日・上限 62 日）/
   `?tab=leave-admin`（付与・一括付与・周期付与）/ `?tab=settings`（勤怠ルール）が**オーナーにだけ**表示される
7. **権限の確認:** `attendance_permission = 2`（参照のみ）の利用者では**打刻ボタンと申請ボタンが出ない**（`== 1` 判定）、
   `0` では `/attendance` 本体を描画せず「勤怠機能の利用権限がありません。」+「ホームへ戻る」
8. **記録系保護の確認:** `akebono_app` ロールで接続して実行する（スーパーユーザは剥奪の対象外なので不可）。
   ```sql
   -- どちらも ERROR: permission denied for table punch_records となること
   UPDATE punch_records SET kind = 1 WHERE FALSE;
   DELETE FROM punch_records WHERE FALSE;
   ```
   期待: **両方とも権限エラー**（`WHERE FALSE` なので権限があった場合でも行は変わらない）。
   打刻は追記のみで、誤登録を削除して復旧する手段は無い（訂正は `source=2`(Fix) の追記による論理置換）。
   `db/verify/rls-smoke.sql §8` が同じ検査を恒久的に行う

---

## 4. 想定エンドポイント

| メソッド | パス | 概要 | 認証 | 権限 |
|---|---|---|---|---|
| GET | `/health` | ヘルスチェック | なし | – |
| GET | `/swagger` | Swagger UI (API ドキュメント + 動作確認画面) | なし | – |
| POST | `/api/maker/v1/auth/sync` | Firebase Auth ログイン直後の業務情報同期 (Iter 4 段階 B、`/auth/login` から置換) | Bearer (Firebase ID Token) | – |
| GET | `/api/maker/v1/auth/me` | 現在のユーザ情報 + 5 権限 (品番台帳 / 発注書作成 / 発注情報 / 工程実績=オーナー / **勤怠**) | Bearer (Firebase ID Token) | – |
| GET | `/api/maker/v1/users` | ユーザ一覧 | Bearer | – |
| GET | `/api/maker/v1/masters/{master}` | マスタ一覧 (17 種) | Bearer | – |
| POST/PATCH/DELETE | `/api/maker/v1/masters/{master}[/{id}]` | マスタ CRUD | Bearer | `product_ledger_permission == 1` |
| POST | `/api/maker/v1/masters/{master}/{id}/restore` | 論理削除取消 | Bearer | `product_ledger_permission == 1` |
| GET | `/api/maker/v1/products/families` | 商品企画一覧 (P-04) | Bearer | – |
| GET | `/api/maker/v1/products/families/{id}` | 商品企画詳細 (P-05) | Bearer | – |
| POST | `/api/maker/v1/products/families/complete` | バルク登録 (P-01〜P-03) | Bearer | `product_ledger_permission == 1` |
| PATCH | `/api/maker/v1/products/families/{id}` | 企画更新 (P-05) | Bearer | `product_ledger_permission == 1` |
| DELETE | `/api/maker/v1/products/families/{id}` | 企画論理削除 (配下 SKU 連動) | Bearer | `product_ledger_permission == 1` |
| GET | `/api/maker/v1/products/families/{id}/supplier-prices` | 仕入単価履歴 | Bearer | – |
| POST | `/api/maker/v1/products/families/{id}/supplier-prices` | 新単価追加 (BR-04) | Bearer | `product_ledger_permission == 1` |
| POST | `/api/maker/v1/products/families/{id}/images` | 画像アップロード (P-06、IFormFile) | Bearer | `product_ledger_permission == 1` |
| PATCH | `/api/maker/v1/products/families/{id}/images/reorder` | 画像順序変更 | Bearer | `product_ledger_permission == 1` |
| DELETE | `/api/maker/v1/products/families/{id}/images/{imageId}` | 画像論理削除 | Bearer | `product_ledger_permission == 1` |
| GET | `/uploads/product-images/{familyId}/{filename}` | 画像配信 (Local モード時のみ。S3 モード時は Pre-signed URL 経由で `https://<bucket>.s3.<region>.amazonaws.com/...` から直接配信) | なし | – |
| GET | `/api/maker/v1/orders` | 発注書一覧 (O-03) | Bearer | – |
| GET | `/api/maker/v1/orders/{id}` | 発注書詳細 (O-04) | Bearer | – |
| POST | `/api/maker/v1/orders` | 新規発注書 (O-01) | Bearer | `purchase_order_create_permission == 1` |
| PATCH | `/api/maker/v1/orders/{id}` | 発注書編集 (O-04、`editReason` 5 値必須 F-16) | Bearer | `purchase_order_create_permission == 1` |
| POST | `/api/maker/v1/orders/{id}/cancel` | 中止 (O-05) | Bearer | `purchase_order_create_permission == 1` |
| POST | `/api/maker/v1/orders/{id}/export` | 帳票出力フォーム (O-06、発注日/出荷指示番号/発注番号 を手入力 + 帳票選択。初回 snapshot 凍結 F-22) | Bearer | `purchase_order_create_permission == 1` |
| POST | `/api/maker/v1/orders/bulk-export` | 一括ダウンロード (#3b、発注書 ZIP / 管理表 xlsx / 両方 ZIP) | Bearer | `purchase_order_create_permission == 1` |
| GET | `/api/maker/v1/orders/communication-suggestions` | 連絡文章テンプレ (O-07) | Bearer | – |

> **2026-07-27 訂正（権限判定は `>= 1` ではなく `== 1`）:** 上表の権限列は当初 `>= 1` と記載していたが、
> `product_ledger_permission`（0=なし / 1=更新可能 / 2=参照のみ / 3=参照のみ(制限)）と
> `purchase_order_create_permission`（0=なし / 1=更新可能 / 2=参照のみ）は **非単調エンコード**であり、
> 実装（`src/Backend/Presentation/Endpoints/AuthEndpoints.cs` の `CheckMasterEditAsync` / `CheckOrderEditAsync`）は
> **`== 1`（更新可能）のみ書込を許可**する。`>= 1` と書くと「参照のみ」ユーザに書込を許すバグになる。
> 同じ規則が §4.1 の勤怠権限にも適用される。

`{master}` は: brands / sizes / functions / countries / suppliers / departments / product-types / product-seasons / product-groups / colors / materials / material-classifications / warehouses / delivery-destinations / document-template-purchases / document-template-confirmations / document-text-purchases

### 4.1 勤怠 (Iteration 30、27 エンドポイント)

> **権限の読み方（重要）:** 勤怠権限 `attendance_permission` は **非単調エンコード**（`0=なし / 1=更新可能 / 2=参照のみ`）。
> 「値が大きい = 高権限」ではないため、書込は `>= 1` ではなく **`== 1`** で判定する。
> 表中の **オーナー** は `process_record_permission >= 1`（工程実績管理権限。0/1 の 2 値なので `>= 1` で正しい）。
> 管理系（全員のタイムカード・承認/却下・休暇付与・各種設定・`scope=all`）は、**勤怠参照権限（1 or 2）かつオーナー**の **AND** で判定する
> （`CheckAttendanceAdminAsync` が参照権限を内包する。**オーナーだけでは足りない** — 2026-07-27 訂正）。
> 他人の勤怠を参照する `?userId` 指定もオーナーが必要（省略時は常に自分）。
> 詳細な I/F は `.ai-native/outputs/phase5/api-design.md §2.7`（`#` 番号は同節と一致）。
>
> **一覧のページング（2026-07-27 追記）:** 申請一覧 2 本（#8 打刻修正申請 / #21 休暇申請）は
> キーセットページング（`?limit`（1〜200）/ `?cursor`、続きの有無は `meta.page.hasMore`、不正は 400 `AKB-SYS-011`）。
> **この 2 本だけ `limit` 省略時の既定が 200**（§0-P の全体既定 50 とは異なる。フロントがまだ `limit` /
> `cursor` を送らないため、既定 50 では申請が黙って欠落するのを避ける措置）。curl 疎通時は
> `meta.page.nextCursor` を辿ること。
>
> **利用者数の上限（2026-07-27 追記）:** #6 タイムカードは 200 人、#27 休暇管理一覧は 500 人を超えると
> **422 `AKB-SYS-002`**「対象の利用者が多すぎます（一度に集計できるのは {N} 人までです）」。
> #6 は `q`（氏名の部分一致）で絞り込む（**期間を短くしても利用者数は減らない**）、#27 は #20 の個人別サマリで確認する。

**打刻・集計 (#1〜#6) — `src/Backend/Presentation/Endpoints/AttendanceEndpoints.cs`**

| # | メソッド | パス | 概要 | 認証 | 権限 |
|---|---|---|---|---|---|
| 1 | POST | `/api/maker/v1/attendance/punches` | 打刻 (対象は常に本人) | Bearer | `attendance_permission == 1` かつ `punch_required` |
| 2 | GET | `/api/maker/v1/attendance/state` | 当日の打刻状態 (打刻ウィジェット用) | Bearer | `attendance_permission` 1 or 2 |
| 3 | GET | `/api/maker/v1/attendance/day` | 日次サマリ (`?userId&date&raw`) | Bearer | 1 or 2 (他人は 1 or 2 **かつオーナー**) |
| 4 | GET | `/api/maker/v1/attendance/month` | 月次サマリ (`?userId&month`) | Bearer | 1 or 2 (他人は 1 or 2 **かつオーナー**) |
| 5 | GET | `/api/maker/v1/attendance/alerts` | 36 協定アラート (`?userId&endMonth`、直近 6 ヶ月) | Bearer | 1 or 2 (他人は 1 or 2 **かつオーナー**) |
| 6 | GET | `/api/maker/v1/attendance/timecard` | 全員のタイムカード (`?from&to&q`、期間上限 62 日 = 両端含み、利用者数上限 200 人)。並びは日付降順 → 氏名昇順 → 利用者 ID 昇順 | Bearer | 1 or 2 **かつオーナー** |

**打刻修正申請 (#7〜#9)**

| # | メソッド | パス | 概要 | 認証 | 権限 |
|---|---|---|---|---|---|
| 7 | POST | `/api/maker/v1/attendance/fix-requests` | 修正申請 (対象は常に本人、理由必須) | Bearer | `attendance_permission == 1` |
| 8 | GET | `/api/maker/v1/attendance/fix-requests` | 申請一覧 (`?status&scope&limit&cursor`) | Bearer | 1 or 2 / `scope=all` は 1 or 2 **かつオーナー** |
| 9 | POST | `/api/maker/v1/attendance/fix-requests/{id}/decision` | 承認 / 却下 (元打刻は削除せず修正打刻を追記) | Bearer | 1 or 2 **かつオーナー** |

**勤怠ルール = 勤務体系マスタ (#10〜#14)**

| # | メソッド | パス | 概要 | 認証 | 権限 |
|---|---|---|---|---|---|
| 10 | GET | `/api/maker/v1/attendance/rules` | 一覧 (`?includeInactive`、既定 false) | Bearer | 1 or 2 |
| 11 | POST | `/api/maker/v1/attendance/rules` | 新規作成 | Bearer | 1 or 2 **かつオーナー** |
| 12 | PATCH | `/api/maker/v1/attendance/rules/{id}` | 部分更新 (null のフィールドは現在値を保持。ただし `flexEnabled=false` を受けるとコアタイムは指定値に関わらず null になる) | Bearer | 1 or 2 **かつオーナー** |
| 13 | DELETE | `/api/maker/v1/attendance/rules/{id}` | 論理削除 | Bearer | 1 or 2 **かつオーナー** |
| 14 | POST | `/api/maker/v1/attendance/rules/{id}/restore` | 論理削除の取消 (同名の有効なルールがあると 409) | Bearer | 1 or 2 **かつオーナー** |

**休暇 (#15〜#27) — `src/Backend/Presentation/Endpoints/AttendanceLeaveEndpoints.cs`**

| # | メソッド | パス | 概要 | 認証 | 権限 |
|---|---|---|---|---|---|
| 15 | GET | `/api/maker/v1/attendance/leave/types` | 休暇種別 一覧 (`?includeInactive`) | Bearer | 1 or 2 |
| 16 | POST | `/api/maker/v1/attendance/leave/types` | 休暇種別 作成 (法定有給は作成不可。**名称 `有給休暇` は予約名のため 422**。作成時のみの制限で、既存種別の更新・復元は妨げない) | Bearer | 1 or 2 **かつオーナー** |
| 17 | PATCH | `/api/maker/v1/attendance/leave/types/{id}` | 休暇種別 部分更新 (法定有給は 409) | Bearer | 1 or 2 **かつオーナー** |
| 18 | DELETE | `/api/maker/v1/attendance/leave/types/{id}` | 休暇種別 論理削除 (付与・申請の実績は残す) | Bearer | 1 or 2 **かつオーナー** |
| 19 | POST | `/api/maker/v1/attendance/leave/types/{id}/restore` | 休暇種別 復元 (同名の有効な種別があると 409) | Bearer | 1 or 2 **かつオーナー** |
| 20 | GET | `/api/maker/v1/attendance/leave/summary` | 残数・年 5 日義務・履歴 (`?userId`) | Bearer | 1 or 2 (他人は 1 or 2 **かつオーナー**) |
| 21 | GET | `/api/maker/v1/attendance/leave/requests` | 休暇申請 一覧 (`?scope&status&from&to&limit&cursor`)。`from` / `to` は取得日の範囲 (両端含み・片側のみ可・**両方省略時は全期間**、上限 366 日) | Bearer | 1 or 2 / `scope=all` は 1 or 2 **かつオーナー** |
| 22 | POST | `/api/maker/v1/attendance/leave/requests` | 休暇申請 (対象は常に本人) | Bearer | `attendance_permission == 1` |
| 23 | POST | `/api/maker/v1/attendance/leave/requests/{id}/decision` | 承認 / 却下 (処理済みの再操作は 409) | Bearer | 1 or 2 **かつオーナー** |
| 24 | POST | `/api/maker/v1/attendance/leave/grants` | 個別付与 (同一 user × 種別 × 付与日は `skipped=1`) | Bearer | 1 or 2 **かつオーナー** |
| 25 | POST | `/api/maker/v1/attendance/leave/grants/bulk` | 一括付与 (`target=all` のみ、既存分は skipped) | Bearer | 1 or 2 **かつオーナー** |
| 26 | POST | `/api/maker/v1/attendance/leave/periodic-grants/run` | 周期自動付与の実行 (冪等、既存付与は変更しない) | Bearer | 1 or 2 **かつオーナー** |
| 27 | GET | `/api/maker/v1/attendance/leave/admin/summary` | 休暇管理一覧 (メンバー × 種別の付与/取得/残。利用者数上限 500 人) | Bearer | 1 or 2 **かつオーナー** |

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
│   │   ├── Infrastructure/     EF Core + 監査 (Iter 4 段階 B で認証は Presentation/Program.cs の JwtBearer に移行)
│   │   └── Presentation/       Minimal API エンドポイント
│   └── Frontend/       Nuxt 3 + Reka UI + Tailwind CSS
│       ├── pages/              ルーティング (login, index, masters, products, orders, production,
│       │                        attendance, sales, shipping, inventory, analytics, users, admin)
│       ├── composables/        useAuth (Firebase Auth、Iter 4 段階 B) / useApi (getIdToken Bearer)
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
| ログイン失敗 (Iter 4 段階 B 以降) | Firebase Console のテストユーザ Email + パスワードを使う。`users.firebase_uid` が紐付け済か確認 (`SELECT firebase_uid, is_active, deleted_at FROM users WHERE login_id='owner'`)。失敗時の audit 記録は: 未紐付け UID → `Auth.UidUnboundProbe` (actor_user_id=NULL)、inactive ユーザ → `Auth.LoginRejected.Inactive` (actor_user_id 付き)。**いずれも 5 分に 1 回しか記録されない**ので連続テスト時は Backend 再起動で cache flush。 |
| `pnpm dev` でポート 3000 衝突 | 別アプリ使用中、`pnpm dev --port 3001` で代替 (`.env` の NUXT_PUBLIC_API_BASE は変更不要) |

---

## 8. Iteration 0 のスコープと制約

**実装済み (Iteration 0):**
- PostgreSQL ローカル起動 + 初期スキーマ + Seed (選択肢 A pgAdmin4 / 選択肢 B docker)
- .NET 8 Backend (4 層) + EF Core 8 + Firebase Auth (JwtBearer + JWKS、Iter 4 段階 B) + audit_logs 記録
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
| EC2(ubuntu) コンテナ + Firebase Hosting | Iteration 4 段階 C/D | 本番デプロイ (Dockerfile / **GHCR** / repository secrets)。当初計画は App Runner/ECR、実装で EC2/GHCR に変更。SoT: `deploy/README.md` |
| CI/CD (GitHub Actions) | ✅ Iteration 4 段階 D 実装 | main push で自動デプロイ (`deploy-backend`/`deploy-frontend`)、DB は手動 `db-migrate` |
| EF Core マイグレーション | Iteration 1 | 現在は `db/init/01-schema.sql` + `db/migration/*.sql` を投入 |
| TLS / セキュリティ強化 | Iteration 4 段階 C | KMS / IAM 最小権限 / audit_logs 改竄防止 |
| User Secrets / Connection String 整理 | ✅ Iteration 4 段階 A 完了 | `dotnet user-secrets` 運用に移行済 |
| Firebase Custom Claims 同期 (シナリオ E) | Iteration 4 段階 C | 権限変更時の RDS → Firebase 後追い同期 + Reconciler バッチ |

詳細は `.ai-native/outputs/phase7/iteration-plan.md` を参照。

---

## 9. 関連ドキュメント

- **本番デプロイ (CI/CD): `deploy/README.md`** — GitHub Actions による Firebase Hosting /
  EC2 コンテナ配置 / RDS 初期化・マイグレーションの手順・必要 secrets 一覧 (SoT)
- Phase 7 Iteration 計画: `.ai-native/outputs/phase7/iteration-plan.md`
- Phase 7 INDEX: `.ai-native/outputs/phase7/_index.md`
- Phase 5 設計: `.ai-native/outputs/phase5/{architecture,data-design,api-design,screen-design}.md`
- Phase 3 機能要件: `.ai-native/outputs/phase3/functional-requirements.md`
- 方法論 SoT: `.ai-native/methodology/`
- 環境固有実装ルール: `CLAUDE.md`
