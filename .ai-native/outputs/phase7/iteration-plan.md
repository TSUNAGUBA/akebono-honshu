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

### Iteration 0: ローカル開発環境 (推奨期間: 1-2 週間)

> **目的 (オペレーター 2026-05-19 確定):** **ローカル PC のみで動作確認できる最小スタック** を整備。AWS インフラ・Firebase 本番接続は Iteration 4 Hardening 前で切替実施。
>
> **ゴール:** `docker-compose up` + `dotnet run` + `pnpm dev` でローカルにログイン → ダッシュボード → ユーザ一覧表示まで疎通

| 領域 | タスク | 完了基準 |
|---|---|---|
| **リポジトリ構造** | `src/Backend/` (.NET 8) + `src/Frontend/` (Nuxt 4) + `docker-compose.yml` + ルート README | 開発手順 README 完成、コマンド 3 本でローカル起動可 |
| **ローカル DB** | docker-compose で PostgreSQL 16 起動 (本番と同じバージョン)、ボリュームマウントで永続化 | `docker-compose up postgres` で起動、psql 接続可 |
| **バックエンド** | .NET 8 Minimal API スケルトン (Clean Architecture 4 層: Domain / Application / Infrastructure / Presentation) + EF Core 8 + **ダミー認証** | `dotnet run` で `http://localhost:5000` 起動、ヘルスチェック OK |
| **ダミー認証** | `POST /api/v1/auth/login` で固定 ID/PW を受け、JWT 風トークン (固定 user_id 含む) を返却。Iteration 4 Hardening で Firebase Admin SDK 接続に置換 | フロントから 200 OK + トークン受信 |
| **DB マイグレーション** | EF Core マイグレーション雛形、**最低限** users + audit_logs の 2 テーブルのみ。Seed データで 1 ユーザ (`owner` / 全権限) 投入 | `dotnet ef database update` 成功、psql で users 1 件確認 |
| **フロントエンド** | Nuxt 4 SSR スケルトン + Reka UI + Tailwind CSS。Firebase Web SDK は **未統合** (ダミー認証用に独自 fetch コンポーザブル) | `pnpm dev` で `http://localhost:3000` 起動 |
| **画面実装 (最小 2 画面)** | (a) ログイン画面 (ID/PW 入力 → POST /auth/login → トークン保存)、(b) ダッシュボード兼ユーザ一覧 (`GET /users` で users テーブルを表示) | フロントでログイン成功後、ユーザ一覧テーブル 1 件表示 |
| **audit_logs 動作確認** | ログイン成功 + ユーザ一覧表示時に audit_logs INSERT、psql で件数増加確認 | psql で `SELECT * FROM audit_logs;` で 2 件以上 |
| **README** | 開発手順 (前提ツール / clone から起動まで / トラブルシュート) | 新メンバーが 30 分以内で起動できる |
| **Iteration 0 で除外する項目 (Iteration 後半 or Hardening へ)** | AWS インフラ (App Runner / RDS / S3) / Firebase Emulator or 本番接続 / LocalStack S3 / CI/CD パイプライン / Terraform | – |

**ゲート:** ゴール (ログイン + ユーザ一覧表示 + audit_logs 記録) のローカル動作確認完了 + 独立コードレビュアー (Clean Architecture 4 層分離 / EF Core マイグレーション / Nuxt 構成 / 認証トークン取扱) + システム監査官 (ダミー認証から本番 Firebase 切替時の影響範囲 / Secret 管理) 指摘ゼロ

#### Iteration 0 完了記録 (2026-05-19)

**動作確認:** オペレーター環境 (Windows + Visual Studio + 既存ローカル PostgreSQL + pgAdmin4 + Volta) で `pgAdmin4 で akebono-honshu DB 作成 → Visual Studio で Backend デバッグ起動 → pnpm dev` の経路で **ログイン → ユーザ一覧表示まで疎通完了**。

**Iteration 0 で得た知見 (Iteration 1 以降に適用):**

