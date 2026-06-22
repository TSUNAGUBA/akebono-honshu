# Phase 5 成果物: データ設計（生産管理拡張）

> **作成日:** 2026-06-22
> **状態:** ドラフト v1（独立レビュー前）
> **位置づけ:** 既存 `data-design.md`（26テーブル）への**増分**。生産管理（BOM・生産指示・素材発注・未/済）を追加する。
> **依存:** `domain-context/business-flow/production-management-flow.md`、オペレーター確定4判断（2026-06-22）、既存 `data-design.md` の命名規約・SoT・正規化原則
> **方針:** 既存設計の規約（第3正規形、サロゲートPK `id BIGSERIAL`、業務PKは UNIQUE、論理削除、共通監査4列＋`legacy_id`、SMALLINT enum、スナップショット凍結、JST naive `TIMESTAMP`）を**完全踏襲**。既存26テーブルは**変更を最小化**し、既存実装・データを破壊しない（CLAUDE.md 原則7）。

---

## 1. 既存設計の継承（前提）

本増分は既存 `data-design.md` の以下をそのまま継承する（再掲しない）:
- 命名規約（§1.2）: テーブル=スネークケース複数形、FK=`<単数>_id`、金額=`NUMERIC(12,2)`、日付=`DATE`、日時=`TIMESTAMP`(JST naive)
- 共通基底列: `id BIGSERIAL PK` / 共通監査4列（`created_at`,`created_by_user_id`,`updated_at`,`updated_by_user_id`）/ `legacy_id VARCHAR(64) NULL`
- 論理削除: トランザクション系は `is_deleted BOOLEAN NOT NULL DEFAULT FALSE`
- enum 表現: `SMALLINT` ＋ Application 層解釈（DP-9）
- スナップショット凍結: 帳票出力時にマスタ値をコピー凍結（マスタ変更耐性、§purchase_orders と同方針）
- SoT: 全業務データ = RDS PostgreSQL（AWS Tokyo）

---

## 2. 追加テーブル一覧（5件）

| # | テーブル | 役割 | 親 | 粒度 |
|---|---|---|---|---|
| P-1 | `product_materials` | BOM（素材構成・所要量） | product_families | 品番 × 部位 × 素材 |
| P-2 | `production_instructions` | 生産指示書ヘッダ | product_families | 1指示 = 1品番の生産1回 |
| P-3 | `production_instruction_lines` | 生産指示明細（色×サイズ別数量） | production_instructions | SKU |
| P-4 | `material_orders` | 素材発注書ヘッダ | （production_instruction 任意） | 1発注 = 1素材仕入先 |
| P-5 | `material_order_lines` | 素材発注明細（所要量展開） | material_orders | 素材 |

> **既存テーブルへの変更:** `product_families` に**列追加なし**（未/済は派生算出のため denormalized 列を持たない、§7.2）。`suppliers` / `materials` マスタは既存をそのまま参照。**既存26テーブルのスキーマ変更ゼロ**（CLAUDE.md 原則7、下位互換完全維持）。

---

## 3. ERD（増分）

```
[既存]                                    [新規]
product_families (品番企画) ─┬─1:N─► product_materials (BOM)
  - upper/insole/outsole_material_id    - material_role (0甲皮/1中底/2底/3付属/4副資材)
    (3部位の代表素材、表示用に存置)      - material_id  ──FK──► materials (既存)
  - products (SKU) ◄─┐                   - required_qty_per_unit / unit
                     │                   - recommended_supplier_id ──FK──► suppliers (既存,NULL可)
                     │
  └──────1:N─────────┼──► production_instructions (生産指示書)
                     │      - instruction_no (UNIQUE)
                     │      - product_family_id ──FK
                     │      - factory_supplier_id ──FK──► suppliers (加工先)
                     │      - planned_quantity / due_date / status
                     │      - instructed_at / first_exported_at / last_exported_at
                     │      - factory_*_snapshot (帳票凍結)
                     │         │ 1:N
                     │         ▼
                     └─── production_instruction_lines
                            - product_id ──FK──► products (SKU)
                            - sku_snapshot / product_name_snapshot / quantity

production_instructions ─0..1:N─► material_orders (素材発注書、素材仕入先別)
                                    - order_no (UNIQUE)
                                    - material_supplier_id ──FK──► suppliers
                                    - production_instruction_id ──FK (NULL可)
                                    - due_date / status / instructed_at
                                    - first_exported_at / supplier_*_snapshot
                                       │ 1:N
                                       ▼
                                  material_order_lines
                                    - material_id ──FK──► materials
                                    - product_family_id ──FK (NULL可, 由来品番)
                                    - material_name_snapshot
                                    - required_quantity / unit
                                    - unit_price / currency_code (NULL可)
```

