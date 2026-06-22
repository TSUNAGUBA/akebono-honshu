# Phase 5 成果物: API 設計（生産管理拡張）

> **作成日:** 2026-06-22
> **状態:** ドラフト v1（独立レビュー前）
> **位置づけ:** 既存 `api-design.md` への**増分**。生産管理（BOM・生産指示・素材発注・未/済）の REST API。
> **依存:** `data-design-production.md`、`functional-requirements-production.md`、既存 `api-design.md` の共通規約（§1）
> **方針:** 既存共通規約を完全踏襲。URL=`/api/v1/`、認証=Bearer(Firebase ID Token)、エラー=Problem Details(RFC7807)＋エラーコード、ページング/ソート/フィルタ規約、冪等性(Idempotency-Key)、1API=1責務（癒着回避）。

---

## 1. 共通規約の継承

既存 `api-design.md` §1 をそのまま適用（再掲しない）:
- バージョニング `/api/v1/`
- 認証 `Authorization: Bearer <Firebase ID Token>`、認可は4権限ポリシー
- 成功/一覧(ページング)/エラー(Problem Details) のレスポンス形式
- HTTP ステータス規約、ソート `?sort=-field`、フィルタ `?filter[x]=`
- 冪等性: 作成系は `Idempotency-Key` ヘッダで二重作成防止
- 監査対象: C/U/D・Excel出力・機密閲覧

**エラーコード（生産拡張）:** `PROD-101`〜`PROD-109`（`functional-requirements-production.md` §2 準拠）。`ErrorCodes.cs` に追加。

---

## 2. エンドポイント設計

### 2.1 BOM（B-01 / B-02）

