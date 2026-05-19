# Phase 5 成果物: データ設計

> **作成日:** 2026-05-19
> **状態:** ドラフト v1（オペレーターレビュー前）
> **依存:** Phase 3 機能要件（21機能、18マスタ）+ 業務ルール BR-01〜BR-11 + 11桁品番ルール
>          + Phase 4 確定スタック（RDS for PostgreSQL 16 + Firebase Auth + EF Core 8）
>          + `architecture.md` SoT 設計（RDS = 業務 SoT / Firebase Auth = 認証 SoT）
> **方針:** 第3正規形を基準。複合キー回避、サロゲートキー採用。論理削除（`delete_flag` / `is_*`）必須。
>          Phase 5 ゲート条件「データ設計が正規化の原則に従っている」を充足する。

---

## 1. 設計方針

### 1.1 原則（Phase 5 方法論準拠）

| # | 原則 | 適用 |
|---|---|---|
| DP-1 | 第3正規形を基準、非正規化は read/write コストの明確な根拠ある場合のみ | 全テーブル |
| DP-2 | テーブルごとに意味を持たないユニーク ID を PK、relation には ID を使用 | `id BIGSERIAL` を全テーブル PK（masters の `code` は業務 PK として残すが、内部 FK は `id` ベース）|
| DP-3 | 複合キーで強い制約を作らない | UNIQUE 制約・部分インデックスで代替 |
| DP-4 | マスタの静的/動的を判断、動的マスタは CRUD 機能を設計 | 18マスタすべて動的（M-02 共通 CRUD） |
| DP-5 | 論理削除のみ（BR-01）| 全テーブル `delete_flag` / `is_deleted` を持つ |
| DP-6 | 監査ログは INSERT 専用、UPDATE/DELETE は DB ロール権限で REVOKE | `audit_logs` |
| DP-7 | データ機密度に応じた保護（仕入単価=中-高） | KMS 保存時暗号化 + アクセス制御 + 監査ログ |

### 1.2 命名規約

| 対象 | 規約 | 例 |
|---|---|---|
| テーブル名 | スネークケース、複数形 | `products`, `purchase_orders`, `audit_logs` |
| カラム名 | スネークケース | `created_at`, `updated_by_user_id` |
| サロゲート PK | `id BIGSERIAL PRIMARY KEY` | 全テーブル |
| 業務 PK（masters）| `code VARCHAR(3)` UNIQUE NOT NULL | 18マスタ |
| FK | `<参照先単数形>_id BIGINT REFERENCES <table>(id)` | `supplier_id`, `product_family_id` |
| 監査列（全テーブル共通） | `created_at`, `created_by_user_id`, `updated_at`, `updated_by_user_id` | `TIMESTAMPTZ NOT NULL` / `BIGINT REFERENCES users(id)` |
| 論理削除 | マスタは `delete_flag BOOLEAN NOT NULL DEFAULT FALSE`（既存スキーマ踏襲）、トランザクションは `is_deleted BOOLEAN NOT NULL DEFAULT FALSE` | - |
| 旧システム由来 ID | `legacy_id VARCHAR(64) NULL` | Phase 4 MIG-3 |
| boolean | `BOOLEAN`、ただし既存スキーマ互換のため `INTEGER` 表現は `standard_print_flag` のみ残す | - |
| 金額 | `NUMERIC(12, 2)`（円 + 為替対応で小数2位）| `unit_price`, `total_amount` |
| 日付（業務）| `DATE`（時刻不要） | `due_date`, `effective_from` |
| 日時（システム）| `TIMESTAMPTZ`（タイムゾーン明示）| `created_at` |

### 1.3 SoT 配置（Phase 4 §5 / architecture.md §1.2 再掲）

| データ種別 | SoT | 配置 |
|---|---|---|
| ユーザ認証情報（UID/Email/PW ハッシュ）| Firebase Authentication | Google グローバル |
| ユーザ業務情報・権限ロール | RDS `users` | AWS Tokyo |
| 全業務マスタ・トランザクション | RDS PostgreSQL 16 Multi-AZ | AWS Tokyo |
| 商品画像バイナリ | S3 (`product-images/{family_id}/{filename}`) | AWS Tokyo |
| 監査ログ（直近 3ヶ月）| RDS `audit_logs` | AWS Tokyo |
| 監査ログ（3ヶ月超 3年保管）| S3 Glacier IR + Object Lock | AWS Tokyo |

---

## 2. ERD 概観

```
[Firebase Auth]              [RDS PostgreSQL]
 (UID, Email, PW)              ┌──────────────────────────────────────┐
       │  uid                  │ users                                 │
       └─────────────────────► │ - firebase_uid (UNIQUE)               │
                               │ - employee_no (UNIQUE)                │
                               │ - permissions (4 権限ロール)          │
                               └───────────────┬───────────────────────┘
                                               │ created_by / updated_by
                                               ▼
 [18 Masters]                  ┌──────────────────────────────────────┐
 ┌────────────┐                │ product_families (企画レベル親)        │
 │ size       │◄────┐          │ - planned_year_code                   │
 │ brand      │     │          │ - product_type_id                     │
 │ function   │     │          │ - season_id ...                       │
 │ country    │◄─┐  │          └───────┬──────────────────────────────┘
 │ supplier   │  │  │                  │ 1:N
 │ department │  │  │                  ▼
 │ product_type    │          ┌──────────────────────────────────────┐
 │ product_season  │          │ products (SKU, 11桁品番)              │
 │ product_group   │          │ - sku (UNIQUE)                        │
 │ color      │────┼──┐       │ - color_id, size_id                   │
 │ material   │    │  │       │ - product_family_id                   │
 │ material_classification    └───────┬──────────────────────────────┘
 │ warehouse  │    │  │               │ 1:N
 │ delivery_destination               ▼
 │ document_template_purchase ┌──────────────────────────────────────┐
 │ document_template_confirmation │ product_supplier_prices            │
 │ document_text_purchase    │ - product_id, supplier_id              │
 └────────────┘              │ - unit_price, effective_from           │
                             │ - currency, exchange_rate              │
                             └──────────────────────────────────────┘

                             ┌──────────────────────────────────────┐
                             │ product_images (S3 参照、family 単位、最大5枚)│
                             │ - product_family_id, s3_key, order_no  │
                             └──────────────────────────────────────┘

                             ┌──────────────────────────────────────┐
                             │ purchase_orders (発注書ヘッダ)         │
                             │ - mgmt_no (作成管理番号)               │
                             │ - order_no (S始まり、初回出力時採番)    │
                             │ - status (Active/Cancelled、2 値)      │
                             │ - first_exported_at (初回出力バッジ用) │
                             │ - last_exported_at                    │
                             └───────┬──────────────────────────────┘
                                     │ 1:N
                                     ▼
                             ┌──────────────────────────────────────┐
                             │ purchase_order_lines (発注明細)       │
                             │ - product_id, quantity, unit_price    │
                             └──────────────────────────────────────┘

                             ┌──────────────────────────────────────┐
                             │ purchase_order_export_logs (出力履歴) │
                             │ - exported_at, exported_by, is_first  │
                             └──────────────────────────────────────┘

                             ┌──────────────────────────────────────┐
                             │ audit_logs (INSERT 専用、3ヶ月で S3 へ)│
                             │ - actor_user_id, action, entity, ... │
                             └──────────────────────────────────────┘
```

