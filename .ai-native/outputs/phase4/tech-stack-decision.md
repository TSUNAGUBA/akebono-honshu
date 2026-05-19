# Phase 4 成果物: 技術スタック確定（ドラフト）

> **作成日:** 2026-05-19
> **状態:** ドラフト（オペレーターレビュー待ち）
> **依存:** Phase 2 確定9項目、Phase 3 機能要件（21機能）+ 非機能要件（性能・セキュリティ・可用性・データ機密度）
> **方針:** 要件と確定前提（インフラ=国内クラウド／既存スキル=Vue+Nuxt/.NET+C#／体制=1-2名／移行=並行稼働）から、各レイヤーの選定をオーバースペック/アンダースペックにならない最小構成で行う。

---

## 1. 確定前提（オペレーターレビューで決定）

| # | 前提 | 値 | 影響 |
|---|------|----|----|
| AS-1 | インフラ大方針 | **国内パブリッククラウド** | リージョン=日本国内、マネージド優先、スケールアウト不要 |
| AS-2 | 既存運用スキル | **Vue/Nuxt 系 + .NET/C# 系** | フロント＝Nuxt、バック＝.NET を第一候補とする |
| AS-3 | 開発・運用体制 | **社内エンジニア 1-2名（小規模）** | マネージドサービス重視、運用負荷最小化、可観測性は SaaS で済ませる |
| AS-4 | 旧3システム移行戦略 | **並行稼働** | 二重入力期間を許容。新旧データ同期は手動（CSV/Excel）または個別同期スクリプト。Phase 5 で詳細化 |

---

## 2. アーキテクチャ全体図

```
[業務 LAN PC: Chrome/Edge]
            │ HTTPS
            ▼
┌─────────────────────────────────────────────┐
│ Azure Front Door / Application Gateway (任意)│
│   ※ MVP は App Service 直アクセスでも可     │
└─────────────────────────────────────────────┘
            │
   ┌────────┴─────────┐
   ▼                  ▼
[Static Web App]   [App Service (Linux)]
 Nuxt 3 SPA       ASP.NET Core 8 Web API (C#)
 (SSGビルド)        ├─ EF Core ─→ [Azure SQL Database]
                    ├─ Blob SDK ─→ [Azure Blob Storage]
                    │              （商品画像 5GB / 監査ログアーカイブ）
                    ├─ ASP.NET Core Identity（ID/パスワード認証）
                    └─ Application Insights（ログ・トレース・メトリクス）

横断:
  - Azure Key Vault: 接続文字列・JWT 署名鍵・暗号化鍵
  - Azure Monitor + Application Insights: 可観測性
  - GitHub Actions: CI/CD（build → test → deploy）
```

> **注:** 上記は Azure Japan East を主候補としたものだが、AWS Tokyo（App Runner + RDS for SQL Server + S3）でも同等構成が可能。最終リージョン/プロバイダ選定は §10 で議論。

---

## 3. レイヤー別技術選定

### 3.1 フロントエンド

| 項目 | 選定 | 理由 |
|------|------|------|
| **フレームワーク** | **Nuxt 3（Vue 3 + TypeScript）** | AS-2 既存スキル整合。Composition API + `<script setup>` で型安全、内製エコシステム成熟 |
| **レンダリング方式** | **SPA モード（`ssr: false`）+ 静的ホスティング** | 業務 LAN 内 PC 利用・SEO 不要・1-2名同時利用のため SSR の利点なし。SSR 起因の Hydration バグも回避（CLAUDE.md Nuxt 注意点）|
| **UI ライブラリ** | **Vuetify 3** または **Naive UI**（Phase 5 で確定） | 業務系の DataTable / Form / Dialog が標準装備で MVP 着工早い。**Vuetify 3 を第一候補**（Vue3 公式準拠・テーブル/カードビュー P-04・O-03 に直結）|
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
| **ORM** | **Entity Framework Core 8** | .NET 標準。EF Core Migration で DB スキーマ管理。CLAUDE.md の N+1・遅延読み込み注意点を Phase 5 で具体ガイドライン化 |
| **バリデーション** | **FluentValidation** | 機能要件のエラーコード体系（AUTH-NNN 等）と相性良好 |
| **ロギング** | **Serilog → Application Insights** | 構造化ログ標準。ERROR_HANDLING_STANDARDS の構造化ログ要件に直結 |
| **マッピング** | **Mapster** または手書き DTO | AutoMapper はリフレクション過多のため避ける。Mapster は AOT 親和性も高い |
| **Excel 出力（O-06）** | **ClosedXML** | テンプレート流し込みで 50明細 5秒以内（NFR §1.1）達成可能 |

