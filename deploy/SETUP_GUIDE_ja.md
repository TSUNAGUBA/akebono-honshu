# Akebono Honshu 本番セットアップ手順書（Windows PowerShell / 初学者向け）

> この手順書は **GitHub Actions で本番デプロイできる状態にする**ための、上から順に実行すれば完了する一本道の手順です。
> コマンドは **Windows PowerShell** 用です。`<...>` の部分だけご自身の値に置き換えてください。
> 不明点はコマンド結果を共有いただければ確認します。
>
> ⚠️ 本ファイルには RDS エンドポイント・EC2 IP 等の**環境識別子**が含まれます（パスワード等の秘密値は含みません）。
> **リポジトリは private 前提**です。public 化する場合は識別子を伏せるか、本手順書を非公開チャネルで配布してください。

## この環境の確定値（置き換え済み）

| 項目 | 値 |
|---|---|
| EC2 ホスト（公開IP） | `57.180.167.243`（SSH ユーザ `ubuntu`） |
| 既存リバースプロキシ | `jwilder/nginx-proxy` + `acme-companion`（自動 TLS）。共有ネットワーク `tsunaguba-dev-001` |
| API ドメイン | `akebono-honshu-api.akebono.work`（このサブドメインを足す） |
| Let's Encrypt メール | `yamashita@tsunaguba.co.jp` |
| RDS | `tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com` / DB `akebono_honshu` |
| Frontend（Firebase Hosting） | `https://akebono-honshu-e388e.web.app`（project `akebono-honshu-e388e`） |
| API ベース URL | `https://akebono-honshu-api.akebono.work/api/v1` |
| GitHub リポジトリ | `tsunaguba/akebono-honshu` |

> **AWS の設定（OIDC/IAM）は不要**です。EC2 は固定 IP のため `EC2_HOST` を直接使い、AWS 認証なしでデプロイします。

---

## 手順 0: 準備するもの

- Windows PowerShell（標準）。`ssh` / `ssh-keyscan` が使えること（`ssh -V` で確認）。
- 変換済み SSH 秘密鍵 `C:\key\akebono-deploy`（前段で `.ppk` から OpenSSH 形式に変換済み）。
- GitHub リポジトリ `tsunaguba/akebono-honshu` の **管理者権限**（Secrets 登録に必要）。
- RDS の **マスターユーザ名 / パスワード**（DB・ユーザ作成に一度だけ使用）。
- Firebase project `akebono-honshu-e388e` の **編集権限**。
- DNS `akebono.work` の **レコード追加権限**。
- （任意・推奨）GitHub CLI `gh`。無くても Web UI で代替できます。
  ```powershell
  winget install --id GitHub.cli -e   # 未導入なら
  gh auth login                        # ブラウザでログイン
  ```

---

## 手順 1: SSH 接続確認 & ホスト鍵の取得

```powershell
# 1-1. 接続確認（成功すると EC2 のプロンプトに入る。確認できたら exit で戻る）
ssh -i C:\key\akebono-deploy ubuntu@57.180.167.243

# 1-2. ホスト鍵を取得（GitHub Actions の MITM 検証用。後で secret に登録）
ssh-keyscan 57.180.167.243 | Out-File -Encoding ascii C:\key\known_hosts_akebono.txt
Get-Content C:\key\known_hosts_akebono.txt
```

> 1-1 で `57.180.167.243` のポート 22 に届かない場合、EC2 のセキュリティグループで SSH(22) が空いていません。手順 5 の前に **「SSH(22) を GitHub Actions から許可」**（後述のセキュリティ注記）を実施してください。

---

## 手順 2: RDS に DB とユーザを作成（EC2 から psql）

RDS は EC2 から到達できます。EC2 上の使い捨て psql コンテナで、**マスターユーザ**として実行します。

```powershell
# 2-1. EC2 に SSH 接続
ssh -i C:\key\akebono-deploy ubuntu@57.180.167.243
```

EC2 に入ったら（Linux 側で）以下を実行。`<MASTER_USER>` は RDS マスターユーザ名に置き換え。パスワードは対話プロンプトで入力します（履歴に残りません）。

```bash
docker run --rm -it postgres:16-alpine psql \
  -h tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com \
  -U <MASTER_USER> -d postgres
```

