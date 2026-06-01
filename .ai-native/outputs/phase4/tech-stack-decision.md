# Phase 4 成果物: 技術スタック確定

> **作成日:** 2026-05-19
> **最終更新:** 2026-05-19（オペレーターレビュー10件すべてクローズ、確定版）
> **状態:** 確定（Phase 4 ゲート通過）
> **依存:** Phase 2 確定9項目、Phase 3 機能要件（21機能）+ 非機能要件（性能・セキュリティ・可用性・データ機密度）
> **方針:** 要件と確定前提（インフラ=AWS Tokyo／既存スキル=Vue+Nuxt/.NET+C#／体制=1-2名／移行=並行稼働）から、各レイヤーの選定をオーバースペック/アンダースペックにならない最小構成で行う。

---

## 1. 確定前提（オペレーターレビューで決定）

| # | 前提 | 値 | 影響 |
|---|------|----|----|
| AS-1 | インフラ大方針 | **国内パブリッククラウド** | リージョン=日本国内、マネージド優先、スケールアウト不要 |
| AS-2 | 既存運用スキル | **Vue/Nuxt 系 + .NET/C# 系** | フロント=Nuxt、バック=.NET を採用 |
| AS-3 | 開発・運用体制 | **社内エンジニア 1-2名（小規模）** | マネージドサービス重視、運用負荷最小化、可観測性は AWS 標準で済ませる |
| AS-4 | 旧3システム移行戦略 | **並行稼働** | 二重入力期間を許容。並行期間中は新システムを SoT とし、旧への還流は手動（#6 A 採用）|

---

## 2. アーキテクチャ全体図

```
[業務 LAN PC: Chrome/Edge]
            │ HTTPS
            ├──────────────────────────────┐
            ▼                              ▼
   [Firebase Hosting (CDN)]      [Firebase Authentication]
   └─→ Nuxt 3 SPA                 └─→ ID Token (JWT, 1h)
       (nuxt generate)                + Custom Claims (role, permissions[])
            │
            │ XHR (Bearer: Firebase ID Token)
            ▼
   [AWS App Runner / VPC コネクタ経由]
   └─→ [ASP.NET Core 8 Web API (C#)]
         ├─ FirebaseAuth.JwtBearer Middleware（ID Token 検証）
         ├─ Firebase Admin SDK for .NET（ユーザ管理・Custom Claims 操作）
         ├─ EF Core 8 + Npgsql ─→ [RDS for PostgreSQL 16 Multi-AZ]
         ├─ AWS SDK ─→ [S3 (画像 5GB / 監査ログ 3年 Glacier IR)]
         └─ Serilog → CloudWatch Logs

横断:
  - Firebase Auth: ユーザ ID/Email/Password 管理（SoT）、Custom Claims で権限
  - RDS users テーブル: 業務情報・権限ロール（SoT）、Firebase UID で紐付け
  - AWS Secrets Manager + KMS: 接続文字列・Firebase サービスアカウント鍵・暗号化鍵
  - CloudWatch Metrics / Logs / Alarms + X-Ray: 可観測性
  - SNS: アラート通知（メール/Slack）
  - GitHub Actions: CI/CD（AWS OIDC で App Runner、Firebase CLI で Hosting デプロイ）
```

**リージョン構成:**
- **AWS:** Tokyo（`ap-northeast-1`）、RDS Multi-AZ で `ap-northeast-1a` / `ap-northeast-1c` を利用
- **Firebase Hosting:** グローバル CDN（静的アセットのみ、業務データを含まない）
- **Firebase Authentication:** Google グローバルインフラ（UID / Email / 認証メタデータが海外含むグローバル配置になる）→ NFR §4.2 との部分矛盾は **オペレーター判断で許容**（後述 §4 留意点 / §8 R-13）

---

## 3. レイヤー別技術選定

### 3.1 フロントエンド

