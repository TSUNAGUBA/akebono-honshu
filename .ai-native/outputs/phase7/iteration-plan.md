# Phase 7 Iteration 計画

> **作成日:** 2026-05-19
> **対象:** Phase 7 (MVP 構築) の Iteration 分割と各 Iteration のスコープ・ゲート条件
> **依存:** Phase 3 機能要件 21 機能 / Phase 4 技術スタック / Phase 5 設計 (architecture / data-design / api-design / screen-design)
> **方針:** 方法論 §Phase 7「直近でユーザーが使用する機能に限定」+ Iteration ベース実装 + CLAUDE.md 原則 9 (改修後の反復レビュー指摘ゼロまで)

---

## 1. MVP 機能スコープ (Phase 3 §1 21 機能)

| カテゴリ | 機能数 | 機能 ID |
|---|---|---|
| 横串 (C) | 3 | C-01 (ログイン) / C-02 (権限制御) / C-03 (監査ログ) |
| マスタ (M) | 5 | M-01 (一覧) / M-02 (編集) / M-03 (ユーザ) / M-04 (仕入先) / M-05 (連絡文章) |
| 商品 (P) | 6 | P-01 (新規) / P-02 (サイズ展開) / P-03 (単価) / P-04 (一覧) / P-05 (詳細) / P-06 (画像) |
| 発注 (O) | 7 | O-01 (新規 from 企画) / O-02 (新規 from 品番) / O-03 (一覧) / O-04 (編集) / O-05 (中止) / O-06 (Excel 出力) / O-07 (連絡文章) |

---

## 2. 依存関係マップ

```
[Iteration 0: 基盤]
   ├─ インフラ (AWS App Runner / RDS / S3 / Firebase Auth)
   ├─ .NET 8 Minimal API スケルトン + EF Core 8 マイグレーション雛形
   ├─ Nuxt 4 SSR スケルトン + Reka UI セットアップ
   ├─ CI/CD パイプライン (lint / test / build / deploy)
   └─ docker-compose ローカル開発環境

       ↓ (基盤完成後)

[Iteration 1: 認証 + マスタ管理基盤]
   ├─ C-01 (Firebase Auth ログイン)
   ├─ C-02 (権限制御 Custom Claims)
   ├─ C-03 (監査ログ audit_logs テーブル)
   ├─ M-01 / M-02 (マスタ共通テンプレート CRUD)
   ├─ M-03 (ユーザマスタ)
   ├─ M-04 (仕入先マスタ、F-22 supplier.official_name / code 帳票準備)
   └─ M-05 (連絡文章マスタ)

       ↓ (マスタ完成後、商品/発注は FK 依存)

[Iteration 2: 商品マスタ]
   ├─ P-01 (商品新規登録ウィザード、F-06 単一バルク登録)
   ├─ P-02 (サイズ展開、F-21 廃止により画面ボタン)
   ├─ P-03 (マルチ仕入先単価)
   ├─ P-04 (一覧、カード/テーブル切替)
   ├─ P-05 (詳細・修正)
   └─ P-06 (画像管理、S3 連携)

       ↓ (商品 SKU 確定後)

[Iteration 3: 発注書 + Excel 出力]
   ├─ O-01 (新規 from 企画、全 SKU 自動転記)
   ├─ O-02 (新規 from 品番、F-14 色×サイズマトリクスダイアログ)
   ├─ O-03 (一覧、出力バッジ表示)
   ├─ O-04 (編集、F-16 編集理由必須化)
   ├─ O-05 (中止)
   ├─ O-07 (連絡文章テンプレ複写)
   └─ O-06 (Excel 出力、F-12 国内用テンプレ、F-22 supplier.official_name + 御中 + code、ClosedXML 流し込み)

       ↓ (機能完成後)

[Iteration 4: Hardening]
   ├─ 統合テスト (UC-1〜UC-4 通しのシナリオ検証)
   ├─ 性能調整 (NFR §1.1 各画面の応答時間)
   ├─ セキュリティ強化 (audit_logs 改竄防止、KMS 暗号化検証)
   ├─ レスポンシブ最終確認 (CLAUDE.md 原則 8)
   └─ ユーザ受け入れテスト (UAT) 準備
```

---

## 3. Iteration 別スコープ詳細

### Iteration 0: 基盤構築 (推奨期間: 1-2 週間)

> **目的:** Iteration 1 以降の機能実装を効率化する開発・運用基盤を整備

