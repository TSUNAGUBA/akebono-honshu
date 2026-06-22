# Phase 5 成果物: API 設計（生産管理拡張）

> **作成日:** 2026-06-22 / **改訂:** 2026-06-22 v2（独立レビュー1周目 反映: 認可トークン是正・エラーコード接頭辞独立化・素材単価のprice権限/マスク/監査・採番同時実行安全化）
> **状態:** ドラフト v2（再レビュー前）
> **位置づけ:** 既存 `api-design.md` への**増分**。生産管理（BOM・生産指示・素材発注・未/済）の REST API。
> **依存:** `data-design-production.md` v2、`functional-requirements-production.md`、既存 `api-design.md` の共通規約（§1）
> **方針:** 既存共通規約を完全踏襲。**認可は既存実在トークンのみ使用**（`product:*`/`price:*`/`purchase_order:*`、監査C-1反映）。素材単価は既存仕入単価と同一保護（監査C-2反映）。

---

## 1. 共通規約の継承

既存 `api-design.md` §1 を適用（再掲しない）: `/api/v1/`、Bearer(Firebase ID Token)、Problem Details(RFC7807)、ページング/ソート/フィルタ、冪等性(Idempotency-Key)、監査対象、`[Authorize]` 必須化（CI Lint、R-6）。

> **エラーコード（生産拡張、接頭辞を独立化＝コードレビュアーC-2反映）:** 既存 `PROD-NNN` は**商品マスタ(品番/SKU)ドメイン専用**（PROD-001/002/003）。生産系は接頭辞を分離し `ErrorCodes.cs` に追加:
>
> | コード | 内容 | 機能 |
> |---|---|---|
> | `BOM-001` | 所要量・単位不正 | B-01 |
> | `BOM-002` | 部位×素材重複 | B-01 |
> | `PINST-001` | 生産指示 数量0 | PI-01 |
> | `PINST-002` | 生産指示 SKU重複 | PI-01 |
> | `PINST-003` | 品番にSKU未展開/family不一致 | PI-01 |
> | `PINST-004` | 中止済生産指示の編集不可 | PI-03 |
> | `MORD-001` | BOM未登録（素材発注の所要量展開不可） | MO-01 |
> | `MORD-002` | 素材発注 数量/単価不正 | MO-01 |
> | `MORD-003` | 中止済素材発注の編集不可 | MO-03 |
> | `EXPORT-001`/`EXPORT-002` | Excel テンプレ不正/生成失敗（**既存コード再利用**） | PI-04/MO-04 |
>
> Phase 3 非機能 §7 のエラーコード接頭辞リストに `BOM/PINST/MORD` を追記。

> **認可トークン（既存実在のもののみ、監査C-1反映）:** `production_info:*` は存在しないため不使用。割当は `data-design-production.md §12`。要約: BOM=`product:*`、生産指示/素材発注=`purchase_order:*`、素材単価開示=`price:read` AND、素材単価設定=`price:write` AND。

---

## 2. エンドポイント設計

### 2.1 BOM（B-01 / B-02）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/products/families/{familyId}/materials` | BOM一覧取得 | `product:read` |
| `PUT` | `/api/v1/products/families/{familyId}/materials` | BOM一括更新（差分upsert） | `product:write` |
| `GET` | `/api/v1/products/families/{familyId}/material-requirements?quantity=...` | 所要量展開プレビュー（B-02、金額なし） | `product:read` |

#### PUT /products/families/{familyId}/materials
**Request:**
```json
{ "materials": [
  { "material_role": 0, "material_id": 12, "required_qty_per_unit": 0.3000, "unit": "㎡", "recommended_supplier_id": 31, "loss_rate": 0.05, "remark": "甲表生地" },
  { "material_role": 2, "material_id": 40, "required_qty_per_unit": 1.0000, "unit": "組", "recommended_supplier_id": 56, "loss_rate": 0 },
  { "material_role": 4, "material_id": 88, "required_qty_per_unit": 1.0000, "unit": "枚", "recommended_supplier_id": null, "loss_rate": 0, "remark": "値札" }
] }
```
**処理:** **単一トランザクション**で `product_materials` を**差分upsert**（既存行のIDを保持、削除は `is_deleted=TRUE`、監査M-3）。**3FK列へは書き戻さない**（疎結合、data-design §7.1）。重複(部位×素材)検証。audit_logs(ProductMaterial.*)。
**Response 200:** 更新後BOM一覧。
**エラー:** 422 `BOM-001`（所要量/単位不正）、409 `BOM-002`（部位×素材重複）。

