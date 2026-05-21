# Iteration 4: 本番移行計画 (AWS / Firebase 段階的アプローチ)

> **作成日:** 2026-05-20
> **対象:** Iteration 4 のうち「本番認証切替」「AWS インフラ構築」「CI/CD パイプライン」3 領域の段階的実施計画
> **前提:** Iteration 1〜3 + MIG-3 までの機能実装は完了。本書は実装ではなく **本番環境への切替手順** を扱う。
> **方針:** インフラ初級者向けに段階を細分化し、各段階で「オペレーター作業 (AWS / Firebase コンソール操作)」と「Claude 作業 (コード変更)」を明確に分離する。

---

## 0. 段階分割の全体像

| 段階 | 目的 | 期間目安 | 完了基準 | 状態 |
|---|---|---|---|---|
| **段階 A** | ローカル Backend を既存 / 新規 AWS RDS に接続 | 1-2 日 | ローカル `dotnet run` で AWS RDS のデータを読み書きできる | ✅ 完了 2026-05-20 |
| **段階 B** | ダミー認証を Firebase Auth に置換 | 3-5 日 | ローカル環境で Firebase ID Token を使ってログインできる | ✅ 完了 2026-05-20 |
| **段階 C** | Backend / Frontend を AWS / Firebase Hosting にデプロイ | 5-7 日 | ブラウザから本番 URL にアクセスし、ログイン → 商品一覧表示まで疎通 | 未着手 |
| **段階 D** | GitHub Actions で自動デプロイ | 2-3 日 | main push で本番反映 | 未着手 |

各段階は **前段が完了してから次に進む** こと。前段が動かないまま次に進むと切り分けが困難になる。

```mermaid
flowchart LR
    L[ローカル<br/>(現状)] --> A[段階 A<br/>RDS 接続]
    A --> B[段階 B<br/>Firebase Auth]
    B --> C[段階 C<br/>本番デプロイ]
    C --> D[段階 D<br/>CI/CD]
    D --> P[本番運用]

    style L fill:#e0e0e0
    style A fill:#c3e6cb
    style B fill:#c3e6cb
    style B fill:#fff3cd
    style C fill:#fff3cd
    style D fill:#fff3cd
    style P fill:#27ae60,color:#fff
```

---

## 1. 既存 AWS リソースの活用判定 (段階 A 着手前ヒアリング)

オペレーターから以下の情報を確認し、再利用 / 新規作成を決定する。**全項目に `__TODO__` あり**、ヒアリング後 Claude が判定表を埋める。

### 1.1 既存 RDS

| 項目 | 値 | 判定 |
|---|---|---|
| エンジン / バージョン | **PostgreSQL 14.17** (2026-05-20 確認) | Phase 4 確定は 16 だが、本 MVP スキーマは PostgreSQL 14 互換 (使用機能: `GENERATED ALWAYS AS ... STORED` / Partial Index は 12+ で利用可) のため **段階 A〜C は 14.17 で進行**。本番運用前に 16 へアップグレード推奨 |
| リージョン | **ap-northeast-1d** ✅ | Phase 4 確定 `ap-northeast-1` と一致 |
| インスタンスクラス | **db.t4g.medium** | サイズ問題なし |
| Multi-AZ | (要確認、ステータス画面で「ロール: インスタンス」だったため Single-AZ 可能性高い) | 本番運用前に Multi-AZ 化推奨 |
| パブリックアクセス | (要確認、開発 PC からの 5432 接続を許可) | 段階 A では一時許可、段階 C で VPC 内部接続のみに変更 |
| 現在の利用状況 | **他システム使用中** (2026-05-20 オペレーター回答) | **新規 DB `akebono_honshu` を作成して論理分離** (PostgreSQL 通常識別子のためクォート不要、他システムとの命名衝突回避) |
| エンドポイント | `akebono1.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com` | – |
| マスターユーザ | `pguser` (パスワードはオペレーターローカル保管) | – |
| **判定** | – | **再利用 (akebono1 インスタンス内に akebono_honshu DB を新規作成)** |

### 1.2 既存 S3