> Mermaid ERD は README/ドキュメント整備時に変換する（Arch-6 と同じ判断）。

---

## 3. マスタテーブル定義（18件）

> **共通基底（全マスタ）:**
> - `id BIGSERIAL PRIMARY KEY`
> - `code VARCHAR(3) NOT NULL UNIQUE`（業務 PK、'000'〜'999' ゼロパディング）
> - `name VARCHAR(255) NOT NULL`
> - `delete_flag BOOLEAN NOT NULL DEFAULT FALSE`
> - `created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()`
> - `created_by_user_id BIGINT NOT NULL REFERENCES users(id)`
> - `updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()`
> - `updated_by_user_id BIGINT NOT NULL REFERENCES users(id)`
> - `legacy_id VARCHAR(64) NULL`（移行用、MIG-3）
>
> 以下各マスタは **拡張カラムのみ** 記載。

### 3.1 `sizes` — サイズマスタ

| カラム | 型 | 補足 |
|---|---|---|
| `item_conversion_code` | `VARCHAR(4) NOT NULL` | 品番末尾2桁の生成元（例: `110c`, `AS`） |

`UNIQUE (item_conversion_code) WHERE delete_flag = FALSE`（部分インデックス）

### 3.2 `brands` — ブランドマスタ
拡張カラムなし（共通基底のみ）。

### 3.3 `functions` — 機能マスタ
拡張カラムなし。

### 3.4 `countries` — 国マスタ
拡張カラムなし。

### 3.5 `suppliers` — 仕入先マスタ（工場兼用）

| カラム | 型 | 補足 |
|---|---|---|
| `official_name` | `VARCHAR(255) NULL` | 法的書面用正式名 |
| `item_conversion_code` | `CHAR(1) NOT NULL` | 11桁品番 7桁目（工場コード） |
| `country_id` | `BIGINT NOT NULL REFERENCES countries(id)` | FK |
| `supplier_type` | `SMALLINT NOT NULL` | 0=国内, 1=海外 |
| `alert_target` | `SMALLINT NOT NULL DEFAULT 0` | アラート対象フラグ |

インデックス: `idx_suppliers_country` (country_id), `idx_suppliers_active` (delete_flag) WHERE delete_flag = FALSE。

### 3.6 `departments` — 事業部マスタ
拡張カラムなし。

### 3.7 `product_types` — 商品タイプマスタ

| カラム | 型 | 補足 |
|---|---|---|
| `item_conversion_code` | `CHAR(1) NOT NULL` | 11桁品番 2桁目 |
| `size_demographic_code` | `CHAR(1) NOT NULL` | R=Women, M=Men, J=Junior |

> **設計判断:** `item_conversion_code` + `size_demographic_code` の組合せが業務的にユニーク（同じ A=吊込W底でも R/M/J で 3 レコード）。UNIQUE 制約は付けず、業務ルールで担保（既存スキーマ準拠）。

### 3.8 `product_seasons` — 商品季節マスタ

| カラム | 型 | 補足 |
|---|---|---|
| `item_conversion_code` | `CHAR(1) NOT NULL` | 11桁品番 3桁目 |
| `conversion_order` | `VARCHAR(64) NULL` | カンマ区切り（例: '1,6,7,8,9'）。同一意味の複数コードを関連付け |

### 3.9 `product_groups` — 商品群マスタ

| カラム | 型 | 補足 |
|---|---|---|
| `planning_fee` | `NUMERIC(12,2) NOT NULL DEFAULT 0` | 企画費（単位は要確認、Phase 0 残課題）|

### 3.10 `colors` — 色マスタ

| カラム | 型 | 補足 |
|---|---|---|
| `item_conversion_code` | `CHAR(2) NOT NULL` | 11桁品番 8-9桁目 |

> **未解決（Phase 0 残課題）:** 色のサブ分類（値札 1-5、色相 -/+）は本マスタには持たず、必要になれば子テーブル化を検討（Phase 7 以降）。

### 3.11 `materials` — 素材マスタ

| カラム | 型 | 補足 |
|---|---|---|
| `material_classification_id` | `BIGINT NOT NULL REFERENCES material_classifications(id)` | FK |

### 3.12 `material_classifications` — 素材分類マスタ
拡張カラムなし。

### 3.13 `warehouses` — 倉庫コードマスタ
拡張カラムなし。

### 3.14 `delivery_destinations` — 納品先マスタ

