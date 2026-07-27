# Phase 5 成果物: API 設計

> **作成日:** 2026-05-19
> **状態:** ドラフト v1（オペレーターレビュー前）
> **依存:** Phase 3 機能要件（21機能 + エラーコード体系）+ Phase 4 確定スタック（ASP.NET Core 8 Minimal API + Firebase Auth + OpenAPI 3.0）
>          + `architecture.md` + `data-design.md`
> **方針:** REST + JSON。1 API = 1 責務（癒着回避）、クライアントに集約・加工責務を押し付けない。
>          Phase 5 ゲート条件「API 設計に癒着がない」「全データフローが I/F レベルで検証済み」を充足する。

> **【運用ルール】バックエンドの API を変更したら、同一コミットで本ドキュメントを更新すること（CLAUDE.md 開発原則 5）。**
>
> 実装だけ先に直してドキュメントを後回しにすると、本ドキュメントが**「修正済みのバグ」を仕様として記述し続ける**状態になり、
> 次の改修者がその記述を根拠にバグを再導入する。
> 実際に Iteration 30 では、ドキュメント整合コミット（`ce30be6`）の**後**にバックエンド修正コミット（`4c6981e`、`.md` の変更 0 件）が入り、
> §2.7 のタイムカード期間上限がオフバイワンのバグを「実装の現状」として正規化したまま残った（2026-07-27 に訂正済み。§8 変更履歴）。
>
> API を変更する PR では、`src/Backend/Presentation/Endpoints/` および `src/Backend/Application/` の差分に対応する
> 本ドキュメントの節を、**同じコミットで**更新する。バグ修正の場合、修正前の挙動を「実装の現状」として残さず、
> 修正後の事実へ書き換える（Push 前セルフチェック 5「ドキュメント」/ 6「波及範囲」）。

---

## 1. 共通規約

### 1.1 URL / バージョニング

```
https://<app-runner-domain>/api/v1/<resource>[/<id>[/<sub-resource>]]
```

- バージョンは URL 埋め込み (`/api/v1/`)。破壊的変更は `/api/v2/` で並行運用
- リソース名は **複数形** (`/products`, `/purchase-orders`)
- ハイフン区切り（snake_case ではなく kebab-case を URL では採用、慣習）
- ID は数値（BIGINT）

### 1.2 認証・認可

| ヘッダ | 値 |
|---|---|
| `Authorization` | `Bearer <Firebase ID Token>` |
| `Content-Type` | `application/json; charset=utf-8`（リクエスト）|
| `Accept` | `application/json`（既定）または `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`（Excel）|

- 全エンドポイントで `[Authorize]` 必須（CI Lint で強制、R-6）
- 公開エンドポイント（認証不要）: なし。MVP は全ログイン前提
- Custom Claims `permissions[]` で各エンドポイントの細粒度認可（§2 各セクションで定義）

### 1.3 共通レスポンス形式

#### 1.3.1 成功

```json
{
  "data": { ... } または [ ... ],
  "meta": {
    "request_id": "trace-id-from-x-ray",
    "timestamp": "2026-05-19T10:00:00Z"
  }
}
```

#### 1.3.2 一覧（ページング）

```json
{
  "data": [ ... ],
  "meta": {
    "request_id": "...",
    "timestamp": "...",
    "pagination": {
      "page": 1,
      "per_page": 50,
      "total_count": 1234,
      "total_pages": 25
    }
  }
}
```

#### 1.3.3 エラー（Problem Details, RFC 7807 準拠）

```json
{
  "type": "https://api.akebono.jp/errors/PROD-001",
  "title": "SKU 重複",
  "status": 409,
  "detail": "SKU 'FA20710F1110' は既に登録されています",
  "code": "PROD-001",
  "instance": "/api/v1/products",
  "errors": [
    { "field": "sku", "code": "PROD-001", "message": "..." }
  ],
  "trace_id": "..."
}
```

- `code` は Phase 3 §10 エラーコード（AUTH-NNN / PROD-NNN / ORDER-NNN / MASTER-NNN / IMAGE-NNN / EXPORT-NNN / USR-NNN / PRICE-NNN）に必ず付与
- バリデーション複数エラーは `errors[]` 配列で返却

### 1.4 HTTP ステータス規約

| 範囲 | 用途 |
|---|---|
| 200 | 取得・更新成功 |
| 201 | 作成成功（`Location` ヘッダで新リソース URL）|
| 204 | 削除成功・本文なし |
| 400 | リクエスト形式不正（JSON パース失敗等）|
| 401 | 未認証（AUTH-002 等）|
| 403 | 認可拒否（AUTH-005）|
| 404 | リソース未存在 |
| 409 | 衝突（コード重複、状態不整合）|
| 422 | バリデーションエラー（必須項目欠落、形式不正）|
| 500 | サーバ内部エラー |
| 503 | DB / 外部依存停止 |

### 1.5 ページング・ソート・フィルタ規約

| パラメータ | 例 | 補足 |
|---|---|---|
| `page` | `?page=1` | 1 オリジン、デフォルト 1 |
| `per_page` | `?per_page=50` | デフォルト 50、最大 200 |
| `sort` | `?sort=-updated_at,sku` | カンマ区切り、`-` プレフィックスで降順 |
| `q` | `?q=サンダル` | フリーワード検索 |
| `filter[<field>]` | `?filter[status]=Active&filter[brand_id]=12` | フィールド別フィルタ |
| `include_deleted` | `?include_deleted=true` | 論理削除済を含む（デフォルト false）|

### 1.6 冪等性

| メソッド | 冪等性 | 補足 |
|---|---|---|
| GET / HEAD | 冪等 | |
| PUT / DELETE | 冪等 | |
| POST | 非冪等 | 二重発行リスクのある操作（バルク商品登録、発注作成、発注書 Excel 初回出力等）は `Idempotency-Key` ヘッダで担保（クライアントが UUID 生成して同じキーで再送可）|

### 1.7 監査対象

- 全 C/U/D + 認証関連 + Excel 出力 + 仕入単価閲覧 → `audit_logs` に自動記録（`AuditLogInterceptor`）
- GET 一覧・検索は記録しない（C-03 BR）

---

## 2. エンドポイント設計

### 2.1 認証・セッション（C-01）

> Firebase Authentication が認証主体。本セクションはサーバ側補助エンドポイント。

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `POST` | `/api/v1/auth/sync` | フロントでログイン直後に呼び、Firebase UID から RDS users を取得・必要時に同期 | 認証済 |
| `GET` | `/api/v1/auth/me` | 現在のユーザ情報 + 権限を返却 | 認証済 |
| `POST` | `/api/v1/auth/logout` | サーバ側ログアウト記録（audit_logs `Login.Logout` 記録、Firebase 側のサインアウトはフロントで実施）| 認証済 |

> ログイン本体（`signInWithEmailAndPassword`）・パスワードリセット・MFA 等は Firebase SDK で完結（バックエンド経由なし）。

#### POST /api/v1/auth/sync

ログイン直後に呼ぶ。Firebase UID から RDS users をルックアップ、未登録なら 403 USR-001（ユーザ未登録）。

**Request:**
```json
{}
```
（ID Token から UID を取得するためボディ不要）

**Response 200:**
```json
{
  "data": {
    "user_id": 42,
    "firebase_uid": "abc123...",
    "employee_no": "001",
    "display_name": "今尾 雅広",
    "permissions": {
      "product_ledger": 1,
      "purchase_order_create": 1,
      "purchase_order_info": 1
    },
    "is_active": true
  }
}
```

**エラー:**
- 403 USR-001: Firebase Auth に登録あるが RDS users に未登録（オペレーター手動連携必要）
- 403 USR-002: `is_active = false`（無効化ユーザ）

#### GET /api/v1/auth/me

`auth/sync` と同じ内容を返す（フロント側の状態回復用）。Custom Claims から取得すれば不要だが、フォールバックとして用意。

---

### 2.2 ユーザ管理（M-03）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/users` | 一覧 | `user:read` |
| `GET` | `/api/v1/users/{id}` | 詳細 | `user:read` |
| `POST` | `/api/v1/users` | 新規（Firebase Auth + RDS 両側に作成）| `user:write`（管理者）|
| `PATCH` | `/api/v1/users/{id}` | 更新（display_name, employee_no 等の業務情報） | `user:write` |
| `PATCH` | `/api/v1/users/{id}/permissions` | 権限変更（RDS 先行 → Firebase Custom Claims 後追い、§Arch §4.5）| `user:write` |
| `POST` | `/api/v1/users/{id}/deactivate` | 無効化（is_active=false + Firebase disabled=true） | `user:write` |
| `POST` | `/api/v1/users/{id}/activate` | 再有効化 | `user:write` |
| `DELETE` | `/api/v1/users/{id}` | 論理削除（is_deleted=true、Firebase 側も disabled）| `user:write` |

> **本節は初版ドラフトのままで実装と乖離している（未整備）。** 実装のパスは `/api/maker/v1/users`、ID は UUID、
> JSON は camelCase、認可は `user:read` / `user:write` スコープではなく**オーナー**（`process_record_permission >= 1`）。
> **`PATCH /api/maker/v1/users/{id}` の現行契約（`UserPatchRequest` による部分更新 = `null` は現在値保持、
> `clearHireDate` / `clearAttendanceRule` の明示クリアフラグ、`email` は空文字でクリア）は §2.7.9 に記載している。**
> 本節の全面改訂は別途行うこと（実装 SoT: `src/Backend/Presentation/Endpoints/UserEndpoints.cs`）。

#### POST /api/v1/users

**Request:**
```json
{
  "employee_no": "001",
  "login_id": "owner",
  "display_name": "今尾 雅広",
  "email": "imai@example.com",
  "initial_password": "...",
  "is_planning_staff": false,
  "is_sales_staff": false,
  "permissions": {
    "product_ledger": 1,
    "purchase_order_create": 1,
    "purchase_order_info": 1,
    "process_record": 0
  }
}
```

**処理順序（CLAUDE.md 原則6）:**
1. RDS `users` に INSERT（firebase_uid は一時 NULL）
2. Firebase Admin SDK `createUser` で Firebase Auth に作成
3. Firebase Admin SDK `setCustomUserClaims` で permissions 設定
4. RDS `users.firebase_uid` を UPDATE（取得した UID）
5. Steps 2-4 失敗時: RDS users を `is_active=false` でマークし USR-003 返却（手動修復必要）

**Response 201:** `Location: /api/v1/users/42`

#### PATCH /api/v1/users/{id}/permissions

**Request:**
```json
{
  "product_ledger": 2,
  "purchase_order_create": 1,
  "purchase_order_info": 0,
  "process_record": 0
}
```

**処理:** §Arch §4.5 シナリオ E と同じ（RDS 先行 → Firebase Custom Claims 後追い、失敗時は Reconciler）。

---

### 2.3 マスタ CRUD（M-01 / M-02、18マスタ共通）