---

## 4. テーブル定義

### 4.1 `product_materials` — BOM（素材構成・所要量）

商品企画（品番）1つを生産するのに必要な素材の構成。1足あたり所要量を保持。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | 親（品番企画） |
| `material_role` | `SMALLINT NOT NULL` | 0=甲皮, 1=中底, 2=底, 3=付属, 4=副資材 |
| `material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 素材マスタ（付属・副資材も materials に登録） |
| `required_qty_per_unit` | `NUMERIC(12,4) NOT NULL` | **1足あたり所要量**（例: 0.3000 ㎡、1.0000 組） |
| `unit` | `VARCHAR(8) NOT NULL` | 単位（足/組/枚/個/m/㎡/cm/本）。当面は列挙、将来マスタ化 |
| `recommended_supplier_id` | `BIGINT NULL REFERENCES suppliers(id)` | 推奨素材仕入先（素材発注時の初期値、NULL可） |
| `loss_rate` | `NUMERIC(5,4) NOT NULL DEFAULT 0` | ロス率（0〜1未満、例 0.0500=5%）。素材発注数 = 所要量×数量×(1+loss_rate) |
| `remark` | `VARCHAR(255) NULL` | 備考 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査4列 | | |

**UNIQUE 制約:** `uq_pm_family_role_material UNIQUE (product_family_id, material_role, material_id) WHERE is_deleted = FALSE`（部分インデックス。同一品番・同一部位・同一素材の重複防止。同一部位に複数素材＝多層構造を許容するため material_id も複合キーに含める）

**インデックス:** `idx_pm_family (product_family_id) WHERE is_deleted = FALSE`、`idx_pm_material (material_id)`、`idx_pm_supplier (recommended_supplier_id) WHERE recommended_supplier_id IS NOT NULL`

**CHECK 制約:**
- `chk_pm_role CHECK (material_role BETWEEN 0 AND 4)`
- `chk_pm_qty CHECK (required_qty_per_unit > 0)`
- `chk_pm_loss CHECK (loss_rate >= 0 AND loss_rate < 1)`

> **設計判断:**
> - 既存 `product_families.upper/insole/outsole_material_id`（3部位の代表素材FK）は**変更せず存置**。これは品番台帳の表示属性（既存実装・MIG-3移行データが依存）。`product_materials` は**所要量を伴うBOMのSoT**であり、3部位の代表素材は `product_materials` の `material_role IN (0,1,2)` 行として登録される想定（重複は許容＝代表素材は「表示用キャッシュ」、BOMが正）。同期は商品マスタ編集時に Application 層で行う（SoT=product_materials、cache=3FK列。CLAUDE.md 原則6）。**移行期は両立、将来 3FK列を派生ビュー化する余地を残す**（§7.1）。
> - 「同一部位に複数素材」（例: 甲皮が表地＋裏地の2素材）を許容するため `(role, material_id)` 複合での一意制約。
> - 付属（面ファスナー・中敷等）・副資材（値札・証紙・箱）も `materials` マスタに登録し `material_role` 3/4 で区別。新マスタは作らない（既存17マスタ＋user の18マスタ構成を維持、Phase 2/4 整合）。

### 4.2 `production_instructions` — 生産指示書（加工指図書）ヘッダ

品番1つを工場（加工先）で生産1回ぶん指示する単位。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `instruction_no` | `VARCHAR(16) NOT NULL UNIQUE` | 生産指示番号（例: `26-PI-00001`、作成時採番） |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | 対象品番 |
| `factory_supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 加工先（工場、supplier 兼用） |
| `planned_quantity` | `INTEGER NOT NULL` | 生産総数量（明細合計と一致、Application 層で整合） |
| `due_date` | `DATE NOT NULL` | 希望納期（工場出荷希望日） |
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Draft(未発行), 1=Issued(発行済=指示済), 2=Completed(生産完了), 9=Cancelled |
| `instructed_at` | `TIMESTAMP NULL` | 発行(指示)日時。NOT NULL = 指示済（未/済バッジの「済」判定根拠） |
| `completed_at` | `TIMESTAMP NULL` | 生産完了日時（次段階「工程実績」への布石、MVPは手動完了） |
| `cancelled_at` | `TIMESTAMP NULL` | |
| `cancelled_by_user_id` | `BIGINT NULL REFERENCES users(id)` | |
| `cancel_reason` | `VARCHAR(255) NULL` | |
| `factory_official_name_snapshot` | `VARCHAR(255) NULL` | 加工先正式名の凍結（帳票宛名、初回 Excel 出力時にコピー） |
| `factory_code_snapshot` | `VARCHAR(3) NULL` | 加工先コードの凍結（帳票宛名） |
| `product_sku9_snapshot` | `VARCHAR(9) NULL` | 品番上位9桁スナップショット（帳票表示用、初回出力時凍結） |
| `product_name_snapshot` | `VARCHAR(255) NULL` | 品番商品名スナップショット |
| `communication_text` | `TEXT NULL` | 連絡文章（指示書本文、最大行数は Application 層検証） |
| `first_exported_at` | `TIMESTAMP NULL` | 初回 Excel 出力日時 |
| `last_exported_at` | `TIMESTAMP NULL` | 最終 Excel 出力日時 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査4列 ＋ `legacy_id` | | |