| # | 知見 | Iteration 1 以降の適用方針 |
|---|---|---|
| 1 | class library (`Microsoft.NET.Sdk`) で `Microsoft.Extensions.*` を使う場合は `using` を明示する必要あり | コード生成時、`Microsoft.Extensions.Configuration` / `Microsoft.Extensions.DependencyInjection` 等を要する箇所は必ず using 列挙。レビュー時の確認項目に追加 |
| 2 | 新規 npm パッケージ追加時、バージョン指定を訓練データから推測すると `ERR_PNPM_NO_MATCHING_VERSION` を起こすケースあり (reka-ui 1.x 不存在問題) | 新規パッケージ追加前に WebFetch で npm レジストリの最新版を事前確認、CLAUDE.md「未知の問題は公式ドキュメントで裏取り」原則を新規 dep 追加にも拡張 |
| 3 | ローカル PostgreSQL を持つ Windows ユーザは docker 経由より既存 PostgreSQL + pgAdmin4 直接利用が早い | RUNBOOK で 2 つの選択肢 (A. 既存 PostgreSQL、B. docker) を併記、デフォルトはユーザ環境に依存 |
| 4 | `appsettings.json` のローカル編集は次回 git pull で衝突する | Iteration 1 で `appsettings.Development.json` + `dotnet user-secrets` の二段運用に正規化、Connection String の Username/Password は User Secrets に格納 |
| 5 | corepack 同梱が環境依存 (Volta 経由など)、pnpm インストール経路は複数想定が必要 | RUNBOOK 1.2 で 3 経路 (corepack / Volta / npm global) を併記 |
| 6 | DB 名・ロール名はリポジトリ名と一致させると認識しやすい | `akebono` → `akebono-honshu` に統一、Iteration 1 以降のテーブル定義はこの DB 内で展開 |

**Iteration 1 着手前に Claude 側で整備するタスク (完了 2026-05-19):**
- ✅ `appsettings.Development.json` にチーム共通の開発デフォルト Connection String を格納、`appsettings.json` は本番プレースホルダ (`__OVERRIDE_ME__`) に変更
- ✅ `dotnet user-secrets` 用に `Akebono.Api.csproj` に `<UserSecretsId>akebono-honshu-iter0-dev-secrets</UserSecretsId>` を追加、RUNBOOK §2.1 に個人認証情報の格納手順記載
- ✅ CLAUDE.md `.NET / C#` セクションに class library での `Microsoft.Extensions.*` 明示 using + NuGet バージョン事前確認 (WebFetch) のルール追記
- 🔁 パッケージ追加時の WebFetch 確認は CLAUDE.md ルール化済、Iteration 1 以降の運用で実証する



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

#### Iteration 1 完了記録 (2026-05-19)

**実装内容 (commit 範囲: `1b89fdc` → `5b5d3cc`):**
- 1.A: `db/init/02-masters.sql` で 17 マスタ + users 拡張 + Seed データ投入
- 1.B: Domain 層 Entity 17 個 + IMasterEntity / MasterEntityBase 共通契約 + User 拡張
- 1.C: AkebonoDbContext 拡張 + MasterService<T> ジェネリック共通 CRUD + SupplierService 個別
- 1.D: 17 マスタ REST エンドポイント (102 routes) + Swagger UI 導入 (Swashbuckle.AspNetCore 6.9.0 + Bearer 認証スキーマ)
- 1.E: Nuxt マスタ管理画面 (動的ルート `/masters/[master]` + MasterEditDialog 共通モーダル + 17 マスタスキーマ定義 + FK 自動取得)
- 1.F: C-02 権限制御最小実装 (`product_ledger_permission >= 1` をマスタ編集系に要求、Frontend で編集 UI 非表示)
- 1.G: RUNBOOK + iteration-plan 更新

**Iteration 1 で得た知見 (Iteration 2 以降に適用):**

