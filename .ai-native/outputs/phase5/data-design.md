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
| 監査列（全テーブル共通） | `created_at`, `created_by_user_id`, `updated_at`, `updated_by_user_id` | `TIMESTAMP NOT NULL`（JST naive、Iter 4 段階 B で `timestamp with time zone` から変更）/ `BIGINT REFERENCES users(id)` |
| 論理削除 | マスタは `delete_flag BOOLEAN NOT NULL DEFAULT FALSE`（既存スキーマ踏襲）、トランザクションは `is_deleted BOOLEAN NOT NULL DEFAULT FALSE` | - |
| 旧システム由来 ID | `legacy_id VARCHAR(64) NULL` | Phase 4 MIG-3 |
| boolean | `BOOLEAN`、ただし既存スキーマ互換のため `INTEGER` 表現は `standard_print_flag` のみ残す | - |
| 金額 | `NUMERIC(12, 2)`（円 + 為替対応で小数2位）| `unit_price`, `total_amount` |
| 日付（業務）| `DATE`（時刻不要） | `due_date`, `effective_from` |
| 日時（システム）| `TIMESTAMP`（JST naive、Iter 4 段階 B 改訂。DB レベル `timezone='Asia/Tokyo'` を永続設定、C# 側は `Akebono.Domain.Common.SystemTime.Now` で JST `Kind=Unspecified` 値を生成）| `created_at` |

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
                               │ - permissions (5 権限ロール)          │
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
 │ document_text_purchase    │ - product_family_id, supplier_id, size_id │
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

 [勤怠 (Iteration 30、akebono-office から移植。詳細は §14)]
 ┌──────────────────────────────────────┐
 │ users (勤怠列 6 本を末尾追加)          │
 │ - attendance_permission (0/1/2)       │
 │ - punch_required                      │
 │ - attendance_rule_id ─────────────┐   │
 │ - hire_date, weekly_days/hours    │   │
 └───────┬───────────────────────────┼───┘
         │ user_id                   │ NULL = 既定ルール
         │                           ▼
         │                 ┌──────────────────────────────────────┐
         │                 │ attendance_rules (勤務体系マスタ)      │
         │                 │ - work_start / work_end / break_minutes│
         │                 │ - legal_holiday_weekday / closing_day  │
         │                 │ - is_default (テナント内 高々 1 件)    │
         │                 └──────────────────────────────────────┘
         │
         ├──► punch_records (打刻。記録系・追記のみ、at=UTC / date=JST)
         │      ▲ source=2(Fix) の行の fixed_from が旧打刻の at を指し論理置換
         │      │
         ├──► attendance_fix_requests (打刻修正申請。承認で punch_records へ追記)
         │
         ├──► leave_grants ────┐  (付与。UNIQUE(tenant,user,type,grant_date) で冪等)
         │                     ├─► leave_types (休暇種別マスタ。法定有給をシード)
         └──► leave_requests ──┘  (申請。status=1(Approved) のみ残数を消化)
