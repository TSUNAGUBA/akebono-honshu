# Iteration 4: 本番移行計画 (AWS / Firebase 段階的アプローチ)

> **作成日:** 2026-05-20
> **対象:** Iteration 4 のうち「本番認証切替」「AWS インフラ構築」「CI/CD パイプライン」3 領域の段階的実施計画
> **前提:** Iteration 1〜3 + MIG-3 までの機能実装は完了。本書は実装ではなく **本番環境への切替手順** を扱う。
> **方針:** インフラ初級者向けに段階を細分化し、各段階で「オペレーター作業 (AWS / Firebase コンソール操作)」と「Claude 作業 (コード変更)」を明確に分離する。

---

## 0. 段階分割の全体像

| 段階 | 目的 | 期間目安 | 完了基準 |
|---|---|---|---|
| **段階 A** | ローカル Backend を既存 / 新規 AWS RDS に接続 | 1-2 日 | ローカル `dotnet run` で AWS RDS のデータを読み書きできる |
| **段階 B** | ダミー認証を Firebase Auth に置換 | 3-5 日 | ローカル環境で Firebase ID Token を使ってログインできる |
| **段階 C** | Backend / Frontend を AWS / Firebase Hosting にデプロイ | 5-7 日 | ブラウザから本番 URL にアクセスし、ログイン → 商品一覧表示まで疎通 |
| **段階 D** | GitHub Actions で自動デプロイ | 2-3 日 | main push で本番反映 |

各段階は **前段が完了してから次に進む** こと。前段が動かないまま次に進むと切り分けが困難になる。

```mermaid
flowchart LR
    L[ローカル<br/>(現状)] --> A[段階 A<br/>RDS 接続]
    A --> B[段階 B<br/>Firebase Auth]
    B --> C[段階 C<br/>本番デプロイ]
    C --> D[段階 D<br/>CI/CD]
    D --> P[本番運用]

    style L fill:#e0e0e0
    style A fill:#fff3cd
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
| エンジン / バージョン | `__TODO__` (例: PostgreSQL 16.2 / MySQL 8.0 等) | PostgreSQL 16.x ならば再利用候補、他は新規必須 |
| リージョン | `__TODO__` (例: ap-northeast-1) | architecture.md §1.1 で `ap-northeast-1` 確定、不一致なら新規 |
| インスタンスクラス | `__TODO__` (例: db.t4g.small / db.t3.medium) | サイズ問わず PoC では OK。本番運用は db.t4g.small 以上推奨 |
| Multi-AZ | `__TODO__` (Yes/No) | 本番は Yes 必須。No なら段階 C で切替 |
| パブリックアクセス | `__TODO__` (Yes/No) | 本来は No (VPC 内部接続)。段階 A で開発接続が必要な場合は IP 制限で一時許可 |
| 現在の利用状況 | `__TODO__` (他システム稼働中 / 空) | 稼働中なら **別データベース新規作成で論理分離**、空なら再利用可 |
| **判定** | – | **再利用 / 新規作成** (オペレーター + Claude 協議で確定) |

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
| **オペレーター** | DB ユーザ作成 | RDS マスターユーザで psql 接続し、`akebono_app` ユーザ作成 (パスワードは自動生成 1Password 等で管理) + `akebono-honshu` データベース作成 + 権限付与 |
| **オペレーター** | セキュリティグループ | 開発用 IP からの 5432 接続を一時許可 (本番では VPC 内部接続のみに変更) |
| **オペレーター** | 接続情報を Claude に共有 | エンドポイント / DB名 / ユーザ / パスワード (パスワードは個別の安全な経路で) |
| **Claude** | スキーマ初期化スクリプト動作確認 | `db/init/01-schema.sql` 〜 `04-orders.sql` を AWS RDS に適用 (psql で `\i` 実行)。docker-compose の自動投入は AWS RDS では効かない |
| **Claude** | `dotnet user-secrets` 設定手順を RUNBOOK に追記 | `ConnectionStrings:Postgres` を `Host=<rds-endpoint>;...` に切替えるコマンドを記載 |
| **オペレーター** | 動作確認 | `dotnet run --project Presentation` 起動 → Frontend からログイン → 商品一覧表示 |

### 2.3 完了基準

- ローカル Frontend からログイン成功
- 商品一覧画面に MIG-3 取込済データ (689 件) または Seed データが表示
- AWS RDS console の `users` テーブルでログインユーザの行が確認できる

### 2.4 ロールバック手順

- `dotnet user-secrets set "ConnectionStrings:Postgres" "<元のローカル PostgreSQL>"` で接続先を戻す
- AWS RDS のデータは保持 (削除しない)

### 2.5 ヒアリング `__TODO__` リスト

- [ ] §1.1 RDS の判定 (再利用 / 新規)
- [ ] RDS エンドポイント / ポート / DB 名 / ユーザ名 (パスワードは別経路)
- [ ] AWS RDS のセキュリティグループに開発 PC の IP を許可できるか

---

## 3. 段階 B: ダミー認証 → Firebase Auth 切替 (3-5 日)

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

- [ ] Firebase project ID (dev / prod 2 つ)
- [ ] Web app config の `apiKey` / `authDomain` / `projectId`
- [ ] Service Account 鍵 (JSON、秘密)
- [ ] テストユーザの Email / 仮パスワード / 想定権限 (`product_ledger_permission` 等の値)

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