#### GET /products/families/{familyId}/material-requirements?quantity=500
**処理:** BOM × quantity で `Σ required_qty_per_unit × quantity`（loss_rate 設定行のみ `×(1+loss_rate)`、M-1反映）を素材別集計、推奨仕入先別グルーピング。**金額は返さない**（→ `product:read` で足りる）。
**Response 200:**
```json
{ "family_id": 100, "total_quantity": 500,
  "groups": [
    { "recommended_supplier": { "id": 31, "name": "岩本ゴム工業" },
      "lines": [ { "material": { "id": 12, "name": "ポリエステル" }, "material_role": 0, "required_quantity": 157.5000, "unit": "㎡" } ] },
    { "recommended_supplier": null, "lines": [ { "material": { "id": 88, "name": "値札" }, "material_role": 4, "required_quantity": 500.0000, "unit": "枚" } ] }
  ] }
```
**エラー:** 422 `MORD-001`（BOM未登録 → 素材発注不可、BOM登録へ誘導）。

### 2.2 生産指示（PI-01〜04）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `POST` | `/api/v1/production-instructions` | 新規作成（PI-01） | `purchase_order:write` |
| `GET` | `/api/v1/production-instructions` | 一覧・検索（PI-02） | `purchase_order:read` |
| `GET` | `/api/v1/production-instructions/{id}` | 詳細 | `purchase_order:read` |
| `PATCH` | `/api/v1/production-instructions/{id}` | 編集（PI-03） | `purchase_order:write` |
| `POST` | `/api/v1/production-instructions/{id}/issue` | 発行（指示確定） | `purchase_order:write` |
| `POST` | `/api/v1/production-instructions/{id}/complete` | 生産完了 | `purchase_order:write` |
| `POST` | `/api/v1/production-instructions/{id}/cancel` | 中止 | `purchase_order:write` |
| `GET` | `/api/v1/production-instructions/{id}/excel` | Excel出力（PI-04） | `purchase_order:read` |

> 生産指示は金額（単価）を持たないため `price` 権限は不要（純粋な数量・加工先の指図）。

#### POST /api/v1/production-instructions
**Request:**
```json
{ "product_family_id": 100, "factory_supplier_id": 14, "due_date": "2026-09-30",
  "communication_text": "サンプル合格後に本生産開始。",
  "lines": [ { "product_id": 1001, "quantity": 200 }, { "product_id": 1002, "quantity": 300 } ] }
```
**処理:** 単一トランザクションで `production_instructions`(status=0)＋`production_instruction_lines` INSERT、`instruction_no`(YY-PI-NNNNN)を **advisory lock + リトライ**で採番（data-design §5）、`planned_quantity`＝明細合計を整合、SKUスナップショット凍結、audit。`Idempotency-Key` で二重作成防止。
**Response 201:** `Location`。
**エラー:** 422 `PINST-001`（数量0）、422 `PINST-002`（同一product_id重複）、422 `PINST-003`（品番にSKU未展開/family不一致）。

#### GET /api/v1/production-instructions
**クエリ:** `?q=`（指示番号/品番/品名/加工先名）、`?filter[status]=Draft,Issued,Completed,Cancelled`、`?filter[factory_supplier_id]=`、`?filter[due_from]=&filter[due_to]=`、`?filter[product_family_id]=`、`?sort=-created_at`、`?page=&per_page=`、`?view=table|card`
**Response 200:** 各要素に `instruction_no, product_sku9, product_name, factory:{id,name}, planned_quantity, due_date, status, export_state(unexported/exported), updated_*`。