> **設計判断（癒着回避）:** BOM は品番(family)の子リソース。`/products/families/{familyId}/materials`。

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/products/families/{familyId}/materials` | BOM一覧取得 | `product_ledger:read` |
| `PUT` | `/api/v1/products/families/{familyId}/materials` | BOM一括更新（全置換） | `product_ledger:write` |
| `GET` | `/api/v1/products/families/{familyId}/material-requirements?quantity=...` | 所要量展開プレビュー（B-02） | `product_ledger:read` |

#### PUT /products/families/{familyId}/materials
**Request:**
```json
{
  "materials": [
    { "material_role": 0, "material_id": 12, "required_qty_per_unit": 0.3000, "unit": "㎡", "recommended_supplier_id": 31, "loss_rate": 0.05, "remark": "甲表生地" },
    { "material_role": 2, "material_id": 40, "required_qty_per_unit": 1.0000, "unit": "組", "recommended_supplier_id": 56, "loss_rate": 0 },
    { "material_role": 4, "material_id": 88, "required_qty_per_unit": 1.0000, "unit": "枚", "recommended_supplier_id": null, "loss_rate": 0, "remark": "値札" }
  ]
}
```
**処理:** トランザクション内で `product_materials` を全置換（既存行は論理削除→新規INSERT、または差分upsert）。部位0/1/2は `product_families` 3FK列へ同期。重複(部位×素材)検証。audit_logs(ProductMaterial.*)。
**Response 200:** 更新後BOM一覧。
**エラー:** 422 PROD-101（所要量/単位不正）、409 PROD-102（部位×素材重複）。

#### GET /products/families/{familyId}/material-requirements?quantity=500
**処理:** BOM × quantity で `Σ required_qty_per_unit × quantity × (1+loss_rate)` を素材別に集計し、推奨仕入先別にグルーピング。
**Response 200:**
```json
{
  "family_id": 100, "total_quantity": 500,
  "groups": [
    { "recommended_supplier": { "id": 31, "name": "岩本ゴム工業" },
      "lines": [ { "material": { "id": 12, "name": "ポリエステル" }, "material_role": 0, "required_quantity": 157.5000, "unit": "㎡" } ] },
    { "recommended_supplier": null, "lines": [ { "material": { "id": 88, "name": "値札" }, "material_role": 4, "required_quantity": 500.0000, "unit": "枚" } ] }
  ]
}
```
**エラー:** 422 PROD-105（BOM未登録 → 素材発注不可、BOM登録へ誘導）。

> **注:** quantity は色×サイズ別内訳でも合計でも可。生産指示id起点の場合は別途 `?production_instruction_id=` で明細合計から算出（下記 MO-01 で利用）。

### 2.2 生産指示（PI-01〜04）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `POST` | `/api/v1/production-instructions` | 新規作成（PI-01） | `production_info:write` |
| `GET` | `/api/v1/production-instructions` | 一覧・検索（PI-02） | `production_info:read` |
| `GET` | `/api/v1/production-instructions/{id}` | 詳細 | `production_info:read` |
| `PATCH` | `/api/v1/production-instructions/{id}` | 編集（PI-03） | `production_info:write` |
| `POST` | `/api/v1/production-instructions/{id}/issue` | 発行（指示確定） | `production_info:write` |
| `POST` | `/api/v1/production-instructions/{id}/complete` | 生産完了 | `production_info:write` |
| `POST` | `/api/v1/production-instructions/{id}/cancel` | 中止 | `production_info:write` |
| `GET` | `/api/v1/production-instructions/{id}/excel` | Excel出力（PI-04） | `production_info:read` |

#### POST /api/v1/production-instructions
**Request:**
```json
{
  "product_family_id": 100,
  "factory_supplier_id": 14,
  "due_date": "2026-09-30",
  "communication_text": "サンプル合格後に本生産開始。",
  "lines": [
    { "product_id": 1001, "quantity": 200 },
    { "product_id": 1002, "quantity": 300 }
  ]
}
```
**処理:** トランザクションで `production_instructions`(status=0/Draft)＋`production_instruction_lines` INSERT、`instruction_no`(YY-PI-NNNNN)採番、`planned_quantity`＝明細合計、SKUスナップショット凍結、audit。
**Response 201:** `Location: /api/v1/production-instructions/{id}`
**エラー:** 422 PROD-103（数量0）、422 PROD-104（同一product_id重複）、422 PROD-107（品番にSKU未展開/family不一致）。

#### GET /api/v1/production-instructions
**クエリ:** `?q=`（instruction_no/品番/品名/加工先名）、`?filter[status]=Draft,Issued,Completed,Cancelled`、`?filter[factory_supplier_id]=`、`?filter[due_from]=&filter[due_to]=`、`?filter[product_family_id]=`、`?sort=-created_at`、`?page=&per_page=`、`?view=table|card`
**Response 200（一覧、ページング）:** 各要素に `instruction_no, product_sku9, product_name, factory:{id,name}, planned_quantity, due_date, status, export_state(unexported/exported), updated_*`。

#### PATCH /api/v1/production-instructions/{id}
**処理:** status=Draft/Issued のみ編集可（明細差し替え可）。Cancelled/Completed は不可（PROD-108）。audit。

#### POST /{id}/issue
**処理:** status 0→1、`instructed_at`=now。これにより品番の「生産指示=済」。冪等（既にIssuedなら200）。audit(ProductionInstruction.Issue)。

#### POST /{id}/cancel
**Request:** `{ "reason": "受注キャンセル" }` → status=9、`cancelled_at`/`cancelled_by`/`cancel_reason`。物理削除しない。

#### GET /{id}/excel
**処理:** ClosedXMLでテンプレ流し込み。初回時のみ `factory_official_name_snapshot`/`factory_code_snapshot`/`product_sku9_snapshot`/`product_name_snapshot` 凍結＋`first_exported_at` SET、毎回 `last_exported_at`＋audit(Excel.Export, entity=ProductionInstruction)。
**Response 200:** `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`、`Content-Disposition: attachment; filename="production-instruction-{instruction_no}.xlsx"`（CORS `WithExposedHeaders("Content-Disposition")`、既存 Iter3 知見#3）。
**エラー:** 500 PROD-109（テンプレ/生成失敗）。

### 2.3 素材発注（MO-01〜04）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `POST` | `/api/v1/material-orders/prepare` | BOM展開→仕入先別ドラフト提案（保存しない） | `production_info:read` |
| `POST` | `/api/v1/material-orders` | 新規作成（MO-01、1仕入先1発注） | `production_info:write` |
| `GET` | `/api/v1/material-orders` | 一覧・検索（MO-02） | `production_info:read` |
| `GET` | `/api/v1/material-orders/{id}` | 詳細 | `production_info:read` |
| `PATCH` | `/api/v1/material-orders/{id}` | 編集（MO-03） | `production_info:write` |
| `POST` | `/api/v1/material-orders/{id}/order` | 発注確定 | `production_info:write` |
| `POST` | `/api/v1/material-orders/{id}/cancel` | 中止 | `production_info:write` |
| `GET` | `/api/v1/material-orders/{id}/excel` | Excel出力（MO-04） | `production_info:read` |

#### POST /api/v1/material-orders/prepare
> **設計判断（癒着回避）:** 「BOM展開＋仕入先別グルーピング」という計算責務を、作成API(`POST /material-orders`)から分離。プレビュー専用で副作用なし。フロントは結果を編集して仕入先別に `POST /material-orders` を呼ぶ。
**Request:** `{ "production_instruction_id": 50 }` または `{ "product_family_id": 100, "quantity": 500 }`
**Response 200:** §2.1 material-requirements と同形式の仕入先別グループ（推奨数量・単位付き）。各行に `source_pi_line_id`（指示起点時）。
**エラー:** 422 PROD-105（BOM未登録）。

#### POST /api/v1/material-orders
**Request:**
```json
{
  "material_supplier_id": 31,
  "production_instruction_id": 50,
  "due_date": "2026-08-31",
  "communication_text": "...",
  "lines": [
    { "material_id": 12, "product_family_id": 100, "source_pi_line_id": 501,
      "required_quantity": 157.5000, "unit": "㎡", "unit_price": 320.00, "currency_code": "JPY" }
  ]
}
```
**処理:** トランザクションで `material_orders`(status=0/Draft)＋`material_order_lines` INSERT、`order_no`(YY-MO-NNNNN)採番、素材名スナップショット、audit。
**Response 201:** `Location`。
**エラー:** 422 PROD-106（数量0/単価負）、422 PROD-105（明細にBOM由来なく品番特定不可は許容、ただし production_instruction_id 指定時は整合チェック）。

#### POST /{id}/order
**処理:** status 0→1、`instructed_at`=now。これにより当該明細の由来品番の「素材発注=済」。冪等。audit(MaterialOrder.Order)。

#### GET /{id}/excel
**処理:** 初回時のみ仕入先スナップショット凍結＋`first_exported_at`。素材単価は帳票には出すが監査ログはマスク。

### 2.4 品番ごと未/済（PS-01）

> **設計判断（癒着回避）:** 既存 `GET /api/v1/products`（P-04 商品一覧）に**生産手配サマリを任意展開**で追加（別リソース化せず、一覧の派生属性）。重い算出を避けるため `?include=production_status` で opt-in。

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/products?include=production_status&filter[material_order_state]=undone&filter[production_instruction_state]=done` | 商品一覧に未/済バッジ＋フィルタ（PS-01） | `product_ledger:read` |