| 項目 | 選定 | 理由 |
|------|------|------|
| **フレームワーク** | **Nuxt 3（Vue 3 + TypeScript）** | AS-2 既存スキル整合。Composition API + `<script setup>` で型安全、内製エコシステム成熟 |
| **レンダリング方式** | **SPA モード（`ssr: false`）+ `nuxt generate` で静的ビルド** | 業務 LAN 内 PC 利用・SEO 不要・1-2名同時利用のため SSR の利点なし。SSR 起因の Hydration バグも回避（CLAUDE.md Nuxt 注意点）|
| **配信** | **Firebase Hosting**（一本化） | グローバル CDN + 無料 SSL + カスタムドメイン + プレビューチャネル（PR ごとに URL 自動払い出し）が標準。`firebase.json` で Rewrite/Headers/Cache を一元管理、デプロイは `firebase deploy --only hosting` 1コマンド。配信対象は静的アセットのみ（業務データを含まないため NFR §4.2 と矛盾しない）|
| **UI ライブラリ** | **TailwindCSS + Reka UI（Headless UI Vue）+ lucide-icons** | TailwindCSS でスタイリング、Reka UI で ARIA/キーボード操作/フォーカストラップ等のアクセシビリティ基本機能、lucide-icons でアイコン。重量級ライブラリ依存を避けつつ実装コストを抑える |
| **状態管理** | **Pinia** | Vue3 標準。`useState` 直接利用と併用しつつ、グローバル状態は Pinia ストアに集約 |
| **HTTP クライアント** | **$fetch（Nuxt 標準）** | 余計な依存追加せず、認証ヘッダ・エラー共通化を composable 化 |
| **ビルド/開発** | **Vite**（Nuxt 同梱） | HMR 高速、設定最小 |
| **多言語化** | MVP 対象外（日本語のみ） | Phase 2 確定（国内のみ）|

### 3.2 バックエンド

| 項目 | 選定 | 理由 |
|------|------|------|
| **言語/ランタイム** | **C# 12 + .NET 8 LTS** | AS-2 既存スキル整合。.NET 8 は LTS（2026年11月までサポート）|
| **フレームワーク** | **ASP.NET Core Web API（Minimal API or Controllers）** | 21機能の REST API に十分。Minimal API で MVP 立ち上げ高速化、規模拡大時は Controllers に容易に移行可能 |
| **API スタイル** | **REST + JSON**（OpenAPI 3.0 で定義） | 内部システムで GraphQL 不要。OpenAPI で Nuxt 側型生成 |
| **ORM** | **Entity Framework Core 8 + Npgsql** | .NET 標準。EF Core Migration で DB スキーマ管理。CLAUDE.md の N+1・遅延読み込み注意点を Phase 5 で具体ガイドライン化 |
| **バリデーション** | **FluentValidation** | 機能要件のエラーコード体系（AUTH-NNN 等）と相性良好 |
| **ロギング** | **Serilog → CloudWatch Logs** | 構造化ログ標準。ERROR_HANDLING_STANDARDS の構造化ログ要件に直結 |
| **マッピング** | **Mapster** または手書き DTO | AutoMapper はリフレクション過多のため避ける。Mapster は AOT 親和性も高い |
| **Excel 出力（O-06）** | **ClosedXML** | テンプレート流し込みで 50明細 5秒以内（NFR §1.1）達成可能 |
| **実行基盤** | **AWS App Runner（VPC コネクタで RDS 接続）** | コンテナ管理不要、ゼロダウンタイムデプロイ、Auto Scaling 標準。最小構成（1 vCPU / 2GB）から開始 |

### 3.3 データベース・ストレージ

| 項目 | 選定 | 理由 |
|------|------|------|
| **RDB** | **Amazon RDS for PostgreSQL 16（Multi-AZ）** | EF Core 8 + Npgsql の .NET 親和性十分、ライセンスコストゼロ、Multi-AZ で SLA 99.95%、PITR 自動。データ量 5年 SKU 2万・発注 5,000 件 → `db.t4g.small` で十分（月額数千円規模） |
| **データベース論理設計の起点** | 第3正規形を基準（`.ai-native/methodology` データ設計原則）| 17マスタ + 2層商品モデル + マルチ仕入先（Phase 2 確定）。複合キー回避、サロゲートキー採用 |
| **画像ストレージ** | **Amazon S3（Standard Storage Class）** | 5GB・読み取り中心。CloudFront 経由で配信高速化、Pre-signed URL で時限アクセス制御 |
| **監査ログ保管** | **RDS PostgreSQL（直近 3ヶ月）+ S3（3年アーカイブ、Glacier Instant Retrieval）** | SEC-16 = 3年保管。INSERT 専用テーブル + DB ロール権限で UPDATE/DELETE 拒否、3ヶ月超は S3 Glacier IR にアーカイブ |
| **キャッシュ** | MVP では不要（1-2名利用） | Phase 7 で性能課題が出れば ElastiCache for Redis 追加検討 |
| **検索** | RDB の `pg_trgm` / `tsvector` 全文検索 | 全文検索専用エンジン（OpenSearch 等）は不要 |