| カラム | 型 | 補足 |
|---|---|---|
| `customer_name` | `VARCHAR(255) NULL` | **新規追加。**取引先名（しまむら / KEYUCA / AEON 等）。Phase 3 機能要件 O-03 で「取引先」列が必要。本 MVP では納品先と紐付く取引先を本フィールドで保持（独立 customer マスタを追加すると 18マスタを超過する Phase 2/4 整合性に影響するため）|
| `remark_1` | `VARCHAR(255) NULL` | 物流発送先住所（郵便番号・住所）|
| `remark_2` | `VARCHAR(255) NULL` | 電話番号 |
| `remark_3` | `VARCHAR(255) NULL` | FAX 番号 |

> **設計上の確認事項 D-1:** 「取引先」を独立マスタにするか、delivery_destinations の属性として持つかオペレーター確認必要（§13 参照）。

### 3.15 `document_template_purchases` — 連絡文書定型・発注書

| カラム | 型 | 補足 |
|---|---|---|
| `body` | `TEXT NOT NULL` | 本文（共通基底の `name` を `body` に読み替え。実装上は `body` 列を別途追加し、`name` は短縮ラベル）|

> **設計判断:** 既存スキーマでは `name` 列に本文を入れる運用だが、新システムでは可読性のため `name`（ラベル）+ `body`（本文）に分離する。移行時は `name` を一旦そのまま、`body` を空でインポートし、必要に応じて転記する。

### 3.16 `document_template_confirmations` — 連絡文章・確認表

| カラム | 型 | 補足 |
|---|---|---|
| `body` | `TEXT NOT NULL` | 本文 |
| `standard_print_flag` | `BOOLEAN NOT NULL DEFAULT FALSE` | 既存 `INTEGER` から `BOOLEAN` に変換（1→true, 0→false）|

### 3.17 `document_text_purchases` — 連絡文章・発注書

| カラム | 型 | 補足 |
|---|---|---|
| `body` | `TEXT NOT NULL` | 本文 |
| `standard_print_flag` | `BOOLEAN NOT NULL DEFAULT FALSE` | 同上 |

### 3.18 `users` — ユーザマスタ（新規追加、Firebase Auth 連携）

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | サロゲート PK |
| `firebase_uid` | `VARCHAR(128) NOT NULL UNIQUE` | Firebase Authentication UID（SoT）|
| `employee_no` | `VARCHAR(8) NOT NULL UNIQUE` | 社員番号（業務 PK、3桁ゼロパディング想定だが将来拡張のため 8桁）|
| `login_id` | `VARCHAR(64) NOT NULL UNIQUE` | ログイン ID（Firebase Auth Email とは別、業務識別子）|
| `display_name` | `VARCHAR(128) NOT NULL` | 表示用日本語名 |
| `email` | `VARCHAR(255) NOT NULL UNIQUE` | Firebase Auth と同期 |
| `is_planning_staff` | `BOOLEAN NOT NULL DEFAULT FALSE` | 企画担当（DB 保持、MVP UI 非表示・BR-07）|
| `is_sales_staff` | `BOOLEAN NOT NULL DEFAULT FALSE` | 営業担当（同上）|
| `product_ledger_permission` | `SMALLINT NOT NULL DEFAULT 0` | 品番台帳管理権限。0=なし, 1=更新可能, 2=参照のみ, 3=参照のみ(制限) |
| `purchase_order_create_permission` | `SMALLINT NOT NULL DEFAULT 0` | 発注書作成権限。0=なし, 1=更新可能, 2=参照のみ |
| `purchase_order_info_permission` | `SMALLINT NOT NULL DEFAULT 0` | 発注情報管理権限。0=なし, 1=あり |
| `process_record_permission` | `SMALLINT NOT NULL DEFAULT 0` | 工程実績管理権限（DB 保持、MVP UI 非表示・BR-07）|
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除（マスタの `delete_flag` 命名に統一すべきだが、Firebase 連携の `disabled` 概念と区別するため明示的に `is_deleted`）|
| `is_active` | `BOOLEAN NOT NULL DEFAULT TRUE` | Firebase Auth `disabled` と同期（false = Firebase 側も disabled）|
| `created_at` / `created_by_user_id` / `updated_at` / `updated_by_user_id` / `legacy_id` | 共通基底 | 自己参照を許容（初回ブートストラップは `0` または `NULL` 許容後に修正、Migration で対応）|

> **設計判断:**
> - 業務 PK は `employee_no`、認証 SoT は `firebase_uid`。両者を持つことで Firebase 不調時にも業務操作のトレースが可能。
> - Firebase Auth Custom Claims には `permissions` キーで `["product:write", ...]` 形式の文字列配列を格納（権限の SoT は本テーブル、Custom Claims はキャッシュ。`architecture.md` §4.5 参照）。
> - `is_deleted` と `is_active`:
>   - `is_deleted = true`: 論理削除（マスタ削除フラグ相当）。BR-01 整合
>   - `is_active = false`: 一時的に無効化（休職等）。Firebase Auth `disabled=true` と双方向同期
> - パスワードハッシュは本テーブルに **持たない**（Firebase Auth 側に閉じ込め）。

---

## 4. 商品関連エンティティ

### 4.1 `product_families` — 商品企画レベル親

11桁品番の上位 9 桁（年式 + 型式 + 季節 + 連番）を確定する企画単位。色 × サイズの展開元。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `planned_year_code` | `CHAR(1) NOT NULL` | 11桁品番 1桁目（A-K, N, Z）|
| `product_type_id` | `BIGINT NOT NULL REFERENCES product_types(id)` | 2桁目ソース + 性別判定 |
| `product_season_id` | `BIGINT NOT NULL REFERENCES product_seasons(id)` | 3桁目ソース |
| `sequence_no` | `VARCHAR(3) NOT NULL` | 4-6桁目（4桁目=サブ分類、5-6=連番）|
| `factory_supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 11桁品番 7桁目ソース（工場兼用） |
| `brand_id` | `BIGINT NOT NULL REFERENCES brands(id)` | 商品属性 |
| `function_id` | `BIGINT NULL REFERENCES functions(id)` | 商品属性 |
| `product_group_id` | `BIGINT NOT NULL REFERENCES product_groups(id)` | 商品属性 |
| `upper_material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 甲皮素材 |
| `insole_material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 中底素材 |
| `outsole_material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 底素材 |
| `product_name_1` | `VARCHAR(255) NOT NULL` | 商品名1 |
| `product_name_2` | `VARCHAR(255) NULL` | 商品名2 |
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Draft, 1=Active, 2=Discontinued |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査 4 列 + `legacy_id` | | |