| 項目 | 値 | 判定 |
|---|---|---|
| バケット数 | `__TODO__` | 用途別の新規バケット作成が原則 (商品画像 / 監査アーカイブ で 2 つ)。既存バケットの prefix で論理分離も可 |
| リージョン | `__TODO__` | `ap-northeast-1` でなければ新規 |
| **判定** | – | **新規作成 (推奨)** / 既存バケット内 prefix |

### 1.3 既存 EC2

| 項目 | 値 | 判定 |
|---|---|---|
| 用途 | `__TODO__` (他システム / 開発用 / 空) | 本システムは App Runner (フルマネージド) で構築するため、原則 **既存 EC2 は再利用しない**。ただし「踏み台 (bastion)」「RDS への一時接続」用途に流用可 |
| **判定** | – | **再利用しない** (App Runner で新規) |

### 1.4 Firebase project

| 項目 | 値 | 判定 |
|---|---|---|
| 既存 project | `__TODO__` (あり / なし) | なしなら新規作成 |
| 認証 Provider | – | Email/Password を有効化 (Phase 4 確定) |

---

## 2. 段階 A: ローカル Backend → AWS RDS 接続 (1-2 日)

### 2.1 ゴール

ローカル PC の `dotnet run` から、AWS RDS PostgreSQL に接続して読み書きできる。Frontend はローカル / DB は AWS、という構成。

### 2.2 作業分担

| 主体 | 作業 | 詳細 |
|---|---|---|
| **オペレーター** | RDS 準備 | 上記 §1.1 判定に基づき、RDS を再利用 or 新規作成。PostgreSQL 16.x、ap-northeast-1、Multi-AZ (PoC は Single-AZ でも可) |
| **オペレーター** | DB ユーザ作成 | RDS マスターユーザで psql 接続し、`akebono_app` ユーザ作成 (パスワードは自動生成 1Password 等で管理) + `akebono_honshu` データベース作成 + 権限付与 |
| **オペレーター** | セキュリティグループ | 開発用 IP からの 5432 接続を一時許可 (本番では VPC 内部接続のみに変更) |
| **オペレーター** | 接続情報を Claude に共有 | エンドポイント / DB名 / ユーザ / パスワード (パスワードは個別の安全な経路で) |
| **Claude** | スキーマ初期化スクリプト動作確認 | `db/init/01-schema.sql` 〜 `04-orders.sql` を AWS RDS に適用 (psql で `\i` 実行)。docker-compose の自動投入は AWS RDS では効かない |
| **Claude** | `dotnet user-secrets` 設定手順を RUNBOOK に追記 | `ConnectionStrings:Postgres` を `Host=<rds-endpoint>;...` に切替えるコマンドを記載 |
| **オペレーター** | 動作確認 | `dotnet run --project Presentation` 起動 → Frontend からログイン → 商品一覧表示 |

### 2.3 完了基準

- [x] ローカル Frontend からログイン成功 (2026-05-20)
- [x] ユーザ一覧画面に AWS RDS から取得した行が表示 (2026-05-20)
- [x] AWS RDS console の `users` テーブルでログインユーザの行が確認できる (2026-05-20)

> **段階 A 完了: 2026-05-20** (Iter 4 着手から 1 日で疎通完了。ローカル Backend + AWS RDS PostgreSQL 14.17 構成で MVP スキーマが正常動作)

### 2.4 ロールバック手順

- `dotnet user-secrets set "ConnectionStrings:Postgres" "<元のローカル PostgreSQL>"` で接続先を戻す
- AWS RDS のデータは保持 (削除しない)

### 2.5 ヒアリング結果 (2026-05-20 確定)

- [x] §1.1 RDS 判定: **既存 `akebono1` インスタンス内に `akebono_honshu` DB を新規作成して論理分離** (アンダースコア命名、PostgreSQL 通常識別子)
- [x] RDS エンドポイント: `akebono1.ct60hj9szuti.ap-northeast-1.rds.amazonaws.com:5432`、ユーザ `pguser` (パスワードはオペレーターローカル保管、`dotnet user-secrets` で Claude に共有せず)
- [x] 開発 PC からの 5432 接続: **許可済 (2026-05-20 オペレーター確認)**