### 3.4 インフラ・ホスティング

| 項目 | 選定 | 理由 |
|------|------|------|
| **バックエンドクラウド** | **Amazon Web Services（Tokyo: `ap-northeast-1`）** | AS-1 国内クラウド、業務データの国内保管を満たす |
| **フロント配信** | **Firebase Hosting**（一本化） | CDN + 無料 SSL + プレビューチャネル（PR 単位 URL 自動払い出し）+ `firebase.json` での Rewrite/Cache 一元管理が標準提供。`firebase deploy --only hosting` でデプロイ。配信対象は静的アセットのみ（業務データを含まない） |
| **バックエンド実行基盤** | **AWS App Runner** | コンテナ管理不要、ゼロダウンタイムデプロイ、ECR 連携、VPC コネクタで RDS 通信。MVP は `1 vCPU / 2GB` から |
| **CORS** | App Runner 側で Firebase Hosting ドメイン（`*.web.app` / `*.firebaseapp.com` / 独自ドメイン）を許可 | クロスオリジン構成（フロント=Firebase、API=AWS）のため `Access-Control-Allow-Origin` を明示設定 |
| **シークレット管理** | **AWS Secrets Manager + KMS** | RDS 接続文字列・Firebase Admin SDK サービスアカウント鍵・S3 Pre-signed URL 署名鍵を一元管理。App Runner はサービスロール経由でアクセス（ハードコード回避）|
| **可観測性** | **AWS CloudWatch Logs / Metrics / Alarms + X-Ray**（API 側） + **Firebase Console**（Hosting/Auth 側） | バックエンドは CloudWatch + X-Ray、フロント配信状況・Auth ログイン状況は Firebase Console。アラートは SNS → メール/Slack。**Phase 7 でダッシュボード品質が不足する場合は Datadog/New Relic 等への切替を再評価可能** |
| **DR/バックアップ** | RDS の自動バックアップ（35日 PITR）+ Cross-Region Snapshot（任意）+ Firebase Auth は Google 標準の冗長化（バックアップは Firebase Admin SDK でエクスポート可能） | NFR §5: RTO 4時間 / RPO 24時間 を満たす |

### 3.5 認証・認可

| 項目 | 選定 | 理由 |
|------|------|------|
| **認証基盤** | **Firebase Authentication（Email/Password プロバイダ）** | SEC-02（ID/パスワード認証）整合。Google 管理の堅牢な認証基盤、scrypt によるパスワードハッシュ（SEC-04）、ブルートフォース対策（SEC-06）、Email 確認・パスワードリセット標準提供。将来 Google/Microsoft SSO への拡張が容易 |
| **トークン形式** | **Firebase ID Token（JWT、有効期限 1 時間）+ Refresh Token（Firebase SDK が自動管理）** | クライアント側は Firebase JS SDK が自動更新。CSRF は Bearer トークン方式のため `Authorization: Bearer <ID Token>` ヘッダで送信、CORS と組み合わせて防御 |
| **セッション管理** | **8 時間アイドルタイムアウトはフロント側で実装**（最終操作時刻を localStorage に記録、超過時に `signOut()`）| SEC-05 整合。Firebase ID Token は 1 時間自動更新だが、業務上のアイドル切断はアプリ層で制御 |
| **バックエンド検証** | **`Microsoft.AspNetCore.Authentication.JwtBearer` + Firebase JWKS** + **FirebaseAdmin (.NET) SDK** | App Runner で受信した `Authorization` ヘッダの ID Token を Firebase の公開鍵（JWKS エンドポイント）で署名検証 → UID 取得 → Custom Claims から権限取得 |
| **権限管理（SoT）** | **RDS `users` テーブルが業務情報・権限ロールの SoT**、Firebase Auth は ID/Email/認証情報の SoT。**Custom Claims は権限のキャッシュ**（RDS で権限変更時に Firebase Admin SDK で `setCustomUserClaims()` を呼び再同期） | データフロー整合性（CLAUDE.md 原則6）: RDS = SoT、Firebase Custom Claims = キャッシュ。SoT 側書込先行、キャッシュ後追い |
| **認可** | **ASP.NET Core Authorization Policies + Custom Claims** | 4 権限カテゴリ × レベル（C-02）をポリシーで宣言、Custom Claims の `role` / `permissions[]` を評価。SEC-11 = サーバサイドで全 API 検証 |
| **削除済ユーザ（AUTH-003 / SEC-12）** | Firebase Auth で `disabled=true` に設定 + RDS `users.is_active=false` を同期。Firebase 側で disabled なユーザは ID Token 発行不可、既存 Token も次回検証で `auth/user-disabled` で拒否 | 両方同期する手順を Phase 5 で文書化（SoT は RDS、削除操作は RDS 先行 → Firebase 反映の順序）|
| **ブルートフォース対策（SEC-06）** | Firebase Auth 標準のレートリミット（同一 IP / 同一アカウントへの過度な試行を自動拒否）+ Firebase Auth の不正検知 | SEC-06 整合。MaxFailedAccessAttempts 等のしきい値設定は Firebase Console / Identity Platform 設定で実施 |
| **シークレット鍵管理** | Firebase Admin SDK のサービスアカウント JSON は AWS Secrets Manager に格納、App Runner サービスロールから取得 | ハードコード回避、ローテーション運用 |
| **SSO 拡張** | MVP 対象外。Post-MVP で Firebase Auth の Google / Microsoft プロバイダ追加で対応可能（実装変更最小）| SEC-02 ノート整合 |
| **SSO** | MVP 対象外、Post-MVP で AWS Cognito / Entra ID 連携を検討 | SEC-02 ノート整合 |

