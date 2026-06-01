# Phase 4 Index（手動作成）

## ドキュメント一覧

| ファイル | 最終更新 | 概要 |
|---------|---------|------|
| tech-stack-decision.md | 2026-05-19 | 技術スタック確定版（レビュー11件全クローズ、#11 で Firebase Auth + Firebase Hosting に変更反映済）。6レイヤー × 選定理由・非機能整合・並行稼働影響・採用しなかった選択肢・リスク14件 |

## Phase 状態

- **現在ステータス:** COMPLETED（レビュー11件全クローズ、確定）
- **ゲート判定:** 3条件すべて PASS（オペレーターレビュー反映済）
  - 4-1 全レイヤーのスタック確定 ✅
  - 4-2 各選定の要件ベース理由 ✅
  - 4-3 インフラ構成と非機能要件の整合 ✅（仕入単価暗号化は Phase 5 で再評価合意、データ国内保管は Firebase Auth 採用による部分矛盾を許容合意）
- **次フェーズ:** Phase 5（基本設計 + プロトタイプ開発）

## 確定前提（オペレーターレビューで決定）

| # | 前提 | 値 |
|---|------|----|
| AS-1 | インフラ大方針 | 国内パブリッククラウド（AWS Tokyo）+ Firebase（Auth/Hosting） |
| AS-2 | 既存運用スキル | Vue/Nuxt 系 + .NET/C# 系 |
| AS-3 | 開発・運用体制 | 社内エンジニア 1-2名（小規模） |
| AS-4 | 旧3システム移行戦略 | 並行稼働（#6 で完全手動同期） |

## 選定サマリ

| レイヤー | 選定 |
|---------|------|
| フロント | Nuxt 3 SPA（Vue 3 + TS, `nuxt generate`）/ TailwindCSS + Reka UI + lucide-icons / Pinia / Firebase JS SDK（Auth） |
| バック | .NET 8 + ASP.NET Core Web API / EF Core 8 + Npgsql / FluentValidation / Serilog / ClosedXML / Firebase Admin SDK for .NET（ID Token 検証 + Custom Claims）|
| DB / ストレージ | Amazon RDS for PostgreSQL 16 Multi-AZ / Amazon S3（画像5GB・監査ログ3年アーカイブ Glacier IR）|
| **認証認可** | **Firebase Authentication（Email/Password）+ ID Token + Custom Claims**。**RDS users = 業務情報・権限 SoT、Firebase Auth = ID/Email SoT、Custom Claims = 権限キャッシュ**。ASP.NET Core Authorization Policies で 4 権限評価 |
| **フロント配信** | **Firebase Hosting**（一本化、CDN + プレビューチャネル）|
| バック実行基盤 | AWS App Runner（VPC コネクタで RDS 接続、CORS で Firebase Hosting ドメイン許可）|
| シークレット | AWS Secrets Manager + KMS（Firebase サービスアカウント鍵も含む）|
| 可観測性 | AWS CloudWatch + X-Ray（API 側）+ Firebase Console（Hosting/Auth 側）+ SNS アラート |
| CI/CD | GitHub Actions + AWS OIDC + Firebase CLI（Hosting デプロイ）|

## オペレーターレビュー結果（11件全クローズ）

| # | 観点 | 決定 |
|---|------|------|
| 1 | クラウド | AWS Tokyo + Firebase ハイブリッド |
| 2 | レンダリング | Nuxt 3 SPA モード |
| 3 | DB | RDS for PostgreSQL 16 Multi-AZ |
| 4 | UI | TailwindCSS + Reka UI + lucide-icons |
| 5 | 仕入単価暗号化 | A. KMS 保存時暗号化（Phase 5 で再評価）|
| 6 | 並行稼働同期 | A. 完全手動 |
| 7 | CI/CD | GitHub Actions + AWS OIDC + Firebase CLI |
| 8 | 監視 | CloudWatch + X-Ray + Firebase Console |
| 9 | 不採用リスト | #11 反映で追記 |
| 10 | リスク | R-2 削除 + R-8〜R-15 追加（計14件）|
| **11** | **認証 / フロント配信** | **Firebase Authentication + Firebase Hosting**（NFR §4.2 部分矛盾は許容）|

## 主要キーワード

Nuxt3 SPA, Vue3, TailwindCSS, Reka UI, Headless UI Vue, lucide-icons, Pinia, Firebase JS SDK, .NET8, ASP.NET Core Web API, EF Core 8, Npgsql, FluentValidation, Serilog, ClosedXML, Firebase Admin SDK for .NET, JwtBearer + Firebase JWKS, Custom Claims, AWS Tokyo, ap-northeast-1, App Runner, VPC コネクタ, CORS Firebase Hosting, RDS for PostgreSQL 16 Multi-AZ, S3 Glacier IR (監査ログ3年), AWS Secrets Manager, KMS, CloudWatch, X-Ray, Firebase Console, SNS アラート, Firebase Authentication, Email/Password, scrypt, ID Token (1h), Refresh Token, RDS users SoT + Firebase Custom Claims キャッシュ, setCustomUserClaims, 4権限ポリシー, Firebase Hosting, プレビューチャネル, firebase.json, GitHub Actions, AWS OIDC, Firebase CLI, EF Core Migration, Dependabot, Trivy, KMS保存時暗号化(Phase5再評価), 完全手動同期, legacy_id, Feature Toggle, NFR §4.2 部分矛盾許容, リスク14件(R-2削除/R-8〜R-15追加), Firebase Auth/RDS 同期(R-11), ベンダーロックイン(R-12), ユーザ識別情報海外配置(R-13), CORS/CSP(R-14), サービスアカウント鍵漏洩(R-15)