### 3.3 データベース・ストレージ

| 項目 | 選定 | 理由 |
|------|------|------|
| **RDB（業務マスタ・トランザクション）** | **Azure SQL Database（General Purpose, Serverless 可）** | .NET/EF Core ネイティブ親和性、TDE 標準有効、マネージドで自動バックアップ・PITR、データ量 5年で SKU 2万・発注 5,000 件 → 最小プランで十分 |
| **データベース論理設計の起点** | 第3正規形を基準（@`.ai-native/methodology` データ設計原則）| 17マスタ + 2層商品モデル + マルチ仕入先（Phase 2 確定）。複合キー回避、サロゲートキー採用 |
| **画像ストレージ** | **Azure Blob Storage（Hot Tier）** | 5GB・読み取り中心。CDN（Front Door）併用で配信高速化。SAS URL で時限アクセス制御 |
| **監査ログ保管** | **Azure SQL（直近 3 ヶ月）+ Blob Storage Cool Tier（3 年アーカイブ）** | SEC-16 = 3 年保管。INSERT 専用テーブルとし、3 ヶ月超は Cool Tier に圧縮アーカイブ |
| **キャッシュ** | MVP では不要（1-2名利用） | Phase 7 で性能課題が出れば Redis Cache 追加検討 |
| **検索** | RDB の LIKE / GIN インデックス相当（Azure SQL の Full-text Search）| 全文検索専用エンジン（Elastic 等）は不要 |

### 3.4 インフラ・ホスティング

| 項目 | 選定 | 理由 |
|------|------|------|
| **クラウドプロバイダ** | **Microsoft Azure（Japan East リージョン）** | .NET 一級サポート、Azure SQL/Blob/Key Vault/Application Insights の統合が最小運用負荷で済む |
| **フロント配信** | **Azure Static Web Apps** | Nuxt SPA ビルドを GitHub Actions から直接デプロイ。HTTPS 自動付与、PR プレビュー機能あり |
| **バックエンド実行基盤** | **Azure App Service（Linux, P0V3 or B2）** | コンテナ管理不要、ゼロダウンタイムデプロイ、可観測性統合。MVP 規模なら B2（約 ¥10K/月）で十分 |
| **シークレット管理** | **Azure Key Vault** | 接続文字列・JWT 鍵・Blob SAS 署名鍵を一元管理。アプリは Managed Identity でアクセス（接続文字列のハードコード回避）|
| **可観測性** | **Application Insights + Azure Monitor** | アプリログ・分散トレース・依存呼び出しの可視化。アラートは Email/Teams 通知 |
| **CDN（任意）** | 当面不要、必要時に **Azure Front Door**（Standard）追加 | 1-2名利用では CDN なしでも十分。海外展開時に再評価 |
| **DR/バックアップ** | Azure SQL の **自動 PITR（35日）+ 週次フルバックアップ Geo-Redundant Storage** | NFR §5: RTO 4時間 / RPO 24時間 を満たす |

> **代替案メモ（AWS）:** App Runner + RDS for SQL Server / Aurora PostgreSQL + S3 + Secrets Manager + CloudWatch でも同等構成。.NET 親和性は Azure に劣るが、社内に AWS 運用知見が厚い場合は再検討。**MVP は Azure を採用、Phase 7 リリース判定前に最終確認**。

### 3.5 認証・認可