**インデックス:**
- `idx_pi_instruction_no (instruction_no)`
- `idx_pi_family (product_family_id)` ← 未/済バッジの EXISTS 算出に必須
- `idx_pi_factory (factory_supplier_id)`
- `idx_pi_status (status, due_date)`
- `idx_pi_family_active (product_family_id) WHERE status IN (1,2) AND is_deleted = FALSE` ← 「生産指示=済」判定の部分インデックス
- `idx_pi_dates (created_at DESC)`

**CHECK 制約:**
- `chk_pi_status CHECK (status IN (0,1,2,9))`
- `chk_pi_qty CHECK (planned_quantity > 0)`
- `chk_pi_issued_consistency CHECK ((status >= 1 AND status <> 9) = (instructed_at IS NOT NULL) OR status = 9)` … 発行済/完了なら instructed_at 必須（Cancelled は別途）
- `chk_pi_last_after_first CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL)`
- `chk_pi_cancelled CHECK ((status = 9) = (cancelled_at IS NOT NULL))`

> **設計判断:**
> - `purchase_orders`（完成品発注）と同じ「ヘッダ＋明細＋スナップショット凍結＋first/last_exported_at」パターンを踏襲し、設計の一貫性とレビュー負荷を最小化。
> - `status` は将来の工程実績（多工程進捗）拡張に備え 4 値（Draft/Issued/Completed/Cancelled）。MVP の未/済バッジは「Issued 以上 = 済」で判定。
> - `completed_at` は次段階の足がかり（MVP は UI で手動完了 or 未使用可）。

### 4.3 `production_instruction_lines` — 生産指示明細（色×サイズ別）

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `production_instruction_id` | `BIGINT NOT NULL REFERENCES production_instructions(id) ON DELETE CASCADE` | 親 |
| `line_no` | `SMALLINT NOT NULL` | 明細番号 |
| `product_id` | `BIGINT NOT NULL REFERENCES products(id)` | SKU（色×サイズ） |
| `sku_snapshot` | `VARCHAR(11) NOT NULL` | 11桁品番スナップショット |
| `product_name_snapshot` | `VARCHAR(255) NOT NULL` | 商品名スナップショット |
| `quantity` | `INTEGER NOT NULL` | 当該SKUの生産数量 |
| 共通監査4列 | | |

**UNIQUE 制約:** `uq_pil_instruction_line UNIQUE (production_instruction_id, line_no)`、`uq_pil_instruction_product UNIQUE (production_instruction_id, product_id)`（同一指示内で同一SKU重複禁止、既存 purchase_order の ORDER-006 と同方針）

**インデックス:** `idx_pil_instruction (production_instruction_id)`、`idx_pil_product (product_id)`

**CHECK 制約:** `chk_pil_qty CHECK (quantity > 0)`

### 4.4 `material_orders` — 素材発注書（生地材料発注）ヘッダ