**UNIQUE 制約:** `(planned_year_code, product_type_id, product_season_id, sequence_no, factory_supplier_id)`（同一企画の重複防止）。

**インデックス:** `idx_pf_status_deleted (status, is_deleted)`, `idx_pf_brand (brand_id)`, `idx_pf_factory (factory_supplier_id)`。

### 4.2 `products` — SKU（11桁品番）

色 × サイズの全組合せで 1 レコード（P-02 サイズ展開）。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | 親 |
| `color_id` | `BIGINT NOT NULL REFERENCES colors(id)` | 11桁品番 8-9桁目ソース |
| `size_id` | `BIGINT NOT NULL REFERENCES sizes(id)` | 11桁品番 10-11桁目ソース |
| `sku` | `VARCHAR(11) NOT NULL UNIQUE` | 11桁品番（生成済み、業務 PK） |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除（BR-02 で SKU 不変、論理削除のみ）|
| 共通監査 4 列 + `legacy_id` | | |

**UNIQUE 制約:** `(product_family_id, color_id, size_id)`（同一企画内で色 × サイズの重複防止、BR の論理化）。

**インデックス:** `idx_products_family (product_family_id)`, `idx_products_search (sku, is_deleted) WHERE is_deleted = FALSE`。

> **設計判断:**
> - `sku` は生成後 INSERT 時に確定（P-02 サイズ展開で一括生成）。Application 層で組み立て、UNIQUE 制約で 2 重生成を防ぐ（CLAUDE.md 原則 2 冪等性）。
> - 性別情報は `product_type_id` から間接参照（既存設計 §3.4 準拠）。

### 4.3 `product_images` — 商品画像（S3 参照）

企画単位で最大 5 枚（BR-10）。先頭が代表画像（カードビューのヒーロー）。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | |
| `s3_key` | `VARCHAR(512) NOT NULL` | `product-images/{family_id}/{uuid}.{ext}` |
| `thumb_s3_key` | `VARCHAR(512) NULL` | サムネ（非同期生成のため NULL 許容）|
| `order_no` | `SMALLINT NOT NULL` | 1〜5 |
| `mime_type` | `VARCHAR(64) NOT NULL` | image/jpeg, image/png, image/webp |
| `file_size_bytes` | `INTEGER NOT NULL` | バリデーション用（最大 5MB = 5_242_880）|
| `width_px` | `INTEGER NULL` | メタデータ（非同期取得可）|
| `height_px` | `INTEGER NULL` | 同上 |
| `original_filename` | `VARCHAR(255) NULL` | アップロード時ファイル名 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除（S3 オブジェクトは別途 Lifecycle で削除）|
| 共通監査 4 列 | | |

**UNIQUE 制約:** `(product_family_id, order_no) WHERE is_deleted = FALSE`（同一企画内で順序の重複防止）。

**CHECK 制約:** `order_no BETWEEN 1 AND 5`, `file_size_bytes <= 5242880`。

> **設計判断:**
> - 画像本体は S3、メタデータのみ RDS。Pre-signed URL 配信で時限アクセス。
> - 削除時は RDS `is_deleted=true` のみ。S3 オブジェクトの物理削除は monthly Lambda で `is_deleted=true` かつ 90日以上経過したオブジェクトを Lifecycle 移行（削除前に S3 Glacier IR にアーカイブ）。

### 4.4 `product_supplier_prices` — マルチ仕入先単価