### 3.6 CI/CD

| 項目 | 選定 | 理由 |
|------|------|------|
| **CI/CD プラットフォーム** | **GitHub Actions（AWS OIDC 連携 + Firebase CLI）** | 既存リポジトリが GitHub。AWS は `aws-actions/configure-aws-credentials` + OIDC で長期 IAM キー漏洩リスクゼロ、Firebase は `FirebaseExtended/action-hosting-deploy` または `firebase deploy` CLI を Service Account 経由で実行。**Phase 5 試作後に CodePipeline 等への切替も再評価可** |
| **パイプライン構成** | `lint → unit test → build → deploy preview → deploy prod` | フロント: Firebase Hosting のプレビューチャネル（PR ごとに自動 URL 払い出し）。バック: PR ごとに別 App Runner サービス（Phase 5 設計）|
| **コード品質** | **dotnet test（xUnit）+ Vitest（Nuxt）+ ESLint/Prettier + dotnet format** | カバレッジ 70%（NFR §7）を CI で計測 |
| **脆弱性スキャン** | **GitHub Dependabot + dotnet list package --vulnerable + Trivy（コンテナ層）+ Amazon Inspector（任意）** | SEC-19 整合 |
| **シークレット** | GitHub Environments + AWS OIDC 連携（IAM Role for GitHub）+ Firebase Service Account（GitHub Secrets 格納、Workload Identity Federation で長期鍵不要化を Phase 5 で検討）| キー管理レス、長期トークン漏洩リスク低減 |
| **DB マイグレーション** | EF Core Migration を CI から `dotnet ef database update` で適用 | 環境別実行、ロールバック手順を Phase 5 で文書化 |

---

## 4. 非機能要件との整合性確認