#### PATCH /{id} / POST /{id}/issue / complete / cancel
- PATCH: status=Draft/Issued のみ編集可（明細差し替えは単一Tx内 DELETE/INSERT）。Cancelled/Completed は 409 `PINST-004`。
- issue: status 0→1、`instructed_at`=now → 品番の「生産指示=済」。冪等（既Issuedは200）。
- cancel: `{ "reason": "..." }` → status=9、cancelled_*。物理削除しない。

#### GET /{id}/excel
**処理:** ClosedXMLでテンプレ流し込み。初回時のみ `factory_official_name_snapshot`/`factory_code_snapshot`/`product_sku9_snapshot`/`product_name_snapshot` 凍結＋`first_exported_at`、毎回 `last_exported_at`。出力履歴は `audit_logs(Excel.Export, entity=ProductionInstruction)`（専用テーブルなし、data-design §7.3）。
**Response 200:** xlsx、`Content-Disposition: attachment; filename="production-instruction-{instruction_no}.xlsx"`（CORS `WithExposedHeaders("Content-Disposition")`）。
**エラー:** 500 `EXPORT-001/002`（テンプレ/生成失敗、監視対象=architecture §M-5）。

### 2.3 素材発注（MO-01〜04） ※素材単価＝機密、price権限ゲート

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `POST` | `/api/v1/material-orders/prepare` | BOM展開→仕入先別ドラフト提案（数量のみ、金額なし） | `purchase_order:read` |
| `POST` | `/api/v1/material-orders` | 新規作成（MO-01、単価設定含む） | `purchase_order:write` **AND** `price:write` |
| `GET` | `/api/v1/material-orders` | 一覧・検索（MO-02、合計はデフォルトマスク） | `purchase_order:read`（金額開示は ＋`price:read`） |
| `GET` | `/api/v1/material-orders/{id}` | 詳細（単価含む） | `purchase_order:read` **AND** `price:read` |
| `PATCH` | `/api/v1/material-orders/{id}` | 編集（MO-03） | `purchase_order:write` **AND** `price:write` |
| `POST` | `/api/v1/material-orders/{id}/order` | 発注確定 | `purchase_order:write` |
| `POST` | `/api/v1/material-orders/{id}/cancel` | 中止 | `purchase_order:write` |
| `GET` | `/api/v1/material-orders/{id}/excel` | Excel出力（単価含む） | `purchase_order:read` **AND** `price:read` |

> **素材単価の保護（監査C-2反映、既存仕入単価パターン踏襲）:**
> - `prepare`・`material-requirements` は**数量のみ返し金額を含まない** → `purchase_order:read`/`product:read` で足りる。
> - 詳細・Excel は単価を含むため `price:read` を AND 必須。開示時は **`MaterialPrice.View` 監査をブロッキングで記録**（記録失敗時は開示拒否、data-design §6）。
> - 一覧の合計金額（`total_amount`）は**デフォルトマスク `"***"`**。`?include_amount=true` ＋ `price:read` 保有時のみ実値＋`MaterialPrice.View` 監査（既存発注一覧と同方式）。

#### POST /api/v1/material-orders/prepare
**Request:** `{ "production_instruction_id": 50 }` または `{ "product_family_id": 100, "quantity": 500 }`
**Response 200:** §2.1 material-requirements と同形式（数量・単位・推奨仕入先、`source_pi_line_id` 付）。金額なし。
**エラー:** 422 `MORD-001`（BOM未登録）。