**処理:** `include=production_status` 指定時、各 family 行に EXISTS×2 を SQL で算出（§data-design §7.2、`idx_pi_family_active`/`idx_mol_family` 利用、N+1なし）。
**Response 200（data[] 各要素に追加）:**
```json
{ "family_id": 100, "sku9": "NA1001A40", "product_name_1": "...",
  "production_status": {
    "material_order": "done",            // done | undone
    "production_instruction": "done"
  } }
```
**フィルタ:** `?filter[material_order_state]=done|undone`、`?filter[production_instruction_state]=done|undone`（未手配抽出）。
**性能:** 2,000品番 500ms（部分インデックス）。

---

## 3. OpenAPI 追加スキーマ（要点）

```yaml
ProductMaterial:
  type: object
  properties:
    material_role: { type: integer, enum: [0,1,2,3,4] }   # 甲皮/中底/底/付属/副資材
    material: { $ref: '#/components/schemas/MasterRef' }
    required_qty_per_unit: { type: number, format: double }
    unit: { type: string }
    recommended_supplier: { $ref: '#/components/schemas/MasterRef', nullable: true }
    loss_rate: { type: number, format: double }
ProductionInstruction:
  type: object
  properties:
    instruction_no: { type: string }
    product_family_id: { type: integer, format: int64 }
    factory: { $ref: '#/components/schemas/MasterRef' }
    planned_quantity: { type: integer }
    due_date: { type: string, format: date }
    status: { type: string, enum: [Draft, Issued, Completed, Cancelled] }
    export_state: { type: string, enum: [unexported, exported] }
    lines: { type: array, items: { $ref: '#/components/schemas/ProductionInstructionLine' } }
MaterialOrder:
  type: object
  properties:
    order_no: { type: string }
    material_supplier: { $ref: '#/components/schemas/MasterRef' }
    production_instruction_id: { type: integer, format: int64, nullable: true }
    status: { type: string, enum: [Draft, Ordered, Cancelled] }
    lines: { type: array, items: { $ref: '#/components/schemas/MaterialOrderLine' } }
```
> Enum は API では文字列（既存 `JsonStringEnumConverter` グローバル登録、Iter3 知見#2）、DB は SMALLINT。