| 項目 | 選定 | 理由 |
|------|------|------|
| **認証方式** | **ASP.NET Core Identity（ID/パスワード）** | SEC-02 確定。bcrypt または Identity 標準の PBKDF2 でハッシュ化（SEC-04）|
| **セッション管理** | **JWT（HttpOnly Secure Cookie 格納）+ サーバ側 refresh token** | XSS リスクを下げつつ 8 時間タイムアウト（SEC-05）を実装。CSRF は SameSite=Strict + AntiforgeryToken（SEC-07）|
| **認可** | **ASP.NET Core Authorization Policies + Claims** | 4 権限カテゴリ × レベル（C-02）をポリシーで宣言。SEC-11 = サーバサイドで全 API 検証 |
| **削除済ユーザ** | Identity の `IsActive` フラグで判定、ログイン段階でリジェクト（AUTH-003）| SEC-12 整合 |
| **ブルートフォース対策** | Identity の `LockoutOnFailure=true`, `MaxFailedAccessAttempts=5` | SEC-06 整合 |
| **SSO** | MVP 対象外、Post-MVP で Entra ID / Azure AD B2B を検討 | SEC-02 ノート整合 |

### 3.6 CI/CD

| 項目 | 選定 | 理由 |
|------|------|------|
| **CI/CD プラットフォーム** | **GitHub Actions** | 既存リポジトリが GitHub。Azure 連携アクション（azure/login, azure/webapps-deploy 等）が公式提供 |
| **パイプライン構成** | `lint → unit test → build → deploy preview → deploy prod` | プレビュー環境は Static Web Apps の PR 機能を活用 |
| **コード品質** | **dotnet test（xUnit）+ Vitest（Nuxt）+ ESLint/Prettier + dotnet format** | カバレッジ 70%（NFR §7）を CI で計測 |
| **脆弱性スキャン** | **GitHub Dependabot + dotnet list package --vulnerable + Trivy（コンテナ層）** | SEC-19 整合 |
| **シークレット** | GitHub Environments + Azure OIDC 連携（Service Principal 不要） | キー管理レス、長期トークン漏洩リスク低減 |
| **DB マイグレーション** | EF Core Migration を CI から `dotnet ef database update` で適用 | 環境別実行、ロールバック手順を Phase 5 で文書化 |

---

## 4. 非機能要件との整合性確認

| 非機能要件 | 値 | 充足方式 | 整合 |
|------------|----|----|---|
| 同時利用 1-2名 / ピーク 5名 | NFR §3 | App Service B2 + Azure SQL Serverless で十分 | ✅ |
| 一覧初期表示 500ms（95%ile）| NFR §1.1 | EF Core でクエリ最適化 + Azure SQL のクエリプラン管理、ページング前提 | ✅ |
| 詳細・設定系初期表示 200ms | NFR §1.1 | 単純な単票取得は十分達成可能 | ✅ |
| Excel 出力 5秒以内 | NFR §1.1 | ClosedXML テンプレート + 非同期処理（必要なら）| ✅ |
| 画像アップ 5秒以内（5MB）| NFR §1.1 | Blob Storage 直接 PUT（SAS URL）+ サムネ生成は Azure Functions で非同期 | ✅ |
| HTTPS 必須（SEC-01） | NFR §2 | Static Web Apps / App Service 標準で HTTPS 強制 | ✅ |
| パスワードハッシュ（SEC-04）| NFR §2 | Identity 標準（PBKDF2-SHA256, 100,000 iterations）| ✅ |
| 監査ログ改竄防止（SEC-17）| NFR §2 | Azure SQL の append-only テーブル + RBAC で UPDATE/DELETE 拒否 | ✅ |
| 業務時間 99% SLA / RTO 4h / RPO 24h | NFR §5 | App Service SLA 99.95%、Azure SQL PITR 自動、Geo-Redundant Backup | ✅ |
| 仕入単価=中-高機密度（暗号化）| NFR §6.2 | TDE 標準 + 通信は TLS。**Always Encrypted は MVP 範囲外**（運用コスト過大、Phase 7 で再評価）| ⚠️ |
| 営業秘密の監査（不競法）| NFR §6.3 | アクセス制御 + 監査ログ + Application Insights でアクセス追跡 | ✅ |
| データ国内保管 | NFR §4.2 | Azure Japan East 単一リージョン、Geo-Redundant も国内ペアリージョン（JapanWest）| ✅ |