| 非機能要件 | 値 | 充足方式 | 整合 |
|------------|----|----|---|
| 同時利用 1-2名 / ピーク 5名 | NFR §3 | App Runner min=1 / max=2 + RDS `t4g.small` で十分 | ✅ |
| 一覧初期表示 500ms（95%ile）| NFR §1.1 | EF Core でクエリ最適化 + PostgreSQL のインデックス設計、ページング前提 | ✅ |
| 詳細・設定系初期表示 200ms | NFR §1.1 | 単純な単票取得は十分達成可能 | ✅ |
| Excel 出力 5秒以内 | NFR §1.1 | ClosedXML テンプレート + 非同期処理（必要なら）| ✅ |
| 画像アップ 5秒以内（5MB）| NFR §1.1 | S3 直接 PUT（Pre-signed URL）+ サムネ生成は Lambda 等で非同期 | ✅ |
| HTTPS 必須（SEC-01） | NFR §2 | Firebase Hosting / App Runner 標準で HTTPS 強制 | ✅ |
| パスワードハッシュ（SEC-04）| NFR §2 | Firebase Authentication 標準（scrypt、Google が鍵管理）| ✅ |
| 監査ログ改竄防止（SEC-17）| NFR §2 | PostgreSQL の append-only テーブル + ロール権限で UPDATE/DELETE 拒否、3年経過分は S3 Object Lock で不変化 | ✅ |
| 業務時間 99% SLA / RTO 4h / RPO 24h | NFR §5 | App Runner SLA 99.95%、RDS Multi-AZ で自動フェイルオーバ、PITR で 24h 以内復旧、Firebase Auth は Google 標準 SLA 99.95% | ✅ |
| 仕入単価=中-高機密度（暗号化）| NFR §6.2 | **A 案採用**: RDS Storage Encryption（KMS）+ TLS + 4権限アクセス制御 + 監査ログ。Phase 5 で再評価 | ⚠️ |
| 営業秘密の監査（不競法）| NFR §6.3 | アクセス制御 + 監査ログ + X-Ray でアクセス追跡 | ✅ |
| データ国内保管 | NFR §4.2 | **業務データは AWS Tokyo（`ap-northeast-1`）に保管**。**Firebase Hosting の静的アセット配信は CDN（業務データを含まない）**、**Firebase Authentication のユーザ識別情報（UID/Email）は Google グローバル配置**となるため部分矛盾、**オペレーター判断で許容**（後述 §8 R-13）| ⚠️ |

> **⚠️ 留意点（#5 A 採用）:** 仕入単価の「中-高」機密度に対し、pgcrypto によるカラム単位暗号化や AWS KMS Envelope Encryption は MVP では運用負荷過大のため見送り。RDS Storage Encryption（KMS）+ TLS + アクセス制御 + 監査ログで対応する。**Phase 5 で詳細設計時に再評価**する（オペレーターレビュー #5 で合意）。

> **⚠️ 留意点（NFR §4.2 部分矛盾 / オペレーター許容）:** Firebase Authentication 採用により、ユーザ識別情報（UID / Email / 認証メタデータ）が Google のグローバルインフラに分散保管される。業務データ本体（仕入単価・取引先・発注書等）は引き続き AWS Tokyo の RDS に国内保管されるが、NFR §4.2「データ国内保管」とは厳密には部分矛盾する。オペレーターレビュー #11 で **許容して採用**を確定（業務上のメリット = Google 管理の堅牢な認証、scrypt ハッシュ、SSO 拡張容易性、運用負荷軽減を優先）。**Phase 3 の NFR §4.2 記述は次回の NFR 改訂タイミングで「業務データ本体は国内保管、ユーザ認証情報は Firebase Auth により海外含むグローバル配置を許容」と明示化することを推奨**。

---

## 5. データ機密度との整合性

| データ種別 | 機密度 | 配置 | 暗号化 | アクセス制御 |
|---|---|---|---|---|
| 仕入単価 | 中-高 | RDS PostgreSQL (AWS Tokyo) | KMS 保存時暗号化 + TLS 1.2+ 通信時 | 4 権限ポリシー + 監査ログ（Phase 5 で再評価） |
| 商品マスタ・発注書 | 中 | RDS PostgreSQL (AWS Tokyo) | KMS + TLS | 4 権限ポリシー + 監査ログ |
| 取引先・仕入先 | 中 | RDS PostgreSQL (AWS Tokyo) | KMS + TLS | 4 権限ポリシー |
| **ユーザ業務情報・権限（SoT）** | 軽微 | **RDS PostgreSQL (AWS Tokyo)** | KMS + TLS | Firebase UID で紐付け、業務情報・権限ロールはここに格納 |
| **ユーザ認証情報（SoT: UID/Email/PW ハッシュ）** | 軽微 | **Firebase Authentication（Google グローバル）** | Firebase 標準（保存時・通信時とも暗号化、scrypt）| Firebase IAM + サービスアカウント |
| 商品画像 | 低-中 | S3 (AWS Tokyo) | SSE-S3（標準有効）+ Pre-signed URL 時限アクセス | Bucket Policy + IAM Role |
| 監査ログ | 中 | RDS → S3 Glacier IR (AWS Tokyo) | KMS + S3 Object Lock（3年アーカイブは不変化）| IAM + 改竄防止設計 |
| シークレット | 高 | AWS Secrets Manager | KMS（CMK） | Managed IAM Role 限定 |
| Firebase サービスアカウント鍵 | 高 | AWS Secrets Manager（または GCP Secret Manager との二重管理） | KMS（CMK） | App Runner サービスロール限定 |