| # | 知見 | Iteration 2 以降の適用方針 |
|---|---|---|
| 1 | Swashbuckle.AspNetCore v10 は Microsoft.OpenApi 2.x への breaking change で名前空間が変更され、訓練データ知識では追従困難 (CS0246 大量発生) | NuGet パッケージ追加時の WebFetch 確認は CLAUDE.md ルール通り実施しつつ、breaking change 含む メジャー version は **安定版へのダウングレード** を即決判断する。具体的に Swashbuckle は v6.9.0 で固定 |
| 2 | `Nuxt 3.21+` で `ssr: false` が `NUXT_VITE_NODE_OPTIONS.socketPath` エラーで起動失敗 | `ssr: false` 使用禁止、`app.vue` 全体を `<ClientOnly>` でラップして実質 CSR 化する方式を採用 (Iteration 4 Firebase Auth 移行時に正式化) |
| 3 | `localStorage` 認証 + SSR 有効 で hydration mismatch が発生し、リロード時にレイアウト破壊 | `middleware/auth.global.ts` 先頭で `if (import.meta.server) return` を必須化、認証チェックは CSR でのみ実行。`pages/index.vue` の `onMounted` ナビゲーションは middleware に移管 |
| 4 | Tailwind CSS の `content` 配列空 (`[]`) で `@nuxtjs/tailwindcss` モジュール任せにすると、新ファイル大量追加時に JIT が認識漏れする可能性 | `tailwind.config.ts` の `content` は **明示パス列挙** で安定化 (二重保護) |
| 5 | `EF Core ChangeTracker` レベルの `AuditLogInterceptor` は実装複雑度が高い (relation の追跡 / DTO mapping / 既存 audit との重複) | Iteration 1 では Service レベル `audit.LogAsync` で十分機能、Interceptor 化は Iteration 2 以降で対象拡大時に再検討 |
| 6 | 17 マスタ × 6 endpoint = 102 routes を 1 ファイルに集約すると保守性低下が懸念されたが、`MapBase<T>` / `MapSimple<T>` ヘルパー導入で重複が消えた | 18+ マスタ追加時 (Iteration 2 で商品関連) も同パターンで拡張可能 |
| 7 | Phase 5 §3.18 `users.product_ledger_permission` の値域 (0-3) は MVP では `>= 1` の 2 値判定で十分機能 | Iteration 2 以降の発注書作成権限 (0/1/2) も同様に「閾値以上」で簡素実装、細分化は実需要時 |
| 8 | C# `out parameter` は `async` メソッドで使用不可、`record` 戻り値で代替 | `MasterEditAuth` パターン (`ActorId` + `ErrorResult`) を権限チェック標準形として Iteration 2 以降の発注権限チェックでも踏襲 |

**Iteration 1 で運用継続される設計判断:**
- ダミー認証 (`DummyTokenService` HMAC) は維持、Iteration 4 で Firebase Auth に置換
- 監査ログは Service レベル `audit.LogAsync(entity_type.action 形式)` で記録
- マスタの 4 権限カテゴリのうち実装したのは品番台帳のみ、他 3 つは Iteration 2/3 で実需に応じて追加

**Iteration 2 着手前の整備事項 (なし):**
- Iteration 1 終了時点で Iteration 2 (商品マスタ) 着手の障壁はなし。`product_families` / `products` の FK 参照先 (brands / suppliers / countries / colors / sizes / materials 等) は全て投入済み

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

#### Iteration 2 完了記録 (2026-05-20)

**実装内容 (commit 範囲: `91b52c0` → `1698051`):**
- 2.A: `db/init/03-products.sql` で商品関連 4 テーブル (product_families / products / product_images / product_supplier_prices) + Seed (1 企画 6 SKU + 単価 1 件)
- 2.B: Domain (ProductFamily / Product / ProductImage / ProductSupplierPrice Entity + Sku Value Object で 11 桁品番組立)
- 2.C: AkebonoDbContext 拡張 + ProductFamilyService (バルク登録 1 トランザクション) + ProductSupplierPriceService (BR-04 履歴管理)
- 2.D: 商品 REST endpoint (バルク登録 / 一覧 / 詳細 / 更新 / 削除 / 単価追加 + 履歴 / 画像 CRUD) + Static Files で画像配信 (`wwwroot/uploads/product-images/`)
- 2.E: Frontend 商品マスタ画面 (一覧テーブル/カード切替 + ウィザード + 詳細・修正 + 画像管理)
- 2.E+: カード表示に代表画像追加 (Phase 5 `primary_image` 設計反映)、カードサイズ調整 (1:1 正方形 / lg 5 列)
- 2.F: 権限制御は Iteration 1.F の `CheckMasterEditAsync` を `ProductEndpoints` に適用 (独立タスクなし)
- 2.G: RUNBOOK + iteration-plan 更新

