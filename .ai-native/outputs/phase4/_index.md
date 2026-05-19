# Phase 4 Index（手動作成）

## ドキュメント一覧

| ファイル | 最終更新 | 概要 |
|---------|---------|------|
| tech-stack-decision.md | 2026-05-19 | 技術スタック確定ドラフト。6レイヤー × 選定理由・非機能整合・並行稼働影響・採用しなかった選択肢・リスク10件 |

## Phase 状態

- **現在ステータス:** IN_PROGRESS（ドラフト完了、オペレーターレビュー待ち）
- **ゲート判定（事前自己評価）:** 3条件すべて PASS
  - 4-1 全レイヤーのスタック確定 ✅
  - 4-2 各選定の要件ベース理由 ✅
  - 4-3 インフラ構成と非機能要件の整合 ✅（1項目 MVP 範囲外で合意要）
- **次フェーズ:** Phase 5（基本設計 + プロトタイプ開発）

## 確定前提（オペレーターレビューで決定）

| # | 前提 | 値 |
|---|------|----|
| AS-1 | インフラ大方針 | 国内パブリッククラウド |
| AS-2 | 既存運用スキル | Vue/Nuxt 系 + .NET/C# 系 |
| AS-3 | 開発・運用体制 | 社内エンジニア 1-2名（小規模） |
| AS-4 | 旧3システム移行戦略 | 並行稼働 |

## 選定サマリ

| レイヤー | 選定 |
|---------|------|
| フロント | Nuxt 3（Vue 3 + TS）SPA モード / Vuetify 3（第一候補）/ Pinia |
| バック | .NET 8 + ASP.NET Core Web API / EF Core 8 / FluentValidation / Serilog |
| DB / ストレージ | Azure SQL Database / Azure Blob Storage（画像・監査アーカイブ） |
| インフラ | Azure Japan East / Static Web Apps + App Service Linux / Key Vault / Application Insights |
| 認証認可 | ASP.NET Core Identity（ID/Password）+ JWT HttpOnly Cookie / Authorization Policies（4権限） |
| CI/CD | GitHub Actions + Azure OIDC / Dependabot / Trivy |

## レビュー観点（10項目）

`tech-stack-decision.md §10` 参照。クラウド選定・SPA方式・DB選定・UIライブラリ・暗号化方針・並行稼働同期・CI/CD・監視・不採用選択肢・リスク。

## 主要キーワード

Nuxt3 SPA, Vue3, Vuetify3, Pinia, .NET8, ASP.NET Core, EF Core 8, FluentValidation, Serilog, ClosedXML, Azure Japan East, Azure SQL Database, Azure Blob Storage, Azure Static Web Apps, App Service Linux, Azure Key Vault, Application Insights, ASP.NET Core Identity, JWT HttpOnly Cookie, Authorization Policies, 4権限ポリシー, GitHub Actions, Azure OIDC, EF Core Migration, Dependabot, Trivy, TDE保存時暗号化, 並行稼働(手動同期), legacy_id, Feature Toggle, リスク10件