> 共通エンドポイントテンプレート。`{master}` には 17 種類のリソース名（`sizes`, `brands`, `functions`, `countries`, `suppliers`, `departments`, `product-types`, `product-seasons`, `product-groups`, `colors`, `materials`, `material-classifications`, `warehouses`, `delivery-destinations`, `document-template-purchases`, `document-template-confirmations`, `document-text-purchases`）が入る。ユーザは §2.2 で個別定義済。

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/masters/{master}` | 一覧（M-01）| `master:read` |
| `GET` | `/api/v1/masters/{master}/{id}` | 詳細 | `master:read` |
| `POST` | `/api/v1/masters/{master}` | 新規（コード重複・FK 整合性チェック）| `master:write` |
| `PATCH` | `/api/v1/masters/{master}/{id}` | 更新 | `master:write` |
| `DELETE` | `/api/v1/masters/{master}/{id}` | 論理削除（delete_flag=true）| `master:write` |
| `POST` | `/api/v1/masters/{master}/{id}/restore` | 論理削除取消 | `master:write` |
| `GET` | `/api/v1/masters/{master}/{id}/usage` | **削除前の参照件数取得（Phase 6 で F-20 対応、確定）**| `master:read` |

#### GET /api/v1/masters/{master}

**クエリ:** `?page=1&per_page=50&sort=code&q=しまむら&include_deleted=false`

**Response 200（例: delivery-destinations）:**
```json
{
  "data": [
    {
      "id": 1,
      "code": "001",
      "name": "しまむらセンター",
      "customer_name": "しまむら",
      "remark_1": "〒...",
      "remark_2": "03-...",
      "remark_3": "",
      "delete_flag": false,
      "updated_at": "2026-05-15T10:00:00Z",
      "updated_by": { "id": 1, "display_name": "今尾 雅広" }
    }
  ],
  "meta": { "pagination": { ... } }
}
```

**Response 200（例: suppliers、FK あり）:**
```json
{
  "data": [
    {
      "id": 5,
      "code": "S001",
      "name": "○○商事",
      "country": { "id": 3, "name": "日本" },
      "delete_flag": false,
      "updated_at": "2026-05-15T10:00:00Z",
      "updated_by": { "id": 1, "display_name": "今尾 雅広" }
    }
  ],
  "meta": { "pagination": { ... } }
}
```

**Response 200（例: materials、FK あり）:**
```json
{
  "data": [
    {
      "id": 12,
      "code": "M001",
      "name": "綿 100%",
      "material_classification": { "id": 2, "name": "天然繊維" },
      "delete_flag": false,
      "updated_at": "2026-05-15T10:00:00Z",
      "updated_by": { "id": 1, "display_name": "今尾 雅広" }
    }
  ],
  "meta": { "pagination": { ... } }
}
```

> **FK 名結合表示方針（Phase 6 確定、F-18 対応）:**
> - マスタ間 FK は `{ id, name }` の **ネスト構造** でレスポンス返却（例: `country_id` → `country: { id, name }`）
> - サーバ側で EF Core `Include` 一括取得により N+1 を回避
> - **17 マスタのうち FK を持つのは 2 マスタのみ:** `suppliers.country_id` → `country` / `materials.material_classification_id` → `material_classification`。他 15 マスタは既存のフラットレスポンスのまま
> - フロント側で別途名前解決 API を呼ぶ必要なし。共通 DataTable コンポーネントは `column.name` を表示するだけで完結
> - 新規 FK 追加時は本セクションのレスポンス例 + EF Core Include に各 1 行追記で対応

#### POST /api/v1/masters/{master}

**エラー:**
- 409 MASTER-001: code 重複
- 422 MASTER-002: FK 不整合（例: 存在しない `country_id` を suppliers に指定）

> **実装方針:** `MasterEntity` を IMaster インターフェースで抽象化、`MasterController<TEntity, TDto>` ジェネリックで共通実装。各マスタ固有の拡張カラム（suppliers.country_id 等）は `IValidator<TDto>` で検証。

#### GET /api/v1/masters/{master}/{id}/usage

> **Phase 6 で F-20 対応として新設、確定。** 削除前にマスタ管理者へ参照件数を表示し、誤削除リスクを低減する。

**処理:**
1. 各マスタの参照テーブル定義 (`IMasterUsage` インターフェース) から COUNT 集計
2. 件数を集約して返却（参照ゼロのマスタも対象、その場合 `usage: {}`）

**Response 200（例: suppliers）:**
```json
{
  "usage": {
    "products": 15,
    "product_supplier_prices": 42,
    "purchase_orders": 30
  }
}
```

**Response 200（参照ゼロ時）:**
```json
{
  "usage": {}
}
```

> **実装方針:** マスタごとの参照テーブル定義は `IMasterUsage<TEntity>` インターフェースで集約。例: `SupplierUsage` は `[Products, ProductSupplierPrices, PurchaseOrders]` を宣言、EF Core で COUNT クエリを並列実行。新規 FK 追加時は該当 `IMasterUsage` 実装に 1 行追加で対応。

> **F-20 解消方針:** 共通テンプレートで 17 マスタ全てを 1 エンドポイント定義でカバー。レスポンス形式は参照テーブル名 → 件数のシンプルなマップ構造、フロント側で「商品 15 件 / 発注書 30 件」等に整形表示。件数ゼロ時は「使用中の業務データはありません」と緑色バッジで安全削除を促す。

---

### 2.4 商品（P-01〜P-06）

#### P-01〜P-03: 商品マスタ新規登録 + サイズ展開 + 仕入単価（バルク登録）

> **Phase 6 修正:** F-06 ロールバック問題への対応として、新規ウィザードは **単一バルク登録エンドポイント** で 1 トランザクション完結。既存の個別エンドポイント (`POST /families`, `POST /expand`, `POST /supplier-prices`) は **P-05 編集機能専用** として残置（再利用）。

| メソッド | パス | 用途 |
|---|---|---|
| `POST` | `/api/v1/products/families/complete` | **新規ウィザード一括登録**（family + products + supplier_prices を 1 トランザクション） |
| `POST` | `/api/v1/products/families` | 企画親のみ新規（編集機能用、Phase 5 後半 or Phase 7 で要否再判断）|
| `POST` | `/api/v1/products/families/{familyId}/expand` | サイズ追加展開（既存企画に色/サイズを追加する編集用途）|
| `GET` | `/api/v1/products/families/{familyId}/preview-sku` | 仮品番プレビュー（9桁 + 候補連番、ウィザード Step 1 用） |

##### POST /api/v1/products/families/complete

**認可:** `product:write` AND `price:write`

**Request:**
```json
{
  "family": {
    "planned_year_code": "F",
    "product_type_id": 12,
    "product_season_id": 3,
    "brand_id": 5,
    "function_id": 2,
    "product_group_id": 7,
    "upper_material_id": 21,
    "insole_material_id": 22,
    "outsole_material_id": 23,
    "factory_supplier_id": 14,
    "product_name_1": "婦人サンダル A",
    "product_name_2": "春夏 新作"
  },
  "expansion": {
    "color_ids": [11, 12, 13],
    "size_ids": [1, 2, 3, 4]
  },
  "supplier_prices": [
    {
      "supplier_id": 14,
      "unit_price": 1250.00,
      "currency_code": "JPY",
      "exchange_rate": null,
      "effective_from": "2026-06-01",
      "decided_at": "2026-05-19"
    }
  ]
}
```

**処理（単一トランザクション内）:**
1. FluentValidation でリクエスト全体検証
2. `product_families` INSERT（sequence_no 採番含む）
3. 色 × サイズ全組合せで `products` バルク INSERT（11桁 SKU 生成）
4. `product_supplier_prices` バルク INSERT（アイテム単位、複数仕入先対応）
5. `audit_logs` INSERT（action=ProductFamilyCreated、子エンティティ件数を summary に記録）
6. コミット

**Response 201:**
```json
{
  "data": {
    "family": { "id": 42, "sequence_no": "071", ... },
    "products": [ {"id": 1, "sku": "FA20710F1101", ...}, ... ],
    "supplier_prices": [ {"id": 1, ...} ]
  }
}
```

**エラー（全体ロールバック）:**
- 422 PROD-002: 必須項目欠落、色/サイズ未指定
- 409 PROD-003: SKU 重複（同一企画キーの再採番衝突、自動リトライで吸収）
- 409 PRICE-001: 仕入単価重複
- 422 PRICE-002: unit_price <= 0
- 422 PROD-005: family/expansion/supplier_prices の整合性違反（factory_supplier_id が supplier_prices に含まれない等）

**冪等性:** `Idempotency-Key` ヘッダ必須（API-8 ポリシー、ネットワーク失敗時の二重生成防止）。

> **設計判断:**
> - F-06 ロールバック問題を解消（中途半端な family + products + prices が DB に残らない）
> - フロントは Step 4 確認画面で一括送信、Step 1-3 はクライアント側状態管理（Pinia store）のみ
> - ネットワーク失敗時はクライアント側に入力データが保持されているため再送可能
> - ペイロードサイズ概算: family 1KB + products 50件 × 0.3KB + supplier_prices 3件 × 0.3KB ≒ 17KB（軽量）

##### POST /api/v1/products/families

**認可:** `product:write`

**Request:**
```json
{
  "planned_year_code": "F",
  "product_type_id": 12,
  "product_season_id": 3,
  "brand_id": 5,
  "function_id": 2,
  "product_group_id": 7,
  "upper_material_id": 21,
  "insole_material_id": 22,
  "outsole_material_id": 23,
  "factory_supplier_id": 14,
  "product_name_1": "婦人サンダル A",
  "product_name_2": "春夏 新作"
}
```

**処理:**
1. 必須項目バリデーション → 422 PROD-002（PRODUCT-001 から改名・Phase 3 §10 整合）
2. 同一企画キー（year+type+season+factory+sequence）の重複は `sequence_no` 採番ロジックで回避
3. `product_families` INSERT
4. audit_logs INSERT

**Response 201:**
```json
{
  "data": {
    "id": 42,
    "preview_sku_prefix": "FA20710F",
    "sequence_no": "071",
    "status": "Draft"
  }
}
```

##### POST /api/v1/products/families/{familyId}/expand

**認可:** `product:write`

**Request:**
```json
{
  "color_ids": [11, 12, 13],
  "size_ids": [1, 2, 3, 4]
}
```

**処理:**
1. 色×サイズの全組合せで 11桁 SKU を生成（家系プレフィックス9桁 + color.item_conversion_code + size.item_conversion_code）
2. `products` バルク INSERT（UNIQUE 制約で 2 重生成防止）
3. 50 SKU で 500ms 以内（NFR §1.1）

**Response 201:**
```json
{
  "data": {
    "generated_count": 12,
    "skus": ["FA20710F1101", "FA20710F1102", ...]
  }
}
```

**エラー:**
- 409 PROD-003: 同一企画内 SKU 重複（既存スキーマ衝突）→ Phase 5 プロトタイプ検証（既存仕様の Phase 2 残課題、E2E ドキュメントトレースで `sequence_no` 再採番ロジックを検証予定）
- 422 PROD-002: 色/サイズ未指定

##### GET /api/v1/products/families/{familyId}/preview-sku

P-01 入力中の動的プレビュー用。読み取り専用、軽量。

#### P-03: マルチ仕入先単価（アイテム単位 + サイズ別）

> **Phase 6 修正:** 仕入単価は **アイテム (product_family) 単位** で管理（旧設計の SKU 単位から変更）。
> **PR2 追補（設計判断Q4=サイズ別必要）:** `size_id`（任意）で**サイズ別単価**に対応。未指定（NULL）= 全サイズ共通の既定単価（従来挙動、下位互換）、指定 = そのサイズ専用単価（既定をオーバーライド）。BR-04 有効日履歴は size 次元込みで維持。

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/products/families/{familyId}/supplier-prices` | 一覧（履歴含む、`size_id` / `size_name` を返却）|
| `POST` | `/api/v1/products/families/{familyId}/supplier-prices` | 新規（同一サイズバケットの既存 effective_to を自動更新）|
| `PATCH` | `/api/v1/products/families/{familyId}/supplier-prices/{priceId}` | 更新（誤入力修正、監査記録）|
| `DELETE` | `/api/v1/products/families/{familyId}/supplier-prices/{priceId}` | 論理削除 |

##### POST /api/v1/products/families/{familyId}/supplier-prices

**認可:** `product:write` AND `price:write`（機密度 中-高 NFR §6.2）

**Request:**
```json
{
  "supplier_id": 14,
  "size_id": null,
  "unit_price": 1250.00,
  "currency_code": "JPY",
  "exchange_rate": null,
  "effective_from": "2026-06-01",
  "decided_at": "2026-05-19"
}
```

> `size_id` は任意（末尾追加 = 下位互換）。NULL = 全サイズ共通の既定単価、非 NULL = そのサイズ専用単価。

**処理:**
1. トランザクション開始
2. 同一 `(product_family_id, supplier_id, size_id バケット)` の現在有効レコード（`effective_to IS NULL`）の `effective_to` を `new.effective_from - 1day` で UPDATE（`size_id=NULL` も 1 バケット。size 専用単価の新設で全サイズ既定をクローズしない、逆も同様）
3. 新レコード INSERT
4. audit_logs INSERT（**unit_price は "***" にマスク**、operator/product_family/supplier/size のみ記録）
5. コミット

**エラー:**
- 409 PRICE-001: `(product_family_id, supplier_id, COALESCE(size_id,-1), effective_from)` 重複
- 422 PRICE-002: unit_price <= 0
- 422 PRICE-003: effective_to <= effective_from