**アイテム（product_family）単位** で複数 (仕入先, 単価, 有効開始日) を保持（P-03 / BR-04）。同一企画内では色違い・サイズ違いでも仕入単価は同じ（業務ルール、Phase 6 で確定）。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | **アイテム単位**（旧設計の SKU 単位 `product_id` から修正、Phase 6 オペレーター確認で確定）|
| `supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | |
| `unit_price` | `NUMERIC(12,2) NOT NULL` | 仕入単価（機密度 中-高、Phase 4 §5 アクセス制御）|
| `currency_code` | `CHAR(3) NOT NULL DEFAULT 'JPY'` | ISO 4217（JPY, USD, CNY 等）|
| `exchange_rate` | `NUMERIC(10,4) NULL` | 為替レート（外貨建ての場合）|
| `effective_from` | `DATE NOT NULL` | 有効開始日 |
| `effective_to` | `DATE NULL` | 有効終了日（NULL = 現在有効）|
| `decided_at` | `DATE NOT NULL` | 仕入単価決定日 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査 4 列 | | |

**UNIQUE 制約:** `(product_family_id, supplier_id, effective_from) WHERE is_deleted = FALSE`（同一企画・同一仕入先・同一開始日の重複防止、PRICE-001）。

**インデックス:** `idx_psp_family_current (product_family_id, effective_from DESC) WHERE effective_to IS NULL AND is_deleted = FALSE`（現在有効単価の高速取得）。

**CHECK 制約:** `unit_price > 0`, `effective_to IS NULL OR effective_to > effective_from`。

> **設計判断（BR-04 履歴管理）:**
> - **アイテム単位** で 1 企画 × 1 仕入先で複数履歴を保持。現在有効＝`effective_to IS NULL`。新単価設定時は旧レコードの `effective_to` を新単価の `effective_from - 1day` で UPDATE + 新レコード INSERT（トランザクション境界）。
> - 機密度「中-高」(NFR §6.2): KMS 保存時暗号化 + アクセス制御 + 監査ログ。**監査ログには金額本体ではなくマスク値（"***"）のみ記録**（architecture.md §4.2）。
> - **発注時の引当てロジック:** `purchase_order_lines.unit_price_snapshot` は SKU の `product_id` → 親 `product_family_id` 経由で `product_supplier_prices` から仕入単価を引当てる。色違い・サイズ違いの SKU はすべて同一の単価が引当てられる。

---

## 5. 発注関連エンティティ

### 5.1 `purchase_orders` — 発注書ヘッダ

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `mgmt_no` | `VARCHAR(16) NOT NULL UNIQUE` | 作成管理番号（例: `26-00411`、発注作成時に採番、BR-03）|
| `order_no` | `VARCHAR(16) NULL UNIQUE` | 発注番号（例: `S3858`、初回 Excel 出力時に採番、BR-03）|
| `status` | `SMALLINT NOT NULL DEFAULT 0` | **0=Active（編集・出力可）, 1=Cancelled（参照のみ）** — Phase 6 で 4 値から 2 値に簡素化（F-10/F-11 対応）|
| `cancelled_at` | `TIMESTAMPTZ NULL` | |
| `cancelled_by_user_id` | `BIGINT NULL REFERENCES users(id)` | |
| `cancel_reason` | `VARCHAR(255) NULL` | |
| `supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 発注先（仕入先/工場）|
| `delivery_destination_id` | `BIGINT NOT NULL REFERENCES delivery_destinations(id)` | 納品先 |
| `customer_name_snapshot` | `VARCHAR(255) NULL` | 取引先名スナップショット（delivery_destinations.customer_name から初回 Excel 出力時にコピー、後の取引先名変更で発注書の表示が変わらないように）|
| `department_id` | `BIGINT NOT NULL REFERENCES departments(id)` | 発注事業部 |
| `warehouse_id` | `BIGINT NOT NULL REFERENCES warehouses(id)` | 納入倉庫 |
| `due_date` | `DATE NOT NULL` | 取引先納入日 |
| `orderer_user_id` | `BIGINT NOT NULL REFERENCES users(id)` | 発注担当者 |
| `sub_orderer_1_user_id` | `BIGINT NULL REFERENCES users(id)` | 副1 |
| `sub_orderer_2_user_id` | `BIGINT NULL REFERENCES users(id)` | 副2 |
| `sub_orderer_3_user_id` | `BIGINT NULL REFERENCES users(id)` | 副3 |
| `sub_orderer_4_user_id` | `BIGINT NULL REFERENCES users(id)` | 副4 |
| `sub_orderer_5_user_id` | `BIGINT NULL REFERENCES users(id)` | 副5 |
| `sub_orderer_6_user_id` | `BIGINT NULL REFERENCES users(id)` | 副6 |
| `manager_user_id` | `BIGINT NOT NULL REFERENCES users(id)` | 発注管理者 |
| `communication_text` | `TEXT NULL` | 連絡文章（O-07 で複写/編集、最大6行は Application 層で検証）|
| `first_exported_at` | `TIMESTAMPTZ NULL` | **初回 Excel 出力日時**（仕入先送付の業務目印、Phase 6 で追加）。NULL = 未出力、NOT NULL = 初回出力済（バッジ表示用）|
| `last_exported_at` | `TIMESTAMPTZ NULL` | **最終 Excel 出力日時**（再出力時に毎回更新）|
| 共通監査 4 列 + `legacy_id` | | |

**インデックス:**
- `idx_po_mgmt (mgmt_no)`, `idx_po_order_no (order_no) WHERE order_no IS NOT NULL`
- `idx_po_status (status, due_date)`（一覧検索、status は 2 値）
- `idx_po_supplier (supplier_id)`, `idx_po_dest (delivery_destination_id)`
- `idx_po_dates (created_at DESC)`（O-03 デフォルトソート）
- `idx_po_unexported (first_exported_at) WHERE first_exported_at IS NULL AND status = 0`（未出力フィルタ用）

**CHECK 制約:**
- `status IN (0, 1)`
- `last_exported_at IS NULL OR first_exported_at IS NOT NULL`（last_exported は first_exported を前提）
- `(status = 1) = (cancelled_at IS NOT NULL)`（Cancelled なら cancelled_at 必須、逆も真）

> **設計判断（Phase 6 簡素化）:**
> - 6 名の副担当者を縦持ち（既存スキーマ準拠）。横持ち（別テーブル化）も検討したが、UI/帳票で固定 6 スロットの運用が確定（O-01 「user × 9」要件）のため縦持ちが自然。
> - `customer_name_snapshot` で初回出力時の取引先名を凍結（マスタ変更による過去発注書の表示変化を防ぐ）。
> - **状態モデルを Active / Cancelled の 2 値に簡素化**（F-10/F-11 対応）:
>   - 旧設計の Draft / Submitted / Revised の区別を廃止
>   - 「Excel 出力 = 発注確定」業務概念を廃止、**Excel 出力はいつでも何度でも可能**
>   - 改訂概念を廃止、編集は常に同一発注書に対して行う（revision_no, parent_order_id 廃止）
> - **初回出力バッジ**: `first_exported_at` で「仕入先への送付実績」を業務可視化。UI 上は「未出力」「初回出力済 (YYYY-MM-DD)」バッジで表示
> - **下位互換性（CLAUDE.md 原則 7）:** MVP リリース前のため既存データなし。Phase 7 実装時に PostgreSQL マイグレーションで対応。旧設計の `revision_no=0, parent_order_id=NULL, is_cancelled=false, status=Draft/Submitted/Revised` → `status=0(Active), first_exported_at=submitted_at (Submitted 以降のみ)` に変換可能

### 5.2 `purchase_order_lines` — 発注明細

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `purchase_order_id` | `BIGINT NOT NULL REFERENCES purchase_orders(id) ON DELETE CASCADE` | 親 |
| `line_no` | `SMALLINT NOT NULL` | 明細番号 |
| `product_id` | `BIGINT NOT NULL REFERENCES products(id)` | SKU |
| `sku_snapshot` | `VARCHAR(11) NOT NULL` | 11桁品番のスナップショット（マスタ変更耐性）|
| `product_name_snapshot` | `VARCHAR(255) NOT NULL` | 商品名スナップショット |
| `quantity` | `INTEGER NOT NULL` | 数量 |
| `unit_price_snapshot` | `NUMERIC(12,2) NOT NULL` | 発注時の単価スナップショット（product_supplier_prices から引当時に複写、BR-04）|
| `currency_code_snapshot` | `CHAR(3) NOT NULL` | 単価通貨スナップショット |
| `subtotal` | `NUMERIC(14,2) GENERATED ALWAYS AS (quantity * unit_price_snapshot) STORED` | 計算列 |
| 共通監査 4 列 | | |