`Password:` と出たらマスターパスワードを入力。psql に入ったら、**2 か所のパスワードを決めて**以下を貼り付け（`__MIGRATOR_PW__` / `__APP_PW__` を強固なパスワードに置換）:

```sql
-- DDL 用（マイグレーション実行ユーザ）と DML 用（アプリ実行ユーザ）を分離
CREATE ROLE akebono_migrator LOGIN PASSWORD '__MIGRATOR_PW__';
CREATE ROLE akebono_app      LOGIN PASSWORD '__APP_PW__';

-- 【RDS 必須】マスターは「真の superuser」ではないため、別ロール所有の DB を作るには
-- 先にそのロールのメンバーになる必要がある。これが無いと CREATE/ALTER ... OWNER が
-- `must be member of role` で失敗し、DB が master 所有のまま → init の ALTER DATABASE で
-- `must be owner of database` エラーになる。
GRANT akebono_migrator TO CURRENT_USER;

-- DB を作成。所有者を migrator に（init スクリプトの ALTER DATABASE 実行に必要）
CREATE DATABASE akebono_honshu OWNER akebono_migrator;

-- 対象 DB に切り替えて権限設定
\connect akebono_honshu
ALTER SCHEMA public OWNER TO akebono_migrator;
GRANT USAGE ON SCHEMA public TO akebono_app;
-- migrator が今後作るテーブル/シーケンスに、app の DML を自動付与
ALTER DEFAULT PRIVILEGES FOR ROLE akebono_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO akebono_app;
ALTER DEFAULT PRIVILEGES FOR ROLE akebono_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO akebono_app;

-- 検証: 両方とも akebono_migrator であること（master のままなら GRANT が効いていない）
SELECT 'DB owner=' || pg_catalog.pg_get_userbyid(datdba) FROM pg_database WHERE datname='akebono_honshu';
SELECT 'schema public owner=' || pg_catalog.pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname='public';

\q
```

接続テスト（EC2 上で。パスワードは `read -s` で入力し、bash 履歴・docker 引数に残しません）:

```bash
read -rs -p "akebono_migrator のパスワード: " PGPASSWORD; export PGPASSWORD; echo
docker run --rm -e PGPASSWORD postgres:16-alpine psql \
  -h tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com \
  -U akebono_migrator -d akebono_honshu -c "SELECT current_user, current_database();"
unset PGPASSWORD
```
`akebono_migrator | akebono_honshu` が返れば成功。確認できたら `exit` で EC2 から戻ります。

> 決めた 2 つのパスワードは手順 5 の secret（`DB_ADMIN_PASSWORD` と `PROD_DB_CONNECTION` 内）で使います。控えておいてください。

---

## 手順 3: DNS の A レコードを追加

`akebono.work` の DNS 管理画面で、次の A レコードを 1 つ追加します。

| ホスト名 | タイプ | 値 |
|---|---|---|
| `akebono-honshu-api.akebono.work` | A | `57.180.167.243` |

> 反映後、Let's Encrypt 証明書は backend を起動した時に **acme-companion が自動発行**します（数分かかることがあります）。

---

## 手順 4: Firebase（`akebono-honshu-e388e`）の設定