### 2.6 オペレーター実行コマンド (RUNBOOK §1.3 選択肢 C)

詳細手順は `RUNBOOK.md` §1.3 選択肢 C に集約。一本道で記載されているため、コピー&ペーストで実行可能。`<rds-endpoint>` / `<your-password>` は手元の値に置換。

---

## 3. 段階 B: ダミー認証 → Firebase Auth 切替 (3-5 日) ✅ 完了 2026-05-20

> **完了サマリ (2026-05-20):**
> - Firebase project `akebono-honshu` (dev 兼用) を作成、Email/Password 認証有効化、テストユーザ `owner@akebono.example` 作成済
> - Backend: `ITokenService` / `DummyTokenService` を完全削除、`Microsoft.AspNetCore.Authentication.JwtBearer 8.0.27` + JWKS 検証に置換、`POST /api/v1/auth/sync` 新設 (commit `1197acb`)
> - Frontend: `firebase@^12.13.0` 導入、`plugins/firebase.client.ts` + `useAuth.ts` を `signInWithEmailAndPassword` ベースに刷新 (commit `1197acb`)
> - 追加変更: timestamp カラムを TIMESTAMP (without time zone) で JST naive 保存に統一、`SystemTime.Now` ヘルパー導入 (commit `b7706bc`、オペレーター要望に基づく)
> - Service Account 鍵は段階 C (シナリオ E `setCustomUserClaims` / Reconciler バッチ) で使用予定、現状は未使用

### 3.1 ゴール

ローカル環境で、Frontend が Firebase Authentication でサインインし、取得した ID Token を Backend に送る。Backend は Firebase JWKS で検証する。RDS には引き続き AWS RDS を使う。

### 3.2 作業分担

| 主体 | 作業 | 詳細 |
|---|---|---|
| **オペレーター** | Firebase project 作成 | Firebase console から新規 project 作成 (project ID 例: `akebono-honshu-dev`)。本番用は別 project (例: `akebono-honshu-prod`) を別途作成 |
| **オペレーター** | Authentication 有効化 | Authentication → Sign-in method → Email/Password を有効化 |
| **オペレーター** | テストユーザ作成 | Authentication → Users → ユーザ追加 (例: `owner@akebono.example` + 任意パスワード) |
| **オペレーター** | Web app 登録 | Project settings → Web app 追加 → Firebase config (`apiKey` / `authDomain` / `projectId` 等) を取得 |
| **オペレーター** | Service Account 鍵作成 | Project settings → Service accounts → 新しい秘密鍵の生成 (JSON ダウンロード)。**Git にコミット禁止**、ローカルでは User Secrets / 本番は Secrets Manager で管理 |
| **オペレーター** | 上記情報を Claude に共有 | Web app config (公開情報なので OK)、Service Account 鍵 (秘密、個別経路) |
| **Claude** | Backend: `FirebaseAuthService` 実装 | `Infrastructure/Auth/FirebaseAuthService.cs` 作成、`FirebaseAdmin` NuGet パッケージ追加、`DummyTokenService` → `FirebaseAuthService` 切替 (DI 登録) |
| **Claude** | Backend: JwtBearer 設定 | `Program.cs` で Firebase JWKS による ID Token 検証ミドルウェア追加。`https://securetoken.google.com/<project-id>` を Issuer に |
| **Claude** | Backend: Custom Claims 同期 | architecture.md §4.5 シナリオ E (権限変更時 RDS 先行 → Firebase Custom Claims 後追い) を実装 |
| **Claude** | Backend: `/auth/sync` `/auth/me` endpoint | Firebase UID から RDS users を引当、未登録時は 403 USR-001 |
| **Claude** | Frontend: Firebase JS SDK 統合 | `plugins/firebase.client.ts` で Firebase init、`composables/useAuth.ts` を `signInWithEmailAndPassword` / `onAuthStateChanged` ベースに書き換え |
| **Claude** | Frontend: 既存 `localStorage` 認証の廃止 | `useAuth` 内のトークン保持を Firebase SDK 任せに |
| **Claude** | Frontend: middleware 修正 | Iter 1 知見 #3 (SSR skip + hydration mismatch 防止) を維持しつつ Firebase 化 |
| **オペレーター** | RDS users への Firebase UID 紐付け | テストユーザの Firebase UID を取得し、`UPDATE users SET firebase_uid='<uid>' WHERE login_id='owner';` を psql で実行 |
| **オペレーター** | 動作確認 | ローカルでログイン → ID Token が Bearer で送信される → Backend が検証 → ユーザ一覧表示 |