> **発注時の単価スナップショット:** 発注作成（§2.5）で各明細行の `unit_price_snapshot` は**クライアント入力値をそのまま凍結**する（サーバ側で引当て・上書きはしない。「単価未決定」= `unit_price_snapshot <= 0` の状態も保持される）。入力補助として size-aware な現単価サジェスト `GET /api/v1/orders/price-suggestion?productId=&supplierId=` を提供する: SKU の `product_id` → 親 `product_family_id` と `size_id` を解決し、「(family, supplier, SKUのsize) の現単価 → 無ければ (…, NULL-size 既定) の現単価」のフォールバックでサジェスト（読取専用、見つからなければ `found=false`）。

#### P-04: 商品マスタ一覧・検索

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/products` | 一覧・検索（カード/テーブル共通データソース）|

##### GET /api/v1/products

**認可:** `product:read`

**クエリ:**
- `?q=<text>` フリーワード（sku, product_name で部分一致）
- `?filter[planned_year_code]=F`
- `?filter[season_id]=3`
- `?filter[product_type_id]=12`
- `?filter[status]=Active`
- `?include_deleted=false`
- `?sort=-updated_at` （デフォルト）
- `?page=1&per_page=50`

**Response 200:**
```json
{
  "data": [
    {
      "id": 42,
      "family_id": 42,
      "sku": "FA20710F1110",
      "product_name_1": "婦人サンダル A",
      "product_name_2": "春夏 新作",
      "product_type": { "id": 12, "name": "吊込W底 婦人" },
      "season": { "id": 3, "name": "春夏" },
      "color": { "id": 11, "name": "ピンク" },
      "size": { "id": 1, "name": "S" },
      "status": "Active",
      "primary_image": {
        "url": "https://signed-url...",
        "thumb_url": "https://signed-url..."
      },
      "image_count": 4,
      "sku_variation_count": 12,
      "price_range": {
        "min": 1100.00,
        "max": 1400.00,
        "currency": "JPY"
      },
      "updated_at": "2026-05-19T10:00:00Z",
      "updated_by": { "id": 1, "display_name": "今尾 雅広" }
    }
  ],
  "meta": { "pagination": { ... } }
}
```

> **設計判断:** P-04.a カード / P-04.b テーブルは **同じレスポンス形式**。ビュー切替はフロント側のみで完結（API は 1 つ）。
> **N+1 対策:** family → brand / season / product_type / 色 / サイズ / 代表画像 / 仕入単価 min-max を一括クエリ（EF Core `Include` + `AsSplitQuery` + SQL 集計）。
> **Pre-signed URL:** primary_image.url は 60 分有効の S3 Pre-signed URL（バッチ生成、レスポンス内で都度発行）。

#### P-05: 商品マスタ詳細・修正

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/products/families/{familyId}` | 企画詳細 + 配下 SKU 全件 + 画像 + 仕入単価 |
| `PATCH` | `/api/v1/products/families/{familyId}` | 企画情報更新 |
| `PATCH` | `/api/v1/products/{productId}` | SKU 単位更新 |
| `DELETE` | `/api/v1/products/families/{familyId}` | 企画論理削除（配下 SKU 連動削除、発注紐付があれば 409 PROD-004）|
| `DELETE` | `/api/v1/products/{productId}` | SKU 論理削除 |

##### GET /api/v1/products/families/{familyId}

詳細画面用。1 リクエストで企画 + SKU + 画像 + 仕入単価をすべて返却（フロントの状態管理を単純化）。

**Response 200:**
```json
{
  "data": {
    "family": { ...product_family のフルフィールド },
    "products": [ {"id": ..., "sku": ..., "color": ..., "size": ..., ...}, ... ],
    "images": [ {"id": ..., "order_no": 1, "url": "https://signed...", "thumb_url": "..." }, ... ],
    "supplier_prices_summary": [
      { "supplier_id": 14, "current_price": {"unit_price": 1250, "effective_from": "2026-06-01"} }
    ]
  }
}
```

#### P-06: 商品画像管理

| メソッド | パス | 用途 |
|---|---|---|
| `POST` | `/api/v1/products/families/{familyId}/images/upload-url` | アップロード用 Pre-signed URL 取得 |
| `POST` | `/api/v1/products/families/{familyId}/images` | アップロード完了後にメタデータ登録 |
| `PATCH` | `/api/v1/products/families/{familyId}/images/reorder` | 並び順変更（一括）|
| `DELETE` | `/api/v1/products/families/{familyId}/images/{imageId}` | 削除（論理）|

##### POST /api/v1/products/families/{familyId}/images/upload-url

**Request:**
```json
{
  "mime_type": "image/jpeg",
  "original_filename": "sample.jpg",
  "file_size_bytes": 1234567
}
```

**処理:**
1. バリデーション (mime_type IN [jpeg, png, webp], file_size_bytes <= 5MB)
2. 5 枚上限チェック（既存有効枚数 + 1 ≤ 5）
3. S3 Pre-signed PUT URL を生成（15 分有効）

**Response 200:**
```json
{
  "data": {
    "upload_url": "https://s3.../signed-put-url",
    "s3_key": "product-images/42/uuid.jpg",
    "expires_at": "2026-05-19T11:15:00Z"
  }
}
```

**エラー:**
- 422 IMAGE-001: file_size_bytes 超過
- 422 IMAGE-002: mime_type 不正
- 409 IMAGE-004: 上限枚数超過

##### POST /api/v1/products/families/{familyId}/images

S3 アップロード完了後、メタデータを DB に登録。

**Request:**
```json
{
  "s3_key": "product-images/42/uuid.jpg",
  "mime_type": "image/jpeg",
  "file_size_bytes": 1234567,
  "original_filename": "sample.jpg",
  "order_no": 1
}
```

**Response 201:**
```json
{
  "data": {
    "id": 100,
    "url": "https://signed-get-url...",
    "thumb_url": null
  }
}
```

> サムネ生成は非同期 Lambda（後続）→ `thumb_s3_key` が UPDATE される。

##### PATCH /api/v1/products/families/{familyId}/images/reorder

**Request:**
```json
{
  "order": [
    { "image_id": 100, "order_no": 1 },
    { "image_id": 101, "order_no": 2 }
  ]
}
```

トランザクション内で全件 UPDATE（UNIQUE 制約 `(family_id, order_no)` を満たす一時値経由が必要）。

---

### 2.5 発注（O-01〜O-07）

#### O-01 / O-02: 発注書作成

> **Phase 6 簡素化:** 状態モデルを Active / Cancelled の 2 値に簡素化（F-10/F-11 対応）。「Draft」概念は廃止し、作成直後から `status=Active`。Excel 出力はいつでも何度でも可能、`first_exported_at` で「仕入先送付済バッジ」を表示。

| メソッド | パス | 用途 |
|---|---|---|
| `POST` | `/api/v1/purchase-orders` | 発注書新規作成（O-01/O-02 共通、`status=Active` で作成）|

##### POST /api/v1/purchase-orders

**認可:** `purchase_order:write`

**Request（共通）:**
```json
{
  "supplier_id": 14,
  "delivery_destination_id": 5,
  "department_id": 1,
  "warehouse_id": 3,
  "due_date": "2026-08-15",
  "orderer_user_id": 10,
  "sub_orderer_user_ids": [11, 12, null, null, null, null],
  "manager_user_id": 9,
  "communication_text": "...",
  "source": "new_planning" or "existing_product",
  "lines": [
    {
      "product_id": 100,
      "quantity": 50,
      "unit_price_supplier_id": 14
    }
  ]
}
```

**処理:**
1. バリデーション（必須 + FK 整合 + supplier に対する unit_price 存在チェック）
2. トランザクション内で:
   - `mgmt_no` 採番（年度-連番、例: `26-00411`）
   - `purchase_orders` INSERT (status=0/Active, first_exported_at=NULL)
   - 各 line について `product_supplier_prices` （`product_family_id` 経由）から現在有効単価を引当て → `purchase_order_lines` バルク INSERT
   - `supplier_official_name_snapshot` / `supplier_code_snapshot` / `customer_name_snapshot` は初回 Excel 出力時に凍結（本作成時は NULL のまま、F-22 対応 2026-05-19）
   - audit_logs INSERT
3. コミット

**Response 201:** `Location: /api/v1/purchase-orders/{id}`

> **実装注記（PR2、単価スナップショットの SoT 訂正）:** 上記処理 2 の「現在有効単価を引当て」は設計初稿の記述であり、**実装は各明細の `unit_price_snapshot` をクライアント入力値のまま凍結保存する**（サーバ側で `product_supplier_prices` から引当て・上書きはしない。「単価未決定」= `unit_price_snapshot <= 0` の状態を保持できるようにするため）。入力補助として size-aware な現単価サジェスト `GET /api/v1/orders/price-suggestion?productId=&supplierId=` を別途提供する（読取専用、認可は発注編集権限と同じ）。SKU の size に対応する現単価を「(family, supplier, SKUのsize) → 無ければ (…, NULL-size 既定)」のフォールバックで返し、見つからなければ `{ "found": false }`。実 route は `/api/v1/orders`（本節の `/api/v1/purchase-orders` は設計初稿の名残。実装の route が SoT）。

**エラー:**
- 422 ORDER-001: 必須項目欠落
- 409 ORDER-002: 指定仕入先に対する unit_price 未設定
- 422 ORDER-004: due_date が過去
- 422 ORDER-006: 同一 `product_id` が `lines[]` に重複指定 (Phase 6 で F-14 対応、確定。新規/編集 共通バリデーション、フロント側でマトリクスダイアログ重複追加時にトースト通知し API リクエストには含めない)

#### O-03: 発注書一覧・検索

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/purchase-orders` | 一覧・検索 |

##### GET /api/v1/purchase-orders

**認可:** `purchase_order:read`

**クエリ:**
- `?q=<text>` フリーワード (mgmt_no, order_no, supplier_official_name_snapshot, supplier_code_snapshot, customer_name_snapshot, supplier.name) — 仕入先帳票 snapshot 2 件と取引先内部識別の 1 件いずれにもマッチ (F-22 対応)
- `?filter[date_from]=2026-01-01&filter[date_to]=2026-12-31`
- `?filter[status]=Active,Cancelled`（Phase 6 簡素化、2 値）
- `?filter[export_state]=unexported,exported`（未出力 / 初回出力済の業務絞り込み、`first_exported_at IS NULL` / `IS NOT NULL` に対応）
- `?filter[supplier_id]=14`
- `?filter[delivery_destination_id]=5`
- `?sort=-created_at`
- `?page=1&per_page=50`

**Response 200:**
```json
{
  "data": [
    {
      "id": 1,
      "mgmt_no": "26-00411",
      "order_no": "S3858",
      "status": "Active",
      "first_exported_at": "2026-05-19T10:30:00Z",
      "last_exported_at": "2026-05-20T09:15:00Z",
      "export_state": "exported",
      "supplier": { "id": 14, "name": "藤東工業" },
      "supplier_official_name_snapshot": "DEPARTURES",
      "supplier_code_snapshot": "336",
      "delivery_destination": { "id": 5, "name": "しまむらセンター" },
      "customer_name_snapshot": "しまむら",
      "due_date": "2026-08-15",
      "line_count": 12,
      "total_amount_masked": "***",
      "primary_thumb_urls": ["https://signed1", "https://signed2"],
      "updated_at": "2026-05-19T10:00:00Z",
      "updated_by": { "id": 10, "display_name": "佐藤 浩始" }
    }
  ],
  "meta": { "pagination": { ... } }
}
```

> **設計判断:** `total_amount` は機密度（中-高）配慮で **デフォルトマスク**。`?include_amount=true` を指定 + 認可 `price:read` 保有時のみ実値返却（audit_logs に `Price.View` 記録）。

#### O-04: 発注書編集

> **Phase 6 簡素化:** 「修正 / 改訂」の区別を廃止、`status=Active` の発注書はいつでも編集可能。改訂用エンドポイント `/revisions` は削除。

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/purchase-orders/{id}` | 詳細（明細 + Excel 出力履歴含む）|
| `PATCH` | `/api/v1/purchase-orders/{id}` | 編集（`status=Active` のみ可。Cancelled は 409）|