**MIG-3 関連 (Iteration 2 中の判断):**
既存生産管理システム CSV (1,288 SKU、138 列、SHIFT_JIS) の取込検討の結果、構造ギャップが大きく Iteration 4 MIG-3 へ送り。8 件の移行課題を §3 Iteration 4 セクションに記録。CSV 本体は機密情報含むため git 管理せず、オペレーター手元保管。

**Iteration 2 で得た知見 (Iteration 3 以降に適用):**

| # | 知見 | Iteration 3 以降の適用方針 |
|---|---|---|
| 1 | Phase 5 `primary_image` を Subquery `Where + OrderBy + Select.FirstOrDefault()` で取得すると N+1 を回避できる | 発注書一覧 (O-03) のように複数の集計 + 関連エンティティ参照がある場合も同パターンを踏襲 |
| 2 | バルク登録の sequence_no 自動採番は同一トランザクション内の `Max(int.Parse(seq_no)) + 1` で簡素実装可能 (UNIQUE 制約があるため衝突しても DB 側で防止) | 発注書 `order_no` 採番も同パターン (初回 Excel 出力時の `order_no` 採番、O-06 詳細) |
| 3 | C# Minimal API で `IFormFile` を扱う endpoint は `DisableAntiforgery()` 必須 (Bearer 認証は別途実施しているため CSRF 不要) | Excel 出力アップロード等の multipart endpoint でも同様 |
| 4 | EF Core 8 の `Database.BeginTransactionAsync()` は `IDbContextTransaction` を返却。`await using` で disposal 確実、`tx.CommitAsync()` / `RollbackAsync()` で明示 | 発注書編集の snapshot 凍結 (O-06) も同パターン |
| 5 | Tailwind `aspect-square` + `object-cover` で正方形画像カードがレスポンシブで安定 | 発注書一覧の出力済バッジ (F-10 解消) も同じ視覚パターンが応用可能 |
| 6 | 画像のローカル保存 (`wwwroot/uploads/...`) → DB `s3_key` 相対パス → Static Files 配信 の構成は、Iteration 4 で `s3_key` を実際の S3 key に変更するだけで切替可能な I/F 設計 | Iteration 4 で `IImageStorageService` 抽象を導入し、`LocalImageStorage` / `S3ImageStorage` で切替実装 |
| 7 | DTO の `decimal` `DateOnly` 型は Npgsql 8.0.x で透過マッピングされ、`numeric(12,2)` / `date` 型と整合 | 発注書の `unit_price_snapshot` / `delivery_date` も同型を採用 |

**Iteration 3 着手前の整備事項 (なし):**
発注書 (purchase_orders / purchase_order_lines) の FK 参照先 (product_families / products / suppliers / delivery_destinations / document_template_* / users) はすべて投入済み。

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

#### Iteration 3 完了記録 (2026-05-20、暫定完了)

**実装内容 (commit 範囲: `0dfbd00` → `19ab08f`):**
- 3.A: `db/init/04-orders.sql` で発注関連 3 テーブル (purchase_orders / purchase_order_lines / purchase_order_export_logs) + Seed (発注書 1 件、明細 3 件 合計 495,000 円)
- 3.B: Domain (PurchaseOrder / PurchaseOrderLine / PurchaseOrderExportLog Entity + OrderStatus / EditReason Enum)
- 3.C: ClosedXML 0.105.0 統合 (Akebono.Infrastructure) + AkebonoDbContext 拡張 (Subtotal を HasComputedColumnSql で DB GENERATED 列マッピング) + PurchaseOrderService + IPurchaseOrderExcelService 抽象 + 実装
- 3.D: Presentation 全 7 endpoint + Excel ダウンロード + EditReason 文字列 JSON 変換 + AuthEndpoints.CheckOrderEditAsync
- 3.E: Frontend (一覧/新規/詳細/編集/中止/Excel ダウンロード)、`📥 Excel ダウンロード` ボタンで Blob 取得 + a タグ download
- 3.E+: CORS で `Content-Disposition` ヘッダを expose 修正 (ファイル名抽出失敗対応)
- 3.F: 権限制御は 3.D で `CheckOrderEditAsync` を `OrderEndpoints` に適用済 (独立タスクなし)
- 3.G: RUNBOOK + iteration-plan 更新