### 3.3 完了基準

- Email/Password で Firebase 経由ログイン成功
- Backend が Firebase ID Token を検証し、RDS users から業務情報を取得して返却
- 既存マスタ管理 / 商品 / 発注 すべての画面でログインユーザの権限チェックが効く
- `audit_logs` テーブルにログインイベントが記録される

### 3.4 ロールバック手順

- `DI` 登録を `DummyTokenService` に戻す (Backend 1 行変更で済むよう抽象化済)
- Frontend は git revert で `useAuth.ts` を戻す

### 3.5 ヒアリング `__TODO__` リスト

- [x] Firebase project ID (dev / prod 2 つ) → dev 兼用 `akebono-honshu` を採用、prod は段階 C で別途作成
- [x] Web app config の `apiKey` / `authDomain` / `projectId` → `nuxt.config.ts:runtimeConfig.public.firebase` に既定値として埋込み、本番は `NUXT_PUBLIC_FIREBASE_*` で上書き
- [x] Service Account 鍵 (JSON、秘密) → オペレーターがリポジトリ外に保管。段階 C 以降で使用
- [x] テストユーザの Email / 仮パスワード / 想定権限 → `owner@akebono.example` (owner 行 = 全 4 権限) を Firebase UID 紐付け済

---

## 4. 段階 C: 本番デプロイ (App Runner + Firebase Hosting + S3 + Secrets Manager) (5-7 日)

### 4.1 ゴール

ブラウザから本番 URL にアクセスでき、Firebase Auth でログイン → 商品一覧表示まで疎通する。Backend は App Runner、Frontend は Firebase Hosting、画像は S3。

### 4.2 作業分担 (4.2.1 〜 4.2.5 の順で実施)

#### 4.2.1 S3 + 画像ストレージ抽象化

| 主体 | 作業 |
|---|---|
| **オペレーター** | S3 バケット作成 (例: `akebono-honshu-images-prod`、SSE-S3、パブリックアクセスブロック有効) |
| **オペレーター** | S3 バケット作成 (例: `akebono-honshu-audit-archive-prod`、Object Lock 有効、Glacier IR ライフサイクル) |
| **Claude** | `IImageStorageService` 抽象作成 + `LocalImageStorage` (現状の `wwwroot/uploads`) + `S3ImageStorage` (Pre-signed URL 配信) 2 実装 |
| **Claude** | DI 登録を環境変数で切替 (`ImageStorage:Provider` = `Local` / `S3`) |
| **オペレーター** | IAM Role 作成 (App Runner 用、S3 read/write 最小権限) |

#### 4.2.2 Secrets Manager

| 主体 | 作業 |
|---|---|
| **オペレーター** | KMS CMK 作成 (例: `akebono-honshu-prod-cmk`) |
| **オペレーター** | Secrets Manager に投入 | `akebono/prod/db-connection` (DB 接続文字列)、`akebono/prod/firebase-sa-key` (Firebase Service Account 鍵 JSON) |
| **Claude** | `AwsSecretsManagerProvider` 実装 (AWS SDK で取得、Backend 起動時に環境変数注入) |

#### 4.2.2bis Prod Firebase project 切替 (dev/prod 取り違え防止、段階 B レビュー指摘 SA P0-1)

> **背景:** 段階 B では dev 用 `akebono-honshu` project を Web/Backend で共有していた。本番では別 project に分離して、dev のテストユーザが本番 RDS に到達できる事故を防ぐ。Backend は `appsettings.json` で `Firebase:ProjectId` を `__OVERRIDE_ME__` のままにし、起動時 throw でフェイルファストする設計に変更済。