**UNIQUE 制約:** `(purchase_order_id, line_no)`。

**インデックス:** `idx_pol_order (purchase_order_id)`, `idx_pol_product (product_id)`。

**CHECK 制約:** `quantity > 0`, `unit_price_snapshot >= 0`。

> **設計判断:**
> - スナップショット（sku/product_name/unit_price/currency）で発注時点の状態を凍結。マスタ変更後も発注書の表示が変わらない（業務帳票としての一貫性）。
> - `subtotal` は計算列で DB 側保証（Application 層の計算ズレ防止）。
> - 単価機密度（中-高）への配慮: 監査ログには `unit_price_snapshot` 本体を残さない（マスク表示）。

### 5.3 `purchase_order_export_logs` — Excel 出力履歴（監査用、非 UI 露出）

> **Phase 6 追加:** 状態モデル簡素化（F-10/F-11 対応）に伴い、Excel 出力履歴を業務ログとして保持。UI には露出せず、監査・問い合わせ対応用。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `purchase_order_id` | `BIGINT NOT NULL REFERENCES purchase_orders(id)` | 対象発注書 |
| `exported_at` | `TIMESTAMPTZ NOT NULL DEFAULT NOW()` | 出力日時 |
| `exported_by_user_id` | `BIGINT NOT NULL REFERENCES users(id)` | 出力操作者 |
| `is_first_export` | `BOOLEAN NOT NULL` | この出力が初回かどうか（`purchase_orders.first_exported_at` 採番のトリガとなったかの可視化）|
| `excel_template_version` | `VARCHAR(16) NOT NULL` | テンプレートバージョン（テンプレ変更時の追跡用）|

**インデックス:** `idx_poel_order_at (purchase_order_id, exported_at DESC)`。

> **設計判断:**
> - 旧設計の `purchase_order_revisions` テーブルは改訂概念廃止により削除。代わりに本テーブルで「いつ・誰が・何回目に Excel 出力したか」を蓄積（仕入先トラブル時の出力履歴照会、テンプレ変更影響調査に利用）。
> - `audit_logs` (Excel.Export action) と一部冗長だが、`purchase_orders` 中心の高速取得用に専用テーブル化。`audit_logs` は横断検索用 SoT、本テーブルは発注書詳細画面の履歴タブ用キャッシュ位置付け（必要なら Phase 5 後半で再判断）。

---

## 6. 監査ログ

### 6.1 `audit_logs` — 監査ログ（INSERT 専用、SEC-13 / SEC-17）

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `occurred_at` | `TIMESTAMPTZ NOT NULL DEFAULT NOW()` | 発生日時 |
| `actor_user_id` | `BIGINT NULL REFERENCES users(id)` | 操作者（未認証イベントは NULL）|
| `actor_firebase_uid` | `VARCHAR(128) NULL` | Firebase UID（users テーブル削除済でも追跡可）|
| `actor_ip` | `INET NULL` | 操作元 IP |
| `actor_user_agent` | `VARCHAR(512) NULL` | UA |
| `action` | `VARCHAR(64) NOT NULL` | 例: `Product.Create`, `Order.Submit`, `Login.Success`, `Login.Failure`, `Price.View`, `Excel.Export` |
| `entity_type` | `VARCHAR(64) NULL` | 対象エンティティ（products, purchase_orders 等）|
| `entity_id` | `BIGINT NULL` | 対象 ID |
| `entity_business_key` | `VARCHAR(64) NULL` | 業務 ID（sku, mgmt_no, order_no 等）|
| `changes` | `JSONB NULL` | 変更前後の差分（機密フィールドはマスク済）。**構造（Phase 6 で F-15 拡張余地として確定）:** `{ "before": { "field": value, ... }, "after": { "field": value, ... } }`。例: 発注書数量変更 `{ "before": { "lines[0].quantity": 10 }, "after": { "lines[0].quantity": 15 } }`。Post-MVP で変更履歴ビュー UI を追加する際にこの構造から差分表示を生成 |
| `result` | `SMALLINT NOT NULL` | 0=Success, 1=Failure, 2=PartialSuccess |
| `error_code` | `VARCHAR(16) NULL` | Phase 3 §10 エラーコード |
| `trace_id` | `VARCHAR(64) NULL` | X-Ray TraceId（リクエストとの紐付け）|
| `note` | `VARCHAR(512) NULL` | 補足 |

> **改竄防止:**
> - PostgreSQL ロール `app_user`（App Runner 接続用）には `INSERT` のみ GRANT、`UPDATE` / `DELETE` は明示 REVOKE。
> - アーカイブ Lambda 専用ロール `audit_archive`（月次バッチ）のみ DELETE 可。アーカイブ前に S3 Glacier IR + Object Lock で不変化保存。

**インデックス:**
- `idx_audit_occurred (occurred_at DESC)` — 直近検索
- `idx_audit_actor (actor_user_id, occurred_at DESC)` — 利用者監査
- `idx_audit_entity (entity_type, entity_id)` — 特定エンティティ追跡
- `idx_audit_action (action, occurred_at DESC)` — 操作種別検索

**パーティション:** PostgreSQL Declarative Partitioning（月単位、`occurred_at` ベース）。3ヶ月超パーティションを月次 Lambda で S3 Glacier IR にエクスポート + DROP。

> **記録対象（C-03 + SEC-13）:**
> - 主要トランザクション: product / product_family / product_supplier_price / purchase_order / purchase_order_line の C/U/D
> - 認証イベント: Login.Success / Login.Failure / Logout / PasswordReset
> - 機密データ閲覧: Price.View（商品詳細での仕入単価表示）
> - エクスポート: Excel.Export, CSV.Export, Image.Download
> - 管理操作: PermissionsChanged, MasterDataChanged