##### PATCH /api/v1/purchase-orders/{id}

**Request:** POST 時と同一スキーマ（部分更新、提供フィールドのみ更新）+ **編集理由必須フィールド**（Phase 6 で F-16 対応、確定）:

```json
{
  "lines": [...],
  "due_date": "...",
  "edit_reason": "quantity",     // 必須、Enum
  "edit_note": "仕入先在庫切れにより数量変更"   // 任意、自由テキスト最大 256 文字
}
```

**`edit_reason` Enum（必須）:**
| 値 | 業務意味 |
|---|---|
| `quantity` | 数量変更（仕入先在庫切れ、生産計画見直し等）|
| `deadline` | 納期変更（出荷遅延、繁忙期前倒し等）|
| `supplier` | 仕入先変更（品質問題、コスト見直し等）|
| `typo` | 誤入力修正 |
| `other` | その他（`edit_note` 推奨）|

**処理:**
1. 発注書取得、`status=Active` を確認（Cancelled なら 409 ORDER-003）
2. **`edit_reason` 必須バリデーション**（未指定なら 422 ORDER-005）
3. ヘッダ + 明細を更新
4. audit_logs INSERT（`changes.before` / `changes.after` に編集前後の差分、`changes.edit_reason` / `changes.edit_note` に業務理由を記録、ただし unit_price はマスク）

**Response 200:** 更新後の発注書

**追加エラー:**
- 422 ORDER-005: `edit_reason` 未指定または Enum 外の値
- 422 ORDER-006: 同一 `product_id` が `lines[]` に重複指定 (POST と共通、F-14 対応)

> **F-16 解消方針（Phase 6 確定）:** 編集理由を選択肢必須化（5 値 Enum）+ 自由メモ任意化。audit_logs.changes JSONB に `edit_reason` / `edit_note` を保存することで、Post-MVP で「数量変更が多い仕入先」「納期変更が多い時期」等の業務分析が可能。F-15 (差分表示) のデータ基盤と一体化、Phase 7 で UI 追加するだけで変更履歴ビュー実現可。

#### O-05: 発注中止

| メソッド | パス | 用途 |
|---|---|---|
| `POST` | `/api/v1/purchase-orders/{id}/cancel` | 中止 |

##### POST /api/v1/purchase-orders/{id}/cancel

**Request:**
```json
{
  "cancel_reason": "顧客都合"
}
```

**処理:**
1. `status=1/Cancelled`, `cancelled_at=NOW()`, `cancelled_by_user_id=current`, `cancel_reason=...` を UPDATE
2. audit_logs INSERT
3. 既に Cancelled の場合 200 で現状を返す（冪等性、Phase 6 で確定）

#### O-06: 発注書 Excel 出力

> **Phase 6 簡素化:** Excel 出力 = 発注確定 という業務概念を廃止。**いつでも何度でも出力可能**。初回出力時のみ `order_no` 採番 + `first_exported_at` SET + 3 件 snapshot (`supplier_official_name_snapshot` / `supplier_code_snapshot` / `customer_name_snapshot`) を一括凍結 (F-22 対応 2026-05-19)。2 回目以降は `last_exported_at` の更新と出力履歴追記のみ。

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/purchase-orders/{id}/excel` | Excel ダウンロード（初回時に order_no 採番、出力履歴追記）|

##### GET /api/v1/purchase-orders/{id}/excel

**認可:** `purchase_order:read` AND `price:read`（金額を含むため）

**Headers:**
- `Accept: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`

**処理:**
1. 発注書 + 明細 + 関連マスタを一括取得（N+1 回避）。`status=1/Cancelled` でも参照のみは可（業務的に過去の発注書を Excel で取り出す要件あり）
2. **初回出力時のみ** (`first_exported_at IS NULL`):
   - `order_no` 採番（`S` + 4桁連番、PostgreSQL sequence で生成）
   - `first_exported_at=NOW()`, `last_exported_at=NOW()`
   - `supplier_official_name_snapshot` / `supplier_code_snapshot` を `suppliers` から、`customer_name_snapshot` を `delivery_destinations` から一括複写凍結 (Phase 6 サンプル受領後 F-22 対応 2026-05-19)
   - 上記を 1 UPDATE で実行
3. **2 回目以降** (`first_exported_at IS NOT NULL`): `last_exported_at=NOW()` のみ UPDATE
4. `purchase_order_export_logs` INSERT（`is_first_export = first_exported_at == NOW()`）
5. ClosedXML テンプレートに流し込み（**MVP は ① 国内用テンプレート `templates/purchase-order-domestic.xlsx` 1 ファイル固定**、Phase 6 オペレーター確認で確定）
6. MemoryStream で Response Body
7. audit_logs INSERT (`Excel.Export`、`excel_template_version` を記録）

> **テンプレート方針（Phase 6 確定）:**
> - MVP: ① 国内用 1 種類のみ実装。Application 層のリソースとしてバンドル
> - Post-MVP: ② 海外用、③ 海外用＋管理表 を追加（発注書の業務区分 = 国内/海外 から自動選択する分岐ロジックを Phase 7 以降で導入）
> - テンプレ更新は Application のリリースに同梱。DB マスタ管理ではない（`document_template_purchases` テーブルは「連絡文章」テンプレ用で別概念）
>
> **帳票宛名印字方針 (Phase 6 サンプル受領後 2026-05-19 確定、F-22):**
> - 帳票の宛名 (発注先) は **仕入先 (suppliers) の情報** を「`<supplier_official_name>` 御中 `<supplier_code>`」(例: 「DEPARTURES 御中 336」) の構造で印字。
> - 「御中」は Excel テンプレートに固定文として埋め込み、`supplier_official_name_snapshot` (初回出力時に suppliers.official_name から凍結) と `supplier_code_snapshot` (suppliers.code から凍結) を流し込む。
> - 取引先名 (`customer_name_snapshot`、例「しまむら」) は帳票には印字しない (画面表示・検索・集計の内部識別用)。
> - 納品先 (`delivery_destination.name`、例「しまむらセンター」) は帳票の「納品先」欄に別途印字。
>
> **発注印スタンプ方針 (Phase 6 サンプル受領後 2026-05-19 確定、F-22):**
> - 既存帳票には「発注 YYYY.MM.DD 商品管理課」の印影スタンプ画像が押下されているが、MVP では **印影画像を Excel に埋め込まず、印刷後に物理的に手押し** する運用とする (既存業務の継続)。
> - ClosedXML 流し込み時にスタンプ画像挿入処理は不要、Excel テンプレートにも印影画像は埋め込まない (スタンプ枠の空白セルのみ確保)。
> - 帳票末尾の注意書き「発注印のない発注書は無効です」は Excel テンプレートに固定文として埋め込み、ユーザは印刷物に手押しを徹底する。
> - Post-MVP で電子押印 (印影画像の動的埋込) を検討する余地は残す。

**冪等性:** 初回出力時の `order_no` 採番は `Idempotency-Key` ヘッダで二重採番防止（推奨）。

**Response 200:**
- `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- `Content-Disposition: attachment; filename="order-S3858.xlsx"`
- Body: Excel バイナリ

**エラー:**
- 503 EXPORT-001: テンプレートファイル読込失敗
- 500 EXPORT-002: ClosedXML 生成失敗

> **性能（NFR §1.1）:** 50明細で 5 秒以内。テンプレート最適化方針: 行コピーではなく `InsertRowsAbove` + 値書込で速度確保。

#### O-07: 連絡文章選択

連絡文章自体は `purchase_orders.communication_text` に直接保持されるため、専用エンドポイント不要。フロントで以下を組み合わせる:
1. `GET /api/v1/masters/document-text-purchases?filter[standard_print_flag]=true` で標準印字対象を取得
2. ユーザがテンプレートを選択 → フロントで `communication_text` を編集
3. `PATCH /api/v1/purchase-orders/{id}` で更新

---

### 2.6 監査ログ閲覧（管理機能、Post-MVP 想定だがエンドポイント設計のみ）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/audit-logs` | 監査ログ検索（直近 3ヶ月、RDS）| `audit:read`（管理者）|
| `GET` | `/api/v1/audit-logs/archive` | アーカイブ検索（S3 Glacier IR、非同期取得、Phase 7+）| `audit:read` |

##### GET /api/v1/audit-logs

**クエリ:** `?filter[actor_user_id]=10&filter[action]=Order.Submit&filter[date_from]=2026-05-01&filter[entity_type]=purchase_orders&filter[entity_id]=42`

直近 3ヶ月のみ。Phase 5 では仕様提示のみ、MVP 実装は Post-MVP で判断。

---

### 2.7 勤怠（勤怠管理・タイムカード、Iteration 30）

> **移植元:** **akebono-office** の勤怠管理・タイムカード機能。打刻・勤怠集計（労基法 32/34/37 条）・
> 36 協定アラート（労基法 36 条）・打刻修正申請・休暇（労基法 39 条）を honshu へ移植した。
> **実装 SoT:** `src/Backend/Presentation/Endpoints/AttendanceEndpoints.cs`（#1〜#14）/
> `AttendanceLeaveEndpoints.cs`（#15〜#27）/ `src/Backend/Application/Attendance/`（`AttendanceDtos.cs` /
> `LeaveDtos.cs` / `AttendanceService.cs` / `AttendanceRuleService.cs` / `LeaveService.cs`）/
> `src/Backend/Domain/Attendance/`（`AttendanceCalc.cs` / `LeaveCalc.cs`）。
> テーブル定義は `data-design.md §14`、画面は `screen-design.md §3.14 / §3.15` を参照。

#### 2.7.0 共通規約（本節の前提）

**ベースパス:** `/api/maker/v1/attendance`（休暇系は `/api/maker/v1/attendance/leave`）。

> **§1.1 との差分（実装事実）:** 本節のパスは実装のとおり `/api/maker/v1/` プレフィックスで記載している。
> プラットフォーム統合改修でサービス区分（`maker`）が URL に入ったため、§1.1 の初版表記 `/api/v1/` とは
> 実際のプレフィックスが異なる（`/api/maker/v1/masters/...` 等、既存エンドポイントも同様）。

- レスポンス封筒は `ApiEnvelope.Ok` / `Created` / `Error`。JSON は camelCase、enum は **camelCase 文字列**
  （`in` / `breakStart` / `pending` / `approved` 等）。
- 時間量はすべて **int の「分」**。業務日付は `YYYY-MM-DD`（JST の日付）、時刻は **UTC の ISO 8601**
  （表示・深夜帯判定はフロント/ドメイン層で JST へ変換する。`data-design.md §14.0` 参照）。
- ID はすべて `UUID`。
- **業務日付の妥当範囲（すべての日付・年月クエリに適用）:** `date` / `from` / `to`（#3 / #6 / #7）・
  `month`（#4）・`endMonth`（#5）・休暇申請の `date`（#22）は、書式が正しいだけでは足りず
  **`2000-01-01` 〜 実行日の 1 年後**（`AttendanceCalc.MinBusinessDate` 〜 `today.AddYears(1)`、JST 基準）に
  収まることを検証する。範囲外は 422 `AKB-SYS-002`。
  書式は妥当でも業務上ありえない日付（`0001-01-01` 等）が .NET の英語例外としてそのまま露出したり、
  非現実的な集計範囲に化けたりするのを防ぐための下限・上限である
  （実装 SoT: `AttendanceService.ParseDate` / `ParseMonth`、`AttendanceCalc.IsBusinessDateInRange`）。