> **データフロー整合性（CLAUDE.md 原則6）:** ユーザ業務情報・権限ロールの SoT は **RDS `users` テーブル**。Firebase Auth Custom Claims は権限のキャッシュであり、RDS で権限変更 → Firebase Admin SDK で `setCustomUserClaims()` を呼び再同期する。SoT 側書込先行、キャッシュ後追いの順序を厳守。

---

## 6. 並行稼働戦略（AS-4 / #6 A 採用）の技術影響

旧3システム（生産管理・販売管理・受発注）との並行稼働を **完全手動運用**（#6 A）で進める方針が技術設計に与える要件:

| # | 要件 | 対応 |
|---|------|------|
| MIG-1 | 初期データ移行：旧システム → 新システム | CSV / Excel インポート機能を Phase 5 設計に含める（マスタ系優先）|
| MIG-2 | 並行期間中の同期 | **完全手動**。新システム = SoT、旧への還流は生産管理部の判断で必要時のみ転記。自動同期は実装しない |
| MIG-3 | 旧システム由来データの ID 整合性 | 旧 ID を保持するための外部キー（`legacy_id`）を主要テーブルに NULL 許容で追加 |
| MIG-4 | 段階的機能切替 | 機能フラグで「旧運用」「新運用」を切替可能にする（Feature Toggle）|
| MIG-5 | カットオーバー時のデータ整合性検証 | リリース判定（Phase 7）で diff 検証スクリプトを準備 |
| MIG-6 | 並行期間の業務オペレータ負荷 | 二重入力期間が一時的に発生することをオペレーターに明示。期間は Phase 6 で確定（想定: 1-2 ヶ月）|

---

## 7. 採用しなかった選択肢（記録）

| 選択肢 | 不採用理由 |
|--------|----------|
| Azure / GCP | #1 で AWS Tokyo 確定。.NET 親和性は Azure 優位だが社内既存知見・運用契約を優先 |
| オンプレ / 社内サーバ | 1-2名体制でバックアップ・冗長化・OS パッチを自前運用するのは負荷過大（AS-3 矛盾）|
| Nuxt SSR / Hybrid モード | 業務 LAN 内・SEO 不要・1-2名利用で SSR の利点なし。SSR 起因のバグ（CLAUDE.md Nuxt 注意点）も回避 |
| Node.js / Express / Go バックエンド | チームスキル AS-2 と整合せず、.NET の型安全性・LINQ・EF Core の生産性に劣る |
| NoSQL（DynamoDB / Cosmos / Firestore） | 業務データが関係的（17マスタ + 2層商品 + マルチ仕入先 + 発注明細）で RDB 適合 |
| GraphQL API | 機能要件 21 機能の REST 設計で過剰スペック。クライアント 1 種類（Nuxt）のみで GraphQL の柔軟性が活きない |
| pgcrypto / Envelope Encryption（仕入単価）| MVP では運用負荷過大。KMS 保存時暗号化 + TLS + アクセス制御 + 監査ログで十分。Phase 5 で再評価 |
| Redis Cache（MVP 導入） | 1-2名同時利用では性能課題が出ない想定。Phase 7 で必要性を再評価 |
| マイクロサービス分割 | 21機能の業務システムに対しモノリス構成で十分。1-2名運用でマイクロサービスは複雑性過剰 |
| Kubernetes（EKS） | App Runner で十分。EKS は運用負荷が AS-3 と矛盾 |
| Vuetify / PrimeVue / Naive UI / Element Plus | #4 で TailwindCSS + Reka UI + lucide-icons 確定。重量級ライブラリ依存を避ける |
| AWS CodePipeline | #7 で GitHub Actions + AWS OIDC 確定。Phase 5 試作後に再評価可 |
| Datadog / New Relic | #8 で CloudWatch + X-Ray 確定。Phase 7 で必要時に切替評価可 |
| RDS for SQL Server / Aurora PostgreSQL | #3 で RDS for PostgreSQL Multi-AZ 確定。SQL Server はライセンスコスト過大、Aurora は MVP 規模でオーバスペック |
| Amplify Hosting / S3 + CloudFront（フロント配信）| #11 で Firebase Hosting 一本化に変更。プレビューチャネル・`firebase.json` 一元管理の運用メリットを優先 |
| ASP.NET Core Identity（認証）| #11 で Firebase Authentication に変更。Google 管理の堅牢な認証基盤、SSO 拡張容易性、scrypt ハッシュ、運用負荷軽減を優先 |
| AWS Cognito / Auth0 / Microsoft Entra ID | #11 で Firebase Authentication 確定。Cognito は GUI/SDK の成熟度で劣る、Auth0 は商用ライセンスコスト、Entra ID は AS-2 の社内既存 Microsoft 365 アカウントとの整合があれば再評価候補 |