**「暫定完了」の理由 (Iteration 4 で正式完了予定):**
- Excel テンプレートは仮テンプレ (ClosedXML 動的生成、TemplateVersion=`iter3-v1`)
- 本テンプレ (`templates/purchase-order-domestic.xlsx`、業務担当者提供) + セルマッピング版への置換は Iteration 4 Hardening でオペレーター検収と同時に実施
- それ以外の機能 (O-01〜O-07 ロジック、snapshot 凍結 F-22、order_no 採番、editReason 必須 F-16、status 2 値 F-10/F-11、中止後 Excel 出力 F-11、連絡文章テンプレ O-07) は **全件動作確認済**

**Iteration 3 で得た知見 (Iteration 4 以降に適用):**

| # | 知見 | Iteration 4 以降の適用方針 |
|---|---|---|
| 1 | EF Core 8 で DB の `GENERATED ALWAYS AS ... STORED` 計算列は `HasComputedColumnSql("...", stored: true)` + `ValueGeneratedOnAddOrUpdate()` でマッピング | Phase 5 §6.2 で計算列導入時 (例: 集計サマリ) は同パターン |
| 2 | C# `enum` (例: EditReason) を JSON で文字列としてやりとりするには `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` を `ConfigureHttpJsonOptions` で **グローバル登録**。属性指定は API スペック乱立を招く | 今後追加する Enum (例: Iter 4 通知種別) も同パターン |
| 3 | `Content-Disposition` ヘッダはブラウザの CORS で **simple response header に含まれない**。Frontend の `response.headers.get('content-disposition')` で取得するには Backend の CORS ポリシーに `WithExposedHeaders("Content-Disposition")` 明示が必須 | Iter 4 でファイルダウンロード追加時 (例: PDF レポート) も同設定。`Content-Length` / `X-Total-Count` も状況に応じ追加 |
| 4 | Phase 5 設計の「初回出力時に snapshot 凍結 + order_no 採番」を 1 トランザクションでまとめて実装。`BeginTransactionAsync` + try/catch/Rollback で整合性保証 | Iter 4 で snapshot 関連の追加 (例: 価格 snapshot の再計算) も同パターン |
| 5 | ClosedXML 動的生成 (`XLWorkbook` + `Cell().Value = ...`) は MVP 仮テンプレに最適。本テンプレ (`.xlsx` テンプレファイル + セルマッピング) は `wb.Worksheets.Add()` ではなく `new XLWorkbook(templatePath)` で読み込み + `ws.Cell("B5").Value = ...` パターンに切替可能 (I/F は同じ) | Iter 4 で本テンプレ版に置換時、`IPurchaseOrderExcelService` の実装クラスのみ差替 |
| 6 | F-22 帳票宛名「`<official_name>` 御中 `<supplier_code>`」は snapshot 凍結 (`supplier_official_name_snapshot` + `supplier_code_snapshot`) で過去発注書の表示変化を防止。Frontend 詳細画面で snapshot 表示すると凍結効果が視覚化される | 取引先名変更時の影響範囲テスト (Iter 4) で snapshot 動作を必ず検証 |
| 7 | 発注書 status 簡素化 (Phase 6 F-10/F-11): Active/Cancelled の 2 値 + Excel 出力は status と独立。中止後も Excel 出力可能、改訂概念廃止 | この簡素モデルは Phase 7 後半 (請求書、検収書) でも同じ「シンプル状態 + イベントログ」パターンを採用 |

**Iteration 4 着手前の整備事項 (なし):**
Iteration 4 (Hardening) のスコープ (本番認証 / AWS インフラ / CI/CD / MIG-3 / Excel 本テンプレ) はすべて MVP 機能完成後の作業。Iteration 3 完了時点で機能側のブロッカーなし。

---