- **一覧のキーセットページング（#8 打刻修正申請一覧 / #21 休暇申請一覧）:**
  クエリは `?limit` / `?cursor`、レスポンスは `ApiEnvelope.OkPaged` の封筒で
  **`meta.page = { nextCursor, limit, hasMore }`** を返す（`data` は従来どおり配列のまま = フロント契約は非破壊。
  `nextCursor` は最終ページで `null`）。
  - `limit` の許容範囲は **1〜200**（`PageRequest.MaxLimit = 200`）。範囲外、または `cursor` が復号できない場合は
    **400 `AKB-SYS-011`**（`PageCursor.Read`）。`limit` が数値ですらない場合はバインド段階の 400 `AKB-SYS-001`。
  - **本節の 2 本は `limit` 省略時の既定が `PageRequest.DefaultLimit`（50）ではなく上限値 200。**
    実装は `PageCursor.Read(limit ?? PageRequest.MaxLimit, cursor)`。フロント
    （`useAttendance.loadFixRequests` / `leaveRequests`）がまだ `limit` / `cursor` を送らないため、
    既定 50 では申請が黙って欠落するのを避ける措置。フロントがカーソル送信に対応したら既定を 50 に戻せる。
  - `cursor` は「`createdAt`（UTC Ticks）`|` `id`」を base64url 化した**不透明トークン**。
    クライアントは中身を解釈せず、`meta.page.nextCursor` をそのまま次要求へ渡す。
  - ソートキーは `(createdAt, id)` の降順で固定（安定ソート。キーセットの整合性のため変更不可）。
  - **§1.3.2 との差分（実装事実）:** 実装の封筒は `meta.pagination`（`page` / `per_page` / `total_count` /
    `total_pages`）ではなく **`meta.page`**。キーセット方式のため**総件数・総ページ数は返さない**。

**認可の考え方:**

| 区分 | 要求する権限 | 実装ヘルパー（`AuthEndpoints`）|
|---|---|---|
| 書込（打刻・打刻修正申請・休暇申請）| `users.attendance_permission == 1`（更新可能）| `CheckAttendanceWriteAsync` |
| 参照（自分の勤怠・集計・一覧・勤怠ルール/休暇種別の参照）| `attendance_permission` が **1 または 2**（0 は不可）| `CheckAttendanceReadAsync` |
| 管理（全員のタイムカード・承認/却下・休暇付与・勤怠ルール/休暇種別の設定・`scope=all`）| **オーナー** `users.process_record_permission >= 1` | `CheckAttendanceAdminAsync` |

- **`attendance_permission` は既存 4 権限と同じ非単調スケール**（0=なし / 1=更新可能 / 2=参照のみ）。
  **書込判定は必ず `== 1`**（`>= 1` は「参照のみ(2)」に書込を許してしまうバグ）。
- 管理系は勤怠専用の権限列を増やさず、既存の**オーナー権限に集約**した
  （office の admin 相当。office の hr 中間ロールは honshu に無いため作らない = 権限を緩めない方向で統合）。
- **他人の勤怠の参照はオーナー限定。** `userId` クエリ省略時は常に自分。自分以外を指定した場合はオーナーを要求し、
  不足なら 403 `AKB-AUTH-010`「他の利用者の勤怠を参照する権限がありません」を返す
  （`AttendanceEndpoints.ResolveTargetAsync` / `AttendanceLeaveEndpoints.EnsureCanReadOtherAsync`）。
- 打刻（#1）はさらに **本人が `users.punch_required = true`** であることを要求する（役員・外注等は打刻対象外）。

**エラーコード（新規採番なし。既存 `AkbErrorCodes` から選択）:**

| 場面 | code | HTTP |
|---|---|---|
| 入力検証（`kind` 不正・`month` / `date` 形式・**日付/年月の妥当範囲外**・理由未入力・期間超過・**`requestedAt` の範囲外**・日数不正 等）| `AKB-SYS-002`（`SysValidation`）| 422 |
| **ページング指定の不正（`limit` 範囲外・`cursor` 復号不能）／集計対象の利用者数が上限超過（#6 / #27）** | `AKB-SYS-011`（`SysPagingInvalid`）| **400** |
| リクエストボディ / クエリの型不正（`limit` が数値でない 等、バインド段階の失敗）| `AKB-SYS-001`（`SysMalformedBody`）| 400 |
| 打刻順序違反（状態機械）・処理済み申請への再操作・名称重複・同日重複申請 | `AKB-SYS-007`（`SysUniqueViolation`）| 409 |
| 権限不足（勤怠権限・オーナー権限・他人の勤怠参照）・打刻対象外 | `AKB-AUTH-010`（`AuthInsufficientPermission`）| 403 |
| 無効ユーザ / 業務ユーザ未紐付 | `AKB-AUTH-005`（`AuthAccountInactive`）| 403 |
| 未認証 | `AKB-AUTH-001`（`AuthTokenMissing`）| 401 |
| 対象が見つからない（越境含む存在秘匿、メッセージは一律「指定されたリソースが見つかりません」）| `AKB-TENANT-010`（`TenantResourceConcealed`）| 404 |

> **`AKB-ATT-*` は HTTP エラーコードではない。** 36 協定アラート（#5）の `code`
> （`AKB-ATT-A45` / `A45W` / `A100` / `A80` / `A6C` / `A6CW`）は
> **画面に出す業務アラートの識別子**であり、エラー封筒の `code` でもエラーコード台帳（Phase 3 §10 /
> `AkbErrorCodes`）の一部でもない。**`AkbErrorCodes` には追加しない**（定数は `AttendanceCalc` 内に置く）。
> アラートを返す API 自体は 200 で成功する。

#### 2.7.1 打刻・集計（#1〜#6）

| # | メソッド | パス | 用途 | 認可 |
|---|---|---|---|---|
| 1 | `POST` | `/api/maker/v1/attendance/punches` | 打刻（対象は常に本人）| 勤怠 `==1` かつ `punch_required` |
| 2 | `GET` | `/api/maker/v1/attendance/state` | 当日の打刻状態（打刻ウィジェット用）| 勤怠 1 or 2 |
| 3 | `GET` | `/api/maker/v1/attendance/day` | 日次サマリ | 勤怠 1 or 2（他人はオーナー）|
| 4 | `GET` | `/api/maker/v1/attendance/month` | 月次サマリ | 同上 |
| 5 | `GET` | `/api/maker/v1/attendance/alerts` | 36 協定アラート（直近 6 ヶ月）| 同上 |
| 6 | `GET` | `/api/maker/v1/attendance/timecard` | 全員のタイムカード | **オーナー** |

| # | リクエスト | レスポンス | 主なエラー |
|---|---|---|---|
| 1 | body `{ "kind": "in" \| "out" \| "breakStart" \| "breakEnd" }`（大小文字非依存）。日付・時刻はサーバ採番（`date` = JST 当日 / `at` = UTC now）| **201** `PunchResultDto { id, state }`（`state` は**打刻後**の状態）。`Location: /api/maker/v1/attendance/state` | 422 `AKB-SYS-002`「打刻種別は in / out / breakStart / breakEnd のいずれかを指定してください」/ 403 `AKB-AUTH-010`「打刻対象の利用者ではありません」/ 409 `AKB-SYS-007`「現在の状態では「{出勤\|退勤\|休憩開始\|休憩終了}」はできません」/ 404 `AKB-TENANT-010` |
| 2 | — | **200** `PunchStateDto { state, punches: PunchDto[] }`。`punches` は**生打刻列**（置換解決前、`at`→`createdAt` 昇順）| 403（勤怠権限なし）|
| 3 | `?userId&date&raw`。`date` 既定 = **JST の今日**。`raw` は `1` / `true` / `yes` で真 | **200** `DaySummaryDto`。`raw` 真のとき `rawPunches` を同梱（偽のときは `null`。フィールド自体は省略されない）| 422「date は YYYY-MM-DD 形式で指定してください」/ 422「date は 2000-01-01 〜 {実行日の 1 年後} の範囲で指定してください」/ 403（他人参照）/ 404 |
| 4 | `?userId&month`。`month=YYYY-MM`、既定 = **JST の当月** | **200** `MonthSummaryDto { days[], total, workDays }` | 422「month は YYYY-MM 形式で指定してください」/ 422「month は 2000-01 〜 {実行日の 1 年後の年月} の範囲で指定してください」/ 403 / 404 |
| 5 | `?userId&endMonth`。`endMonth=YYYY-MM`、既定 = 当月。`endMonth` を最終月とする直近 6 ヶ月を判定 | **200** `Article36AlertDto[]`（0 件なら空配列 = アラートなし）| 422「endMonth は YYYY-MM 形式で指定してください」/ 422「endMonth は 2000-01 〜 {実行日の 1 年後の年月} の範囲で指定してください」/ 403 / 404 |
| 6 | `?from&to&q`。既定 `from` = **当月 1 日**、`to` = **今日**（JST）。`q` = 表示名の部分一致 | **200** `TimecardRowDto[]`。**日付降順 → 氏名昇順**（`StringComparison.Ordinal` で決定的に）| 422「from / to は YYYY-MM-DD 形式で指定してください」/ 422「from / to は 2000-01-01 〜 {実行日の 1 年後} の範囲で指定してください」/ 422「期間の開始日は終了日以前にしてください」/ 422「期間は最大 62 日までです」（**両端を含めて 62 日超**）/ **400 `AKB-SYS-011`「対象の利用者が多すぎます（一度に集計できるのは 200 人までです）」**/ 403（非オーナー）|

**計算上の重要事項:**
- **`GET /day` も月次経由で算出する。** 月 60 時間超の残業バケット（`over60Ot`）は月内累計に依存するため、
  単日だけ計算すると常に 0 になる。実装は対象月を丸ごと集計してから該当日を射影する。
- `GET /timecard` の期間上限は **バックエンド定数 `AttendanceService.TimecardRangeMaxDays = 62` が SoT**。
  判定式は **`toDate.DayNumber - fromDate.DayNumber + 1 > TimecardRangeMaxDays`**、すなわち
  **両端を含めて 62 日までを受け付け、63 日は 422「期間は最大 62 日までです」で拒否する**。
  例: `from=2026-01-01` に対し `to=2026-03-03` は 62 日ちょうどで可、`to=2026-03-04` は 63 日で不可。
  メッセージ文言「最大 62 日」と実効値が一致する。
  フロントも同じ「両端含み」の数え方に統一済みで、**3 箇所**が本定数への参照コメント付きの同値定数
  `TIMECARD_RANGE_MAX_DAYS = 62` を共有する
  （`composables/useAttendance.ts`: 定数定義と `Math.min(diffBizDays + 1, TIMECARD_RANGE_MAX_DAYS)` /
  `pages/attendance/index.vue`: `diffBizDays(from, to) + 1 > TIMECARD_RANGE_MAX_DAYS` で
  `addBizDays(to, -(TIMECARD_RANGE_MAX_DAYS - 1))` へクランプ / `pages/attendance/timecard.vue`: 同じクランプ）。
  クランプ幅が `-(62 - 1)` なのは両端含みで数えるため。ここがずれるとサーバ 422 と食い違う。
  > **訂正（2026-07-27）:** 本項は当初「判定式は `from + 62 日 < to` のため両端を含めて 63 日まで受け付ける
  > （メッセージとの 1 日のずれは実装の現状）」と記載していたが、これは**オフバイワンのバグを仕様として
  > 正規化した誤記**だった（API を直叩きすれば宣言上限を 1 日超えられた）。バックエンド修正コミット `4c6981e` で
  > 両端含みの数え方へ統一済みのため、実装の事実に合わせて書き換えた。**63 日は受け付けない。**
- **`GET /timecard` の対象利用者数の上限は `AttendanceService.TimecardMaxUsers = 200` 人**（`q` で絞り込んだ後の件数で判定）。
  利用者数 × 期間（最大 62 日）分の打刻をすべてメモリへ載せて集計するため、非有界のままでは在籍者の増加に比例して
  必ずタイムアウトする。超過は **400 `AKB-SYS-011`**「対象の利用者が多すぎます（一度に集計できるのは 200 人までです）」、
  `userAction`「氏名で絞り込むか、期間を短くして再検索してください」。
  判定は**上限 +1 件だけ取得して超過を検知する**（既存のページング規約と同じ考え方）。
- `TimecardRowDto` の `inAt` = 有効打刻の**最初の In**、`outAt` = **最後の Out**。
  有効打刻が 0 件の日は行を出さない。対象は `is_active` かつ未削除の利用者のみ。

#### 2.7.2 打刻修正申請（#7〜#9）

| # | メソッド | パス | 用途 | 認可 |
|---|---|---|---|---|
| 7 | `POST` | `/api/maker/v1/attendance/fix-requests` | 修正申請（対象は常に本人）| 勤怠 `==1` |
| 8 | `GET` | `/api/maker/v1/attendance/fix-requests` | 申請一覧 | 勤怠 1 or 2 / `scope=all` は**オーナー** |
| 9 | `POST` | `/api/maker/v1/attendance/fix-requests/{id:guid}/decision` | 承認 / 却下 | **オーナー** |

