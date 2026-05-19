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
            ▼
   [CloudFront]
        │
        ├──→ [S3 (Static Hosting)]  ← Nuxt 3 SPA（nuxt generate）
        │
        └──→ [App Runner / VPC コネクタ経由] → [ASP.NET Core 8 Web API (C#)]
                                                  ├─ EF Core 8 + Npgsql ─→ [RDS for PostgreSQL Multi-AZ]
                                                  ├─ AWS SDK ─→ [S3 (画像 5GB / 監査ログアーカイブ)]
                                                  ├─ ASP.NET Core Identity（ID/パスワード認証）
                                                  └─ Serilog → CloudWatch Logs

横断:
  - AWS Secrets Manager + KMS: 接続文字列・JWT 署名鍵・暗号化鍵
  - CloudWatch Metrics / Logs / Alarms + X-Ray: 可観測性
  - SNS: アラート通知（メール/Slack）
  - GitHub Actions + AWS OIDC: CI/CD（build → test → deploy）
```

**リージョン:** AWS Tokyo（`ap-northeast-1`）、RDS Multi-AZ で `ap-northeast-1a` / `ap-northeast-1c` を利用。

---

## 3. レイヤー別技術選定

### 3.1 フロントエンド

| 項目 | 選定 | 理由 |
|------|------|------|
| **フレームワーク** | **Nuxt 3（Vue 3 + TypeScript）** | AS-2 既存スキル整合。Composition API + `<script setup>` で型安全、内製エコシステム成熟 |
| **レンダリング方式** | **SPA モード（`ssr: false`）+ `nuxt generate` で静的ビルド** | 業務 LAN 内 PC 利用・SEO 不要・1-2名同時利用のため SSR の利点なし。SSR 起因の Hydration バグも回避（CLAUDE.md Nuxt 注意点）|
| **配信** | **S3（Static Hosting）+ CloudFront** | グローバル CDN（業務 LAN からのアクセスも高速化）。HTTPS 自動付与、Origin Access Control で S3 直アクセス禁止 |
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
| **クラウドプロバイダ** | **Amazon Web Services（Tokyo: `ap-northeast-1`）** | AS-1 国内クラウド、社内既存知見・運用契約があれば再利用可能 |
| **フロント配信** | **S3 Static Hosting + CloudFront** | GitHub Actions から `aws s3 sync` でデプロイ、CloudFront Invalidation で即時反映 |
| **バックエンド実行基盤** | **AWS App Runner** | コンテナ管理不要、ゼロダウンタイムデプロイ、ECR 連携、VPC コネクタで RDS 通信。MVP は `1 vCPU / 2GB` から |
| **シークレット管理** | **AWS Secrets Manager + KMS** | 接続文字列・JWT 鍵・S3 Pre-signed URL 署名鍵を一元管理。App Runner はサービスロール経由でアクセス（ハードコード回避）|
| **可観測性** | **CloudWatch Logs / Metrics / Alarms + X-Ray** | アプリログ・分散トレース・依存呼び出しの可視化。Anomaly Detection で異常検知。アラートは SNS → メール/Slack。**Phase 7 でダッシュボード品質・アラート表現が不足する場合は Datadog/New Relic 等の SaaS への切替を再評価可能** |
| **CDN** | **CloudFront**（標準採用）| Static ホスティングと一体運用。Origin Access Control で S3 直アクセス禁止 |
| **DR/バックアップ** | RDS の自動バックアップ（35日 PITR）+ Cross-Region Snapshot（任意） | NFR §5: RTO 4時間 / RPO 24時間 を満たす。MVP は単一リージョンで十分、Phase 7 で Cross-Region 評価 |

### 3.5 認証・認可

| 項目 | 選定 | 理由 |
|------|------|------|
| **認証方式** | **ASP.NET Core Identity（ID/パスワード）** | SEC-02 確定。Identity 標準の PBKDF2-SHA256 でハッシュ化（SEC-04）|
| **セッション管理** | **JWT（HttpOnly Secure Cookie 格納）+ サーバ側 refresh token** | XSS リスクを下げつつ 8時間タイムアウト（SEC-05）を実装。CSRF は SameSite=Strict + AntiforgeryToken（SEC-07）|
| **認可** | **ASP.NET Core Authorization Policies + Claims** | 4 権限カテゴリ × レベル（C-02）をポリシーで宣言。SEC-11 = サーバサイドで全 API 検証 |
| **削除済ユーザ** | Identity の `IsActive` フラグで判定、ログイン段階でリジェクト（AUTH-003）| SEC-12 整合 |
| **ブルートフォース対策** | Identity の `LockoutOnFailure=true`, `MaxFailedAccessAttempts=5` | SEC-06 整合 |
| **SSO** | MVP 対象外、Post-MVP で AWS Cognito / Entra ID 連携を検討 | SEC-02 ノート整合 |

### 3.6 CI/CD

| 項目 | 選定 | 理由 |
|------|------|------|
| **CI/CD プラットフォーム** | **GitHub Actions（AWS OIDC 連携）** | 既存リポジトリが GitHub。`aws-actions/configure-aws-credentials` + OIDC で長期 IAM キー漏洩リスクゼロ。**Phase 5 試作後に CodePipeline 等への切替も再評価可** |
| **パイプライン構成** | `lint → unit test → build → deploy preview → deploy prod` | プレビュー環境は PR ごとに別 S3 Prefix + 別 App Runner サービスで分離（Phase 5 設計）|
| **コード品質** | **dotnet test（xUnit）+ Vitest（Nuxt）+ ESLint/Prettier + dotnet format** | カバレッジ 70%（NFR §7）を CI で計測 |
| **脆弱性スキャン** | **GitHub Dependabot + dotnet list package --vulnerable + Trivy（コンテナ層）+ Amazon Inspector（任意）** | SEC-19 整合 |
| **シークレット** | GitHub Environments + AWS OIDC 連携（IAM Role for GitHub） | キー管理レス、長期トークン漏洩リスク低減 |
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
| HTTPS 必須（SEC-01） | NFR §2 | CloudFront / App Runner 標準で HTTPS 強制 | ✅ |
| パスワードハッシュ（SEC-04）| NFR §2 | Identity 標準（PBKDF2-SHA256, 100,000 iterations）| ✅ |
| 監査ログ改竄防止（SEC-17）| NFR §2 | PostgreSQL の append-only テーブル + ロール権限で UPDATE/DELETE 拒否、3年経過分は S3 Object Lock で不変化 | ✅ |
| 業務時間 99% SLA / RTO 4h / RPO 24h | NFR §5 | App Runner SLA 99.95%、RDS Multi-AZ で自動フェイルオーバ、PITR で 24h 以内復旧 | ✅ |
| 仕入単価=中-高機密度（暗号化）| NFR §6.2 | **A 案採用**: RDS Storage Encryption（KMS）+ TLS + 4権限アクセス制御 + 監査ログ。Phase 5 で再評価 | ⚠️ |
| 営業秘密の監査（不競法）| NFR §6.3 | アクセス制御 + 監査ログ + X-Ray でアクセス追跡 | ✅ |
| データ国内保管 | NFR §4.2 | AWS Tokyo 単一リージョン、バックアップも `ap-northeast-1` 内 | ✅ |

> **⚠️ 留意点（#5 A 採用）:** 仕入単価の「中-高」機密度に対し、pgcrypto によるカラム単位暗号化や AWS KMS Envelope Encryption は MVP では運用負荷過大のため見送り。RDS Storage Encryption（KMS）+ TLS + アクセス制御 + 監査ログで対応する。**Phase 5 で詳細設計時に再評価**する（オペレーターレビュー #5 で合意）。

---

## 5. データ機密度との整合性

| データ種別 | 機密度 | 配置 | 暗号化 | アクセス制御 |
|---|---|---|---|---|
| 仕入単価 | 中-高 | RDS PostgreSQL | KMS 保存時暗号化 + TLS 1.2+ 通信時 | 4 権限ポリシー + 監査ログ（Phase 5 で再評価） |
| 商品マスタ・発注書 | 中 | RDS PostgreSQL | KMS + TLS | 4 権限ポリシー + 監査ログ |
| 取引先・仕入先 | 中 | RDS PostgreSQL | KMS + TLS | 4 権限ポリシー |
| ユーザマスタ | 軽微 | RDS PostgreSQL | KMS + TLS | Identity 標準保護 |
| 商品画像 | 低-中 | S3 | SSE-S3（標準有効）+ Pre-signed URL 時限アクセス | Bucket Policy + IAM Role |
| 監査ログ | 中 | RDS → S3 Glacier IR | KMS + S3 Object Lock（3年アーカイブは不変化）| IAM + 改竄防止設計 |
| シークレット | 高 | AWS Secrets Manager | KMS（CMK） | Managed IAM Role 限定 |

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
| Amplify Hosting | #2 で S3 + CloudFront 確定（より単純で IaC 制御容易） |

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
| R-10 | CloudFront キャッシュ更新タイミング → デプロイ後の旧版表示 | 低 | デプロイ時に Invalidation 自動実行、`index.html` は `Cache-Control: no-cache` |

> **削除済リスク:** R-2 (Azure SQL Serverless の Cold start 遅延) は #3 で RDS Multi-AZ 採用により消滅。

---

## 9. Phase 4 ゲート判定（事前自己評価 + オペレーターレビュー反映）

| # | ゲート条件 | 状態 | 根拠 |
|---|-----------|------|------|
| 4-1 | 全レイヤーの技術スタックが確定 | ✅ PASS | §3.1〜3.6 でフロント・バック・DB/ストレージ・インフラ・認証認可・CI/CD を全選定。10件のレビュー観点すべてクローズ |
| 4-2 | 各選定に対して要件ベースの理由が説明可能 | ✅ PASS | §3 各表の「理由」列で要件・確定前提との対応を明示 |
| 4-3 | インフラ構成が非機能要件と整合 | ✅ PASS | §4 で 12 項目の非機能要件と充足方式を対応付け（仕入単価暗号化は MVP 範囲 + Phase 5 再評価で合意）|

**Phase 4 ゲート 3条件すべて PASS（自己評価 + オペレーターレビュー）。次フェーズ（Phase 5 基本設計 + プロトタイプ）へ進行可。**

---

## 10. レビュー結果（オペレーター確認）

| # | 観点 | 状態 | 決定事項 |
|---|------|------|---------|
| 1 | クラウドプロバイダ選定 | ✅ 反映済 | **AWS Tokyo（`ap-northeast-1`）** を採用（当初推奨の Azure から変更）|
| 2 | フロントレンダリング方式 | ✅ 確定 | **Nuxt 3 SPA モード**（`nuxt generate` → S3 + CloudFront 配信） |
| 3 | DB 選定 | ✅ 反映済 | **Amazon RDS for PostgreSQL 16 Multi-AZ** を採用（EF Core 8 + Npgsql）|
| 4 | UI ライブラリ | ✅ 反映済 | **TailwindCSS + Reka UI（Headless UI Vue）+ lucide-icons** の組み合わせを採用（重量級ライブラリ不採用）|
| 5 | 仕入単価の暗号化方針 | ✅ 確定 | **A. KMS 保存時暗号化 + アクセス制御**で MVP 進行、**Phase 5 で再評価** |
| 6 | 並行稼働の同期方針 | ✅ 確定 | **A. 完全手動**（初期 CSV/Excel、並行期間は新システム=SoT、旧への還流は手動）|
| 7 | CI/CD プラットフォーム | ✅ 確定 | **GitHub Actions + AWS OIDC**（Phase 5 試作後に CodePipeline 等への切替も再評価可）|
| 8 | 監視・可観測性 | ✅ 確定 | **CloudWatch + X-Ray**（柔軟に見直し可、不要判断もあり得る前提）|
| 9 | 不採用選択肢リスト | ✅ 確定 | 追加・見直し不要 |
| 10 | リスクリスト R-1〜R-7 | ✅ 反映済 | **R-2 削除（RDS Multi-AZ で消滅）+ R-8/R-9/R-10 追加**で確定（計9件）|

**全10項目クローズ。Phase 4 完了。Phase 5（基本設計 + プロトタイプ開発）へ進行可。**