#### POST /api/v1/material-orders
**Request:**
```json
{ "material_supplier_id": 31, "production_instruction_id": 50, "due_date": "2026-08-31",
  "communication_text": "...",
  "lines": [ { "material_id": 12, "product_family_id": 100, "source_pi_line_id": 501,
              "required_quantity": 157.5000, "unit": "㎡", "unit_price": 320.00, "currency_code": "JPY" } ] }
```
**処理:** **独立トランザクション**（1リクエスト=1発注=1採番、監査C-4/M-1: 複数仕入先は逐次POST）で `material_orders`(status=0)＋`material_order_lines` INSERT、`order_no`(YY-MO-NNNNN)を advisory lock + リトライで採番、素材名スナップショット、audit。`Idempotency-Key` 必須。
**Response 201:** `Location`。
**エラー:** 422 `MORD-002`（数量0/単価負）、422 `MORD-001`（production_instruction_id 指定だが由来品番にBOMなし）。

#### POST /{id}/order
status 0→1、`instructed_at`=now → 当該明細の由来品番の「素材発注=済」。冪等。audit(MaterialOrder.Order)。

#### GET /{id} / /excel
- 詳細: `unit_price`/`subtotal` を含むため `price:read` 必須。**単価未確定(NULL)明細の subtotal は 0**（data-design Mi-2）。`MaterialPrice.View` 監査（ブロッキング）。
- Excel: 単価を帳票に出すため `price:read` 必須。初回時に仕入先スナップショット凍結＋`first_exported_at`。`Excel.Export`＋`MaterialPrice.View` 監査（ブロッキング）。

### 2.4 品番ごと未/済（PS-01）

| メソッド | パス | 用途 | 認可 |
|---|---|---|---|
| `GET` | `/api/v1/products?include=production_status&filter[material_order_state]=undone&filter[production_instruction_state]=done` | 商品一覧に未/済バッジ＋フィルタ | `product:read` |

**処理:** `include=production_status` 指定時、各 family 行に EXISTS×2 を SQL で算出（data-design §7.2、**明細 is_deleted は参照せず**親 `mo.status=1 AND mo.is_deleted=FALSE` / `pi.status IN(1,2) AND pi.is_deleted=FALSE`、`idx_pi_family_active`/`idx_mol_family`/`idx_mo_active` 利用、N+1なし）。
**Response 200（data[] に追加）:**
```json
{ "family_id": 100, "sku9": "NA1001A40", "product_name_1": "...",
  "production_status": { "material_order": "done", "production_instruction": "done" } }
```
**フィルタ:** `?filter[material_order_state]=done|undone`、`?filter[production_instruction_state]=done|undone`。
**性能:** 2,000品番 500ms（部分インデックス）。

---

## 3. OpenAPI 追加スキーマ（要点）

```yaml
ProductMaterial:
  properties: { material_role: {enum:[0,1,2,3,4]}, material: {$ref: MasterRef}, required_qty_per_unit: {type:number}, unit: {type:string}, recommended_supplier: {$ref: MasterRef, nullable:true}, loss_rate: {type:number} }
ProductionInstruction:
  properties: { instruction_no:{}, product_family_id:{}, factory:{$ref:MasterRef}, planned_quantity:{}, due_date:{format:date}, status:{enum:[Draft,Issued,Completed,Cancelled]}, export_state:{enum:[unexported,exported]}, lines:{} }
MaterialOrder:
  properties: { order_no:{}, material_supplier:{$ref:MasterRef}, production_instruction_id:{nullable:true}, status:{enum:[Draft,Ordered,Cancelled]}, total_amount_masked:{type:string}, lines:{} }   # 単価は price:read 時のみ展開
```
> Enum は API では文字列（既存 `JsonStringEnumConverter` グローバル登録）、DB は SMALLINT。

---

## 4. API 癒着回避の検証（既存 §4 4原則）

| 原則 | 検証 |
|---|---|
| 1API=1責務 | BOM展開(`/material-requirements`,`/prepare`)を作成APIから分離。発行/完了/中止/発注確定は専用サブリソースPOST |
| 集約・加工をクライアントに押し付けない | 所要量展開・仕入先別グルーピング・未/済算出・採番はサーバ側で完結 |
| 別リソースを混在させない | 生産指示と素材発注は別リソース。未/済は商品一覧の派生属性として opt-in(`include=`)。金額は price 権限で分離開示 |
| 使い方が分かる単位 | 既存命名(purchase-orders)と並列(production-instructions / material-orders) |