| # | リクエスト | レスポンス | 主なエラー |
|---|---|---|---|
| 7 | body `FixRequestCreateRequest { date, kind, requestedAt, reason }`。`date` は必須（既定なし）。`requestedAt` は**タイムゾーン付き**文字列（例 `2026-07-27T09:00:00+09:00`）でフロントが送り、サーバが UTC 化して保存 | **201** `IdResultDto { id }`。`Location: .../fix-requests/{id}` | 422「date を指定してください」/「date は YYYY-MM-DD 形式で指定してください」/「date は 2000-01-01 〜 {実行日の 1 年後} の範囲で指定してください」/「打刻種別は …」/「修正後の時刻は YYYY-MM-DDTHH:mm:ss+09:00 形式（タイムゾーン付き）で指定してください」/ **「修正後の時刻は対象日または翌日（夜勤の日跨ぎ）の範囲で指定してください」**/「修正理由を入力してください（客観的記録の担保）」/「修正理由は 512 文字以内で入力してください」|
| 8 | `?status&scope&limit&cursor`。`status` = `pending` / `approved` / `rejected`（省略 = 絞り込みなし）。`scope=all` で全員分。**`limit` = 1〜200（省略時 200 = `PageRequest.MaxLimit`）**、**`cursor`** = 前ページの `meta.page.nextCursor`（不透明トークン）| **200** `FixRequestDto[]`。**`createdAt` 降順 → `id` 降順**。申請者・処理者の氏名は削除済ユーザでも解決して返す（監査表示のため）。**`meta.page = { nextCursor, limit, hasMore }`**（`ApiEnvelope.OkPaged`。`data` は配列のままで非破壊、続きの有無は `hasMore`）| 422「status は pending / approved / rejected のいずれかを指定してください」/ **400 `AKB-SYS-011`「limit は 1〜200 の整数で指定してください」/「cursor が不正です」**/ 403（`scope=all` を非オーナーが指定）|
| 9 | body `FixDecisionRequest { action: "approved" \| "rejected" }` | **200** `IdResultDto { id }` | 422「action は approved / rejected を指定してください」/ 404 `AKB-TENANT-010` / 409 `AKB-SYS-007`「この申請は処理済みです」|

> **承認処理（トランザクション、記録系保護）:**
> `BeginTransaction` → `SELECT ... FROM attendance_fix_requests WHERE id = {0} FOR UPDATE`（パラメータ化）→
> 申請を再読込して `status != Pending` なら 409 → **`punch_records` に修正打刻を追記**
> （`source=Fix`, `at=requestedAt`, `fixedFrom=` 置換対象の有効打刻の `at`, `fixReason`, `approvedByUserId`）→
> 申請の `status` / `decidedByUserId` を更新 → Commit。
> **元打刻は削除も更新もしない**（`data-design.md §14.2`）。409 判定を必ずトランザクション内で行うことで二重承認を防ぐ。
> 却下時は打刻を追記しない。

> **`requestedAt` の範囲検証（記録系保護）:**
> `requestedAt` を JST 換算した日付が **対象日（`date`）または翌日**に収まることを検証する
> （夜勤の日跨ぎを許容するため翌日まで。範囲外は 422 `AKB-SYS-002`
> 「修正後の時刻は対象日または翌日（夜勤の日跨ぎ）の範囲で指定してください」、
> `userAction`「対象日を選び直すか、時刻を対象日の範囲内へ修正して再送信してください」）。
> 対象日から極端に離れた時刻を承認すると `punch_records`（UPDATE / DELETE を剥奪済み = **削除で復旧できない**）に
> 異常値が残り、以後その日の集計が破綻する。書式検証（タイムゾーン必須）だけでは防げないため必須。

> **一覧のページング（#8）:** `status` 未指定の `scope=all` は運用年数に比例して全履歴を返すため、非有界のままでは
> 必ずタイムアウトする。既存のキーセットページング規約（§2.7.0、`PurchaseOrderService.ListAsync` と同形）へ移行した。
> `data` は配列のままなので既存フロントは無改修で動作し、続きの有無は `meta.page.hasMore` で判定する。

#### 2.7.3 勤怠ルール（勤務体系マスタ、#10〜#14）

| # | メソッド | パス | 用途 | 認可 |
|---|---|---|---|---|
| 10 | `GET` | `/api/maker/v1/attendance/rules` | 一覧（`?includeInactive`、既定 false）| 勤怠 1 or 2 |
| 11 | `POST` | `/api/maker/v1/attendance/rules` | 新規作成 | **オーナー** |
| 12 | `PATCH` | `/api/maker/v1/attendance/rules/{id:guid}` | **部分更新** | **オーナー** |
| 13 | `DELETE` | `/api/maker/v1/attendance/rules/{id:guid}` | 論理削除 | **オーナー** |
| 14 | `POST` | `/api/maker/v1/attendance/rules/{id:guid}/restore` | 論理削除の取消 | **オーナー** |

| # | リクエスト | レスポンス | 主なエラー |
|---|---|---|---|
| 10 | `?includeInactive=true` で無効・論理削除済も返す（復元 UI 用）| **200** `AttendanceRuleDto[]`（`name` 昇順 → `id` 昇順）| 403 |
| 11 | body `AttendanceRuleWriteRequest`（全項目必須）| **201** `AttendanceRuleDto`。`Location: .../rules/{id}` | 422（下記検証一覧）|
| 12 | body `AttendanceRulePatchRequest`（**全項目 nullable。`null` = 未指定 = 現在値を保持**）| **200** `AttendanceRuleDto` | 422（マージ後の値に対して同じ検証）/ 404 |
| 13 | — | **204 No Content** | 404 |
| 14 | — | **204 No Content** | 404 |

**入力検証（すべて 422 `AKB-SYS-002`、上から順に評価して最初の違反を返す）:**

| 条件 | メッセージ |
|---|---|
| `name` が空 | 「名称を入力してください」|
| `name` が 128 文字超 | 「名称は 128 文字以内で入力してください」|
| `workStart` / `workEnd` が `HH:mm` 形式でない | 「始業・終業は HH:mm 形式で入力してください」|
| `workStart >= workEnd`（序数比較。**同時刻も不可**）| 「終業は始業より後の時刻にしてください」|
| `breakMinutes` が 0〜240 外 | 「休憩時間は 0〜240 分で入力してください」|
| `closingDay` が 1〜31 外 | 「締め日は 1〜31 で入力してください（31 = 月末）」|
| `legalHolidayWeekday` が 0〜6 外 | 「法定休日の曜日は 0〜6（0 = 日曜）で入力してください」|
| `flexSettlementMonths` が 1〜3 外 | 「フレックスの清算期間は 1〜3 ヶ月で入力してください」|
| `flexEnabled` かつコアタイムが `HH:mm` でない | 「コアタイムは HH:mm 形式で入力してください」|
| `flexEnabled` かつ `flexCoreStart >= flexCoreEnd` | 「コアタイムの終了は開始より後の時刻にしてください」|

> **設計上の注意:**
> - **`isDefault=true` で保存すると、同一テナントの他ルールの `isDefault` が同一 `SaveChanges` 内で false に落ちる**
>   （中間状態を作らない排他制御。既に複数 default が存在する不整合も自己修復する）。
> - PATCH は**部分更新**。`null` のフィールドは更新しない（CLAUDE.md の Zod `.partial()` による既定値上書き障害と
>   同じ轍を踏まないため、書込用と別 record を用意している）。検証はマージ後の値に対して行うので、
>   「始業だけ更新して終業との前後関係が壊れる」ようなケースも 422 で弾ける。
>   なお `flexCoreStart` / `flexCoreEnd` は本 I/F では `null` でクリアできない（未指定と区別できないため。空文字を送るとクリアされる）。
> - 論理削除（#13）は `deletedAt` を立てると同時に **`isDefault` を false に落とす**（削除済ルールが既定として
>   解決に参加しないようにするため）。復元（#14）では `isDefault` を戻さない（明示的に再設定する運用）。

#### 2.7.4 休暇（#15〜#27）

| # | メソッド | パス | 用途 | 認可 |
|---|---|---|---|---|
| 15 | `GET` | `/api/maker/v1/attendance/leave/types` | 休暇種別 一覧（`?includeInactive`）| 勤怠 1 or 2 |
| 16 | `POST` | `/api/maker/v1/attendance/leave/types` | 休暇種別 作成 | **オーナー** |
| 17 | `PATCH` | `/api/maker/v1/attendance/leave/types/{id:guid}` | 休暇種別 部分更新 | **オーナー** |
| 18 | `DELETE` | `/api/maker/v1/attendance/leave/types/{id:guid}` | 休暇種別 論理削除 | **オーナー** |
| 19 | `POST` | `/api/maker/v1/attendance/leave/types/{id:guid}/restore` | 休暇種別 復元 | **オーナー** |
| 20 | `GET` | `/api/maker/v1/attendance/leave/summary` | 残数・年 5 日義務・履歴 | 勤怠 1 or 2（他人はオーナー）|
| 21 | `GET` | `/api/maker/v1/attendance/leave/requests` | 休暇申請 一覧 | 勤怠 1 or 2 / `scope=all` は**オーナー** |
| 22 | `POST` | `/api/maker/v1/attendance/leave/requests` | 休暇申請（対象は常に本人）| 勤怠 `==1` |
| 23 | `POST` | `/api/maker/v1/attendance/leave/requests/{id:guid}/decision` | 承認 / 却下 | **オーナー** |
| 24 | `POST` | `/api/maker/v1/attendance/leave/grants` | 個別付与 | **オーナー** |
| 25 | `POST` | `/api/maker/v1/attendance/leave/grants/bulk` | 一括付与 | **オーナー** |
| 26 | `POST` | `/api/maker/v1/attendance/leave/periodic-grants/run` | 周期自動付与の実行 | **オーナー** |
| 27 | `GET` | `/api/maker/v1/attendance/leave/admin/summary` | 休暇管理一覧（メンバー × 種別）| **オーナー** |

| # | リクエスト | レスポンス | 主なエラー |
|---|---|---|---|
| 15 | `?includeInactive=true` で無効・論理削除済も返す | **200** `LeaveTypeDto[]`（`displayOrder` 昇順 → `name` 昇順）| 403 |
| 16 | body `LeaveTypeWriteRequest { name, grantMethod, expiryMonths, description, displayOrder=1, isActive=true }`。**`isStatutory` は受け取らない**（改竄防止）| **201** `LeaveTypeDto` | 422「名称を入力してください」/「名称は 64 文字以内で入力してください」/「有効期間は 1〜120 ヶ月で指定してください（無期限にする場合は未指定）」/「表示順は 0 以上で指定してください」/ 409「休暇種別「{名称}」は既に登録されています」|
| 17 | body `LeaveTypePatchRequest`（**`null` = 未指定 = 現在値を保持**。無期限へ戻す場合のみ `clearExpiryMonths=true`）| **200** `LeaveTypeDto` | 409「法定有給の種別は変更できません」/ 422（#16 と同じ検証）/ 409 名称重複 / 404 |
| 18 | — | **204 No Content**（付与・申請の実績は残す）| 409「法定有給の種別は削除できません」/ 404 |
| 19 | — | **204 No Content** | 409 名称重複（同名の未削除種別が存在する場合）/ 404 |
| 20 | `?userId`（省略 = 自分）| **200** `LeaveSummaryDto` | 403「他の利用者の勤怠を参照する権限がありません」|
| 21 | `?scope&status&limit&cursor`。`scope=all` で全員分、`status` = `pending` / `approved` / `rejected`。**`limit` = 1〜200（省略時 200 = `PageRequest.MaxLimit`）**、**`cursor`** = 前ページの `meta.page.nextCursor`（不透明トークン）| **200** `LeaveRequestDto[]`（**`createdAt` 降順 → `id` 降順**。同一 `createdAt` の順序を確定させるタイブレーカー。#8 と同形）。**`meta.page = { nextCursor, limit, hasMore }`**（`ApiEnvelope.OkPaged`。`data` は配列のままで非破壊）| 422「状態は pending / approved / rejected のいずれかを指定してください」/ **400 `AKB-SYS-011`「limit は 1〜200 の整数で指定してください」/「cursor が不正です」**/ 403 |
| 22 | body `LeaveRequestWriteRequest { leaveTypeId, date, unit="full", reason? }`（`userId` は受け取らない）| **201** `LeaveIdDto { id }` | 422「休暇種別が存在しません」/ **「取得日は 2000-01-01 〜 {実行日の 1 年後} の範囲で指定してください」**/「理由は 255 文字以内で入力してください」/ 409「同じ日付の休暇申請が既にあります」|
| 23 | body `LeaveDecisionRequest { action: "approved" \| "rejected" }` | **200** `LeaveIdDto { id }` | 422「操作は approved / rejected のいずれかを指定してください」/ 409「この申請は処理済みです」/ 404 |
| 24 | body `LeaveGrantWriteRequest { userId, leaveTypeId, grantDate, days }` | **201** `LeaveGrantResultDto { id, skipped }`。**既存があれば既存レコードの `id` と `skipped=1`** を返す（201 のまま）| 422「付与日数は正の数で指定してください」/「休暇種別が存在しません」/「周期自動付与の種別は手動付与できません」/「利用者が存在しません」|
| 25 | body `LeaveBulkGrantRequest { leaveTypeId, days, target:"all", grantDate? }`。`grantDate` 省略時は **JST の当日** | **200** `LeaveGrantBulkResultDto { granted, skipped }` | 422「一括付与の対象は all のみ指定できます」/ 上記 #24 と同じ検証 |
| 26 | — | **200** `LeaveGrantBulkResultDto { granted, skipped }` | **業務エラーを投げない**（対象種別・対象者が無ければ `{0, 0}` を返す。原則4 グレースフルデグラデーション）|
| 27 | — | **200** `LeaveAdminRowDto[]`（利用者の `employeeNo` 昇順 × 種別の `displayOrder` 昇順。付与も取得も 0 の組合せは行を出さない）| **400 `AKB-SYS-011`「対象の利用者が多すぎます（一度に集計できるのは 500 人までです）」**/ 403 |