> **⚠️ 留意点:** 仕入単価の「中-高」機密度に対し、Always Encrypted（カラム単位暗号化、サーバでも復号不可）は MVP では運用負荷過大のため見送り。TDE（保存時暗号化）+ TLS（通信時）+ アクセス制御 + 監査ログで対応する。Phase 7 でリリース判定時に再評価する。

---

## 5. データ機密度との整合性

| データ種別 | 機密度 | 配置 | 暗号化 | アクセス制御 |
|---|---|---|---|---|
| 仕入単価 | 中-高 | Azure SQL | TDE（保存時）+ TLS 1.2+（通信時） | 4 権限ポリシー + 監査ログ |
| 商品マスタ・発注書 | 中 | Azure SQL | TDE + TLS | 4 権限ポリシー + 監査ログ |
| 取引先・仕入先 | 中 | Azure SQL | TDE + TLS | 4 権限ポリシー |
| ユーザマスタ | 軽微 | Azure SQL | TDE + TLS | Identity 標準保護 |
| 商品画像 | 低-中 | Blob Storage | Storage Service Encryption（標準有効）+ SAS URL 時限アクセス | RBAC + Container 単位制御 |
| 監査ログ | 中 | Azure SQL → Blob Cool Tier | TDE + 不変ストレージ（Immutable Blob）| RBAC + 改竄防止設計 |
| シークレット | 高 | Azure Key Vault | HSM-backed | Managed Identity 限定 |

---

## 6. 並行稼働戦略（AS-4）の技術影響

旧3システム（生産管理・販売管理・受発注）との並行稼働方針が技術設計に与える要件:

| # | 要件 | 対応 |
|---|------|------|
| MIG-1 | 初期データ移行：旧システム → 新システム | CSV / Excel インポート機能を Phase 5 設計に含める（マスタ系優先）|
| MIG-2 | 並行期間中の双方向同期 | **手動運用を前提**。自動同期はスコープ外。Phase 5 で運用手順書化 |
| MIG-3 | 旧システム由来データの ID 整合性 | 旧 ID を保持するための外部キー（`legacy_id`）を主要テーブルに NULL 許容で追加 |
| MIG-4 | 段階的機能切替 | 機能フラグで「旧運用」「新運用」を切替可能にする（Feature Toggle）|
| MIG-5 | カットオーバー時のデータ整合性検証 | リリース判定（Phase 7）で diff 検証スクリプトを準備 |
| MIG-6 | 並行期間の業務オペレータ負荷 | 二重入力期間が発生することをオペレーターに明示。期間は Phase 6 で確定 |

---

## 7. 採用しなかった選択肢（記録）

| 選択肢 | 不採用理由 |
|--------|----------|
| **AWS（Tokyo）** | 技術的には同等可能だが、.NET 親和性で Azure に一歩劣り、運用統合（App Service + SQL + Insights）の単純さで Azure 優位。社内に AWS 運用知見が厚いなら Phase 7 前に再評価可能 |
| **オンプレ / 社内サーバ** | 1-2名体制でバックアップ・冗長化・OS パッチを自前運用するのは負荷過大（AS-3 矛盾）|
| **Nuxt SSR モード** | 業務 LAN 内・SEO 不要・1-2名利用で SSR の利点なし。SSR 起因のバグ（CLAUDE.md Nuxt 注意点）も回避 |
| **Node.js / Express バックエンド** | チームスキル AS-2 と整合せず、.NET の型安全性・LINQ・EF Core の生産性に劣る |
| **NoSQL（Cosmos DB / Firestore）** | 業務データが関係的（17マスタ + 2層商品 + マルチ仕入先 + 発注明細）で RDB 適合。Firestore は CLAUDE.md でセキュリティルール注意点ありリスクも |
| **GraphQL API** | 機能要件 21 機能の REST 設計で過剰スペック。クライアント 1 種類（Nuxt）のみで GraphQL の柔軟性が活きない |
| **Always Encrypted（仕入単価）** | MVP では運用負荷過大。TDE + TLS + アクセス制御 + 監査ログで十分。Phase 7 で再評価 |
| **Redis Cache（MVP 導入）** | 1-2名同時利用では性能課題が出ない想定。Phase 7 で必要性を再評価 |
| **マイクロサービス分割** | 21機能の業務システムに対しモノリス構成で十分。1-2名運用でマイクロサービスは複雑性過剰 |
| **Kubernetes（AKS）** | App Service で十分。AKS は運用負荷が AS-3 と矛盾 |