| 主体 | 作業 |
|---|---|
| **オペレーター** | Firebase Console で本番 project 新規作成 (例: `akebono-honshu-prod`)、Authentication / Email/Password 有効化、テストユーザ作成 |
| **オペレーター** | Web app 登録 → 本番用 `firebaseConfig` を取得 (apiKey / authDomain / projectId など) |
| **オペレーター** | 本番 Service Account 鍵を生成して Secrets Manager (`akebono/prod/firebase-sa-key`) に投入 |
| **オペレーター** | App Runner 環境変数 `Firebase__ProjectId=akebono-honshu-prod` を設定 (`__OVERRIDE_ME__` から上書き) |
| **オペレーター** | Firebase Hosting / Github Actions の build 環境で `NUXT_PUBLIC_FIREBASE_*` 環境変数を本番値に設定 (.env はリポジトリ外なので CI/CD 側で注入) |
| **オペレーター** | 本番 RDS の `users.firebase_uid` を **本番** project の UID で再紐付け (`UPDATE users SET firebase_uid='<prod-uid>' WHERE login_id='owner';`)。dev で発行された UID は別 project のため本番では認証されなくなる |
| **オペレーター** | UID 再紐付け直後は App Runner の `IMemoryCache` flush のため、全インスタンスを再起動 (or 1 instance に絞って起動) する。さもないと最大 60s は dev UID キャッシュが残る。**cold start ~30-60s のダウンタイム発生** だが切替直後は新規ログイン無し前提のため許容範囲。継続トラフィックがある状況での切替は App Runner の rolling deployment (新リビジョン作成 → 自動切替) を利用すれば最小ダウンタイム |
| **オペレーター** | Firebase Console → Authentication → Settings → **Authorized domains** に本番 Frontend ドメイン (`*.web.app` / 独自ドメイン) を追加、dev domain を除外 |
| **Claude** | デプロイ前検証: 起動ログで `Firebase:ProjectId` が `akebono-honshu-prod` であることを確認 (もし dev のまま起動したら Program.cs の throw で落ちる設計) |

#### 4.2.3 App Runner (Backend)

| 主体 | 作業 |
|---|---|
| **Claude** | `Dockerfile` 作成 (Backend、multi-stage build) |
| **オペレーター** | ECR リポジトリ作成 (例: `akebono-honshu-backend`) |
| **オペレーター** | ローカルから初回 `docker build` + `docker push <ecr-uri>` (Claude が手順書化) |
| **オペレーター** | App Runner サービス作成 (ECR 連携、1 vCPU / 2GB、min=1 max=2) |
| **オペレーター** | App Runner 環境変数 / Secrets 連携 |
| **オペレーター** | VPC コネクタ作成 (App Runner → RDS / Secrets Manager) |
| **オペレーター** | RDS セキュリティグループに App Runner VPC コネクタからの接続を許可 |

#### 4.2.4 Firebase Hosting (Frontend)

| 主体 | 作業 |
|---|---|
| **Claude** | `nuxt.config.ts` 修正 (`ssr: false` 廃止して `<ClientOnly>` ラップ済、`nuxi generate` で静的サイト生成設定) |
| **Claude** | `firebase.json` + `.firebaserc` 作成 (Hosting 設定、SPA リダイレクト rewrite) |
| **オペレーター** | `firebase login` + `firebase init hosting` 確認 (project 紐付け) |
| **オペレーター** | 初回 `firebase deploy --only hosting` (Claude が手順書化) |
| **オペレーター** | CORS 設定 | App Runner Backend に Firebase Hosting のドメイン (`*.web.app` 等) を Allow Origin に追加 |

#### 4.2.5 動作確認

| 主体 | 作業 |
|---|---|
| **オペレーター** | 本番 URL アクセス → ログイン → 商品一覧表示 |
| **オペレーター** | 画像アップロード動作確認 (S3 Pre-signed URL 経由) |
| **オペレーター** | 発注書 Excel ダウンロード動作確認 |

### 4.3 完了基準

- 本番 Firebase Hosting URL でログイン → 商品一覧 → 詳細 → 発注書作成 → Excel ダウンロード まで疎通
- CloudWatch Logs に Backend ログが流れる
- audit_logs に各操作が記録される

### 4.4 ロールバック手順