| 領域 | タスク | 完了基準 |
|---|---|---|
| **インフラ** | AWS App Runner / RDS PostgreSQL 16 Multi-AZ / S3 / Firebase Auth プロジェクト初期化 | `terraform plan` クリーン、各リソース起動確認 |
| **バックエンド** | .NET 8 Minimal API スケルトン (Clean Architecture 4 層) + EF Core 8 + AuditLogInterceptor 雛形 + Firebase Admin SDK 統合 | `dotnet build` 成功、Firebase ID Token 検証エンドポイント疎通 |
| **フロントエンド** | Nuxt 4 SSR スケルトン + Reka UI + Tailwind CSS + Firebase Web SDK | `pnpm dev` 起動、ログイン画面雛形表示 |
| **DB マイグレーション** | EF Core マイグレーション雛形、最低限の users / audit_logs テーブル定義 | `dotnet ef migrations add Init` 成功、RDS 適用確認 |
| **CI/CD** | GitHub Actions (lint / test / build / Docker image push / App Runner deploy) | main ブランチ push で自動デプロイ確認 |
| **ローカル開発** | docker-compose (PostgreSQL + LocalStack + Firebase Emulator) | `docker-compose up` で全サービス起動、E2E スモークテスト通過 |
| **Phase 7 事前タスク** | F-21 不要化により T-1 はスキップ、T-2 .xlsx 取得 (Iteration 1 開始 1 週間前まで、画像は受領済) | オペレーターから .xlsx 受領完了 |

**ゲート:** 全領域完了 + 独立コードレビュアー (基盤コードのセキュリティ・スケーラビリティ・保守性) 指摘ゼロ + システム監査官 (IAM 最小権限 / Secret 管理 / コスト見積) 指摘ゼロ

---

### Iteration 1: 認証 + マスタ管理基盤 (推奨期間: 2-3 週間)

> **目的:** ログイン + 権限制御 + 17 マスタの CRUD (共通テンプレート + 個別 3 マスタ) を完成、商品・発注の FK 参照先を準備

| 機能 ID | 内容 | 主要設計参照 |
|---|---|---|
| C-01 | Firebase Auth ログイン (Email/Password) + `auth/sync` + `auth/me` | api-design.md §2.1 |
| C-02 | RDS users.permissions + Firebase Custom Claims 同期 (シナリオ E、§Arch §4.5) | api-design.md §2.2, architecture.md §4.5 |
| C-03 | audit_logs テーブル + AuditLogInterceptor (全 C/U/D + 認証 + Excel 出力 + 単価閲覧) | data-design.md §6.1, api-design.md §1.7 |
| M-01 / M-02 | マスタ共通テンプレート CRUD (`MasterController<TEntity, TDto>` ジェネリック) + 17 リソース対応 | api-design.md §2.3, screen-design.md §3.11 |
| M-01 拡張 | F-18 FK ネスト返却 (suppliers.country / materials.material_classification) + F-20 usage API | api-design.md §2.3 (Phase 6 確定箇所) |
| M-03 | ユーザマスタ (7 UI 露出フィールド + 3 DB 保持) | api-design.md §2.2, screen-design.md §3.12 |
| M-04 | 仕入先マスタ (M-04 拡張カラム + 工場兼用 + **F-22 official_name 帳票印字準備**) | data-design.md §3.5, screen-design.md §3.11 拡張表 |
| M-05 | 連絡文章テンプレ 3 種 (document_template_purchases / confirmations / text_purchases) | data-design.md §3.15-3.17 |

**追加対応 (Phase 6 確定事項):**
- F-21: F-key 廃止、画面ボタンで統一 (Reka UI 標準キーバインド Esc/Tab/Enter/矢印のみ採用)
- F-22: suppliers.official_name = 「DEPARTURES」等の英字スペル可、業務マニュアル整備

**ゲート:** 全 5 マスタ機能完成 + 独立コードレビュアー (Clean Architecture 準拠 / EF Core N+1 検出 / Firebase 同期失敗時の Reconciler 動作) + システム監査官 (audit_logs 改竄防止 / RDS ロール権限 / Firebase Custom Claims 同期パス) 指摘ゼロ

---

### Iteration 2: 商品マスタ (推奨期間: 2-3 週間)

> **目的:** 商品マスタの CRUD + サイズ展開 + 単価管理 + 画像管理を完成、発注書 (Iteration 3) の SKU 参照先を準備