---

## 8. リスクと留意点

| # | リスク | 影響度 | 緩和策 |
|---|--------|------|--------|
| R-1 | Azure 単一リージョン障害時のサービス停止 | 中 | Geo-Redundant Backup で復旧可能だが RTO 4 時間を超える可能性。マルチリージョン構成は MVP では見送り、Phase 7 で評価 |
| R-2 | Azure SQL Serverless の Cold start 遅延 | 低 | 業務時間中は常時アクティブ運用、Auto-pause 無効化で回避 |
| R-3 | 並行稼働期間の二重入力負荷 | 高 | MIG-6 でオペレーターと合意。CSV インポートで業務効率化 |
| R-4 | EF Core の N+1 クエリ問題（CLAUDE.md） | 中 | `Include` / `AsSplitQuery` のガイドラインを Phase 5 で文書化、Code Review チェック項目に追加 |
| R-5 | Nuxt SPA モードのバンドルサイズ肥大 | 低 | コード分割 + 動的 import で初期表示 500ms 維持 |
| R-6 | 4 権限ポリシーの実装漏れ（SEC-11） | 高 | 全 API エンドポイントに `[Authorize]` 必須化を CI Lint で強制、テストカバレッジで網羅性検証 |
| R-7 | 監査ログ INSERT 専用の運用ミス | 中 | DB ユーザ権限で UPDATE/DELETE を拒否、Migration 適用前に Code Review で確認 |

---

## 9. Phase 4 ゲート判定（事前自己評価）

| # | ゲート条件 | 状態 | 根拠 |
|---|-----------|------|------|
| 4-1 | 全レイヤーの技術スタックが確定 | ✅ PASS | §3.1〜3.6 でフロント・バック・DB/ストレージ・インフラ・認証認可・CI/CD を全選定 |
| 4-2 | 各選定に対して要件ベースの理由が説明可能 | ✅ PASS | §3 各表の「理由」列で要件・確定前提との対応を明示 |
| 4-3 | インフラ構成が非機能要件と整合 | ✅ PASS | §4 で 12 項目の非機能要件と充足方式を対応付け（1項目は MVP 範囲外の留意付きで合意要） |

第1層 3条件すべて PASS（自己評価）。第2層レビューはオペレーター確認時に実施。

---

## 10. レビュー観点（オペレーター向け）

以下の観点でレビューをお願いします。1つでも懸念があれば指摘してください。

1. **クラウドプロバイダ選定** §3.4: **Azure（Japan East）** で進める方針で良いか。AWS（Tokyo）に切り替える明確な要因（社内 AWS 運用知見の厚さ・既存契約等）はあるか
2. **フロントレンダリング方式** §3.1: **Nuxt SPA モード + Static Web Apps** で良いか。SSR が必要となる業務要件（SEO・初期表示最適化等）はないか
3. **DB 選定** §3.3: **Azure SQL Database** で良いか。PostgreSQL（Azure Database for PostgreSQL）に切り替える要因はあるか
4. **UI ライブラリ** §3.1: **Vuetify 3** を第一候補とするが、デザイン要件・既存社内 UI ガイドラインで縛りはあるか
5. **仕入単価の暗号化方針** §4: Always Encrypted を MVP 範囲外とし、TDE + アクセス制御 + 監査ログで対応する判断で良いか
6. **並行稼働の手動同期** §6（MIG-2）: 並行期間中の旧→新／新→旧の同期は **手動運用**を前提とする方針で良いか。自動同期スクリプトを MVP に含める要望はあるか
7. **CI/CD プラットフォーム** §3.6: **GitHub Actions** で良いか。Azure DevOps に切り替える要因はあるか
8. **Application Insights による監視** §3.4: 監視 SaaS として Application Insights を採用するが、社内既存の監視基盤（Datadog / New Relic 等）への統合要件はあるか
9. **採用しなかった選択肢** §7: 不採用理由に反論や追加で検討してほしい選択肢はあるか
10. **リスク R-1〜R-7** §8: 緩和策で不足する観点や、追加で評価したいリスクはあるか