> **非記録:** 単純な GET（一覧・検索）は記録しない（C-03 BR、性能配慮）。

---

## 7. 制約・インデックス設計

### 7.1 グローバル運用

| 種別 | ポリシー |
|---|---|
| FK | すべて `ON UPDATE NO ACTION ON DELETE NO ACTION`（論理削除のみのため）。例外: `purchase_order_lines.purchase_order_id` は `ON DELETE CASCADE`（発注書ヘッダ完全削除時の整合）|
| インデックス命名 | `idx_<table>_<columns>`、部分インデックスは `WHERE` 条件を含む |
| UNIQUE 命名 | `uq_<table>_<columns>` |
| CHECK 命名 | `chk_<table>_<rule>` |
| デフォルト Collation | `pg_catalog."default"`（UTF-8、日本語照合は ICU を後で評価）|

### 7.2 N+1 対策（CLAUDE.md R-4）

| クエリ | 対策 |
|---|---|
| 商品一覧（P-04）| `Include(p => p.Family).ThenInclude(f => f.Brand).ThenInclude(...)` で必要マスタを Join |
| 発注一覧（O-03）| supplier / delivery_destination / orderer を一括 Include、明細品番数は `count(*)` を別クエリ or 計算列 |
| 価格レンジ（P-04 カード）| `product_supplier_prices` の min/max を SQL 集計（DB 側で完結）|
| Excel 出力（O-06）| 発注書 + 明細 + 関連マスタを 1 トランザクション内 で一括取得（`AsSplitQuery` 検討）|

### 7.3 トランザクション境界

| 操作 | トランザクション |
|---|---|
| 商品マスタ登録（P-01〜P-03）| product_family + products（バルク INSERT） + product_supplier_prices + audit_logs を 1 トランザクション |
| 発注書作成（O-01/O-02）| purchase_orders + purchase_order_lines（バルク INSERT） + audit_logs |
| Excel 出力（O-06）| 初回時: purchase_orders 更新（`order_no` 採番、`first_exported_at`, `last_exported_at` SET、`customer_name_snapshot` 凍結） + purchase_order_export_logs INSERT + audit_logs。2回目以降: `last_exported_at` のみ更新 + purchase_order_export_logs INSERT + audit_logs |
| 発注編集（O-04）| 同一 purchase_orders レコードを直接更新（status=Active 時のみ可、Cancelled は不可）+ 明細差し替え + audit_logs。改訂概念は廃止 |
| 権限変更（§Arch §4.5）| RDS users UPDATE + audit_logs。Firebase Custom Claims 更新は **トランザクション外**（失敗時は Reconciler で復旧）|

---

## 8. 旧システムからの移行（MIG-1 / MIG-3）

| データ | 移行元 | 移行方法 | 備考 |
|---|---|---|---|
| 17マスタ | 旧3システム CSV エクスポート | バルクインポート機能（Phase 5 後半設計）| `legacy_id` に旧 ID を格納 |
| ユーザマスタ | 旧「利用者マスタメンテナンス」CSV | バルクインポート + Firebase Auth へユーザ一括作成（Firebase Admin SDK `importUsers`、scrypt パスワードハッシュは Firebase 互換変換が必要、現実的には初回ログインでパスワード再設定要求が無難）| firebase_uid は importUsers レスポンスから取得して RDS に紐付け |
| 商品マスタ（11桁品番）| 旧生産管理 CSV | バルクインポート | 既存 SKU を `legacy_id` で追跡、新 `id` で内部 FK |
| 発注書履歴 | 旧受発注システム CSV | 直近 1 年分のみ移行（過去は旧システムで参照）| 移行スコープは Phase 6 で再確認 |
| 商品画像 | 旧サーバ or 紙資料 | S3 アップロード（手動 + 簡易ツール）| Phase 7 で実施 |
| 監査ログ | 移行しない | 新システム稼働後分のみ蓄積 | |

---

## 9. データ機密度との対応（NFR §6.2 / Phase 4 §5）

| データ | 機密度 | 配置 | 暗号化 | 監査 |
|---|---|---|---|---|
| 仕入単価（`product_supplier_prices.unit_price` (アイテム単位), `purchase_order_lines.unit_price_snapshot`）| 中-高 | RDS | RDS Storage Encryption (KMS) + TLS | `Price.View` / `PriceSet` / `Excel.Export` を audit_logs に記録（金額本体はマスク）|
| 商品マスタ・発注書 | 中 | RDS | 同上 | C/U/D を記録 |
| 取引先・仕入先 | 中 | RDS | 同上 | マスタ変更を記録 |
| ユーザ業務情報 | 軽微 | RDS | 同上 | 権限変更を記録 |
| ユーザ認証情報 | 軽微 | Firebase Auth | Firebase 標準 | Login.Success / Failure / PasswordReset を記録 |
| 商品画像 | 低-中 | S3 | SSE-S3 + Pre-signed URL | Image.Download を記録 |

> **Phase 5 で再評価（Phase 4 #5 合意）:** pgcrypto / Envelope Encryption による `unit_price` のカラム単位暗号化採否を再評価。本ドラフトでは **A 案（KMS Storage Encryption + アクセス制御）** で設計、Phase 5 後半で監査結果に応じ判断。

---

## 10. データボリューム見積（NFR §3 / Phase 0 仮説）

| エンティティ | 5 年想定 | サイジング根拠 |
|---|---|---|
| product_families | 約 2,000 件 | 年 400 企画 × 5 年 |
| products (SKU) | 約 2 万件 | 1 企画あたり平均 10 SKU（色 5 × サイズ 2）|
| product_supplier_prices | 約 6,000 件 | 1 アイテムあたり平均 1.5 仕入先 × 2 履歴（アイテム単位確定により旧見積 6 万件から 1/10 に減）|
| product_images | 約 1 万件 | 1 企画あたり平均 5 枚 |
| purchase_orders | 約 5,000 件 | 年 1,000 件 × 5 年 |
| purchase_order_lines | 約 25 万件 | 1 発注あたり平均 50 明細 |
| users | 約 50 件 | 社員数想定 |
| audit_logs（RDS 直近 3ヶ月）| 約 30 万件 | 業務イベント月 10 万件想定 |
| audit_logs（S3 アーカイブ 3 年）| 約 360 万件 | |