| 機能 ID | 内容 | 主要設計参照 / Phase 6 確定 |
|---|---|---|
| P-01 + P-02 + P-03 | 商品新規ウィザード (Step 1-4) + サイズ展開 + マルチ仕入先単価 を **単一バルク登録エンドポイント** (`POST /products/families/complete`) で実装 | api-design.md §2.4, **F-06 ロールバック対応済** |
| P-02 | サイズ展開 = 画面ボタン (F-21 廃止により F9 不採用) | screen-design.md §3.4 |
| P-03 | アイテム単位の仕入単価 (色違い・サイズ違いの SKU は同一単価)、effective_from/to で履歴管理 | data-design.md §3.10 |
| P-04 | 商品一覧 (カード/テーブル切替、F-04 Loading 適用) | screen-design.md §3.6 |
| P-05 | 商品詳細・修正 (`POST /families`, `POST /expand` 個別エンドポイントを編集用に活用) | api-design.md §2.4 |
| P-06 | 画像管理 (S3 Pre-signed URL、最大 5 枚/企画、最大 5MB/枚) | api-design.md §2.4, data-design.md §3.11 |

**ゲート:** 全 6 機能完成 + 独立コードレビュアー (バルク登録のトランザクション境界 / S3 整合性 / 画像順序管理) + システム監査官 (画像 S3 アクセス制御 / Pre-signed URL TTL / KMS 暗号化) 指摘ゼロ

---

### Iteration 3: 発注書 + Excel 出力 (推奨期間: 3 週間、MVP のクリティカルパス)

> **目的:** 発注書の作成・編集・出力を完成、ユーザの最頻使用機能を完全動作させる

| 機能 ID | 内容 | 主要設計参照 / Phase 6 確定 |
|---|---|---|
| O-01 | 新規 (企画から、全 SKU 自動転記) | api-design.md §2.5 |
| O-02 | 新規 (既存品番から) + **F-14 色×サイズマトリクス選択ダイアログ** + ORDER-006 重複チェック | screen-design.md §3.6, api-design.md §2.5 |
| O-03 | 一覧 (`first_exported_at` ベースの「未出力 / 初回出力済 (date)」バッジ、F-10 解消の出力バッジ) | screen-design.md §3.7 |
| O-04 | 編集 + **F-16 edit_reason 5 値 Enum 必須化 + edit_note 任意** + ORDER-005 | api-design.md §2.5, screen-design.md §3.8 編集保存ダイアログ |
| O-05 | 中止 (status=Cancelled、Excel 出力は中止後も可) | api-design.md §2.5 |
| O-07 | 連絡文章複写 + 標準印字取込 + 自由編集 | screen-design.md §3.7 |
| **O-06** | **Excel 出力 (MVP のクリティカルパス)** | – |
| O-06 詳細 | ① 国内用テンプレ `templates/purchase-order-domestic.xlsx` 固定 (F-12 確定) | api-design.md §2.5 |
| O-06 詳細 | 初回出力時のみ `order_no` 採番 + `first_exported_at` SET + **3 件 snapshot 一括凍結** (`supplier_official_name_snapshot` / `supplier_code_snapshot` / `customer_name_snapshot`、F-22 確定) | api-design.md §2.5, data-design.md §3.20 |
| O-06 詳細 | 帳票宛名「supplier_official_name 御中 supplier_code」(F-22 確定)、発注印スタンプは MVP 手押し運用 | api-design.md §2.5 帳票宛名印字方針 |

**追加対応:**
- F-15 audit_logs.changes JSONB に `{ before, after, edit_reason, edit_note }` 構造で記録 (Post-MVP UI 追加余地確保)

**ゲート:** 全 7 機能完成 + Excel 体裁が既存帳票と完全一致 (オペレーター検収) + 独立コードレビュアー (snapshot 凍結トランザクション / Idempotency-Key 二重採番防止 / ClosedXML 性能) + システム監査官 (audit_logs 改竄防止 / Excel ファイル一時保存セキュリティ) 指摘ゼロ

---

### Iteration 4: Hardening (推奨期間: 1-2 週間)

> **目的:** 統合検証 + 性能調整 + UAT 準備、Phase 7 ゲート (品質ゲート + 安全ゲート + オペレーター承認) 通過

| 領域 | 内容 |
|---|---|
| 統合テスト | UC-1〜UC-4 通しシナリオ (Phase 2 §2 各 UC) を E2E 自動化 |
| 性能調整 | Phase 3 NFR §1.1 各画面・処理の応答時間検証と最適化 (発注書 Excel 出力 5 秒以内、1 万件商品一覧 100ms 等) |
| セキュリティ最終確認 | audit_logs 改竄防止 (DB ロール権限 + S3 Object Lock)、KMS 暗号化、IAM 最小権限の再確認 |
| レスポンシブ最終確認 | CLAUDE.md 原則 8: 全画面でモバイル/タブレット/PC の表示確認 |
| Excel テンプレート最終再現 | T-2 Step B (.xlsx 取得後の完全再現) 完了、業務担当者の印刷検収 |
| UAT 準備 | UAT シナリオ作成、オペレーター + 業務担当者向けマニュアル |
| Post-Phase6 実フィードバック反映 | Phase 6 §6.2 アジェンダで Post-Phase6 並行実施結果を本 Iteration で吸収 |