> **休暇の設計上の注意:**
> - **法定有給（`isStatutory=true`）の種別は作成・編集・論理削除を禁止**（409 `AKB-SYS-007`）。
>   初期シードの 1 件のみ存在し、`PATCH` / `POST` は `isStatutory` を**受け取らない**ため API 経由で昇格もできない。
> - **付与（#24 / #25 / #26）は冪等。** 同一 `(userId, leaveTypeId, grantDate)` が既にあれば**挿入せず `skipped` に数える**。
>   既存の付与を更新・削除しない（`data-design.md §14.5` の UNIQUE 制約が最終防壁）。
>   周期自動付与は何度実行しても既存の付与日数・消化履歴が変わらない。
> - 一括付与の対象指定は **`target: "all"`（在籍中の全ユーザ）のみ**。office の雇用区分/部署指定は honshu に
>   該当属性が無いため移植対象外。
> - `GrantMethod == Periodic` の種別は手動付与（#24 / #25）の対象外（422）。
> - 残数は付与と Approved の申請から **FIFO 引当で毎回導出**する（`LeaveCalc`）。残数列は持たない。
>   法定有給の残数には繰越上限 40 日を適用する。
> - 同一日付の重複申請判定は `(userId, date)` のみで行う（種別は問わない）。**却下済みは再申請をブロックしない。**
- **#27 休暇管理一覧の対象利用者数の上限は `LeaveService.AdminSummaryMaxUsers = 500` 人**（在籍中 = `is_active` かつ未削除）。
  全在籍者の付与・取得の全履歴をメモリへ載せて集計するため、非有界のままでは在籍者数に比例して必ずタイムアウトする。
  超過は **400 `AKB-SYS-011`**「対象の利用者が多すぎます（一度に集計できるのは 500 人までです）」、
  `userAction`「利用者ごとの休暇サマリから個別に確認してください」で **#20 の個人別サマリへ誘導**する。
  判定は上限 +1 件だけ取得して検知する（#6 タイムカードと同じ考え方。上限値が異なるのは 1 件あたりの集計コストの差）。
- **#21 休暇申請一覧はキーセットページング必須**（§2.7.0）。#8 打刻修正申請一覧と同じ規約・同じソートキー。

#### 2.7.5 主要 DTO

```
PunchDto            { id, kind, at, source, fixedFrom, fixReason, approvedByUserId }
PunchResultDto      { id, state }
PunchStateDto       { state, punches: PunchDto[] }
BucketsDto          { scheduled, statutoryOt, nonStatutoryOt, over60Ot, night, legalHoliday }   // 分
DaySummaryDto       { date, workMinutes, breakMinutes, nightMinutes, buckets, breakShortage,
                      punches: PunchDto[], rawPunches: PunchDto[]? }
MonthSummaryDto     { days: DaySummaryDto[], total: BucketsDto, workDays }
Article36AlertDto   { level: "warn" | "crit", code: "AKB-ATT-*", message }
TimecardRowDto      { userId, userName, date, inAt, outAt, workMinutes, breakMinutes }
FixRequestDto       { id, userId, userName, date, kind, requestedAt, reason, status,
                      decidedByUserId, decidedByUserName, createdAt }
AttendanceRuleDto   { id, name, workStart, workEnd, breakMinutes, flexEnabled, flexCoreStart,
                      flexCoreEnd, flexSettlementMonths, closingDay, legalHolidayWeekday,
                      isDefault, isActive, deletedAt }
LeaveTypeDto        { id, name, grantMethod, expiryMonths, isStatutory, description,
                      displayOrder, isActive, deletedAt }
LeaveSummaryDto     { paidRemaining, paidUsedThisFiscalYear,
                      nextExpire: { date, days } | null,
                      byType:    [ { leaveTypeId, leaveTypeName, isStatutory, granted, taken,
                                     remaining, nextExpireDate } ],
                      obligation: { applicable, grantDate, deadline, requiredDays, takenDays,
                                    daysLeft, achieved, warn },
                      history:   [ { date, kind: "grant"|"take", leaveTypeId, leaveTypeName,
                                     detail, status, days } ] }
LeaveRequestDto     { id, userId, userName, leaveTypeId, leaveTypeName, date, unit, status,
                      reason, decidedByUserId, decidedByUserName, createdAt }
LeaveAdminRowDto    { userId, userName, leaveTypeId, leaveTypeName, granted, taken, remaining,
                      lastGrantDate }
LeaveGrantResultDto { id, skipped }        LeaveGrantBulkResultDto { granted, skipped }
```

- enum の JSON 値: `kind` = `in` / `out` / `breakStart` / `breakEnd`、`source` = `web` / `mobile` / `fix`、
  `state` = `before` / `working` / `breaking` / `done`、`status` = `pending` / `approved` / `rejected`、
  `unit` = `full` / `half`、`grantMethod` = `periodic` / `manual`。
- `LeaveSummaryDto.history[].days` は符号や「—」を含むため**表示専用の文字列**で返す（数値ではない）。

#### 2.7.6 監査ログ（§1.7 の対象に追加）

主処理の commit 後に `IAuditLogger.LogAsync` を呼ぶ（記録失敗は主要フローを止めない）。参照系は記録しない。

`Punch.Create` / `AttendanceFixRequest.Create` / `AttendanceFixRequest.Approve` / `AttendanceFixRequest.Reject` /
`AttendanceRule.Create` / `.Update` / `.Delete` / `.Restore` /
`LeaveType.Create` / `.Update` / `.Delete` / `.Restore` /
`LeaveRequest.Create` / `.Approve` / `.Reject` /
`LeaveGrant.Create` / `.BulkCreate` / `.PeriodicRun`

`entityType` は `PunchRecord` / `AttendanceFixRequest` / `AttendanceRule` / `LeaveType` / `LeaveRequest` / `LeaveGrant`。

#### 2.7.7 認証エンドポイントへの影響（§2.1 の追補）

`POST /auth/sync` / `GET /auth/me` のレスポンスに **`attendancePermission`（既定 1）** と
**`punchRequired`（既定 true）** を**末尾追加**した（下位互換。`src/Backend/Application/Auth/LoginDtos.cs`）。
フロントはこれらから `canPunch = attendancePermission === 1 && punchRequired` /
`canUseAttendance = attendancePermission === 1 || attendancePermission === 2` を導出する。

#### 2.7.8 移植の除外スコープと未検証事項

**除外スコープ（honshu に対応機能が無い / 別基盤依存のため移植しない）:**

| 除外した office の機能 | 理由 |
|---|---|
| 祝日マスタ・営業日計算 | 翌営業日計算専用であり、honshu には翌営業日計算を使う機能が無い。日次集計の法定休日判定は**曜日**で行うため不要（外部 HTTP 依存も増やさない）|
| AI 参照範囲・チャットボット・日報連携・通知・エスカレーション | honshu に対応機能が無い |
| 権限ルールマトリクスエンジン（subjectKind × resource × field の allow/deny）| honshu の 4+1 権限カテゴリ方式に置換（§2.7.0 の認可の考え方）|
| 雇用区分（`employment_type`）| 勤怠ルールは「既定ルール方式」、有給付与は週所定日数・時間（`users.weekly_days` / `weekly_hours`）で判定するため不要 |

**未検証事項:**
- 本改修を行った環境では **.NET SDK を取得できず、バックエンドをローカルでコンパイル検証できていない**。
- `docs/api/openapi.json` は**未再生成**。CI の `regen-openapi` ワークフロー（main 向け PR で自動再生成し、
  生成物を head ブランチへ自動コミットする）に委ねる。本節と OpenAPI の突合はその再生成後に行うこと。

#### 2.7.9 利用者 API への影響（§2.2 の追補 — 勤怠列の追加に伴う変更）

> **記載場所について:** §2.2「ユーザ管理（M-03）」は初版のドラフト（`/api/v1/users`、数値 ID、snake_case、
> `user:read` / `user:write` スコープ）のままで実装（`/api/maker/v1/users`、UUID、camelCase、
> オーナー権限）と乖離しており未整備のため、勤怠列の追加に伴う変更は**本節にまとめて記載する**。
> §2.2 の全面改訂は別途行うこと。
> **実装 SoT:** `src/Backend/Presentation/Endpoints/UserEndpoints.cs` /
> `src/Backend/Application/Users/UserDtos.cs`（`UserWriteRequest` / `UserPatchRequest`）/ `UserQueryService.cs`。

**`PATCH /api/maker/v1/users/{id:guid}` は `UserWriteRequest` ではなく `UserPatchRequest` を受け取る（部分更新）。**

| 項目 | 内容 |
|---|---|
| 認可 | **オーナー**（`users.process_record_permission >= 1`、`AuthEndpoints.CheckUserAdminAsync`）|
| リクエスト | `UserPatchRequest`（**全項目 nullable。`null` = 未指定 = 現在値を保持**）|
| レスポンス | **200** `UserListItem` |
| 主なエラー | 422 `AKB-SYS-002`（権限値の検証 /「自分自身のオーナー権限の解除・無効化はできません」/「最後の有効なオーナーの権限解除・無効化はできません」/ 勤務体系が存在しない）/ 404 |

- **`POST /api/maker/v1/users`（作成）は従来どおり `UserWriteRequest`（既定値付き）で据え置き。**
  更新用の record を分離したのは既存 I/F を変更しないため（原則7）。
- **`null` = 未指定 = 現在値保持。** 送っていない項目は一切変更しない。
  作成用 `UserWriteRequest` をそのまま更新に使うと、送っていない項目が record の既定値
  （`AttendancePermission = 1` / `PunchRequired = true` / `AttendanceRuleId = null` / `HireDate = null` /
  `WeeklyDays = 5` / `WeeklyHours = 40`）で上書きされ、**表示名を 1 文字直しただけで
  入社日・週所定日数・週所定時間・勤務体系が初期化される**（`4c6981e` で修正した BLOCKER）。
  入社日は有給の周期自動付与の起算日であり、消えると労基法 39 条の付与が黙って止まる。
  週所定が既定へ戻ると比例付与のはずの短時間勤務者が通常付与と判定され、付与日数が法的に誤る。
  （CLAUDE.md の Zod `.partial()` による既定値上書き障害と同じ轍。`LeaveTypePatchRequest` /
  `AttendanceRulePatchRequest`（§2.7.3）と同じ手法。）