素材仕入先1社あての素材発注。生産指示を起点に作成（任意で紐付け）。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `order_no` | `VARCHAR(16) NOT NULL UNIQUE` | 素材発注番号（例: `26-MO-00001`、作成時採番） |
| `material_supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 素材仕入先（supplier 兼用） |
| `production_instruction_id` | `BIGINT NULL REFERENCES production_instructions(id)` | 起点の生産指示（NULL可＝指示なしの単独素材発注も許容） |
| `due_date` | `DATE NOT NULL` | 素材納入希望日 |
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Draft(未発注), 1=Ordered(発注済=済), 9=Cancelled |
| `instructed_at` | `TIMESTAMP NULL` | 発注確定日時。NOT NULL = 発注済（未/済バッジの「済」判定根拠） |
| `cancelled_at` | `TIMESTAMP NULL` | |
| `cancelled_by_user_id` | `BIGINT NULL REFERENCES users(id)` | |
| `cancel_reason` | `VARCHAR(255) NULL` | |
| `supplier_official_name_snapshot` | `VARCHAR(255) NULL` | 仕入先正式名の凍結（帳票宛名、既存 purchase_orders と同方針） |
| `supplier_code_snapshot` | `VARCHAR(3) NULL` | 仕入先コードの凍結 |
| `communication_text` | `TEXT NULL` | 連絡文章 |
| `first_exported_at` | `TIMESTAMP NULL` | 初回 Excel 出力日時 |
| `last_exported_at` | `TIMESTAMP NULL` | 最終 Excel 出力日時 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査4列 ＋ `legacy_id` | | |

**インデックス:**
- `idx_mo_order_no (order_no)`
- `idx_mo_supplier (material_supplier_id)`
- `idx_mo_instruction (production_instruction_id) WHERE production_instruction_id IS NOT NULL`
- `idx_mo_status (status, due_date)`
- `idx_mo_dates (created_at DESC)`

**CHECK 制約:**
- `chk_mo_status CHECK (status IN (0,1,9))`
- `chk_mo_ordered_consistency CHECK ((status = 1) = (instructed_at IS NOT NULL))`
- `chk_mo_last_after_first CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL)`
- `chk_mo_cancelled CHECK ((status = 9) = (cancelled_at IS NOT NULL))`

### 4.5 `material_order_lines` — 素材発注明細（所要量展開）

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `material_order_id` | `BIGINT NOT NULL REFERENCES material_orders(id) ON DELETE CASCADE` | 親 |
| `line_no` | `SMALLINT NOT NULL` | 明細番号 |
| `material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 素材 |
| `material_name_snapshot` | `VARCHAR(255) NOT NULL` | 素材名スナップショット |
| `product_family_id` | `BIGINT NULL REFERENCES product_families(id)` | 由来品番（未/済の品番ロールアップに使用、NULL可） |
| `source_pi_line_id` | `BIGINT NULL REFERENCES production_instruction_lines(id)` | 由来の生産指示明細（トレース用、NULL可） |
| `required_quantity` | `NUMERIC(14,4) NOT NULL` | 発注数量（推奨= Σ所要量×生産数量×(1+loss_rate)、手調整可） |
| `unit` | `VARCHAR(8) NOT NULL` | 単位 |
| `unit_price` | `NUMERIC(12,2) NULL` | 素材単価（NULL可、機密度 中-高として扱う＝既存仕入単価と同等保護） |
| `currency_code` | `CHAR(3) NOT NULL DEFAULT 'JPY'` | ISO 4217 |
| `subtotal` | `NUMERIC(16,2) GENERATED ALWAYS AS (required_quantity * COALESCE(unit_price, 0)) STORED` | 計算列（DB保証、既存 purchase_order_lines と同方針） |
| 共通監査4列 | | |

**UNIQUE 制約:** `uq_mol_order_line UNIQUE (material_order_id, line_no)`

**インデックス:** `idx_mol_order (material_order_id)`、`idx_mol_material (material_id)`、`idx_mol_family (product_family_id) WHERE product_family_id IS NOT NULL` ← 未/済バッジ算出に使用

**CHECK 制約:** `chk_mol_qty CHECK (required_quantity > 0)`、`chk_mol_price CHECK (unit_price IS NULL OR unit_price >= 0)`

> **設計判断:**
> - `material_order_lines` は素材単位（同一素材を複数品番分まとめて1行に集約 or 品番別に複数行のいずれも許容）。`product_family_id` を持つことで「品番ごと素材発注 未/済」のロールアップが可能。
> - 素材単価は仕入単価と同じ機密度（中-高）として扱い、監査ログにはマスク（§6）。