→ RDS インスタンス `db.t4g.small`（2 vCPU / 2GB / 20GB ストレージ）で 5 年は十分余裕。

---

## 11. 正規化チェック（DP-1 適合性）

| エンティティ | 第1NF | 第2NF | 第3NF | 備考 |
|---|---|---|---|---|
| 18 masters | ✅ | ✅ | ✅ | サロゲート PK、業務 PK は UNIQUE |
| product_families | ✅ | ✅ | ✅ | マスタへの FK のみ、推移依存なし |
| products | ✅ | ✅ | ✅ | 同上 |
| product_images | ✅ | ✅ | ✅ | S3 メタのみ |
| product_supplier_prices | ✅ | ✅ | ✅ | 履歴は `effective_from/to` で時間軸正規化 |
| purchase_orders | ✅ | ✅ | ⚠️ | **意図的非正規化:** `customer_name_snapshot`（業務帳票の凍結要件）、6 名の副担当者を縦持ち（既存運用準拠）。DP-1 例外として明示記録 |
| purchase_order_lines | ✅ | ✅ | ⚠️ | **意図的非正規化:** sku/product_name/unit_price/currency のスナップショット（業務帳票要件）。`subtotal` は計算列で DB 保証 |
| audit_logs | ✅ | ✅ | ✅ | INSERT 専用 |

> **非正規化の根拠記録（DP-1 例外）:**
> - `customer_name_snapshot`, `sku_snapshot`, `product_name_snapshot`, `unit_price_snapshot`, `currency_code_snapshot`: 業務帳票（発注書 Excel）は発注時点の値を保持する必要があり、後のマスタ変更で帳票表示が変わると業務的に不整合（既に取引相手に送付済の文書との不一致）。read コスト + write コスト + 業務整合性のバランスで非正規化を採用。

---

## 12. I/F 設計 6 視点チェック（データ層）

| # | 視点 | チェック結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ PostgreSQL 16 + EF Core 8 + Npgsql の型マッピングは標準対応（BIGSERIAL → long, TIMESTAMPTZ → DateTimeOffset, NUMERIC → decimal, JSONB → string or JsonDocument）|
| 2 | ユースケース | ✅ UC-1〜UC-4 全カバー、商品 SKU 生成・マルチ仕入先・発注作成・改訂・中止・Excel 出力すべてエンティティに対応 |
| 3 | ユーザビリティ | ✅ マスタの `delete_flag` 部分インデックスでアクティブ件数の高速取得、商品一覧の `idx_pf_status_deleted` で初期表示 500ms 達成可能 |
| 4 | データ設計上の都合 | ✅ §11 正規化チェック完了、非正規化箇所は根拠付き |
| 5 | 型の継承関係 | ✅ EF Core Entity → Mapster → DTO → API 公開型 で写像。スナップショット列は DTO で別フィールドとして表現（混乱回避）|
| 6 | データフロー整合性 | ✅ architecture.md §4 の 5 シナリオでデータの流れを検証済、本設計のテーブル粒度と整合 |

---

## 13. 設計上の確認事項（オペレーターレビュー Phase5-Data）

| # | 論点 | 推奨案 |
|---|---|---|
| **D-1** | 「取引先（customer）」の扱い: 独立マスタ追加 vs delivery_destinations.customer_name で対応 | **delivery_destinations.customer_name で対応**（推奨）。Phase 2/4 で 18マスタ確定済のため独立マスタ追加は影響大。複数の納品先が同じ取引先を持つケース（しまむら → 複数センター）は customer_name の重複を許容、業務的に問題なし |
| ~~D-2~~ | ~~`purchase_order_revisions` テーブル新設の要否~~ | **解消 (Phase 6)** 状態モデル簡素化（F-10/F-11）で改訂概念自体を廃止。`purchase_order_export_logs` を新設して出力履歴に置換 |
| ~~D-3~~ | ~~`is_cancelled` と `status=Cancelled` の二重表現~~ | **解消 (Phase 6)** 状態モデル簡素化で `is_cancelled` 削除、`status` (Active/Cancelled) 単独に統一 |
| D-4 | `users.is_deleted` と `is_active` の二重保持 | **両保持**。意味が異なる（恒久削除 vs 一時無効化）|
| D-5 | 商品画像の S3 物理削除タイミング（90日後 Lifecycle）| 推奨案で確定（運用ガイドに記載）|
| D-6 | 仕入単価 pgcrypto 採否（Phase 4 #5 再評価）| 本ドラフトでは A 案維持。Phase 5 後半で再評価 |
| D-7 | RDS パーティショニング対象（audit_logs のみ vs 他テーブルも）| audit_logs のみ。他は規模的に不要 |
| D-8 | 文書テンプレート（template）の `name` / `body` 分離 | **分離推奨**（既存 `name` 単独運用の可読性が低い）。Migration で旧 `name` をそのまま残し、`body` 空でインポート → 運用で再整理 |
| D-9 | enum 型の表現: SMALLINT + Application 層解釈 vs PostgreSQL ENUM 型 | **SMALLINT** 推奨。EF Core 親和性 + ENUM の変更コスト回避 |
| D-10 | EF Core Migration の管理ポリシー | per-PR 1 Migration、CI で `dotnet ef migrations script` 生成 + DBA レビュー必須化を Phase 5 後半で文書化 |

---

## 14. 変更履歴

| 日付 | 内容 |
|---|---|
| 2026-05-19 | 初版作成（18 マスタ + 商品 4 + 発注 3 + 監査 1 = 計 26 テーブル、機能要件 21 全対応）|