---

## 5. 全データフロー I/F 検証（主要シナリオ）

### P-A: 商品マスタ→生産指示→素材発注→未/済
```
[1] PUT  /products/families/100/materials        product:write  … BOM登録（差分upsert、3FK書戻しなし）
[2] POST /production-instructions                purchase_order:write … 生産指示Draft（advisory lock採番）→ 26-PI-00001
[3] POST /production-instructions/{id}/issue     purchase_order:write … Issued → 品番100「生産指示=済」
[4] GET  /production-instructions/{id}/excel      purchase_order:read … 生産指示書Excel（加工先名凍結、Excel.Export監査）
[5] POST /material-orders/prepare {pi_id:50}      purchase_order:read … BOM×数量で仕入先別ドラフト（金額なし）
[6] POST /material-orders (仕入先31)              purchase_order:write AND price:write … 独立Tx・採番 → 26-MO-00001
[7] POST /material-orders/{id}/order             purchase_order:write … Ordered → 品番100「素材発注=済」
[8] GET  /material-orders/{id}/excel              purchase_order:read AND price:read … 素材発注書Excel（MaterialPrice.View+Excel.Export 監査=ブロッキング）
[9] GET  /products?include=production_status     product:read … 品番100: material_order=done, production_instruction=done
```
**SoTチェック:** 全RDS。BOMは独立SoT（3FK疎結合）。未/済はSoT直読EXISTS（明細is_deleted非参照）。
**冪等性:** issue/order冪等。作成はIdempotency-Key＋advisory lock採番（並行安全）。複数素材発注は独立Tx逐次。
**機密:** 素材単価は price権限ゲート＋ブロッキング監査。一覧合計はデフォルトマスク。
**非ブロッキング:** 一般audit失敗は警告継続（原則4）。**機密閲覧(MaterialPrice.View)/Excel.Exportは例外でブロッキング**（監査M-4）。

### P-B: BOM未登録での素材発注
```
POST /material-orders/prepare {product_family_id:100} → 422 MORD-001（BOM未登録）→ BOM登録画面(B-01)へ誘導
```
（既存品番は移行時にBOM未生成のため、初回素材発注時に必ず本ガードを通る＝誤発注防止、監査C-3）

---

## 6. I/F 6視点チェック（API層）

| # | 視点 | 結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ ASP.NET Core Minimal API、ClosedXML、Npgsql、advisory lock |
| 2 | ユースケース | ✅ UC-PROD-1〜5 全カバー |
| 3 | ユーザビリティ | ✅ prepare展開プレビュー、未/済opt-in、エラー誘導、単価マスク |
| 4 | データ設計上の都合 | ✅ 5テーブルと1:1、Enum文字列⇔SMALLINT、明細is_deleted非参照で整合 |
| 5 | 型の継承関係 | ✅ MasterRef共通DTO、Enum変換グローバル設定流用 |
| 6 | データフロー整合性 | ✅ §5で起点→派生を一気通貫検証、SoT順序・採番並行安全・機密ゲート |

---

## 7. 変更履歴
| 日付 | 内容 |
|---|---|
| 2026-06-22 | 初版 |
| 2026-06-22 v2 | 認可を既存実在トークン(product/purchase_order/price)へ是正＋§2全件反映（監査C-1）/ 素材単価にprice権限AND・デフォルトマスク・MaterialPrice.Viewブロッキング監査（監査C-2/M-4）/ エラーコード接頭辞をBOM/PINST/MORD＋EXPORT再利用に独立化（コードレビュアーC-2）/ 採番をadvisory lock+リトライ・複数発注は独立Tx逐次（監査C-4/M-1）/ prepareは金額非返却/ BOM差分upsert・3FK書戻しなし（監査M-3）/ ロス率任意（M-1）/ 出力履歴audit集約（M-2）/ 未/済クエリの明細is_deleted非参照（コードレビュアーC-1）|