---

## 8. リスクと留意点

| # | リスク | 影響度 | 緩和策 |
|---|--------|------|--------|
| R-1 | AWS Tokyo 単一リージョン障害時のサービス停止 | 中 | RDS Multi-AZ + S3 99.99% で大半をカバー、Cross-Region は MVP では見送り、Phase 7 で評価 |
| R-3 | 並行稼働期間の二重入力負荷 | 高 | MIG-6 でオペレーターと合意。CSV インポートで業務効率化、並行期間を 1-2 ヶ月に短縮 |
| R-4 | EF Core の N+1 クエリ問題（CLAUDE.md） | 中 | `Include` / `AsSplitQuery` のガイドラインを Phase 5 で文書化、Code Review チェック項目に追加、X-Ray トレースで可視化 |
| R-5 | Nuxt SPA モードのバンドルサイズ肥大 | 低 | コード分割 + 動的 import で初期表示 500ms 維持、CloudFront キャッシュ活用 |
| R-6 | 4 権限ポリシーの実装漏れ（SEC-11） | 高 | 全 API エンドポイントに `[Authorize]` 必須化を CI Lint で強制、テストカバレッジで網羅性検証 |
| R-7 | 監査ログ INSERT 専用の運用ミス | 中 | PostgreSQL ロール権限で UPDATE/DELETE を REVOKE、Migration 適用前に Code Review で確認 |
| R-8 | RDS / App Runner の **メンテナンスウィンドウ通知**見逃し → 予期せぬ再起動 | 中 | SNS → メール/Slack 通知、計画停止ウィンドウを業務時間外（土日深夜）に固定 |
| R-9 | App Runner の **オートスケール上限**設定誤り → 1-2名利用で過剰課金 | 中 | min=1 / max=2 で固定、CloudWatch 課金アラート設定（月額上限超過で通知）|
| R-10 | ~~CloudFront キャッシュ更新タイミング~~ → **Firebase Hosting キャッシュ更新タイミング** → デプロイ後の旧版表示 | 低 | Firebase Hosting は新版デプロイで即時切替（CDN が自動 Purge）、`firebase.json` で `index.html` を `no-cache` 指定 |
| R-11 | **Firebase Auth と RDS users の同期ずれ** → 削除済ユーザがログイン可能 / 権限変更が反映されない | 高 | RDS 側操作を SoT 化し、Firebase Admin SDK 呼び出しを必須化（トランザクション境界外でも reconciler バッチで日次照合）。Phase 5 で同期手順とリカバリパスを文書化 |
| R-12 | **Firebase / Google サービスのベンダーロックイン** | 中 | 認証層は抽象化レイヤー（`IAuthService`）でラップ、将来 Cognito / Entra ID 等への切替パスを設計上担保。Phase 5 で抽象化方針を文書化 |
| R-13 | **Firebase Auth のユーザ識別情報海外配置（NFR §4.2 部分矛盾）** | 中 | オペレーター許容済（#11）。業務データ本体は AWS Tokyo 国内保管を維持。Phase 3 NFR §4.2 記述の改訂を次回 NFR レビュー時に推奨。海外データ移転リスクが将来規制で問題化する場合、Cognito / Entra ID 等への切替（R-12 緩和策と一体）|
| R-14 | **クロスオリジン構成（フロント=Firebase / API=AWS）の CORS / CSP 設定ミス** | 中 | App Runner で Firebase Hosting ドメインを明示的に Allow Origin に指定、CSP は `connect-src` で App Runner ドメインを許可。Phase 5 で設定テンプレート作成、E2E テストで検証 |
| R-15 | **Firebase Admin SDK サービスアカウント鍵の漏洩** | 高 | AWS Secrets Manager で管理、CI/CD はサービスアカウント発行を最小化、Workload Identity Federation で長期鍵不要化を Phase 5 で検討 |