- App Runner サービスを停止 (旧バージョンの ECR image にロールバック)
- Firebase Hosting は前のリリースに rollback (`firebase hosting:rollback`)
- データは保持 (削除しない)

### 4.5 ヒアリング `__TODO__` リスト

- [ ] AWS アカウント ID (ECR / App Runner / IAM のリソース ARN に必要)
- [ ] VPC / Subnet / Security Group ID (既存 VPC を使うか新規かの判断)
- [ ] 独自ドメインを使うか (`akebono.example.jp` 等)、Firebase デフォルトドメイン (`*.web.app`) で十分か
- [ ] HTTPS 証明書の調達経路 (独自ドメインの場合)

---

## 5. 段階 D: CI/CD (GitHub Actions) (2-3 日)

### 5.1 ゴール

`main` ブランチへの push で自動的に Backend (App Runner) と Frontend (Firebase Hosting) が更新される。

### 5.2 作業分担

| 主体 | 作業 |
|---|---|
| **オペレーター** | IAM OIDC プロバイダ作成 (GitHub Actions → AWS の信頼関係) |
| **オペレーター** | IAM Role 作成 (GitHub Actions 用、ECR push + App Runner deploy 最小権限) |
| **オペレーター** | Firebase CI トークン or Service Account 鍵を GitHub Secrets に登録 |
| **オペレーター** | GitHub repository Secrets に `AWS_ACCOUNT_ID` / `AWS_OIDC_ROLE_ARN` 等を登録 |
| **Claude** | `.github/workflows/deploy-backend.yml` 作成 | dotnet test → docker build → ECR push → App Runner update |
| **Claude** | `.github/workflows/deploy-frontend.yml` 作成 | pnpm typecheck → nuxi generate → firebase deploy |
| **Claude** | 既存 `.github/workflows/pr-checks.yml` 拡張 | Backend `dotnet test`、Frontend `pnpm typecheck` を追加 |
| **オペレーター** | 動作確認 (main に空 commit push → 自動デプロイ確認) |

### 5.3 完了基準

- main push で本番が自動更新される
- PR 作成で lint / test / typecheck が自動実行される
- デプロイ失敗時に通知 (Slack or GitHub email)

### 5.4 ロールバック手順

- workflow ファイルを git revert
- 失敗時の自動ロールバック: App Runner は前バージョンの ECR image を維持しているため、コンソールから切替可能

### 5.5 ヒアリング `__TODO__` リスト

- [ ] デプロイ失敗時の通知先 (Slack channel / メールアドレス)
- [ ] ブランチ運用ルール (main 直 push 可 / PR 必須 / 承認者要)

---

## 5.6 退職処理オペレーション手順 (SEC-12 SoT 単一ポイント運用、5 周目レビュー反映)

`architecture.md §5.1` の設計判断「Firebase Auth `disabled=true` 同期を SoT 防御の単一ポイント」を運用面で担保するため、退職/異動でユーザを無効化する手順は **必ず以下の順序** で実施する。`P-12 admin UI` 実装後は本手順を UI で自動化するが、それまでは admin オペレータが手動で行う。

### 5.6.1 通常退職 (再雇用無し)

1. **Firebase Console** で当該ユーザの認証を無効化:
   - `Authentication → Users → 該当 UID → 三点メニュー → "Disable account"`
   - これにより Firebase ID Token 発行段階で拒否されるため、Backend には到達しなくなる
2. **RDS users テーブル** で論理削除フラグを立てる:
   ```sql
   UPDATE users SET is_deleted = true, is_active = false, updated_at = NOW()
   WHERE login_id = '<対象ユーザ>';
   ```
3. **App Runner Backend** (本番では multi-instance) を rolling restart して `IMemoryCache` を flush:
   - `aws apprunner start-deployment --service-arn <ARN>` で新リビジョンを配備
   - これにより `fb_uid_resolve:{uid}` と `audit_logged:{uid}` が即時 flush され、60s 反映遅延を回避

### 5.6.2 一時無効化 (休職等、再有効化前提)

1. **Firebase Console** で disabled=true (5.6.1 と同手順)
2. **RDS users テーブル**: `is_active=false` のみ (is_deleted は触らない):
   ```sql
   UPDATE users SET is_active = false, updated_at = NOW()
   WHERE login_id = '<対象ユーザ>';
   ```
