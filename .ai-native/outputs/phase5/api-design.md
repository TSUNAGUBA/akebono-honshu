# Phase 5 成果物: API 設計

> **作成日:** 2026-05-19
> **状態:** ドラフト v1（オペレーターレビュー前）
> **依存:** Phase 3 機能要件（21機能 + エラーコード体系）+ Phase 4 確定スタック（ASP.NET Core 8 Minimal API + Firebase Auth + OpenAPI 3.0）
>          + `architecture.md` + `data-design.md`
> **方針:** REST + JSON。1 API = 1 責務（癒着回避）、クライアントに集約・加工責務を押し付けない。
>          Phase 5 ゲート条件「API 設計に癒着がない」「全データフローが I/F レベルで検証済み」を充足する。

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
| POST | 非冪等 | 二重発行リスクのある操作（発注確定等）は `Idempotency-Key` ヘッダで担保（クライアントが UUID 生成して同じキーで再送可）|

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

#### POST /api/v1/masters/{master}

**エラー:**
- 409 MASTER-001: code 重複
- 422 MASTER-002: FK 不整合（例: 存在しない `country_id` を suppliers に指定）

> **実装方針:** `MasterEntity` を IMaster インターフェースで抽象化、`MasterController<TEntity, TDto>` ジェネリックで共通実装。各マスタ固有の拡張カラム（suppliers.country_id 等）は `IValidator<TDto>` で検証。

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

#### P-03: マルチ仕入先単価（アイテム単位）

> **Phase 6 修正:** 仕入単価は **アイテム (product_family) 単位** で管理（旧設計の SKU 単位から変更）。同一企画内では色違い・サイズ違いでも仕入単価は同じ。

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/products/families/{familyId}/supplier-prices` | 一覧（履歴含む）|
| `POST` | `/api/v1/products/families/{familyId}/supplier-prices` | 新規（既存 effective_to を自動更新）|
| `PATCH` | `/api/v1/products/families/{familyId}/supplier-prices/{priceId}` | 更新（誤入力修正、監査記録）|
| `DELETE` | `/api/v1/products/families/{familyId}/supplier-prices/{priceId}` | 論理削除 |

##### POST /api/v1/products/families/{familyId}/supplier-prices

**認可:** `product:write` AND `price:write`（機密度 中-高 NFR §6.2）

**Request:**
```json
{
  "supplier_id": 14,
  "unit_price": 1250.00,
  "currency_code": "JPY",
  "exchange_rate": null,
  "effective_from": "2026-06-01",
  "decided_at": "2026-05-19"
}
```

**処理:**
1. トランザクション開始
2. 同一 `(product_family_id, supplier_id)` の現在有効レコード（`effective_to IS NULL`）の `effective_to` を `new.effective_from - 1day` で UPDATE
3. 新レコード INSERT
4. audit_logs INSERT（**unit_price は "***" にマスク**、operator/product_family/supplier のみ記録）
5. コミット

**エラー:**
- 409 PRICE-001: `(product_family_id, supplier_id, effective_from)` 重複
- 422 PRICE-002: unit_price <= 0
- 422 PRICE-003: effective_to <= effective_from

> **発注時の引当てロジック:** 発注作成（§2.5）で各明細行の `unit_price_snapshot` を埋める際、`products.product_family_id` 経由で `product_supplier_prices` を引き、現在有効レコードの単価を採用。色違い・サイズ違いの SKU はすべて同一の単価が引当てられる。

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

| メソッド | パス | 用途 |
|---|---|---|
| `POST` | `/api/v1/purchase-orders` | 発注書 (Draft) 新規作成（O-01/O-02 共通）|

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
   - `purchase_orders` INSERT (status=Draft, revision_no=0)
   - 各 line について `product_supplier_prices` から現在有効単価を引当て → `purchase_order_lines` バルク INSERT
   - `delivery_destinations.customer_name` を `customer_name_snapshot` に複写
   - audit_logs INSERT
3. コミット

**Response 201:** `Location: /api/v1/purchase-orders/{id}`

**エラー:**
- 422 ORDER-001: 必須項目欠落
- 409 ORDER-002: 指定仕入先に対する unit_price 未設定
- 422 ORDER-004: due_date が過去

#### O-03: 発注書一覧・検索

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/purchase-orders` | 一覧・検索 |

##### GET /api/v1/purchase-orders

**認可:** `purchase_order:read`

**クエリ:**
- `?q=<text>` フリーワード (mgmt_no, order_no, customer_name_snapshot, supplier.name)
- `?filter[date_from]=2026-01-01&filter[date_to]=2026-12-31`
- `?filter[status]=Draft,Submitted`
- `?filter[is_cancelled]=false`
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
      "revision_no": 0,
      "status": "Submitted",
      "is_cancelled": false,
      "supplier": { "id": 14, "name": "藤東工業" },
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