---

## 4. API 癒着回避の検証（既存 §4 4原則）

| 原則 | 検証 |
|---|---|
| 1API=1責務 | BOM展開(`/material-requirements`,`/prepare`)を作成APIから分離。発行/完了/中止は専用サブリソースPOST |
| 集約・加工をクライアントに押し付けない | 所要量展開・仕入先別グルーピング・未/済算出はサーバ側で完結 |
| 別リソースを混在させない | 生産指示と素材発注は別リソース。未/済は商品一覧の派生属性として opt-in（`include=`） |
| 使い方が分かる単位 | RESTful・既存命名(purchase-orders)と並列(production-instructions / material-orders) |

---

## 5. 全データフロー I/F 検証（主要シナリオ）

### シナリオ P-A: 商品マスタ→生産指示→素材発注→未/済
```
[1] PUT /products/families/100/materials  … BOM登録（甲皮/底/値札の所要量）
[2] POST /production-instructions          … 品番100＋色サイズ別数量で生産指示Draft作成 → 26-PI-00001
[3] POST /production-instructions/{id}/issue … status=Issued, instructed_at SET → 品番100「生産指示=済」
[4] GET  /production-instructions/{id}/excel  … 生産指示書Excel（加工先名凍結）
[5] POST /material-orders/prepare {production_instruction_id:50} … BOM×数量で仕入先別ドラフト提案
[6] POST /material-orders (仕入先31)        … 素材発注Draft作成 → 26-MO-00001（明細に product_family_id=100）
[7] POST /material-orders/{id}/order        … status=Ordered → 品番100「素材発注=済」
[8] GET  /material-orders/{id}/excel         … 素材発注書Excel（仕入先名凍結）
[9] GET  /products?include=production_status … 品番100: material_order=done, production_instruction=done
```
**SoTチェック:** 全てRDS。BOM(SoT)→3FK列(cache)同期は[1]で。未/済は[9]でSoT直読(EXISTS)。
**冪等性:** issue/order は冪等（既済なら200）。作成は Idempotency-Key。Excel再出力は初回採番のみ凍結。
**非ブロッキング:** audit_logs 失敗は警告ログのみで主処理継続（既存原則4）。

### シナリオ P-B: BOM未登録での素材発注
```
POST /material-orders/prepare {product_family_id:100} → 422 PROD-105（BOM未登録）
  → フロントはBOM登録画面(B-01)へ誘導
```

---

## 6. I/F 6視点チェック（API層）

| # | 視点 | 結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ ASP.NET Core Minimal API、ClosedXML、Npgsql。既存と同構成 |
| 2 | ユースケース | ✅ UC-PROD-1〜5 を全エンドポイントでカバー |
| 3 | ユーザビリティ | ✅ prepare で展開プレビュー、未/済 opt-in で一覧軽量、エラーは誘導付き |
| 4 | データ設計上の都合 | ✅ data-design-production の5テーブルと1:1対応、Enum文字列⇔SMALLINT |
| 5 | 型の継承関係 | ✅ MasterRef 共通DTO、Enum 文字列変換グローバル設定を流用 |
| 6 | データフロー整合性 | ✅ §5 で起点→派生の一気通貫を検証、SoT順序遵守 |

---

## 7. 変更履歴
| 日付 | 内容 |
|---|---|
| 2026-06-22 | 初版（BOM 3本＋生産指示 8本＋素材発注 8本＋未/済 商品一覧拡張） |