### Iteration 4: Hardening + 本番化 (推奨期間: 2-3 週間)

> **目的:** 統合検証 + 性能調整 + **ダミー認証 → Firebase 本番接続切替** + **AWS インフラ構築 (App Runner / RDS / S3) + CI/CD パイプライン** + UAT 準備、Phase 7 ゲート通過

| 領域 | 内容 |
|---|---|
| **本番認証切替** | ダミー認証 (Iteration 0 で実装) を Firebase Auth + Custom Claims に置換。Iteration 1 の C-01/C-02 設計通り完成 |
| **AWS インフラ構築** | App Runner / RDS PostgreSQL 16 Multi-AZ / S3 / KMS / IAM をオペレーターと初期化、Terraform でコード化 |
| **CI/CD パイプライン** | GitHub Actions (lint / test / build / Docker image push / App Runner deploy)、main 自動デプロイ |
| **統合テスト** | UC-1〜UC-4 通しシナリオを E2E 自動化 |
| **性能調整** | Phase 3 NFR §1.1 各画面・処理の応答時間検証と最適化 (発注書 Excel 出力 5 秒以内、1 万件商品一覧 100ms 等) |
| **セキュリティ最終確認** | audit_logs 改竄防止 (DB ロール権限 + S3 Object Lock)、KMS 暗号化、IAM 最小権限の再確認 |
| **レスポンシブ最終確認** | CLAUDE.md 原則 8: 全画面でモバイル/タブレット/PC の表示確認 |
| **Excel テンプレート最終再現** | T-2 Step B (.xlsx 取得後の完全再現) 完了、業務担当者の印刷検収 |
| **UAT 準備** | UAT シナリオ作成、オペレーター + 業務担当者向けマニュアル |
| **Post-Phase6 実フィードバック反映** | Phase 6 §6.2 アジェンダで Post-Phase6 並行実施結果を本 Iteration で吸収 |
| **MIG-3 既存データ移行** | 既存生産管理システムの商品マスタ CSV (約 1,300 SKU、138 列、SHIFT_JIS) を Phase 5 設計の `product_families` / `products` / `product_supplier_prices` に取込。下記「Iteration 2 で判明した移行課題」を解消するマッピング設計が必須 |

**Iteration 2 で判明した既存データ移行課題 (2026-05-20、MIG-3 着手時に解決):**

| # | 課題 | 影響 | 暫定方針 |
|---|---|---|---|
| 1 | 旧品番 `FA2071F` (7 桁系) と 新 11 桁品番 (`NA1001A4010`) の体系違い | SKU 再採番が必要 | `products.legacy_id` に旧品番を保持、新品番は移行スクリプトで生成 |
| 2 | 旧カラーコード (11/30 等) と 新 `colors.code` (030/040/080/090) の不一致 | 直接 join 不可 | `legacy_code_mapping` 表 (旧→新) をオペレーター提供で作成 |
| 3 | 旧サイズ (M/L 文字列) と 新 `sizes.code` (001-005 数値) の不一致 | 直接 join 不可 | 同上、name 列での name 一致検索が可能 |
| 4 | 旧仕入先コード (411 等) と 新 `suppliers.code` (336/404/437) の不一致 | 直接 join 不可 | 同上 |
| 5 | 旧「商品分類 1〜20」(20 種類) と 新マスタ (brands / functions / product_groups / product_types 等) の対応不明 | 分類体系の再構造化が必要 | オペレーターと業務担当者で対応表作成 (Phase 6 関連) |
| 6 | 旧「部位 1〜10 + 素材 + 混率」(10 種) と 新 `materials` × 3 (甲皮/中底/底) の数差 | 統合ルールが必要 | 部位分類の集約方針をオペレーター確定 |
| 7 | 旧 単価種別 13 種 (税抜販売/購買/原価/上代/参考上代 + SKU 単位) と 新 `product_supplier_prices.unit_price` (1 種) | どの単価を使うか確定要 | 「税抜購買単価」または「原価単価」を採用候補 (要確認) |
| 8 | 機密性 — CSV に取引先名・単価情報が含まれる | git コミット禁止 | オペレーター手元保管、Iteration 4 開始時に取込ジョブの引数として提供 |