#### O-04: 発注書修正・改訂

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/purchase-orders/{id}` | 詳細（明細 + 改訂チェーン含む）|
| `PATCH` | `/api/v1/purchase-orders/{id}` | 修正（Draft のみ可、軽微編集）|
| `POST` | `/api/v1/purchase-orders/{id}/revisions` | 改訂（Submitted 後の正式改訂、新 ID + revision_no++ で枝番採番）|

##### POST /api/v1/purchase-orders/{id}/revisions

**Request:**
```json
{
  "revised_reason": "数量変更：50→80",
  "lines": [...],
  "communication_text": "..."
}
```

**処理:**
1. 親発注書取得、`status IN (Submitted, Revised)` を確認（Cancelled なら 409 ORDER-003）
2. 新 `purchase_orders` レコード作成（`parent_order_id = parent.id`, `revision_no = parent.revision_no + 1`, `status = Draft`）
3. `purchase_order_revisions` INSERT
4. 新明細 INSERT
5. audit_logs INSERT

**Response 201:** 新発注書 ID

#### O-05: 発注中止

| メソッド | パス | 用途 |
|---|---|---|
| `POST` | `/api/v1/purchase-orders/{id}/cancel` | 中止フラグ ON |

##### POST /api/v1/purchase-orders/{id}/cancel

**Request:**
```json
{
  "cancel_reason": "顧客都合"
}
```

**処理:**
1. `is_cancelled=true`, `status=Cancelled`, `cancelled_at=NOW()`, `cancelled_by_user_id=current`, `cancel_reason=...` を UPDATE
2. audit_logs INSERT
3. 既に Cancelled の場合 409 ORDER-005（冪等性配慮で 200 にすべきか Phase 5 で判断）

#### O-06: 発注書 Excel 出力

| メソッド | パス | 用途 |
|---|---|---|
| `GET` | `/api/v1/purchase-orders/{id}/excel` | Excel ファイルダウンロード + 初回時に発注番号採番 |

##### GET /api/v1/purchase-orders/{id}/excel

**認可:** `purchase_order:read`

**Headers:**
- `Accept: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`

**処理:**
1. 発注書 + 明細 + 関連マスタを一括取得（N+1 回避）
2. `order_no` 未採番なら採番（`S` + 4桁連番）+ `status=Submitted`, `submitted_at=NOW()` UPDATE（冪等性: Idempotency-Key 推奨）
3. ClosedXML テンプレートに流し込み
4. MemoryStream で Response Body
5. audit_logs INSERT (`Excel.Export`)

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

## 3. OpenAPI 3.0 雛形

> 完全な OpenAPI YAML は Phase 5 後半のプロトタイプ実装時に生成（Swashbuckle.AspNetCore で自動生成）。本ドキュメントでは構造例のみ示す。

```yaml
openapi: 3.0.3
info:
  title: あけぼの本州 アパレル生産管理 API
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
| API 定義で使い方がわかる単位 | 動詞ベースの専用エンドポイント（`/expand`, `/cancel`, `/confirm`, `/revisions`）で意図を明示 |

---

## 5. 全データフロー I/F 検証（architecture.md §4 シナリオ → API マッピング）

| シナリオ | API 呼出 | 検証結果 |
|---|---|---|
| A. 商品マスタ登録 | `POST /products/families` → `POST /products/families/{id}/expand` → `POST /products/{id}/supplier-prices`（複数）| ✅ 全 I/F が定義済、トランザクション境界がエンドポイント単位で完結 |
| B. 仕入先 × 商品 × 仕入単価設定 | `POST /products/{productId}/supplier-prices` | ✅ 認可は AND 評価（product:write + price:write）、監査ログマスク仕様明示 |
| C. 発注書作成 | `POST /purchase-orders` → `POST /purchase-orders/{id}/confirm` (= GET /excel 兼用) | ✅ 単価引当ロジック・Customer 名スナップショット仕様明示 |
| D. 発注書 Excel 出力 | `GET /purchase-orders/{id}/excel` | ✅ 初回採番ロジック、Idempotency-Key 推奨で冪等性担保 |
| E. 権限変更（Firebase 同期）| `PATCH /users/{id}/permissions` | ✅ RDS 先行 → Firebase Custom Claims 後追い、失敗時の Reconciler 設計明示 |

**追加で網羅すべきデータフロー検証:**

| シナリオ | API 呼出 |
|---|---|
| F. ログイン → セッション確立 | Firebase SDK `signInWithEmailAndPassword` → `POST /auth/sync` → `GET /auth/me` |
| G. マスタ追加（M-02）| `POST /masters/{master}`（共通テンプレート） |
| H. 画像アップロード（P-06）| `POST /products/families/{id}/images/upload-url` → S3 Pre-signed PUT → `POST /products/families/{id}/images` |
| I. 発注書改訂（O-04）| `POST /purchase-orders/{id}/revisions` |
| J. 中止 → 取消不可検証 | `POST /purchase-orders/{id}/cancel` → 再度 cancel で 409、改訂試行で 409 |

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