**NULL 許容列の「未指定」と「クリア」の区別:**

| フィールド | クリア方法 | 備考 |
|---|---|---|
| `hireDate`（入社日）| **`clearHireDate: true`** | クリアフラグは値の指定より**優先**（`clearHireDate=true` なら `hireDate` の値は無視して `null`）|
| `attendanceRuleId`（勤務体系）| **`clearAttendanceRule: true`** | 同上。値を指定する場合は実在する勤怠ルールであることを検証（不在は 422）|
| `email` | **空文字 `""` を送る** | `null` = 未指定（保持）／空文字・空白のみ = `null` へクリア。ブール型のクリアフラグは持たない |
| `firebaseUid` | **本 I/F ではクリアできない** | `null` / 空文字はいずれも既存値を保持（連携解除は本 I/F では行わない、非破壊）|

- クリアフラグ（`clearAttendanceRule` / `clearHireDate`）は `UserPatchRequest` の**末尾に追加**した
  `bool`（既定 `false`）。送らなければ従来どおり現在値を保持するため、既存クライアントは無改修で動く（下位互換）。
- 勤怠 4 列（`attendancePermission` / `punchRequired` / `attendanceRuleId` / `hireDate` /
  `weeklyDays` / `weeklyHours`）は利用者フォームからも編集できる。勤務体系は `MasterSelect` で選択し、
  選択肢の取得失敗は握りつぶす（原則4）。これが無いと周期自動付与が一度も動かず、
  DB を直接操作するという手動手順が必要になっていた（原則1）。
- ロックアウト防止の検証は**マージ後の値**に対して行う（自分自身のオーナー権限解除・無効化の禁止、
  最後の有効オーナーの権限解除・無効化の禁止）。

---

## 3. OpenAPI 3.0 雛形

> 完全な OpenAPI YAML は Phase 5 後半のプロトタイプ実装時に生成（Swashbuckle.AspNetCore で自動生成）。本ドキュメントでは構造例のみ示す。

```yaml
openapi: 3.0.3
info:
  title: Akebono Honshu アパレル生産管理 API
  version: 1.0.0
  description: |
    MVP API. Firebase Authentication + RDS PostgreSQL バックエンド。
servers:
  - url: https://api.akebono.jp/api/v1
    description: Production
  - url: https://stg-api.akebono.jp/api/v1
    description: Staging
components:
  securitySchemes:
    FirebaseAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT
      description: Firebase ID Token
  schemas:
    Pagination:
      type: object
      properties:
        page: { type: integer }
        per_page: { type: integer }
        total_count: { type: integer }
        total_pages: { type: integer }
    ProblemDetails:
      type: object
      properties:
        type: { type: string, format: uri }
        title: { type: string }
        status: { type: integer }
        detail: { type: string }
        code: { type: string, description: "Phase 3 §10 エラーコード" }
        instance: { type: string }
        errors:
          type: array
          items:
            type: object
            properties:
              field: { type: string }
              code: { type: string }
              message: { type: string }
        trace_id: { type: string }
    ProductListItem: { ... }
    PurchaseOrderListItem: { ... }
    User: { ... }
  responses:
    Unauthorized:
      description: 401
      content:
        application/problem+json:
          schema: { $ref: '#/components/schemas/ProblemDetails' }
    Forbidden:
      description: 403
      content: ...
security:
  - FirebaseAuth: []
paths:
  /products:
    get:
      summary: 商品一覧
      parameters: [...]
      responses:
        '200': ...
        '401': { $ref: '#/components/responses/Unauthorized' }
        '403': { $ref: '#/components/responses/Forbidden' }
  /products/families:
    post:
      summary: 商品企画作成 (P-01)
      ...
  /products/families/{familyId}/expand:
    post:
      summary: サイズ展開 (P-02)
      ...
  # ... (省略)
```

> **フロント連携:** `openapi-typescript` で TypeScript 型を自動生成、`useApi` composable が型安全に利用。

---

## 4. API 癒着回避の検証（Phase 5 方法論 4 原則）

| 原則 | 検証 |
|---|---|
| 1 API = 1 責務 | 各エンドポイントが単一の責務（作成・取得・更新・削除・特殊操作）。例外: `GET /products/families/{id}` は family + products + images + prices を返すが、これは「企画詳細」という単一概念に集約された取得（複数 API 呼び分けは画面側の負担増、N+1 リスク） |
| クライアントに集約・加工責務を押し付けない | 商品一覧（P-04）の price_range は **DB 側 SQL 集計**で返却（フロント側計算不要）。発注一覧（O-03）の line_count も同様 |
| 別リソースを混在させない | `GET /products` は商品のみ、`GET /purchase-orders` は発注のみ。詳細では関連を含むが「子リソース」として明示 |
| API 定義で使い方がわかる単位 | 動詞ベースの専用エンドポイント（`/complete`, `/expand`, `/cancel`, `/excel`）で意図を明示 |

---

## 5. 全データフロー I/F 検証（architecture.md §4 シナリオ → API マッピング）

| シナリオ | API 呼出 | 検証結果 |
|---|---|---|
| A. 商品マスタ登録 | `POST /products/families/complete`（バルク登録単一 API、Phase 6 F-06 対応）| ✅ family + 全 SKU + supplier_prices を 1 トランザクションで完結、Idempotency-Key で冪等性担保 |
| B. 仕入先 × アイテム × 仕入単価設定 | `POST /products/families/{familyId}/supplier-prices`（Phase 6 でアイテム単位に修正）| ✅ 認可は AND 評価（product:write + price:write）、監査ログマスク仕様明示 |
| C. 発注書作成 | `POST /purchase-orders`（status=Active で作成、改訂概念なし、Phase 6 簡素化）| ✅ 単価引当ロジック（family 経由）・customer_name 凍結タイミング（初回 Excel 出力時）明示 |
| D. 発注書 Excel 出力 | `GET /purchase-orders/{id}/excel`（何度でも可能、初回時のみ order_no 採番、Phase 6 簡素化）| ✅ 初回採番ロジック、Idempotency-Key で初回時の二重採番防止 |
| E. 権限変更（Firebase 同期）| `PATCH /users/{id}/permissions` | ✅ RDS 先行 → Firebase Custom Claims 後追い、失敗時の Reconciler 設計明示 |

**追加で網羅すべきデータフロー検証:**

| シナリオ | API 呼出 |
|---|---|
| F. ログイン → セッション確立 | Firebase SDK `signInWithEmailAndPassword` → `POST /auth/sync` → `GET /auth/me` |
| G. マスタ追加（M-02）| `POST /masters/{master}`（共通テンプレート） |
| H. 画像アップロード（P-06）| `POST /products/families/{id}/images/upload-url` → S3 Pre-signed PUT → `POST /products/families/{id}/images` |
| I. 発注書編集（O-04）| `PATCH /purchase-orders/{id}`（status=Active のみ可、改訂概念は Phase 6 で廃止）|
| J. 中止 → 中止後参照検証 | `POST /purchase-orders/{id}/cancel` → 再度 cancel で 200（冪等）、編集試行（PATCH）で 409 ORDER-003。**Excel 出力は中止後も可能**（業務的に過去発注書の取出要件あり）|

→ **全 21 機能 × 主要パスで API I/F 矛盾なし。**

---

## 6. I/F 設計 6 視点チェック（API 層）

| # | 視点 | チェック結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ ASP.NET Core 8 Minimal API + JwtBearer + FluentValidation + Mapster の標準スタックで実装可能 |
| 2 | ユースケース | ✅ UC-1〜UC-4 全カバー（§5 マッピングで A〜J の 10 主要フロー検証）|
| 3 | ユーザビリティ | ✅ 詳細取得 1 リクエスト方針（P-05）でフロント状態管理が単純、エラーコード体系で一貫表示 |
| 4 | データ設計上の都合 | ✅ data-design.md のエンティティ粒度と API リソース粒度が 1:1 もしくは集約として整合 |
| 5 | 型の継承関係 | ✅ Entity → DTO → API スキーマ → openapi-typescript で生成された TS 型 まで一貫 |
| 6 | データフロー整合性 | ✅ §5 で 10 シナリオの起点 → 派生 → 終点まで I/F 矛盾なくトレース完了 |

---

## 7. 設計上の確認事項（オペレーターレビュー Phase5-Api）

| # | 論点 | 推奨案 |
|---|---|---|
| **API-1** | URL バージョニング方式（`/api/v1/` URL 埋込）| 採用（破壊的変更時に並行運用容易）|
| API-2 | エラー形式（RFC 7807 Problem Details）| 採用（標準準拠、Phase 3 §10 エラーコードを `code` フィールドで保持）|
| API-3 | マスタ CRUD の共通テンプレート化 | 採用（17マスタ × 6エンドポイント = 102 個別実装を回避）|
| API-4 | 商品詳細 1リクエスト方針（family + products + images + prices 一括）| 採用（フロント状態単純化）|
| API-5 | 発注一覧 `total_amount` デフォルトマスク | 採用（機密度配慮、`price:read` + `?include_amount=true` で開示）|
| API-6 | Excel 出力時の発注番号採番（GET + 副作用）| 採用、ただし `Idempotency-Key` ヘッダ推奨で冪等性担保。HTTP 仕様的に GET の副作用は議論あるが既存運用慣習を優先 |
| API-7 | OpenAPI YAML 完全版の生成タイミング | Phase 5 後半のプロトタイプ実装時に Swashbuckle で自動生成 |
| API-8 | Idempotency-Key 必須化エンドポイント | `POST /purchase-orders`, `POST /purchase-orders/{id}/confirm`, `GET /excel`（採番系）|
| API-9 | レート制限（429 Too Many Requests）| MVP では設定なし、Phase 7 で必要時 App Runner + CloudFront の WAF 設定追加 |
| API-10 | API ドキュメント公開先 | stg 環境で Swagger UI 公開（社内のみ）。本番は非公開 |

---

## 8. 変更履歴

| 日付 | 内容 |
|---|---|
| 2026-05-19 | 初版作成（21機能 × 全エンドポイント定義、A〜J 10 主要フロー検証完了）|
| 2026-07-27 | Iteration 30: §2.7 勤怠（勤怠管理・タイムカード）を追加。実装済みエンドポイント 27 本（打刻・集計 6 / 打刻修正申請 3 / 勤怠ルール 5 / 休暇 13）、認可の考え方（勤怠権限 0/1/2 + オーナー集約 + 他人参照ガード）、`AKB-ATT-*` がアラート識別子でエラーコードではない旨、主要 DTO、監査ログ action、§2.1 への `attendancePermission` / `punchRequired` 追加を記載（akebono-office からの移植）|
| 2026-07-27 | **バックエンド修正コミット `4c6981e`（監査・レビュー指摘の反映）の未反映分を §2.7 へ反映（原則5）。** ドキュメント整合コミット `ce30be6` が `4c6981e` より前にあったため未反映だった。**訂正:** §2.7.1 タイムカードの期間上限 — 「両端を含めて 63 日まで受け付ける（メッセージとの 1 日のずれは実装の現状）」はオフバイワンのバグを仕様として正規化した誤記。実装は `to - from + 1 > 62` で**両端を含めて 62 日まで**（63 日は 422）、フロント 3 箇所も同じ数え方に統一済み。**追記:** #8 / #21 一覧へのキーセットページング（`?limit`（1〜200、当該 2 本は省略時 200）/ `?cursor`、`meta.page = {nextCursor, limit, hasMore}`、不正は 400 `AKB-SYS-011`）/ #6 タイムカード 200 人・#27 休暇管理 500 人の利用者数上限（400 `AKB-SYS-011`）/ #7 `requestedAt` の対象日〜翌日の範囲検証（422）/ 日付・年月の業務日付範囲 `2000-01-01` 〜 実行日の 1 年後（422）/ §2.7.9 として `PATCH /api/maker/v1/users/{id}` の `UserPatchRequest` 化（`null` = 現在値保持、`clearHireDate` / `clearAttendanceRule`、`email` は空文字クリア）。あわせて冒頭に「API 変更は同一コミットでドキュメント更新」の運用ルールを明記 |