---

## 5. 採番ルール（BR 追加）

| 番号 | 形式 | 採番タイミング | 既存との整合 |
|---|---|---|---|
| 生産指示番号 `instruction_no` | `YY-PI-NNNNN`（例 26-PI-00001） | 生産指示書 作成時 | 既存 `mgmt_no`（YY-NNNNN）と接頭辞で区別 |
| 素材発注番号 `order_no` | `YY-MO-NNNNN`（例 26-MO-00001） | 素材発注書 作成時 | 既存発注の `order_no`（Sxxxx）と衝突しない |

> 採番は既存 `mgmt_no` と同じく「年度2桁＋連番」。同一トランザクション内 `MAX(連番)+1`、UNIQUE 制約で衝突防止（既存 Iteration 2 知見#2 と同方式）。Idempotency-Key で二重作成防止（既存 §api 1.6）。

---

## 6. データ機密度（既存 NFR §6.2 へ追従）

| データ | 機密度 | 保護 |
|---|---|---|
| 素材単価（`material_order_lines.unit_price`） | 中-高 | 既存仕入単価と同等。KMS保存時暗号化＋アクセス制御＋監査ログは金額マスク（`MaterialPrice.View`/`Excel.Export`） |
| 生産指示・素材発注（数量・納期・加工先・素材仕入先） | 中 | アクセス制御＋監査ログ（C/U/D） |
| BOM（素材構成・所要量・ロス率） | 中 | 競争力源泉（歩留り情報）。アクセス制御＋監査ログ |

**監査ログ追加 action（既存 `audit_logs.action` 体系に追加）:**
`ProductMaterial.Create/Update/Delete`、`ProductionInstruction.Create/Issue/Complete/Cancel`、`MaterialOrder.Create/Order/Cancel`、`Excel.Export`（対象 entity で区別）、`MaterialPrice.View`。

---

## 7. 設計上の重要判断と確認事項

### 7.1 BOM と既存3部位素材FK の関係（下位互換）
- 既存 `product_families.upper/insole/outsole_material_id` は**変更せず存置**（MIG-3移行データ・既存実装・既存画面が依存）。
- `product_materials` を**所要量付きBOMのSoT**として新設。3部位の代表素材は `product_materials` の `material_role IN (0,1,2)` 行として登録され、Application 層で `product_families` の3FK列へ反映（SoT先行→cache後追い、CLAUDE.md 原則6）。
- **既存データへの影響:** 既存 `product_families` 行には `product_materials` 行が無い状態となる。**データ更新パッチ**（§9）で、既存3FK列から `product_materials` の3部位行を `required_qty_per_unit=NULL→暫定1` で生成する移行スクリプトを用意（CLAUDE.md 原則7）。所要量の実値は実ユーザが後追い入力。

### 7.2 未/済バッジの算出方式（denormalized 列を持たない理由）
- 「品番ごと 素材発注 未/済 / 生産指示 未/済」は **派生算出**（`product_families` に状態列を追加しない）。
- 算出ロジック（一覧の各品番行に対し EXISTS サブクエリ、データ規模 約2,000品番でインデックス有効）:
  - 生産指示 **済** = `EXISTS(production_instructions WHERE product_family_id = pf.id AND status IN (1,2) AND is_deleted=FALSE)`（`idx_pi_family_active` 利用）
  - 素材発注 **済** = `EXISTS(material_order_lines mol JOIN material_orders mo ON mol.material_order_id=mo.id WHERE mol.product_family_id = pf.id AND mo.status = 1 AND mo.is_deleted=FALSE AND mol.is_deleted=FALSE)`（`idx_mol_family` 利用）
- **denormalized 列を持たない理由:** 同期バグ回避（CLAUDE.md 原則2/6）。SoT（指示・発注レコード）を直接 EXISTS で読む方が単一の真実源で安全。2,000品番規模では性能十分（NFR一覧 500ms）。**将来 品番が大規模化し性能課題が出た場合のみ** denormalized cache 列（`material_order_status`/`production_instruction_status`）をトランザクション同期で追加する余地を残す（D-prod-2）。

### 7.3 オペレーターレビュー確認事項