```

> Mermaid ERD は README/ドキュメント整備時に変換する（Arch-6 と同じ判断）。

---

## 3. マスタテーブル定義（18件）

> **共通基底（全マスタ）:**
> - `id BIGSERIAL PRIMARY KEY`
> - `code VARCHAR(3) NOT NULL UNIQUE`（業務 PK、'000'〜'999' ゼロパディング）
> - `name VARCHAR(255) NOT NULL`
> - `delete_flag BOOLEAN NOT NULL DEFAULT FALSE`
> - `created_at TIMESTAMP NOT NULL DEFAULT NOW()`
> - `created_by_user_id BIGINT NOT NULL REFERENCES users(id)`
> - `updated_at TIMESTAMP NOT NULL DEFAULT NOW()`
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
| `official_name` | `VARCHAR(255) NULL` | 法的書面用正式名。**発注書 Excel 帳票の宛名印字に使用** (英字スペル可、例: `DEPARTURES`)。帳票表記は「`<official_name>` 御中 `<code>`」の 3 要素構造で出力 (F-22 対応、2026-05-19 確定) |
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
| `customer_name` | `VARCHAR(255) NULL` | **新規追加。**取引先名（しまむら / KEYUCA / AEON 等）。Phase 3 機能要件 O-03 で「取引先」列が必要。用途は画面表示・検索・集計の内部識別用 (発注書 Excel 帳票の宛名は仕入先 = `suppliers.official_name` + 御中 + `suppliers.code`、F-22 で確認済 2026-05-19)。本 MVP では納品先と紐付く取引先を本フィールドで保持（独立 customer マスタを追加すると 18マスタを超過する Phase 2/4 整合性に影響するため）|
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
| `attendance_permission` | `SMALLINT NOT NULL DEFAULT 1` | **勤怠権限（Iteration 30 追加）。0=なし, 1=更新可能, 2=参照のみ。既存 4 権限と同じ非単調スケールのため、書込判定は `== 1`（`>= 1` は「参照のみ」に書込を許すバグ）。`CHECK (attendance_permission BETWEEN 0 AND 2)`** |
| `punch_required` | `BOOLEAN NOT NULL DEFAULT TRUE` | **打刻対象か（Iteration 30 追加）。役員・外注等は false。打刻 API は `attendance_permission == 1` かつ本列が true のときのみ許可** |
| `attendance_rule_id` | `UUID NULL REFERENCES attendance_rules(id)` | **個別に割り当てた勤務体系（Iteration 30 追加）。NULL = 既定ルール（§14.1 の `is_default`）を使用。`fk_users_attendance_rule` / 部分インデックス `idx_users_attendance_rule ... WHERE attendance_rule_id IS NOT NULL`** |
| `hire_date` | `DATE NULL` | **入社日（Iteration 30 追加）。有給の周期自動付与の起算日（`hire_date + 6ヶ月`、以降 1 年ごと）。NULL のユーザは周期自動付与の対象外** |
| `weekly_days` | `NUMERIC(3,1) NOT NULL DEFAULT 5` | **週所定日数（Iteration 30 追加）。有給の比例付与判定に使用。`CHECK (weekly_days BETWEEN 0 AND 7)`** |
| `weekly_hours` | `NUMERIC(4,1) NOT NULL DEFAULT 40` | **週所定時間（Iteration 30 追加）。有給の比例付与判定に使用。`CHECK (weekly_hours BETWEEN 0 AND 168)`** |

> **既知の課題（Iteration 30）→ 解消済み（コミット `4c6981e`）:** 当初、勤怠列のうち
> `attendance_rule_id` / `hire_date` / `weekly_days` / `weekly_hours` の 4 件に入力 UI が無い一方、
> `PATCH /users/{id}` が全項目を無条件で上書きしていたため、利用者フォームから保存するたびに
> これらが既定値へ巻き戻る BLOCKER があった。利用者フォームへの 4 項目追加と、
> `PATCH` の部分更新化（`UserPatchRequest`。`null` = 未指定 = 現在値保持、NULL 許容列は
> 明示クリアフラグ）の**両方**を実施して解消済み。経緯は `screen-design.md §3.12`、
> API 契約は `api-design.md §2.7.9` を参照。

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

**アイテム（product_family）単位** で複数 (仕入先, 単価, 有効開始日) を保持（P-03 / BR-04）。
仕入単価は原則アイテム単位（色違い・サイズ違いで共通）だが、PR2（設計判断Q4=サイズ別必要）で
**サイズ別単価**に対応した: `size_id` が NULL のとき全サイズ共通の既定単価（従来挙動、既存行は NULL の
まま下位互換）、非 NULL のときそのサイズ専用単価（既定をオーバーライド）。BR-04 有効日履歴は size 次元
込みで維持する。現単価解決は「そのサイズ専用の有効行があればそれを、無ければ NULL-size 既定行」の
フォールバック。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | **アイテム単位**（旧設計の SKU 単位 `product_id` から修正、Phase 6 オペレーター確認で確定）|
| `supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | |
| `size_id` | `BIGINT NULL REFERENCES sizes(id)` | **サイズ別仕入単価（PR2、設計判断Q4）**。NULL = 全サイズ共通の既定単価（従来挙動）、非 NULL = そのサイズ専用単価。既存行は NULL のまま下位互換 |
| `unit_price` | `NUMERIC(12,2) NOT NULL` | 仕入単価（機密度 中-高、Phase 4 §5 アクセス制御）|
| `currency_code` | `CHAR(3) NOT NULL DEFAULT 'JPY'` | ISO 4217（JPY, USD, CNY 等）|
| `exchange_rate` | `NUMERIC(10,4) NULL` | 為替レート（外貨建ての場合）|
| `effective_from` | `DATE NOT NULL` | 有効開始日 |
| `effective_to` | `DATE NULL` | 有効終了日（NULL = 現在有効）|
| `decided_at` | `DATE NOT NULL` | 仕入単価決定日 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査 4 列 | | |

> **補足（旧項目パリティ追補、全 NULL 許容）:** 上表のほか、`exchange_rate`（為替レート）、Phase C 仕入コスト計算明細 9 列（`estimate_unit_price` / `estimate_received_date` / `estimate_cost` / `estimate_margin_rate` / `purchase_cost` / `purchase_margin_rate` / `loss_cost` / `drayage_cost`（旧「トレー代」、設計判断Q6 で名称統一）/ `tax_rate`）を保持する。全カラムの正規定義は `db/init/03-products.sql`（SoT）を参照。

**UNIQUE 制約:** `(product_family_id, supplier_id, COALESCE(size_id, -1), effective_from) WHERE is_deleted = FALSE`（同一企画・同一仕入先・**同一サイズ**・同一開始日の重複防止、PRICE-001）。PR2 で `size_id` を一意キーに追加。Postgres は NULL を distinct 扱いするため、`size_id=NULL` の既定行どうしの一意性が緩まないよう `COALESCE(size_id, -1)` 式一意インデックス（`uq_psp_family_supplier_size_from`）を用いる（移植性重視。`sizes.id` は BIGSERIAL ≥ 1 のため -1 は衝突しない）。

**インデックス:** `idx_psp_family_current (product_family_id, supplier_id, COALESCE(size_id, -1), effective_from DESC) WHERE effective_to IS NULL AND is_deleted = FALSE`（現単価ルックアップ (family, supplier, size) と BR-04 履歴クローズの選択性確保、PR2 で size 込みに拡張）。