> **削除済リスク:** R-2 (Azure SQL Serverless の Cold start 遅延) は #3 で RDS Multi-AZ 採用により消滅。
> **更新リスク:** R-10 は CloudFront → Firebase Hosting 変更により内容を更新（Invalidation 手動実行不要、Hosting が自動処理）。

---

## 9. Phase 4 ゲート判定（事前自己評価 + オペレーターレビュー反映）

| # | ゲート条件 | 状態 | 根拠 |
|---|-----------|------|------|
| 4-1 | 全レイヤーの技術スタックが確定 | ✅ PASS | §3.1〜3.6 でフロント・バック・DB/ストレージ・インフラ・認証認可・CI/CD を全選定。11件のレビュー観点すべてクローズ（#11 で認証=Firebase Auth、フロント配信=Firebase Hosting に変更）|
| 4-2 | 各選定に対して要件ベースの理由が説明可能 | ✅ PASS | §3 各表の「理由」列で要件・確定前提との対応を明示 |
| 4-3 | インフラ構成が非機能要件と整合 | ✅ PASS（⚠️ 注記あり）| §4 で 12 項目の非機能要件と充足方式を対応付け。仕入単価暗号化は MVP 範囲 + Phase 5 再評価で合意（#5）、データ国内保管は Firebase Auth 採用による部分矛盾をオペレーター許容（#11）|

**Phase 4 ゲート 3条件すべて PASS（自己評価 + オペレーターレビュー）。次フェーズ（Phase 5 基本設計 + プロトタイプ）へ進行可。**

---

## 10. レビュー結果（オペレーター確認）

| # | 観点 | 状態 | 決定事項 |
|---|------|------|---------|
| 1 | クラウドプロバイダ選定 | ✅ 反映済 | **AWS Tokyo（`ap-northeast-1`）** を採用（当初推奨の Azure から変更、後に #11 で認証・フロント配信に Firebase 追加してハイブリッド構成へ）|
| 2 | フロントレンダリング方式 | ✅ 確定 | **Nuxt 3 SPA モード**（`nuxt generate` → Firebase Hosting 配信、#11 で S3+CloudFront から変更）|
| 3 | DB 選定 | ✅ 反映済 | **Amazon RDS for PostgreSQL 16 Multi-AZ** を採用（EF Core 8 + Npgsql）|
| 4 | UI ライブラリ | ✅ 反映済 | **TailwindCSS + Reka UI（Headless UI Vue）+ lucide-icons** の組み合わせを採用（重量級ライブラリ不採用）|
| 5 | 仕入単価の暗号化方針 | ✅ 確定 | **A. KMS 保存時暗号化 + アクセス制御**で MVP 進行、**Phase 5 で再評価** |
| 6 | 並行稼働の同期方針 | ✅ 確定 | **A. 完全手動**（初期 CSV/Excel、並行期間は新システム=SoT、旧への還流は手動）|
| 7 | CI/CD プラットフォーム | ✅ 確定 | **GitHub Actions + AWS OIDC + Firebase CLI**（Phase 5 試作後に CodePipeline 等への切替も再評価可）|
| 8 | 監視・可観測性 | ✅ 確定 | **CloudWatch + X-Ray（API 側）+ Firebase Console（Hosting/Auth 側）**（柔軟に見直し可、不要判断もあり得る前提）|
| 9 | 不採用選択肢リスト | ✅ 反映済 | #11 反映で Amplify Hosting/S3+CloudFront・ASP.NET Core Identity・Cognito/Auth0/Entra ID を追記 |
| 10 | リスクリスト | ✅ 反映済 | R-2 削除（RDS Multi-AZ）+ R-8/R-9/R-10 追加 + **#11 反映で R-11〜R-15 追加（計14件、Firebase 関連の同期・ベンダーロックイン・データ国内保管・CORS・サービスアカウント鍵管理）**|
| **11** | **認証認可とフロント配信の Firebase 切替** | ✅ 反映済 | **認証認可: Firebase Authentication（Email/Password、Custom Claims）**、**フロント配信: Firebase Hosting 一本化**。NFR §4.2 部分矛盾は **オペレーター許容**、Phase 3 NFR 記述の改訂を次回 NFR レビュー時に推奨 |

**全11項目クローズ。Phase 4 完了。Phase 5（基本設計 + プロトタイプ開発）へ進行可。**