**ゲート:** 方法論 §Phase 7 完了ゲート 3 件 (機能完成 / コードレビュアー 7 視点 / システム監査官リリース OK) + オペレーターサインオフ

---

## 4. 各 Iteration 共通の品質運用

CLAUDE.md 原則 9 / 方法論 SP-8 に従い、**各 Iteration 完了時に独立 2 ロール (コードレビュアー + システム監査官) の反復レビューを実施**し、指摘ゼロになるまで Iteration 内で対応する。

| 工程 | 担当 | 内容 |
|---|---|---|
| 実装 | coding-agent (Claude Code) | Iteration スコープのコード実装 |
| インクリメンタルレビュー | code-reviewer (独立サブエージェント) | 7 視点レビュー (データ設計 / I/F 設計 / コード品質 / セキュリティ / 性能 / テスト / 一貫性) |
| 安全ゲート | system-auditor (独立サブエージェント) | リリース OK 判定 (セキュリティ / リソースコスト / IAM / 可用性) |
| オペレーター承認 | オペレーター | サマリ確認、Iteration 完了承認 |
| 次 Iteration 着手 | – | 全ゲート通過後 |

---

## 5. Iteration 別スコープ集計

| Iteration | 期間 | 機能数 | 内容 |
|---|---|---|---|
| Iteration 0 | 1-2 週間 | – | 基盤 (インフラ / スタック / CI/CD / docker-compose) |
| Iteration 1 | 2-3 週間 | 8 機能 | C-01〜C-03 + M-01〜M-05 |
| Iteration 2 | 2-3 週間 | 6 機能 | P-01〜P-06 |
| Iteration 3 | 3 週間 | 7 機能 | O-01〜O-07 (Excel 出力含む) |
| Iteration 4 | 1-2 週間 | – | Hardening + UAT 準備 |
| **合計** | **約 9-13 週間 (約 2-3 ヶ月)** | **21 機能** | MVP リリース |

---

## 6. Post-MVP 計画 (Phase 6 で記録済の Later カテゴリ + 設計論点)

> Phase 7 MVP リリース後の追加開発で対応する項目。Phase 6 feedback-log.md §5.3 と整合。

| Later カテゴリ | 内容 | 再評価トリガ |
|---|---|---|
| F-01 | Email vs 社員 ID ログイン | 業務ヒアリング後判断 |
| F-02 | ホームから直接動線 | ホーム UI 充実時 |
| F-03 | Step 1 完了時の連番確定 | UX 細部調整 |
| F-04 | サイズ展開中の Loading 表示 | screen-design.md §5.1 で吸収済 |
| **F-07** | 発注ヘッダのデフォルト値 | MVP リリース後 4 週間 / 入力時間 60 秒超 / 苦情 3 件 |
| **F-15** | 編集時の差分ハイライト表示 | MVP リリース後 4 週間 / 編集月 50 件以上 / 問合せ 3 件 |
| F-19 | コード採番方式 | 業務ヒアリング結果 |
| **F-23** | 海外発注の複数倉庫分配 | Post-MVP で `purchase_order_line_warehouses` 紐付テーブル等の拡張 |
| **F-24** | 海外発注の分割出荷スケジュール | Post-MVP で `purchase_order_line_shipments` 子テーブル等の拡張 |

| Excel テンプレ | 内容 |
|---|---|
| ② 海外用 ORDER SHEET | Post-MVP で追加、F-23 複数倉庫分配と連動 |
| ③ 海外用+管理表 ORDER SHEET + ORDER DETAIL | Post-MVP で追加、F-24 分割出荷スケジュールと連動 |
| 海外用押印 3 枠 | Post-MVP で電子押印化検討時に枠数も論点 |

---

## 関連ドキュメント

- 方法論 Phase 7: `.ai-native/methodology/common/phase-definitions.md` §Phase 7
- Phase 5 設計: `.ai-native/outputs/phase5/{architecture,data-design,api-design,screen-design}.md`
- Phase 3 機能要件: `.ai-native/outputs/phase3/functional-requirements.md`
- Phase 7 事前タスク: `.ai-native/outputs/phase7/pre-iteration-tasks.md` (T-2 Excel サンプル調達)
- Phase 6 フィードバックログ: `.ai-native/outputs/phase6/feedback-log.md`