**ゲート:** 方法論 §Phase 7 完了ゲート 3 件 (機能完成 / コードレビュアー 7 視点 / システム監査官リリース OK) + オペレーターサインオフ

#### Iteration 4 進捗棚卸し (2026-05-20 時点)

> **位置付け:** Iteration 4 着手途中の中間棚卸し。MIG-3 (既存 CSV 取込) は実装完了、商品マスタ画面に対する「品番/他品番」UI 整備は暫定完了。
> **本番化計画:** 詳細手順は `iteration4-prod-migration-plan.md` (段階 A → B → C → D の段階的移行手順) を参照。

| カテゴリ | サブタスク | 状態 | コミット範囲 / 参照 | 残作業 |
|---|---|---|---|---|
| **MIG-3 既存データ移行** | 取込戦略ドキュメント | ✅ 完了 | `517fb84` / `docs/migration/mig-3-strategy.md` | – |
| **MIG-3 既存データ移行** | 取込スクリプト 4 種 (pre-patch / step-01 / step-02 / step-03) | ✅ 完了 | `b04bb13` / `db/migration/*.sql` | – |
| **MIG-3 既存データ移行** | 画面 1 操作完結 UI (Backend `LegacyImportService` + Frontend `/admin/legacy-import`) | ✅ 完了 | `536ee05` 〜 `34a3d5e` | – |
| **MIG-3 既存データ移行** | 取込後の品番/他品番 UI 整備 (暫定) | 🟡 暫定完了 | `445f7f2` 〜 `a1fe898` | 本実装 (`Sku9Digit` リネーム + 用語統一)、新規企画ウィザード対応 |
| **MIG-3 既存データ移行** | fallback supplier で取込まれた family の整合性パッチ | 未着手 | – | `products.legacy_id` 末尾 1 桁から factory_supplier_id 逆引き SQL パッチ |
| **MIG-3 既存データ移行** | 仮割当マスタの一括メンテナンス UI (商品分類 / brand / function / 素材 = Iter 2 課題 #5・#6) | 未着手 | – | `status=Draft` の family を Draft タブで一括修正できる画面追加 |
| **本番認証切替** | `ITokenService` / `IAuthService` 抽象化 | ✅ 完了 (Iter 0) | `src/Backend/Application/Auth/ITokenService.cs` | – |
| **本番認証切替** | `FirebaseAuthService` / Firebase Admin SDK 実装 | 未着手 | – | `DummyTokenService` → `FirebaseAuthService` 置換、JwtBearer + Firebase JWKS 検証、Custom Claims 同期 (シナリオ E) |
| **本番認証切替** | Frontend Firebase JS SDK 統合 | 未着手 | 現状 `localStorage` + ダミートークン | `plugins/firebase.client.ts` + `composables/useAuth.ts` の Firebase 化 (Iter 1 知見 #2/#3 で `<ClientOnly>` 化 + middleware SSR skip 必須) |
| **AWS インフラ構築** | App Runner (Backend ホスティング) | 未着手 | – | Terraform 雛形作成 + ECR + デプロイ |
| **AWS インフラ構築** | RDS PostgreSQL 16 Multi-AZ | 未着手 (既存 RDS 再利用可否ヒアリング待ち) | – | 既存 RDS のバージョン確認、再利用 / 新規作成判断 |
| **AWS インフラ構築** | S3 (商品画像 + 監査ログアーカイブ) | 未着手 (既存 S3 再利用可否ヒアリング待ち) | – | 用途別バケット作成、SSE-S3 / Object Lock 設定 |
| **AWS インフラ構築** | KMS + Secrets Manager (DB 接続文字列、Firebase SA 鍵) | 未着手 | – | CMK 作成、Secret 投入 |
| **AWS インフラ構築** | CloudWatch Logs / Metrics / Alarms + SNS | 未着手 | – | Serilog 出力先設定、アラーム閾値設計 |
| **画像ストレージ抽象化** | `IImageStorageService` 抽象 + `LocalImageStorage` / `S3ImageStorage` | 未着手 (Iter 2 知見 #6 で先送り) | 現状 `wwwroot/uploads/...` 直書き | DI 切替で本番 = S3 / ローカル = ファイル |
| **CI/CD パイプライン** | GitHub Actions: lint / test / build | 未着手 (PR チェック workflow は存在) | `.github/workflows/pr-checks.yml` 等 | Backend `dotnet test` / Frontend `pnpm typecheck` / Docker build 追加 |
| **CI/CD パイプライン** | Backend → ECR Push → App Runner Deploy | 未着手 | – | AWS OIDC + ECR push + App Runner update |
| **CI/CD パイプライン** | Frontend → Firebase Hosting Deploy | 未着手 | – | `firebase deploy` workflow |
| **統合テスト E2E** | UC-1〜UC-4 通しシナリオ | 未着手 | – | Playwright or Cypress 採用判断、シナリオ実装 |
| **性能調整** | NFR §1.1 応答時間検証 | 未着手 | – | 大量データ投入後の計測、必要に応じて Index 追加 |
| **セキュリティ最終確認** | audit_logs INSERT 専用権限 + S3 Object Lock | 未着手 | – | DB ロール権限 REVOKE、S3 Object Lock 設定 |
| **セキュリティ最終確認** | IAM 最小権限再確認 | 未着手 | – | App Runner / GitHub Actions の IAM Role 最小化 |
| **レスポンシブ最終確認** | 全画面のモバイル/タブレット/PC 表示確認 | 未着手 | – | CLAUDE.md 原則 8 (UI レスポンシブ) チェックリスト |
| **Excel 本テンプレ実装** | `templates/purchase-order-domestic.xlsx` セルマッピング | 未着手 (テンプレファイル待ち) | 現状: ClosedXML 動的生成 (iter3-v1 仮テンプレ) | `IPurchaseOrderExcelService` 実装差替、印刷検収 |
| **UAT 準備** | UAT シナリオ + 業務担当者マニュアル | 未着手 | – | UC-1〜UC-4 ベースでシナリオ作成 |
| **Post-Phase6 フィードバック反映** | Phase 6 §6.2 アジェンダ吸収 | 未着手 | – | オペレーターセッション実施後 |

**進捗概況:** MVP 機能 (Iter 1〜3 + MIG-3) は完成。残るは **本番化** (認証切替 + AWS インフラ + CI/CD) + **品質ハードニング** (E2E / 性能 / セキュリティ) + **検収準備** (Excel 本テンプレ / UAT)。「本番化」は段階的移行手順 (`iteration4-prod-migration-plan.md`) に沿って進める。

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

| Iteration | 期間 | 機能数 | 状態 | 内容 |
|---|---|---|---|---|
| Iteration 0 | 1-2 週間 | – | ✅ 完了 (2026-05-19) | **ローカル開発環境のみ** (docker-compose / .NET 8 + Nuxt 4 スケルトン / ダミー認証 / ログイン + ユーザ一覧 1 画面表示) |
| Iteration 1 | 2-3 週間 | 8 機能 | ✅ 完了 (2026-05-19) | C-01〜C-03 + M-01〜M-05 (ローカルダミー認証で動作)。C-02 は品番台帳権限のみ実装、他 3 権限は Iteration 2/3 で実需対応 |
| Iteration 2 | 2-3 週間 | 6 機能 | ✅ 完了 (2026-05-20) | P-01〜P-06 (ローカルダミー認証で動作)。画像管理はローカルファイル保存、Iter 4 で S3 移行。MIG-3 既存 CSV 取込は Iter 4 へ申し送り |
| Iteration 3 | 3 週間 | 7 機能 | ✅ 暫定完了 (2026-05-20) | O-01〜O-07 (Excel 出力含む、ローカルダミー認証で動作)。Excel テンプレは仮 (iter3-v1)、本テンプレ + セルマッピングは Iter 4 で正式完了 |
| Iteration 4 | 2-3 週間 | – | 次 | Hardening + **AWS インフラ構築 + Firebase 本番認証切替** + CI/CD + UAT 準備 + MIG-3 + Excel 本テンプレ |
| **合計** | **約 10-14 週間 (約 2.5-3.5 ヶ月)** | **21 機能** | | MVP リリース |

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
