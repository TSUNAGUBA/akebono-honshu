# Phase 4 Index（手動作成）

## ドキュメント一覧

| ファイル | 最終更新 | 概要 |
|---------|---------|------|
| tech-stack-decision.md | 2026-05-19 | 技術スタック確定版（レビュー10件全クローズ）。6レイヤー × 選定理由・非機能整合・並行稼働影響・採用しなかった選択肢・リスク9件 |

## Phase 状態

- **現在ステータス:** COMPLETED（レビュー10件全クローズ、確定）
- **ゲート判定:** 3条件すべて PASS（オペレーターレビュー反映済）
  - 4-1 全レイヤーのスタック確定 ✅
  - 4-2 各選定の要件ベース理由 ✅
  - 4-3 インフラ構成と非機能要件の整合 ✅（仕入単価暗号化は Phase 5 で再評価合意）
- **次フェーズ:** Phase 5（基本設計 + プロトタイプ開発）

## 確定前提（オペレーターレビューで決定）

| # | 前提 | 値 |
|---|------|----|
| AS-1 | インフラ大方針 | 国内パブリッククラウド |
| AS-2 | 既存運用スキル | Vue/Nuxt 系 + .NET/C# 系 |
| AS-3 | 開発・運用体制 | 社内エンジニア 1-2名（小規模） |
| AS-4 | 旧3システム移行戦略 | 並行稼働（#6 で完全手動同期） |

## 選定サマリ

| レイヤー | 選定 |
|---------|------|
| フロント | Nuxt 3 SPA（Vue 3 + TS, `nuxt generate`）/ TailwindCSS + Reka UI + lucide-icons / Pinia |
| バック | .NET 8 + ASP.NET Core Web API / EF Core 8 + Npgsql / FluentValidation / Serilog / ClosedXML |
| DB / ストレージ | Amazon RDS for PostgreSQL 16 Multi-AZ / Amazon S3（画像5GB・監査ログ3年アーカイブ Glacier IR） |
| インフラ | AWS Tokyo (`ap-northeast-1`) / S3+CloudFront（フロント）/ App Runner（バック）/ Secrets Manager + KMS / CloudWatch + X-Ray |
| 認証認可 | ASP.NET Core Identity（ID/Password）+ JWT HttpOnly Cookie / Authorization Policies（4権限） |
| CI/CD | GitHub Actions + AWS OIDC / Dependabot / Trivy（Phase 5 試作後に切替再評価可） |

## オペレーターレビュー結果（10件全クローズ）

| # | 観点 | 決定 |
|---|------|------|
| 1 | クラウド | AWS Tokyo（当初推奨の Azure から変更） |
| 2 | レンダリング | Nuxt 3 SPA モード |
| 3 | DB | RDS for PostgreSQL 16 Multi-AZ |
| 4 | UI | TailwindCSS + Reka UI + lucide-icons |
| 5 | 仕入単価暗号化 | A. KMS 保存時暗号化（Phase 5 で再評価）|
| 6 | 並行稼働同期 | A. 完全手動 |
| 7 | CI/CD | GitHub Actions + AWS OIDC（見直し可）|
| 8 | 監視 | CloudWatch + X-Ray（見直し可、不要判断もあり得る）|
| 9 | 不採用リスト | 追加・見直し不要 |
| 10 | リスク | R-2 削除 + R-8/9/10 追加で計9件 |

## 主要キーワード

Nuxt3 SPA, Vue3, TailwindCSS, Reka UI, Headless UI Vue, lucide-icons, Pinia, .NET8, ASP.NET Core Web API, EF Core 8, Npgsql, FluentValidation, Serilog, ClosedXML, AWS Tokyo, ap-northeast-1, S3, CloudFront, App Runner, VPC コネクタ, RDS for PostgreSQL 16 Multi-AZ, S3 Glacier IR (監査ログ3年), AWS Secrets Manager, KMS, CloudWatch, X-Ray, SNS アラート, ASP.NET Core Identity, JWT HttpOnly Cookie, Authorization Policies, 4権限ポリシー, GitHub Actions, AWS OIDC, EF Core Migration, Dependabot, Trivy, KMS保存時暗号化(Phase5再評価), 完全手動同期, legacy_id, Feature Toggle, リスク9件(R-2削除/R-8/9/10追加)