| # | 論点 | 推奨案 |
|---|------|--------|
| D-prod-1 | 同一部位に複数素材（表地+裏地等）の許容 | **許容**（`(role, material_id)` 複合一意）。OEM実態に即す |
| D-prod-2 | 未/済の denormalized cache 採否 | **持たない**（派生算出）。性能課題発生時に再評価 |
| D-prod-3 | 素材発注の集約単位（品番別 vs 素材仕入先別にまとめる） | **素材仕入先別にヘッダ、明細に由来品番**。実務の「1仕入先1発注書」に整合、品番ロールアップも可能 |
| D-prod-4 | ロス率の保持場所 | **BOM行（`product_materials.loss_rate`）**。素材ごとに歩留りが異なるため |
| D-prod-5 | 単位のマスタ化 | **MVPは列挙**（足/組/枚/個/m/㎡/cm/本）。将来 単位マスタ化 |
| D-prod-6 | 生産指示と既存完成品発注（purchase_orders）の連携 | **MVPは非連携**（独立）。§7 ヒアリング後に「生産指示→完成品発注」自動生成を検討 |

---

## 8. 正規化チェック（DP-1 適合）

| エンティティ | 1NF | 2NF | 3NF | 備考 |
|---|---|---|---|---|
| product_materials | ✅ | ✅ | ✅ | FKのみ、推移依存なし |
| production_instructions | ✅ | ✅ | ⚠️ | **意図的非正規化:** factory/product のスナップショット（帳票凍結、既存 purchase_orders と同根拠） |
| production_instruction_lines | ✅ | ✅ | ⚠️ | sku/name スナップショット（帳票要件） |
| material_orders | ✅ | ✅ | ⚠️ | supplier スナップショット（帳票凍結） |
| material_order_lines | ✅ | ✅ | ⚠️ | material_name スナップショット、`subtotal` 計算列（DB保証） |

> 非正規化根拠は既存設計と同一（帳票は発行時点の値を凍結。マスタ変更で過去帳票表示が変わると業務不整合）。read/write/業務整合のバランスで採用。

---

## 9. 移行・データ更新パッチ（CLAUDE.md 原則7）

| 対象 | 内容 |
|---|---|
| 既存 product_families → product_materials 初期生成 | 既存3FK列（upper/insole/outsole_material_id）から `material_role` 0/1/2 の BOM 行を生成。`required_qty_per_unit` は暫定 NULL 不可のため `1.0000`、`unit='組'` で投入し、実ユーザが後追い修正。冪等（`ON CONFLICT DO NOTHING`） |
| 付属・副資材の materials 登録 | 値札/証紙/箱/中敷/面ファスナー等を `materials` に追加（`material_classifications` に「付属」「副資材」分類追加）。オペレーター提供リストで投入 |
| 既存26テーブル | **変更なし**（スキーマ破壊ゼロ） |

> リリース手順: 新規5テーブルの DDL（`db/init/05-production.sql` 想定）を冪等適用 → product_materials 初期生成パッチ → materials 付属/副資材投入。既存データ・既存機能への影響なし。

---

## 10. I/F 設計 6 視点チェック（データ層）

| # | 視点 | 結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ PostgreSQL 16(本番14.17互換)＋EF Core 8。`GENERATED ALWAYS AS STORED`/部分インデックスは 12+ 対応。`NUMERIC`→decimal, `DATE`→DateOnly, `TIMESTAMP`→DateTime(JST naive) |
| 2 | ユースケース | ✅ UC-PROD-1〜5（BOM登録/生産指示/素材発注/未済可視化）を全カバー（§usecases-production.md） |
| 3 | ユーザビリティ | ✅ 未/済の部分インデックスで一覧高速化。色×サイズ別数量は既存マトリクスUI流用 |
| 4 | データ設計上の都合 | ✅ 正規化＋非正規化根拠明示（§8）、既存26テーブル無変更（下位互換） |
| 5 | 型の継承関係 | ✅ 既存 Entity→DTO→API 写像（Mapster）に追従。Enum は SMALLINT |
| 6 | データフロー整合性 | ✅ BOM(SoT)→3FK列(cache)、生産指示→素材発注の起点連鎖、未/済は SoT 直読で派生（§api-design-production.md で全フロー検証） |

---

## 11. 変更履歴
| 日付 | 内容 |
|---|---|
| 2026-06-22 | 初版（生産管理拡張 5テーブル: product_materials / production_instructions / production_instruction_lines / material_orders / material_order_lines。既存26テーブル無変更） |