**CHECK 制約:** `unit_price > 0`, `effective_to IS NULL OR effective_to > effective_from`。

> **設計判断（BR-04 履歴管理）:**
> - **アイテム単位 × サイズ次元** で 1 企画 × 1 仕入先 × 1 サイズバケット（`size_id` 値、NULL も 1 バケット）に複数履歴を保持。現在有効＝`effective_to IS NULL`。新単価設定時は**同一サイズバケットの**旧レコードの `effective_to` を新単価の `effective_from - 1day` で UPDATE + 新レコード INSERT（トランザクション境界）。size 専用単価の新設で全サイズ既定をクローズしない（逆も同様）。
> - 機密度「中-高」(NFR §6.2): KMS 保存時暗号化 + アクセス制御 + 監査ログ。**監査ログには金額本体ではなくマスク値（"***"）のみ記録**（architecture.md §4.2）。
> - **発注時の単価スナップショット:** `purchase_order_lines.unit_price_snapshot` は発注作成/編集時のクライアント入力値をそのまま凍結保存する（SoT は入力時点の業務判断、「単価未決定」= `unit_price_snapshot <= 0` の状態も保持される）。入力補助として size-aware な現単価サジェスト（`GET /api/v1/orders/price-suggestion`、PR2）を提供する: SKU の `product_id` → 親 `product_family_id` と `size_id` を解決し、「(family, supplier, SKUのsize) の現単価 → 無ければ (…, NULL-size 既定) の現単価」のフォールバックでサジェストする。サジェストは読取専用で、サーバ側で snapshot を上書きしない。

---

## 5. 発注関連エンティティ