3. **App Runner 再起動** (5.6.1 と同手順)
4. 復職時は逆順 (`is_active=true` → Firebase disabled=false → Backend 再起動)

### 5.6.3 順序の根拠

- **Firebase → RDS の順**: 逆順だと「RDS は inactive だが Firebase は通る → Backend 到達 → `Auth.LoginRejected.Inactive` が actor_user_id 付きで大量記録される」状態を経由する (cache TTL 60s 内に複数試行で 5 分 de-dup の効き始めまで)。Firebase を先に止めれば Backend に到達しない
- **Backend 再起動を最後**: cache flush は副次的、SoT (Firebase + RDS) が確定してから cache を整理する順序

### 5.6.4 監査ログ確認

退職処理後、想定外のアクセス試行が無いことを確認:
```sql
SELECT occurred_at, actor_user_id, action, note
FROM audit_logs
WHERE action IN ('Auth.LoginRejected.Inactive', 'Auth.UidUnboundProbe')
  AND occurred_at > '<処理日時>'
ORDER BY occurred_at DESC LIMIT 20;
```
- `Auth.UidUnboundProbe` が 0 件 = Firebase 側で確実に拒否されている
- 件数が出ている場合は手順 1 (Firebase disabled) が反映されていない可能性、Firebase Console を再確認

---

## 6. 段階横断: 障害時の切り分け手順

| 症状 | 確認ポイント | 対処 |
|---|---|---|
| Frontend が真っ白 | ブラウザ DevTools Console / Network タブ | Firebase config 不一致、CORS エラー、Backend 503 |
| ログイン失敗 | Backend ログ (CloudWatch) / Firebase Authentication console | JWKS 検証エラー、users テーブル未登録 |
| 商品一覧空 | Backend → RDS 接続 | Security Group、VPC コネクタ、`audit_logs` の権限エラー |
| 画像 404 | S3 Pre-signed URL の TTL | TTL 切れなら再取得、Bucket Policy で再生成 |
| Excel ダウンロード失敗 | CORS `Content-Disposition` expose (Iter 3 知見 #3) | App Runner の CORS 設定確認 |

---

## 7. Iteration 4 ゲート条件との対応

`iteration-plan.md` §3 Iter 4 ゲート (方法論 §Phase 7 完了ゲート 3 件) を本書の段階と対応付ける:

| ゲート | 対応段階 | 検証手段 |
|---|---|---|
| 機能完成 (21 機能) | Iter 1〜3 で完了済 + 段階 C で本番動作確認 | UC-1〜UC-4 通しシナリオ実行 |
| コードレビュアー 7 視点 | 各段階の Claude 実装後、独立サブエージェントで再レビュー (CLAUDE.md 原則 9) | code-reviewer サブエージェント |
| システム監査官リリース OK | 段階 C 完了後にセキュリティ最終確認 (IAM 最小権限 / audit_logs 不変化 / KMS 暗号化) | system-auditor サブエージェント |
| オペレーターサインオフ | UAT 完了 + 業務担当者の Excel 印刷検収 | – |

---

## 8. 関連ドキュメント

- 全体計画: `iteration-plan.md` §3 Iteration 4
- Phase 5 アーキテクチャ: `.ai-native/outputs/phase5/architecture.md` (特に §1.1 デプロイメント構成 / §4.5 シナリオ E)
- Phase 5 API 設計: `.ai-native/outputs/phase5/api-design.md` (特に §2.1 認証)
- 既存 RUNBOOK: `/RUNBOOK.md` (ローカル開発手順)
- 既存 CLAUDE.md: 原則 6 (データフロー整合性 SoT 順序)、原則 7 (下位互換性)、原則 9 (反復レビュー)

---

## 9. 次のアクション

本書を読み終えたら、まず **§1 ヒアリング項目** をオペレーターに確認する。判定が確定したら **段階 A** から着手する。

Claude は本書を SoT として、各段階のコード変更時にこの計画と齟齬がないか自問する。計画変更が必要な場合は本書を更新してから実装に入る。