[Firebase コンソール](https://console.firebase.google.com/) で project `akebono-honshu-e388e` を開きます。

1. **Authentication 有効化**: 左メニュー Authentication →「始める」→ Sign-in method →「メール/パスワード」を**有効化**。
2. **テストユーザ作成**: Authentication → Users →「ユーザーを追加」→ 例 `owner@akebono.example` + 任意パスワード。
   作成後、その行の **ユーザー UID** をコピー（手順 6 で使用）。
3. **Web アプリ設定の取得**: プロジェクトの設定（⚙）→「全般」→ マイアプリ → Web アプリ（無ければ「アプリを追加」→ Web）。
   `firebaseConfig` の以下をメモ:
   - `apiKey` → `NUXT_PUBLIC_FIREBASE_API_KEY`
   - `authDomain`（=`akebono-honshu-e388e.firebaseapp.com`）→ `NUXT_PUBLIC_FIREBASE_AUTH_DOMAIN`
   - `projectId`（=`akebono-honshu-e388e`）→ `NUXT_PUBLIC_FIREBASE_PROJECT_ID`
   - `storageBucket` → `NUXT_PUBLIC_FIREBASE_STORAGE_BUCKET`
   - `messagingSenderId` → `NUXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID`
   - `appId` → `NUXT_PUBLIC_FIREBASE_APP_ID`
4. **サービスアカウント鍵の発行**: プロジェクトの設定 →「サービス アカウント」→「新しい秘密鍵の生成」→ JSON をダウンロード（例 `C:\key\firebase-sa.json`）。
   → `FIREBASE_SERVICE_ACCOUNT`（JSON 全文）に使用。**Git にはコミットしない**。
5. **承認済みドメイン**: Authentication → Settings → 承認済みドメインに `akebono-honshu-e388e.web.app` があること（既定で入っています）。

---

## 手順 5: GitHub Secrets を登録

`Settings → Secrets and variables → Actions → Repository secrets` に登録します。
**方法 A（gh CLI・推奨）** か **方法 B（Web UI）** のどちらかで。

### 登録する値の一覧（この環境用）

| Secret 名 | 値 |
|---|---|
| `EC2_HOST` | `57.180.167.243` |
| `EC2_SSH_USER` | `ubuntu` |
| `EC2_SSH_PRIVATE_KEY` | `C:\key\akebono-deploy` の中身（全文） |
| `EC2_SSH_KNOWN_HOSTS` | `C:\key\known_hosts_akebono.txt` の中身（手順 1-2） |
| `API_VIRTUAL_HOST` | `akebono-honshu-api.akebono.work` |
| `LETSENCRYPT_EMAIL` | `yamashita@tsunaguba.co.jp` |
| `PROD_DB_CONNECTION` | `Host=tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com;Port=5432;Database=akebono_honshu;Username=akebono_app;Password=<APP_PW>;SSL Mode=Require;Trust Server Certificate=true` |
| `FIREBASE_PROJECT_ID` | `akebono-honshu-e388e` |
| `CORS_ORIGINS` | `https://akebono-honshu-e388e.web.app` |
| `DB_HOST` | `tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com` |
| `DB_NAME` | `akebono_honshu` |
| `DB_ADMIN_USER` | `akebono_migrator` |
| `DB_ADMIN_PASSWORD` | `<MIGRATOR_PW>`（手順 2 で決めた値） |
| `NUXT_PUBLIC_API_BASE` | `https://akebono-honshu-api.akebono.work/api/v1` |
| `NUXT_PUBLIC_FIREBASE_API_KEY` | （手順 4-3 の `apiKey`） |
| `NUXT_PUBLIC_FIREBASE_AUTH_DOMAIN` | `akebono-honshu-e388e.firebaseapp.com` |
| `NUXT_PUBLIC_FIREBASE_PROJECT_ID` | `akebono-honshu-e388e` |
| `NUXT_PUBLIC_FIREBASE_STORAGE_BUCKET` | （手順 4-3 の `storageBucket`） |
| `NUXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID` | （手順 4-3 の `messagingSenderId`） |
| `NUXT_PUBLIC_FIREBASE_APP_ID` | （手順 4-3 の `appId`） |
| `FIREBASE_SERVICE_ACCOUNT` | `C:\key\firebase-sa.json` の中身（全文） |

> `<APP_PW>` は手順 2 のアプリ用パスワード。`PROXY_NETWORK` は既定 `tsunaguba-dev-001` のため登録不要。AWS 系（`AWS_ROLE_ARN` 等）も不要です。

### 方法 A: gh CLI（PowerShell）

```powershell
$repo = "tsunaguba/akebono-honshu"

# 単純な値
gh secret set EC2_HOST                         --repo $repo --body "57.180.167.243"
gh secret set EC2_SSH_USER                     --repo $repo --body "ubuntu"
gh secret set API_VIRTUAL_HOST                 --repo $repo --body "akebono-honshu-api.akebono.work"
gh secret set LETSENCRYPT_EMAIL                --repo $repo --body "yamashita@tsunaguba.co.jp"
gh secret set FIREBASE_PROJECT_ID              --repo $repo --body "akebono-honshu-e388e"
gh secret set CORS_ORIGINS                     --repo $repo --body "https://akebono-honshu-e388e.web.app"
gh secret set DB_HOST                          --repo $repo --body "tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com"
gh secret set DB_NAME                          --repo $repo --body "akebono_honshu"
gh secret set DB_ADMIN_USER                    --repo $repo --body "akebono_migrator"
gh secret set NUXT_PUBLIC_API_BASE             --repo $repo --body "https://akebono-honshu-api.akebono.work/api/v1"
gh secret set NUXT_PUBLIC_FIREBASE_AUTH_DOMAIN --repo $repo --body "akebono-honshu-e388e.firebaseapp.com"
gh secret set NUXT_PUBLIC_FIREBASE_PROJECT_ID  --repo $repo --body "akebono-honshu-e388e"

# パスワードを含む値は Read-Host で入力する（PowerShell 履歴にはコマンド本文＝変数名のみ残り、
# 値は履歴に残りません。より厳密にしたい場合は履歴に残らない「方法 B: Web UI」を使用）。
$migPw = Read-Host "akebono_migrator のパスワード"
gh secret set DB_ADMIN_PASSWORD  --repo $repo --body $migPw
$appPw = Read-Host "akebono_app のパスワード"
gh secret set PROD_DB_CONNECTION --repo $repo --body "Host=tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com;Port=5432;Database=akebono_honshu;Username=akebono_app;Password=$appPw;SSL Mode=Require;Trust Server Certificate=true"

# Firebase Web config は公開情報のため --body で可
gh secret set NUXT_PUBLIC_FIREBASE_API_KEY          --repo $repo --body "<apiKey>"
gh secret set NUXT_PUBLIC_FIREBASE_STORAGE_BUCKET   --repo $repo --body "<storageBucket>"
gh secret set NUXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID --repo $repo --body "<messagingSenderId>"
gh secret set NUXT_PUBLIC_FIREBASE_APP_ID           --repo $repo --body "<appId>"

# ファイルの中身をそのまま（複数行も安全）
Get-Content C:\key\akebono-deploy            -Raw | gh secret set EC2_SSH_PRIVATE_KEY  --repo $repo
Get-Content C:\key\known_hosts_akebono.txt   -Raw | gh secret set EC2_SSH_KNOWN_HOSTS  --repo $repo
Get-Content C:\key\firebase-sa.json          -Raw | gh secret set FIREBASE_SERVICE_ACCOUNT --repo $repo

# 確認
gh secret list --repo $repo
```

### 方法 B: Web UI

リポジトリ →`Settings`→`Secrets and variables`→`Actions`→`New repository secret` を上表の数だけ繰り返します。
鍵・JSON（`EC2_SSH_PRIVATE_KEY` / `EC2_SSH_KNOWN_HOSTS` / `FIREBASE_SERVICE_ACCOUNT`）は、メモ帳でファイルを開き **全文をコピーして貼り付け**ます。

### セキュリティ注記: SSH(22) の開放

GitHub Actions のランナーは IP が固定でないため、EC2 のセキュリティグループ Inbound 22 が届く必要があります。
- 簡易: 22 を `0.0.0.0/0` に開放（**パスワード認証は無効・鍵のみ**であること）。
- 安全度を上げるなら、Appendix の **self-hosted runner**（SSH を開けずに EC2 上で実行）も検討してください。

---

## 手順 6: 初回デプロイ（順番が重要）

GitHub の **Actions** タブから手動実行します。

### 6-1. DB 初期化
`Actions → DB Init / Migrate (RDS) → Run workflow` → `action` で **`init`** を選択 → Run。
- 空 DB に `db/init/*.sql`（01..06、06 はリアルなデモ業務データ）を番号順に投入し、現行マイグレーションを baseline 記録します。
- 緑（成功）になったら次へ。

### 6-2. Backend デプロイ
`Actions → Deploy Backend (EC2) → Run workflow`（ブランチ `main`）→ Run。
- GHCR へ build/push → EC2 で `docker compose up`（nginx-proxy に相乗り）→ health 確認。
- 初回は Let's Encrypt 証明書発行に数分かかることがあります。

### 6-3. ログインユーザの紐付け（owner に Firebase UID と権限を付与）
EC2 に SSH し、手順 4-2 でコピーした **Firebase UID** を使って実行（`<MIGRATOR_PW>` / `<FIREBASE_UID>` を置換）:

> `owner` 行には **init で既に全 4 権限が付与済み**です（`db/init/02-masters.sql`）。
> ここでの主目的は **Firebase UID の紐付け** と **監査ログの追記専用化** です。

EC2 上で、パスワードを `read -s` で入力（履歴・docker 引数に残りません）してから 2 つを実行します
（`<FIREBASE_UID>` は手順 4-2 の UID に置換）:

```bash
read -rs -p "akebono_migrator のパスワード: " PGPASSWORD; export PGPASSWORD; echo
RDS=tsunaguba-dev-001.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com

# (1) owner に Firebase UID を紐付け
docker run --rm -e PGPASSWORD postgres:16-alpine psql -h "$RDS" \
  -U akebono_migrator -d akebono_honshu \
  -c "UPDATE users SET firebase_uid = '<FIREBASE_UID>', updated_at = NOW() WHERE login_id = 'owner';"

# (2) 監査ログを追記専用に（改竄防止・必須。db/init/01-schema.sql の設計: audit_logs は INSERT 専用）
docker run --rm -e PGPASSWORD postgres:16-alpine psql -h "$RDS" \
  -U akebono_migrator -d akebono_honshu \
  -c "REVOKE UPDATE, DELETE ON audit_logs FROM akebono_app;"

unset PGPASSWORD
```

### 6-4. Frontend デプロイ
`Actions → Deploy Frontend (Firebase Hosting) → Run workflow` → Run。
- Nuxt を静的生成し `akebono-honshu-e388e.web.app` に配信します。

---

## 手順 7: 動作確認

```powershell
# 7-1. API ヘルスチェック（証明書発行後）
curl.exe https://akebono-honshu-api.akebono.work/health
# → {"status":"ok"} が返れば OK
```
7-2. ブラウザで `https://akebono-honshu-e388e.web.app` を開く → 手順 4-2 のメール/パスワードでログイン → 商品一覧などが表示されれば疎通完了。

---

## 以降の運用

- **コード変更**: `main` に push すると、変更箇所に応じて backend / frontend が自動デプロイされます。手動は Actions タブから。
- **スキーマ変更**: `db/migration/` に `mig-3-*` 以外の `*.sql` を追加 → `DB Init / Migrate` を `action=migrate` で実行（適用済みは自動 skip）。
- **デモ業務データの反映（既存 DB）**: 稼働中 DB は `init` が中止されるため、リアルなデモデータ（商品・付属情報・発注・生産）は `db/migration/iter6-demo-data.sql`（`db/init/06-demo-data.sql` を取り込む、冪等）が `action=migrate` で適用されます。
- **MIG-3（既存 CSV 取込）**: 画面 `/admin/legacy-import` から実施（このワークフローの対象外）。

---

## トラブルシュート

| 症状 | 確認 |
|---|---|
| `Deploy Backend` が SSH で失敗 | SG の 22 が GitHub から届くか / `EC2_SSH_PRIVATE_KEY`・`EC2_SSH_KNOWN_HOSTS` の中身 |
| `/health` が繋がらない・証明書エラー | DNS A レコード（手順 3）反映済みか / 数分待つ（acme 発行）/ EC2 で `docker logs nginx-proxy-acme` |
| ログインできるが画面が空/403 | 手順 6-3 の UID 紐付け・権限付与をしたか（`owner` 行）|
| API は動くが画面から呼べない | `CORS_ORIGINS` と `NUXT_PUBLIC_API_BASE` が正しいか / ブラウザ DevTools の Console/Network |
| DB 初期化が「既に存在」で中止 | 既存 DB 保護のため正常。スキーマ更新は `action=migrate` を使用 |
| backend がすぐ落ちる | Actions ログの health 失敗箇所 / EC2 で `docker logs akebono-honshu-api` |

---

## Appendix: self-hosted runner（SSH を開けたくない場合・任意）

EC2 上に GitHub Actions の self-hosted runner を置くと、SSH(22) を開けずに「EC2 自身が」デプロイを実行できます（より安全）。導入する場合は別途ご案内します（`runs-on: self-hosted` への切替が必要なため、希望時に対応します）。

> self-hosted runner 採用時は、GitHub ホストランナーと違いホストが永続するため、デプロイ後に
> `~/.ssh/deploy_key` 等の一時鍵・機密が残らないようクリーンアップ（`if: always()` での削除）を併せて入れます。