### 5.1 `purchase_orders` — 発注書ヘッダ

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `mgmt_no` | `VARCHAR(16) NOT NULL UNIQUE` | 作成管理番号（例: `26-00411`、発注作成時に採番、BR-03）|
| `order_no` | `VARCHAR(16) NULL UNIQUE` | 発注番号（例: `S3858`、初回 Excel 出力時に採番、BR-03）|
| `status` | `SMALLINT NOT NULL DEFAULT 0` | **0=Active（編集・出力可）, 1=Cancelled（参照のみ）** — Phase 6 で 4 値から 2 値に簡素化（F-10/F-11 対応）|
| `cancelled_at` | `TIMESTAMP NULL` | |
| `cancelled_by_user_id` | `BIGINT NULL REFERENCES users(id)` | |
| `cancel_reason` | `VARCHAR(255) NULL` | |
| `supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 発注先（仕入先/工場）|
| `supplier_official_name_snapshot` | `VARCHAR(255) NULL` | **仕入先 official_name スナップショット (F-22 対応 2026-05-19)**。`suppliers.official_name` から初回 Excel 出力時にコピー凍結。発注書帳票の宛名「`<supplier_official_name>` 御中 `<supplier_code>`」第 1 要素として印字、マスタ変更による過去発注書帳票表示変化を防ぐ |
| `supplier_code_snapshot` | `VARCHAR(3) NULL` | **仕入先 code スナップショット (F-22 対応 2026-05-19)**。`suppliers.code` から初回 Excel 出力時にコピー凍結。発注書帳票の宛名「`<supplier_official_name>` 御中 `<supplier_code>`」第 2 要素として印字 |
| `delivery_destination_id` | `BIGINT NOT NULL REFERENCES delivery_destinations(id)` | 納品先 |
| `customer_name_snapshot` | `VARCHAR(255) NULL` | 取引先名スナップショット（delivery_destinations.customer_name から初回 Excel 出力時にコピー、後の取引先名変更で発注書一覧・検索表示が変わらないように）。**用途は画面表示・検索・集計の内部識別用**、Excel 帳票には印字されない (帳票の宛名は仕入先側 = supplier_official_name + 御中 + supplier_code、F-22) |
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
| `first_exported_at` | `TIMESTAMP NULL` | **初回 Excel 出力日時**（仕入先送付の業務目印、Phase 6 で追加）。NULL = 未出力、NOT NULL = 初回出力済（バッジ表示用）|
| `last_exported_at` | `TIMESTAMP NULL` | **最終 Excel 出力日時**（再出力時に毎回更新）|
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
> - 帳票印字対象の `supplier_official_name_snapshot` / `supplier_code_snapshot` (発注書宛名)、内部識別用の `customer_name_snapshot` の 3 件を初回出力時に凍結（マスタ変更による過去発注書の表示・検索結果変化を防ぐ、F-22 対応 2026-05-19）。
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
| `unit_price_snapshot` | `NUMERIC(12,2) NOT NULL` | 発注時の単価スナップショット（発注作成/編集時のクライアント入力値を凍結。入力補助として size-aware 現単価サジェスト `GET /orders/price-suggestion` を提供するが、サーバ側で上書きはしない、PR2）|
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
| `exported_at` | `TIMESTAMP NOT NULL DEFAULT NOW()` | 出力日時 |
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
| `occurred_at` | `TIMESTAMP NOT NULL DEFAULT NOW()` | 発生日時 |
| `actor_user_id` | `BIGINT NULL REFERENCES users(id)` | 操作者（未認証イベントは NULL）|
| `actor_firebase_uid` | `VARCHAR(128) NULL` | Firebase UID（users テーブル削除済でも追跡可）|
| `actor_ip` | `INET NULL` | 操作元 IP |
| `actor_user_agent` | `VARCHAR(512) NULL` | UA |
| `action` | `VARCHAR(64) NOT NULL` | 例: `Product.Create`, `Order.Submit`, `Login.Success`, `Auth.LoginRejected.Inactive`, `Auth.UidUnboundProbe`, `Price.View`, `MaterialPrice.View`, `Excel.Export`。認証拒否系は OnTokenValidated 側で記録される (`Login.Failure` という名称は使用しない、§Architecture §5.1 参照)。**生産管理拡張の action（`ProductMaterial.*` / `ProductionInstruction.*` / `MaterialOrder.*` / `MaterialPrice.View`）は `data-design-production.md §6` 参照** |
| `entity_type` | `VARCHAR(64) NULL` | 対象エンティティ（products, purchase_orders 等）|
| `entity_id` | `BIGINT NULL` | 対象 ID |
| `entity_business_key` | `VARCHAR(64) NULL` | 業務 ID（sku, mgmt_no, order_no 等）|
| `changes` | `JSONB NULL` | 変更前後の差分（機密フィールドはマスク済）。**構造（Phase 6 で F-15/F-16 対応として確定）:** `{ "before": { "field": value, ... }, "after": { "field": value, ... }, "edit_reason": "<Enum>", "edit_note": "<任意テキスト>" }`。`edit_reason` / `edit_note` は **`PATCH /purchase-orders/{id}` 由来の編集時のみ必須** (F-16 ORDER-005 バリデーション)、その他の action では NULL 可。`edit_reason` Enum: `quantity` / `deadline` / `supplier` / `typo` / `other`。例: 発注書数量変更 `{ "before": { "lines[0].quantity": 10 }, "after": { "lines[0].quantity": 15 }, "edit_reason": "quantity", "edit_note": "仕入先在庫切れ" }`。Post-MVP で変更履歴ビュー UI を追加する際にこの構造から差分表示と編集理由集計を生成 |
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
> - 認証イベント: `Login.Success` (成功) / `Auth.LoginRejected.Inactive` (IsActive=false ユーザ拒否、`actor_user_id` 付き) / `Auth.UidUnboundProbe` (未紐付け Firebase UID 偵察、`actor_user_id=NULL`) / `Logout` / `PasswordReset`。OnTokenValidated 内で per-UID 5 分 atomic de-dup が掛かっているため、同一 UID の連続失敗試行は 5 分に 1 件のみ記録 (audit_logs DoS 増幅対策、`architecture.md §5.1` 参照)
> - 機密データ閲覧: Price.View（商品詳細での仕入単価表示）、MaterialPrice.View（素材発注の素材単価表示。生産管理拡張、ブロッキング監査、`data-design-production.md §6`）
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
| Excel 出力（O-06）| 初回時: purchase_orders 更新（`order_no` 採番、`first_exported_at`, `last_exported_at` SET、`supplier_official_name_snapshot` / `supplier_code_snapshot` / `customer_name_snapshot` の 3 件を一括凍結、F-22 対応） + purchase_order_export_logs INSERT + audit_logs。2回目以降: `last_exported_at` のみ更新 + purchase_order_export_logs INSERT + audit_logs |
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
| ユーザ認証情報 | 軽微 | Firebase Auth | Firebase 標準 | `Login.Success` / `Auth.LoginRejected.Inactive` / `Auth.UidUnboundProbe` / `PasswordReset` を記録 (`architecture.md §5.1` 参照) |
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
| purchase_orders | ✅ | ✅ | ⚠️ | **意図的非正規化:** `supplier_official_name_snapshot` / `supplier_code_snapshot` / `customer_name_snapshot` (業務帳票の凍結要件、F-22)、6 名の副担当者を縦持ち（既存運用準拠）。DP-1 例外として明示記録 |
| purchase_order_lines | ✅ | ✅ | ⚠️ | **意図的非正規化:** sku/product_name/unit_price/currency のスナップショット（業務帳票要件）。`subtotal` は計算列で DB 保証 |
| audit_logs | ✅ | ✅ | ✅ | INSERT 専用 |

> **非正規化の根拠記録（DP-1 例外）:**
> - `supplier_official_name_snapshot`, `supplier_code_snapshot`, `customer_name_snapshot`, `sku_snapshot`, `product_name_snapshot`, `unit_price_snapshot`, `currency_code_snapshot`: 業務帳票（発注書 Excel）は発注時点の値を保持する必要があり、後のマスタ変更で帳票表示が変わると業務的に不整合（既に仕入先へ送付済の文書との不一致）。read コスト + write コスト + 業務整合性のバランスで非正規化を採用 (F-22 で supplier 2 件を追加 2026-05-19)。

---

## 12. I/F 設計 6 視点チェック（データ層）

| # | 視点 | チェック結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ PostgreSQL 16 + EF Core 8 + Npgsql の型マッピングは標準対応（BIGSERIAL → long, TIMESTAMP → DateTime [Kind=Unspecified、Iter 4 段階 B JST naive 移行に合わせ DateTimeOffset から変更], NUMERIC → decimal, JSONB → string or JsonDocument）|
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

## 14. 勤怠関連エンティティ（Iteration 30、akebono-office からの移植）

> **移植元:** **akebono-office** の勤怠管理・タイムカード機能。打刻・勤怠集計（労基法 32/34/37 条）・
> 36 協定アラート（労基法 36 条）・打刻修正申請・休暇（労基法 39 条）を honshu へ移植した。
> **章番号について:** 本章は §7〜§13 への既存クロスリファレンス（`data-design §7.1` / `§7.2` /
> `§6.1` 等、コードコメント・他ドキュメントから多数参照）を壊さないため、既存章を再採番せず
> 変更履歴の直前に追加している。
>
> **実装 SoT:** `db/init/10-attendance.sql`（新規 DB 初期化）/ `db/migration/iter30-attendance.sql`
> （既存 DB への追加適用、両者は等価）/ `src/Backend/Infrastructure/Persistence/AkebonoDbContext.cs`
> （EF Core マッピング）/ `src/Backend/Domain/Attendance/`（エンティティ・ドメインロジック）。
> API は `api-design.md §2.7`、画面は `screen-design.md §3.14 / §3.15` を参照。

### 14.0 共通事項

**共通基底（勤怠 6 テーブル全件）** — §1.2 命名規約の初版（`id BIGSERIAL` / `TIMESTAMP` JST naive）ではなく、
プラットフォーム統合改修（2026-07-09）以降の現行規約に従う:

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `UUID PRIMARY KEY DEFAULT gen_random_uuid()` | サロゲート PK |
| `tenant_id` | `UUID NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id)` | テナント分離。RLS（`USING` + `WITH CHECK` + `FORCE ROW LEVEL SECURITY`）を 6 テーブル全件に配線 |
| `created_at` | `TIMESTAMPTZ NOT NULL DEFAULT NOW()` | UTC 格納 |
| `updated_at` | `TIMESTAMPTZ NOT NULL DEFAULT NOW()` | `set_updated_at()` トリガで自動更新。**`punch_records` のみ本列を持たない**（追記のみのため） |

C# 側は全エンティティが `ITenantScoped` を実装し、`tenant_id` 列マッピングとグローバルクエリフィルタは
`AkebonoDbContext.OnModelCreating` の一括配線が担う。enum は `HasConversion<short>()` で `SMALLINT` 列へ写像する
（既存 `OrderStatus` と同方式、D-9「enum は SMALLINT」に整合）。

> **時刻の扱い（`src/Backend/Domain/Common/SystemTime.cs` が SoT）:**
> - 打刻時刻 `punch_records.at` ・ 申請時刻 `attendance_fix_requests.requested_at` は
>   **`TIMESTAMPTZ` に UTC を格納**する（C# は `DateTime` / `Kind=Utc`、採番は `SystemTime.UtcNow`）。
> - 業務日付 `date` / `grant_date` / `expire_date` は **JST の日付**を `DATE` で持つ（採番は `SystemTime.TodayJst`）。
> - 深夜帯（22時〜5時）判定・時刻表示は必ず `SystemTime.ToJst(at)` で JST 変換してから行う
>   （UTC 値の `Hour` を直接使わない）。
> - `db/migration/iter4-tz-to-jst-naive.sql`（§1.2 が参照する JST naive 方式）は**廃止済みの旧方式**であり、
>   勤怠テーブルは従わない。

**RLS とロール権限:** `db/init/10-attendance.sql` は `08-tenancy-rls.sql`（RLS 一括配線）・
`09-updated-at-triggers.sql`（トリガ一括配線）より後（番号 10）に実行されるため、自ファイル末尾で
RLS ポリシーと `updated_at` トリガを自ら配線する。アプリロール `akebono_app` には
`punch_records` のみ `SELECT, INSERT` を付与し **`UPDATE` / `DELETE` を明示 REVOKE** する
（`audit_logs` と同方針、DP-6 の考え方を記録系へ拡張）。

### 14.1 `attendance_rules` — 勤務体系マスタ

所定労働時間・法定休日曜日・締め日・フレックスを保持する設定系マスタ。
ルール解決は「既定ルール方式」（`AttendanceCalc.ResolveRule`）: ① `users.attendance_rule_id` が指す有効ルール →
② 有効かつ `is_default` のルール → ③ 一覧の先頭（`ORDER BY name, id`） → ④ いずれも無ければ null
（所定 480 分・法定休日=日曜 として扱う）。

| カラム | 型 | 補足 |
|---|---|---|
| `name` | `VARCHAR(128) NOT NULL` | 勤務体系名。アプリ層で 128 文字以内を検証 |
| `work_start` | `VARCHAR(5) NOT NULL DEFAULT '09:00'` | 始業 `'HH:mm'`（JST 壁時計）。ゼロ埋め固定長のため序数比較で前後判定可 |
| `work_end` | `VARCHAR(5) NOT NULL DEFAULT '18:00'` | 終業 `'HH:mm'`。`work_start < work_end` をアプリ層で強制 |
| `break_minutes` | `INTEGER NOT NULL DEFAULT 60` | 所定休憩（分）。`CHECK (break_minutes BETWEEN 0 AND 240)` |
| `flex_enabled` | `BOOLEAN NOT NULL DEFAULT FALSE` | フレックスタイム制。office の `flex` JSON を平坦化（`jsonb` を使わない）|
| `flex_core_start` | `VARCHAR(5) NULL` | コアタイム開始 `'HH:mm'`。`flex_enabled=true` のとき必須 |
| `flex_core_end` | `VARCHAR(5) NULL` | コアタイム終了 `'HH:mm'`。同上、`start < end` |
| `flex_settlement_months` | `INTEGER NOT NULL DEFAULT 1` | 清算期間（月）。`CHECK (flex_settlement_months BETWEEN 1 AND 3)` |
| `closing_day` | `INTEGER NOT NULL DEFAULT 31` | 締め日（31 = 月末）。`CHECK (closing_day BETWEEN 1 AND 31)` |
| `legal_holiday_weekday` | `INTEGER NOT NULL DEFAULT 0` | 法定休日の曜日（0=日曜 〜 6=土曜、`DayOfWeek` の数値と一致）。`CHECK (... BETWEEN 0 AND 6)` |
| `is_default` | `BOOLEAN NOT NULL DEFAULT FALSE` | 既定ルール。**テナント内で高々 1 件**（DB 制約ではなく `AttendanceRuleService` の排他制御。既定に設定すると同一 `SaveChanges` 内で他ルールの `is_default` を落とす = 中間状態を作らない）|
| `is_active` | `BOOLEAN NOT NULL DEFAULT TRUE` | 無効化（削除ではない）|
| `deleted_at` | `TIMESTAMPTZ NULL` | 論理削除（第二段階規約: `deleted_at` に統一）。論理削除時は `is_default` も false に落とす |
| 共通基底（§14.0）| | |

**UNIQUE 制約:** `uq_attendance_rules_tenant_name (tenant_id, name) WHERE deleted_at IS NULL`（部分インデックス）。
**インデックス:** `idx_attendance_rules_tenant (tenant_id)`。

### 14.2 `punch_records` — 打刻（記録系・**追記のみ**）

| カラム | 型 | 補足 |
|---|---|---|
| `user_id` | `UUID NOT NULL REFERENCES users(id)` | 打刻者 |
| `date` | `DATE NOT NULL` | **JST の業務日付**。集計はこの日付で束ねる |
| `kind` | `SMALLINT NOT NULL` | 0=In（出勤）, 1=Out（退勤）, 2=BreakStart（休憩開始）, 3=BreakEnd（休憩終了）。`CHECK (kind BETWEEN 0 AND 3)` |
| `at` | `TIMESTAMPTZ NOT NULL` | 打刻時刻（**UTC 格納**）。表示・深夜帯判定は `SystemTime.ToJst` を通す |
| `source` | `SMALLINT NOT NULL DEFAULT 0` | 0=Web, 1=Mobile, 2=Fix（修正申請の承認による追記）。`CHECK (source BETWEEN 0 AND 2)` |
| `fixed_from` | `TIMESTAMPTZ NULL` | 置換した旧打刻の `at`（UTC）。NULL = 通常打刻 |
| `fix_reason` | `VARCHAR(512) NULL` | 修正理由（客観的記録の担保）。修正打刻のみ |
| `approved_by_user_id` | `UUID NULL REFERENCES users(id)` | 修正を承認したオーナー。修正打刻のみ |
| `created_at` | `TIMESTAMPTZ NOT NULL DEFAULT NOW()` | **`updated_at` は持たない**（追記のみのため §14.0） |
| 共通基底の `id` / `tenant_id` | | |

**インデックス:** `idx_punch_records_user_date (tenant_id, user_id, date)`, `idx_punch_records_date (tenant_id, date)`。

> **設計判断（記録系の保護、CLAUDE.md 原則2 / DP-6）:**
> - **本テーブルは追記のみ。UPDATE / DELETE を行わない。** 訂正は打刻修正申請の承認による**論理置換**で表現する:
>   `source=2(Fix)` の行を**追記**し、`fixed_from` に置換対象の旧打刻の `at` を入れる。**元打刻は削除しない。**
> - そのため `updated_at` 列を持たず（`09-updated-at-triggers.sql` の対象外）、アプリロールからも
>   `UPDATE` / `DELETE` を REVOKE している（DB レベルの二重防壁）。
> - 「いま有効な打刻列」は `AttendanceCalc.EffectivePunches` が導出する（**レコード Id 単位で 1 件だけ無効化**する
>   アルゴリズム。`at` でのグルーピングでは修正の連鎖・元時刻への差戻しで破綻するため）。
>   有効打刻は SoT から毎回導出する派生値であり、DB には持たない。
> - 打刻の直列化（二重打刻防止）は `users` 行の悲観ロック（`SELECT 1 FROM users WHERE id = {0} FOR UPDATE`、
>   パラメータ化必須）で行う。office の `pg_advisory_xact_lock` 相当。

### 14.3 `attendance_fix_requests` — 打刻修正申請

| カラム | 型 | 補足 |
|---|---|---|
| `user_id` | `UUID NOT NULL REFERENCES users(id)` | 申請者（常に本人。API は body で他人を指定させない）|
| `date` | `DATE NOT NULL` | 修正対象の業務日付（JST）|
| `kind` | `SMALLINT NOT NULL` | 修正対象の打刻種別（§14.2 と同じ値域）。`CHECK (kind BETWEEN 0 AND 3)` |
| `requested_at` | `TIMESTAMPTZ NOT NULL` | 修正後の打刻時刻（**UTC 格納**。API はタイムゾーン付き文字列で受け取り UTC 化）|
| `reason` | `VARCHAR(512) NOT NULL` | 修正理由（必須。空白のみは 422）|
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Pending, 1=Approved, 2=Rejected。`CHECK (status BETWEEN 0 AND 2)` |
| `decided_by_user_id` | `UUID NULL REFERENCES users(id)` | 承認/却下したオーナー |
| 共通基底（§14.0）| | |

**インデックス:** `idx_afr_status (tenant_id, status)`, `idx_afr_user_date (tenant_id, user_id, date)`。

> **設計判断:** 承認処理はトランザクション内で `SELECT ... FOR UPDATE`（本テーブル行の悲観ロック）→
> `status` 再確認 → `punch_records` へ修正打刻を**追記** → 申請の `status` 更新、の順で行う。
> `status != Pending` の 409 判定を必ずトランザクション内で行うことで二重承認を防ぐ。
> 監査ログ（`AttendanceFixRequest.Approve` / `.Reject`）は commit 後に記録する。

### 14.4 `leave_types` — 休暇種別マスタ

| カラム | 型 | 補足 |
|---|---|---|
| `name` | `VARCHAR(64) NOT NULL` | 種別名 |
| `grant_method` | `SMALLINT NOT NULL DEFAULT 1` | 0=Periodic（周期自動付与）, 1=Manual（手動付与）。`CHECK (grant_method BETWEEN 0 AND 1)`。Periodic の種別は手動付与不可（422）|
| `expiry_months` | `INTEGER NULL` | 失効までの月数。**NULL = 無期限**。`CHECK (expiry_months IS NULL OR expiry_months BETWEEN 1 AND 120)` |
| `is_statutory` | `BOOLEAN NOT NULL DEFAULT FALSE` | 法定有給（労基法 39 条）。**API では受け取らない**（改竄防止）。true の種別は作成・編集・論理削除を禁止（409）|
| `description` | `VARCHAR(255) NOT NULL DEFAULT ''` | 説明 |
| `display_order` | `INTEGER NOT NULL DEFAULT 1` | 表示順（一覧・サマリの並び順の第 1 キー）|
| `is_active` | `BOOLEAN NOT NULL DEFAULT TRUE` | 無効化 |
| `deleted_at` | `TIMESTAMPTZ NULL` | 論理削除 |
| 共通基底（§14.0）| | |

**UNIQUE 制約:** `uq_leave_types_tenant_name (tenant_id, name) WHERE deleted_at IS NULL`。
**インデックス:** `idx_leave_types_tenant (tenant_id)`。

**シード（`db/init/10-attendance.sql` §2 / `iter30-attendance.sql` §4、全テナント・冪等）:**
法定有給を 1 件だけ投入する — `name='有給休暇'`, `grant_method=0`(Periodic), `expiry_months=24`（時効 2 年）,
`is_statutory=true`, `description='労働基準法 39 条の年次有給休暇 (時効 2 年)'`, `display_order=1`。
`NOT EXISTS` ガード付き INSERT のため、再実行しても既存行を更新・削除しない。

### 14.5 `leave_grants` — 休暇の付与（個別 / 一括 / 周期自動）

| カラム | 型 | 補足 |
|---|---|---|
| `user_id` | `UUID NOT NULL REFERENCES users(id)` | 付与対象 |
| `leave_type_id` | `UUID NOT NULL REFERENCES leave_types(id)` | 休暇種別 |
| `grant_date` | `DATE NOT NULL` | 付与日（JST）。**冪等キーの一部** |
| `days` | `NUMERIC(4,1) NOT NULL` | 付与日数（0.5 刻み）。`CHECK (days > 0)` |
| `kind` | `SMALLINT NOT NULL DEFAULT 2` | 0=Normal（通常付与）, 1=Proportional（比例付与）, 2=Special（特別付与 = 手動付与）。`CHECK (kind BETWEEN 0 AND 2)` |
| `expire_date` | `DATE NOT NULL` | 失効日（この日以降は引当不可）。**無期限は `9999-12-31`** |
| `granted_by_user_id` | `UUID NULL REFERENCES users(id)` | 付与操作をしたオーナー。**NULL = 周期自動付与**（履歴表示の「通常付与」「比例付与」の判定に使う）|
| 共通基底（§14.0）| | |

**UNIQUE 制約:** `uq_leave_grants_tenant_user_type_date (tenant_id, user_id, leave_type_id, grant_date)`。
**インデックス:** `idx_leave_grants_user (tenant_id, user_id, leave_type_id)`。

> **設計判断（冪等一意制約の意図、CLAUDE.md 原則2）:**
> `(tenant_id, user_id, leave_type_id, grant_date)` の UNIQUE は、**周期自動付与
> （`POST /attendance/leave/periodic-grants/run`）を何度実行しても二重付与が発生しない**ことを DB レベルで保証する
> ための冪等キーである。「入社日 + 6ヶ月、以降 1 年ごと」という付与日は入社日から決定論的に再計算できるため、
> 付与日を冪等キーに含めれば再実行が自然に冪等になる。
> - 付与処理は**挿入のみ**。既存の付与行を更新・削除しない（記録系保護）。重複は `skipped` として件数だけ返す。
> - EF Core からは `ON CONFLICT` を直接発行できないため、主経路は「既存の (user_id, leave_type_id, grant_date) を
>   事前 SELECT して除外してから `AddRange`」。DB の UNIQUE は競合時の最終防壁。
> - 個別付与（`POST /leave/grants`）も同じ判定を行い、既存があれば**既存レコードの Id** と `skipped=1` を返す。

### 14.6 `leave_requests` — 休暇申請（1 行 = 1 日分）

| カラム | 型 | 補足 |
|---|---|---|
| `user_id` | `UUID NOT NULL REFERENCES users(id)` | 申請者（常に本人）|
| `leave_type_id` | `UUID NOT NULL REFERENCES leave_types(id)` | 休暇種別 |
| `date` | `DATE NOT NULL` | 取得日（JST）|
| `unit` | `SMALLINT NOT NULL DEFAULT 0` | 0=Full（全日 1.0 日）, 1=Half（半日 0.5 日）。`CHECK (unit BETWEEN 0 AND 1)` |
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Pending, 1=Approved, 2=Rejected。**残数を消化するのは Approved のみ**。`CHECK (status BETWEEN 0 AND 2)` |
| `reason` | `VARCHAR(255) NOT NULL DEFAULT ''` | 理由（任意）|
| `decided_by_user_id` | `UUID NULL REFERENCES users(id)` | 承認/却下したオーナー |
| 共通基底（§14.0）| | |

**インデックス:** `idx_leave_requests_user_date (tenant_id, user_id, date)`, `idx_leave_requests_status (tenant_id, status)`。

> **設計判断:** 残数は `leave_grants`（付与）と Approved の `leave_requests`（消化）から
> **FIFO 引当で毎回導出**する（`LeaveCalc`）。残数列は持たない（SoT からの導出値をキャッシュしない）。
> 同一利用者・同一日付で Pending / Approved の申請が既にある場合は 409（種別は問わない）。
> Rejected は再申請をブロックしない。

### 14.7 移植の除外スコープと未検証事項

**除外スコープ（honshu に対応機能が無い / 別基盤依存のため移植しない）:**

| 除外した office の機能 | 理由 |
|---|---|
| 祝日マスタ・営業日計算（内閣府 CSV 取込、`workingWeekdays` / `holidayAware`）| 翌営業日計算専用の機能であり、honshu には翌営業日計算を使う機能が無い。日次集計の法定休日判定は**曜日**（`attendance_rules.legal_holiday_weekday`）で行うため不要。外部 HTTP 依存を増やさない判断も含む |
| AI 参照範囲（`ai-scope`）・チャットボット・日報連携・通知（`notifyAdmins`）・エスカレーション | honshu に対応機能が無い |
| 権限ルールマトリクスエンジン（subjectKind × resource × field の allow/deny）| honshu の 4+1 権限カテゴリ方式に置換。勤怠権限 `users.attendance_permission`（0/1/2）を 5 つ目のカテゴリとして追加し、管理系は既存のオーナー権限 `process_record_permission >= 1` に集約した（office の hr 中間ロールは作らない = 権限を緩めない方向で統合）|
| 雇用区分（`employment_type`）| 勤怠ルールの解決を「既定ルール方式」（§14.1）に、有給の付与判定を `users.weekly_days` / `weekly_hours` による比例付与判定に置き換えたため、区分マスタが不要になった |

**未検証事項:**
- 本改修を行った環境では **.NET SDK を取得できず、バックエンドをローカルでコンパイル検証できていない**。
  既存コードの記述パターンを逐語的に踏襲することで代替している（型・API の検証は CI に委ねる）。
- `docs/api/openapi.json` は**未再生成**。CI の `regen-openapi` ワークフロー
  （main 向け PR で自動再生成し、生成物を head ブランチへ自動コミットする）に委ねる。

---

## 15. 変更履歴

| 日付 | 内容 |
|---|---|
| 2026-05-19 | 初版作成（18 マスタ + 商品 4 + 発注 3 + 監査 1 = 計 26 テーブル、機能要件 21 全対応）|
| 2026-07-27 | Iteration 30: 勤怠 6 テーブル（`attendance_rules` / `punch_records` / `attendance_fix_requests` / `leave_types` / `leave_grants` / `leave_requests`）を §14 に追加。§3.18 `users` に勤怠列 6 本（`attendance_permission` / `punch_required` / `attendance_rule_id` / `hire_date` / `weekly_days` / `weekly_hours`）を追記、§2 ERD 概観に勤怠エンティティ群を追加（akebono-office からの移植）|
