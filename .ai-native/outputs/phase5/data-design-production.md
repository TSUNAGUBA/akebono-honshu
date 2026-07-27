# Phase 5 成果物: データ設計（生産管理拡張）

> **作成日:** 2026-06-22 / **改訂:** 2026-06-22 v2（独立レビュー1周目 指摘反映: コードレビュアー C-1/C-2/M-1/M-2/Mi-1/Mi-2/Mi-3/Ni-1、システム監査官 C-1/C-2/C-3/C-4/M-1/M-3/M-4/M-5）
> **状態:** ドラフト v2（再レビュー前）
> **位置づけ:** 既存 `data-design.md`（26テーブル）への**増分**。生産管理（BOM・生産指示・素材発注・未/済）を追加。
> **依存:** `domain-context/business-flow/production-management-flow.md`、オペレーター確定4判断（2026-06-22）、既存 `data-design.md` の命名規約・SoT・正規化原則
> **方針:** 既存規約（第3正規形、サロゲートPK `id BIGSERIAL`、業務PKは UNIQUE、論理削除、共通監査4列＋`legacy_id`、SMALLINT enum、スナップショット凍結、JST naive `TIMESTAMP`）を完全踏襲。既存26テーブルは**スキーマ変更ゼロ**（CLAUDE.md 原則7）。

---

## 1. 既存設計の継承（前提）

既存 `data-design.md` の以下を継承（再掲しない）: 命名規約（§1.2）、共通基底列（`id`/監査4列/`legacy_id`）、論理削除（トランザクション系 `is_deleted`、ただし**明細テーブルは親CASCADEで削除し `is_deleted` を持たない**＝既存 `purchase_order_lines` と同方針）、enum=SMALLINT、スナップショット凍結、SoT=RDS。

> **認可トークン（既存実在のもののみ使用、監査C-1反映）:** 本増分は新規トークンを発明しない。Custom Claims `permissions[]` の既存トークン `product:read/write`・`price:read/write`・`purchase_order:read/write` のみを使用（実装は段階Bでは RDS 直読 `product_ledger_permission`/`purchase_order_create_permission`、段階C で Custom Claims 二重化）。割当は §12。

---

## 2. 追加テーブル一覧（5件）

| # | テーブル | 役割 | 親 | 粒度 | 論理削除 |
|---|---|---|---|---|---|
| P-1 | `product_materials` | BOM（素材構成・所要量） | product_families | 品番 × 部位 × 素材 | `is_deleted` あり |
| P-2 | `production_instructions` | 生産指示書ヘッダ | product_families | 1指示=1品番の生産1回 | `is_deleted` あり |
| P-3 | `production_instruction_lines` | 生産指示明細（色×サイズ別数量） | production_instructions | SKU | なし（親CASCADE） |
| P-4 | `material_orders` | 素材発注書ヘッダ | （production_instruction 任意） | 1発注=1素材仕入先 | `is_deleted` あり |
| P-5 | `material_order_lines` | 素材発注明細（所要量展開） | material_orders | 素材 | なし（親CASCADE） |

> **明細テーブルの論理削除（コードレビュアー C-1 反映）:** `production_instruction_lines` / `material_order_lines` は既存 `purchase_order_lines` と同様 **`is_deleted` を持たず、親ヘッダの CASCADE で削除**。明細の差し替えはヘッダ編集トランザクション内で行単位 DELETE/INSERT。→ 未/済の派生クエリ（§7.2）は明細の `is_deleted` を参照しない（親 `is_deleted=FALSE` のみで判定）。
> **既存テーブルへの変更:** `product_families` に**列追加なし**（未/済は派生算出、§7.2）。**既存26テーブルのスキーマ変更ゼロ**（下位互換完全維持）。

---

## 3. ERD（増分）

```
[既存・無変更]                                [新規]
product_families (品番企画)                product_materials (BOM)  ※is_deletedあり
  - upper/insole/outsole_material_id ──┐     - material_role (0甲皮/1中底/2底/3付属/4副資材)
    （代表素材＝表示用、BOMとは疎結合） │     - material_id ──FK──► materials (既存)
    (BOM編集の初期シード元、書戻しなし) ─┘     - required_qty_per_unit / unit
  - products (SKU)                            - recommended_supplier_id ──FK──► suppliers (NULL可)
       ▲                                      - loss_rate (任意,DEFAULT 0)
       │                          ┌──1:N── product_families
       │                          │
       └─────────── production_instructions (生産指示書)  ※is_deletedあり
                      - instruction_no (UNIQUE) / product_family_id
                      - factory_supplier_id (加工先)
                      - planned_quantity / due_date / status / instructed_at
                      - first_exported_at / last_exported_at / factory_*_snapshot
                         │ 1:N (CASCADE)
                         ▼
                    production_instruction_lines  ※is_deletedなし
                      - product_id (SKU) / sku_snapshot / product_name_snapshot / quantity

production_instructions ─0..1:N─► material_orders (素材発注書、素材仕入先別)  ※is_deletedあり
                                    - order_no (UNIQUE) / material_supplier_id
                                    - production_instruction_id (NULL可)
                                    - due_date / status / instructed_at
                                    - first_exported_at / last_exported_at / supplier_*_snapshot
                                       │ 1:N (CASCADE)
                                       ▼
                                  material_order_lines  ※is_deletedなし
                                    - material_id / material_name_snapshot
                                    - product_family_id (NULL可,由来品番) / source_pi_line_id
                                    - required_quantity / unit / unit_price(機密) / currency_code / subtotal

品番ごと「未/済」= production_instructions / material_orders の存在・状態から派生算出（denormalized列なし、§7.2）
```

---

## 4. テーブル定義

### 4.1 `product_materials` — BOM（素材構成・所要量）

商品企画（品番）1つを生産するのに必要な素材の構成。1足あたり所要量を保持。**`product_families` の3部位代表素材FKとは疎結合**（§7.1）。

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | 親（品番企画） |
| `material_role` | `SMALLINT NOT NULL` | 0=甲皮, 1=中底, 2=底, 3=付属, 4=副資材 |
| `material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 素材マスタ（付属・副資材も materials に登録） |
| `required_qty_per_unit` | `NUMERIC(12,4) NOT NULL` | **1足あたり所要量**（例: 0.3000 ㎡）。登録時必須＝所要量不明のBOM行は作らない（誤発注防止、§9） |
| `unit` | `VARCHAR(8) NOT NULL` | 単位（足/組/枚/個/m/㎡/cm/本）。当面列挙、将来マスタ化 |
| `recommended_supplier_id` | `BIGINT NULL REFERENCES suppliers(id)` | 推奨素材仕入先（素材発注時の初期値、NULL可） |
| `loss_rate` | `NUMERIC(5,4) NOT NULL DEFAULT 0` | **任意のロス率**（0=ロス考慮なし）。MVP推奨数量は `所要量×数量` を基本とし、loss_rate 設定時のみ `×(1+loss_rate)` を適用（§4.5・M-1反映） |
| `remark` | `VARCHAR(255) NULL` | 備考 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除（行単位の履歴保持・トレースのため明細と異なり保持） |
| 共通監査4列 | | |

**UNIQUE 制約:** `uq_pm_family_role_material UNIQUE (product_family_id, material_role, material_id) WHERE is_deleted = FALSE`（部分インデックス。同一品番・同一部位・同一素材の重複防止。同一部位に複数素材＝多層構造は material_id 差で許容）

**インデックス:** `idx_pm_family (product_family_id) WHERE is_deleted = FALSE`、`idx_pm_material (material_id)`、`idx_pm_supplier (recommended_supplier_id) WHERE recommended_supplier_id IS NOT NULL`

**CHECK 制約:**
- `chk_pm_role CHECK (material_role BETWEEN 0 AND 4)`
- `chk_pm_qty CHECK (required_qty_per_unit > 0)`
- `chk_pm_loss CHECK (loss_rate >= 0 AND loss_rate < 1)`

### 4.2 `production_instructions` — 生産指示書（加工指図書）ヘッダ

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `instruction_no` | `VARCHAR(16) NOT NULL UNIQUE` | 生産指示番号（例 `26-PI-00001`、作成時採番、§5） |
| `product_family_id` | `BIGINT NOT NULL REFERENCES product_families(id)` | 対象品番 |
| `factory_supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 加工先（工場、supplier 兼用） |
| `planned_quantity` | `INTEGER NOT NULL` | 生産総数量（明細合計と一致、Application 層で整合） |
| `due_date` | `DATE NOT NULL` | 希望納期 |
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Draft, 1=Issued(指示済), 2=Completed, 9=Cancelled |
| `instructed_at` | `TIMESTAMP NULL` | 発行(指示)日時。NOT NULL=指示済（未/済「済」根拠） |
| `completed_at` | `TIMESTAMP NULL` | 生産完了日時（次段階「工程実績」への布石） |
| `cancelled_at` / `cancelled_by_user_id` / `cancel_reason` | `TIMESTAMP NULL` / `BIGINT NULL REFERENCES users(id)` / `VARCHAR(255) NULL` | 中止 |
| `factory_official_name_snapshot` | `VARCHAR(255) NULL` | 加工先正式名の凍結（帳票宛名、初回 Excel 出力時コピー） |
| `factory_code_snapshot` | `VARCHAR(3) NULL` | 加工先コードの凍結 |
| `product_sku9_snapshot` | `VARCHAR(9) NULL` | 品番上位9桁スナップショット（帳票、初回出力時） |
| `product_name_snapshot` | `VARCHAR(255) NULL` | 品番商品名スナップショット |
| `communication_text` | `TEXT NULL` | 連絡文章 |
| `first_exported_at` / `last_exported_at` | `TIMESTAMP NULL` | 初回/最終 Excel 出力日時 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査4列 ＋ `legacy_id` | | |

**インデックス:** `idx_pi_instruction_no (instruction_no)`、`idx_pi_family (product_family_id)`、`idx_pi_factory (factory_supplier_id)`、`idx_pi_status (status, due_date)`、`idx_pi_family_active (product_family_id) WHERE status IN (1,2) AND is_deleted = FALSE`（未/済=済 判定）、`idx_pi_dates (created_at DESC)`

**CHECK 制約:**
- `chk_pi_status CHECK (status IN (0,1,2,9))`
- `chk_pi_qty CHECK (planned_quantity > 0)`
- `chk_pi_issued_consistency CHECK ( ((status >= 1 AND status <> 9) = (instructed_at IS NOT NULL)) OR (status = 9) )` … 明示括弧付（Mi-3反映）。Draft→instructed_at NULL、Issued/Completed→NOT NULL、Cancelled→不問
- `chk_pi_last_after_first CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL)`
- `chk_pi_cancelled CHECK ((status = 9) = (cancelled_at IS NOT NULL))`

### 4.3 `production_instruction_lines` — 生産指示明細（色×サイズ別）

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `production_instruction_id` | `BIGINT NOT NULL REFERENCES production_instructions(id) ON DELETE CASCADE` | 親 |
| `line_no` | `SMALLINT NOT NULL` | 明細番号 |
| `product_id` | `BIGINT NOT NULL REFERENCES products(id)` | SKU |
| `sku_snapshot` | `VARCHAR(11) NOT NULL` | 11桁品番スナップショット |
| `product_name_snapshot` | `VARCHAR(255) NOT NULL` | 商品名スナップショット |
| `quantity` | `INTEGER NOT NULL` | 当該SKU生産数量 |
| 共通監査4列 | | （`is_deleted` なし＝親CASCADE） |

**UNIQUE:** `uq_pil_instruction_line (production_instruction_id, line_no)`、`uq_pil_instruction_product (production_instruction_id, product_id)`（同一指示内SKU重複禁止）
**インデックス:** `idx_pil_instruction (production_instruction_id)`、`idx_pil_product (product_id)`
**CHECK:** `chk_pil_qty CHECK (quantity > 0)`

### 4.4 `material_orders` — 素材発注書（生地材料発注）ヘッダ

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `order_no` | `VARCHAR(16) NOT NULL UNIQUE` | 素材発注番号（例 `26-MO-00001`、作成時採番、§5） |
| `material_supplier_id` | `BIGINT NOT NULL REFERENCES suppliers(id)` | 素材仕入先 |
| `production_instruction_id` | `BIGINT NULL REFERENCES production_instructions(id)` | 起点の生産指示（NULL可） |
| `due_date` | `DATE NOT NULL` | 素材納入希望日 |
| `status` | `SMALLINT NOT NULL DEFAULT 0` | 0=Draft, 1=Ordered(発注済), 9=Cancelled |
| `instructed_at` | `TIMESTAMP NULL` | 発注確定日時。NOT NULL=発注済（未/済「済」根拠） |
| `cancelled_at` / `cancelled_by_user_id` / `cancel_reason` | 同 PI | 中止 |
| `supplier_official_name_snapshot` / `supplier_code_snapshot` | `VARCHAR(255)/(3) NULL` | 仕入先凍結（帳票宛名、既存 purchase_orders と同方針） |
| `communication_text` | `TEXT NULL` | 連絡文章 |
| `first_exported_at` / `last_exported_at` | `TIMESTAMP NULL` | 初回/最終 Excel 出力 |
| `is_deleted` | `BOOLEAN NOT NULL DEFAULT FALSE` | 論理削除 |
| 共通監査4列 ＋ `legacy_id` | | |

**インデックス:** `idx_mo_order_no (order_no)`、`idx_mo_supplier (material_supplier_id)`、`idx_mo_instruction (production_instruction_id) WHERE production_instruction_id IS NOT NULL`、`idx_mo_status (status, due_date)`、`idx_mo_dates (created_at DESC)`（未/済=済 判定は §7.2 の想定実行計画参照。`idx_mol_family` で mol を引き親 mo を PK lookup＋status フィルタ。`(id) WHERE status=1` の部分インデックスは PK 等価結合で選ばれず非効率なため**設けない**＝監査CR Major-2反映）
**CHECK:** `chk_mo_status CHECK (status IN (0,1,9))`、`chk_mo_ordered_consistency CHECK ((status = 1) = (instructed_at IS NOT NULL))`、`chk_mo_last_after_first CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL)`、`chk_mo_cancelled CHECK ((status = 9) = (cancelled_at IS NOT NULL))`

### 4.5 `material_order_lines` — 素材発注明細（所要量展開）

| カラム | 型 | 補足 |
|---|---|---|
| `id` | `BIGSERIAL PRIMARY KEY` | |
| `material_order_id` | `BIGINT NOT NULL REFERENCES material_orders(id) ON DELETE CASCADE` | 親 |
| `line_no` | `SMALLINT NOT NULL` | 明細番号 |
| `material_id` | `BIGINT NOT NULL REFERENCES materials(id)` | 素材 |
| `material_name_snapshot` | `VARCHAR(255) NOT NULL` | 素材名スナップショット |
| `product_family_id` | `BIGINT NULL REFERENCES product_families(id)` | 由来品番（未/済ロールアップに使用）。**prepare 経由で作成された明細は必ず充足**。完全手動明細で NULL の場合は未/済に寄与しない（仕様、BR-P6・監査CR Minor-2反映） |
| `source_pi_line_id` | `BIGINT NULL REFERENCES production_instruction_lines(id)` | 由来の生産指示明細（トレース、NULL可） |
| `required_quantity` | `NUMERIC(14,4) NOT NULL` | 発注数量（推奨= `Σ所要量×生産数量`、loss_rate設定時 `×(1+loss_rate)`、手調整可） |
| `unit` | `VARCHAR(8) NOT NULL` | 単位 |
| `unit_price` | `NUMERIC(12,2) NULL` | 素材単価（**機密度 中-高**＝既存仕入単価と同等保護、§6・§12）。NULL=単価未確定 |
| `currency_code` | `CHAR(3) NOT NULL DEFAULT 'JPY'` | ISO 4217 |
| `subtotal` | `NUMERIC(16,2) GENERATED ALWAYS AS (required_quantity * COALESCE(unit_price, 0)) STORED` | 計算列（DB保証） |
| 共通監査4列 | | （`is_deleted` なし＝親CASCADE） |

**UNIQUE:** `uq_mol_order_line (material_order_id, line_no)`
**インデックス:** `idx_mol_order (material_order_id)`、`idx_mol_material (material_id)`、`idx_mol_family (product_family_id) WHERE product_family_id IS NOT NULL`（未/済算出）
**CHECK:** `chk_mol_qty CHECK (required_quantity > 0)`、`chk_mol_price CHECK (unit_price IS NULL OR unit_price >= 0)`

> **設計判断（レビュー反映）:**
> - **subtotal 精度（Mi-1）:** `NUMERIC(16,2)`。既存 `purchase_order_lines.subtotal` は `(14,2)` だが、素材は `required_quantity NUMERIC(14,4)`（完成品の整数 `quantity` より桁が大きい）×単価 のため最大桁が拡大し得る。**意図的に既存より精度拡張**（「同方針だが精度は素材数量に合わせ拡張」）。
> - **NULL単価時の subtotal（Mi-2）:** `unit_price` NULL（単価未確定）明細の `subtotal` は `COALESCE(...,0)` により **0** になる（NULLではない）。一覧の合計金額（MO-02）は**単価確定分のみの合計**を表す。単価未確定明細を含む発注書には UI で「**単価未設定あり**」注記を表示し、過小集計の誤認を防ぐ（screen §2.6）。
> - 素材単価は機密度 中-高。監査ログには金額本体を残さずマスク（§6）。開示は §12 の price 権限ゲート＋ `MaterialPrice.View` 監査（**ブロッキング**、§6・M-4反映）。

---

## 5. 採番ルール（同時実行安全・監査C-4/M-1反映）

| 番号 | 形式 | タイミング |
|---|---|---|
| 生産指示番号 `instruction_no` | `YY-PI-NNNNN` | 生産指示書 作成時 |
| 素材発注番号 `order_no` | `YY-MO-NNNNN` | 素材発注書 作成時 |

> **同時実行安全な採番方式（監査C-4反映、`MAX()+1`単独＋例外頼みを廃止）:**
> - 採番は**トランザクション内で `pg_advisory_xact_lock(hashtext(<doc_type>||<年度>))` を取得してから** その年度の最大連番+1を確定（**実行基盤のインスタンス数に依らず**＝本番 EC2 単一ホスト docker compose でも将来の複数インスタンス化でも、並行作成を (doc_type, 年度) 単位で直列化）。
> - **防御多重化:** UNIQUE 制約違反を捕捉した場合は採番を再取得して**自動リトライ**（最大3回、Application 層）。**リトライ上限超過時は `PINST-005`/`MORD-004`（採番競合、再試行を促す）を返し、トランザクション全体をロールバック**（明細の部分作成なし、監査Minor-3反映）。advisory lock により競合は逐次化されるため上限超過は実質発生しない想定。
> - **複数素材発注の連続作成（1生産指示→複数仕入先、M-1デッドロック対策）:** 各 `POST /material-orders` を**独立トランザクション**で処理（1リクエスト=1発注=1採番）。フロントは仕入先別に逐次 POST（並列禁止）。これにより同一Tx内での複数採番ロック競合・番号穴・部分失敗を回避。採番Txは「番号確定→即コミット」の最小スコープに保ち、Excel生成等の重処理をロック保持中に含めない（監査Minor-1）。
> - 年度プレフィックスは採番時点の年度。年度切替時は連番リセット（advisory lock のキーに年度を含むため安全）。
>
> **既存 mgmt_no/order_no（完成品発注）との関係:** 既存は `MAX()+1`＋UNIQUE 方式（Iter2知見#2）。本増分の生産系は上記の advisory lock 方式を採用し、既存方式の並行リスクを持ち込まない。**既存の採番方式も同様の改善余地がある旨をオペレーターに申し送る**（本増分のスコープ外、別Issue）。

---

## 6. データ機密度・監査（既存 NFR §6.2 / 監査C-2・M-4反映）

| データ | 機密度 | 保護 |
|---|---|---|
| 素材単価（`material_order_lines.unit_price`） | 中-高 | 既存仕入単価と**同等**: KMS保存時暗号化＋TLS＋アクセス制御（§12 price権限）＋監査ログは金額マスク。**一覧合計はデフォルトマスク**（API §2.3） |
| 生産指示・素材発注（数量・納期・加工先・素材仕入先） | 中 | アクセス制御（§12）＋監査（C/U/D） |
| BOM（素材構成・所要量・ロス率＝歩留り） | 中 | アクセス制御（§12 product権限）＋監査。**歩留り情報の閲覧範囲はオペレーター確認推奨**（監査I-2、§13 D-prod-8） |

**監査ログ action 追加（既存 `audit_logs.action` 体系に追加。CR Major-1反映: action 定義の SoT である既存 `data-design.md §6.1` の例示一覧と §9 にも本 action を追記すること）:**
`ProductMaterial.Create/Update/Delete`、`ProductionInstruction.Create/Issue/Complete/Cancel`、`MaterialOrder.Create/Order/Cancel`、`Excel.Export`（entity で区別: ProductionInstruction / MaterialOrder）、`MaterialPrice.View`。

> **`MaterialPrice.View` を新設する理由（CR Major-1反映）:** 既存の製品仕入単価閲覧は `Price.View`。素材単価は別エンティティ（material_order_lines）で監査・分析上区別したいため**別 action 名 `MaterialPrice.View` を新設**（entity_type=MaterialOrder で更に区別）。既存 `data-design.md §6.1`（action 定義の SoT）に追記して一貫性を保つ（原則5）。

> **機密閲覧監査のブロッキング実装（監査M-4/Major-3反映）:** 既存の `AuditLogInterceptor` は SaveChanges Hook（書込Tx内・失敗時警告のみ＝非ブロッキング）だが、素材単価開示・Excel出力は**読取(GET)で書込Txを経由しない**ため、**明示的なサービス層 INSERT** で監査する（インターセプタに依存しない）。実装方針:
> - `MaterialPrice.View` / `Excel.Export`（単価含む帳票）は、**専用の短命トランザクションで監査INSERTを先に確定してから**単価・ファイルを返す。
> - 監査INSERT に**タイムアウト（2秒）**を設け主処理の無限待ちを防ぐ。失敗時は1回リトライ。
> - 永続失敗時は `AUDIT-001`（監査記録失敗のため開示不可）を返し**開示・出力を拒否**（営業秘密の閲覧証跡欠落を防ぐ＝trail完全性優先）。監査INSERT障害は §M-5 監視対象（サイレント業務停止検知）。
> - **可用性トレードオフ（オペレーター確認可）:** 上記は「証跡完全性 > 可用性」を採る既定。1-2名・低トラフィックの内部システムのため監査INSERT失敗は稀で実害は限定的。可用性優先なら「開示は許可しCRITICALアラート＋永続リトライキュー」に切替可（要オペレーター判断）。
> - 一般の C/U/D 監査は従来どおり**非ブロッキング**（原則4、既存インターセプタ）。

---

## 7. 設計上の重要判断と確認事項

### 7.1 BOM と既存3部位素材FK の関係（疎結合・監査M-3/コードレビュアー反映）
- 既存 `product_families.upper/insole/outsole_material_id`（NOT NULL）は**変更せず存置＝代表素材の表示用**（品番台帳ビュー・MIG-3移行データ・既存実装が依存）。
- `product_materials` は**所要量を伴うBOMの独立SoT**。**両者は疎結合**: BOM編集は `product_materials` のみを更新し、**3FK列へ書き戻さない**（双方向同期を廃止＝同期失敗・寸断リスクを排除、監査M-3）。
- **利便のため**、BOM未登録の品番でBOM編集を開くと、3部位（role 0/1/2）を 3FK列の素材で**初期シード**表示する（所要量は空欄、保存は product_materials のみ）。これは一方向の読み取りシードで、保存後の同期は行わない。
- **更新方式（監査M-3）:** ~~BOM保存は**差分upsert**（既存行のIDを保持＝`source_pi_line_id` 等のトレース寸断を回避）。削除は `is_deleted=TRUE`。~~ **2026-07-27 訂正（第 13 イテレーション監査）:** 実装 `ProductMaterialService.ReplaceAsync` は**全置換**（既存行を全件 `deleted_at` で論理削除 → 新規 INSERT）で、**行 ID は保持されない**。削除フラグの列名も `is_deleted` ではなく **`deleted_at`**（W-A で統一済み）。M-3 の「ID 保持」是正は実装されていない。PUT 全体の**単一トランザクション**化のみ実在する。関連する既知の欠陥は `screen-design.md §3.16「スコープ外ドリフト OD-1」`。
- **既存データへの影響（監査C-3）:** 既存 `product_families` 行には移行時に `product_materials` を**自動生成しない**（暫定所要量の投入による誤発注を防止）。BOMは実ユーザが明示登録（UC-PROD-1）。BOM未登録品番は素材発注の所要量展開を**ブロック**（MORD-001、§9・API §2.1）。

### 7.2 未/済バッジの算出方式（denormalized列なし・コードレビュアーC-1反映）
- 「品番ごと 素材発注 未/済 / 生産指示 未/済」は **派生算出**（`product_families` に状態列を追加しない＝同期バグ回避、原則2/6）。
- 算出ロジック（一覧の各品番に対し SQL EXISTS、約2,000品番でインデックス有効）:
  - 生産指示 **済** = `EXISTS(SELECT 1 FROM production_instructions pi WHERE pi.product_family_id = pf.id AND pi.status IN (1,2) AND pi.is_deleted = FALSE)` （`idx_pi_family_active` 利用）
  - 素材発注 **済** = `EXISTS(SELECT 1 FROM material_order_lines mol JOIN material_orders mo ON mol.material_order_id = mo.id WHERE mol.product_family_id = pf.id AND mo.status = 1 AND mo.is_deleted = FALSE)` （`idx_mol_family` で mol を引き、親 mo を PK lookup＋`status=1`/`is_deleted=FALSE` フィルタ。下記想定実行計画参照）
- **明細の `is_deleted` は参照しない**（明細は親CASCADEのみ、§2）。親 `mo.is_deleted=FALSE` ＋ `mo.status=1`（Ordered）で判定。
- **想定実行計画（監査CR Major-2反映）:** 生産指示=済 は `idx_pi_family_active`（`product_family_id` WHERE status IN(1,2) AND not deleted）で family 起点に直接判定。素材発注=済 は `idx_mol_family` で当該 family の mol（通常少数）を引き、各 mol の親 mo を PK（`material_orders_pkey`）lookup して `status=1 AND is_deleted=FALSE` をフィルタ。EXISTS は最初の1件ヒットで打切り。
- denormalized cache 列は持たない（SoT直読が単一真実源で安全）。2,000品番で 500ms 担保（実測再評価 D-prod-2）。大規模化時のみ cache を検討。

### 7.3 出力履歴の方式（コードレビュアーM-2反映）
- 生産指示・素材発注は **専用の出力履歴テーブル（`*_export_logs`）を設けない**。既存完成品発注の `purchase_order_export_logs`（`is_first_export`/`excel_template_version` 付き）とは**異なる**。
- 出力履歴は `audit_logs(action='Excel.Export', entity_type='ProductionInstruction'|'MaterialOrder', entity_id, occurred_at, actor)` に集約。`excel_template_version` 相当の追跡が必要な場合は audit_logs の詳細（`changes`/`note` JSON）に含める。
- → §2/§4 の「発注書と同型」は「**発注書のヘッダ/明細/スナップショット凍結/first・last_exported_at パターンを踏襲（出力履歴のみ audit_logs に集約）**」と読み替える。

### 7.4 オペレーターレビュー確認事項

| # | 論点 | 推奨案 |
|---|------|--------|
| D-prod-1 | 同一部位に複数素材（表地+裏地）の許容 | 許容（`(role, material_id)` 複合一意） |
| D-prod-2 | 未/済の denormalized cache 採否 | 持たない（派生算出）。性能課題時に再評価 |
| D-prod-3 | 素材発注の集約単位 | 素材仕入先別ヘッダ＋明細に由来品番 |
| D-prod-4 | ロス率の保持場所と扱い | BOM行 `loss_rate`（任意・DEFAULT 0）。MVPは `所要量×数量` 基本、設定時のみ反映（M-1反映） |
| D-prod-5 | 単位のマスタ化 | MVPは列挙、将来マスタ化 |
| D-prod-6 | 生産指示と既存完成品発注の連携 | MVPは非連携（独立）。ヒアリング後に検討 |
| D-prod-7 | **生産系の権限割当** | **§12 で確定**（BOM=product、生産指示/素材発注=purchase_order、素材単価=price）。既存実在トークンのみ使用（監査C-1反映） |
| D-prod-8 | BOM（歩留り）の閲覧範囲 | product:read 保有者に開示（品番台帳参照権限）。狭めるか要確認（監査I-2） |

---

## 8. 正規化チェック（DP-1 適合）

| エンティティ | 1NF | 2NF | 3NF | 備考 |
|---|---|---|---|---|
| product_materials | ✅ | ✅ | ✅ | FKのみ、推移依存なし |
| production_instructions | ✅ | ✅ | ⚠️ | 意図的非正規化: factory/product スナップショット（帳票凍結、既存 purchase_orders と同根拠） |
| production_instruction_lines | ✅ | ✅ | ⚠️ | sku/name スナップショット |
| material_orders | ✅ | ✅ | ⚠️ | supplier スナップショット |
| material_order_lines | ✅ | ✅ | ⚠️ | material_name スナップショット、subtotal 計算列 |

---

## 9. 移行・データ更新パッチ（CLAUDE.md 原則7・監査C-3反映）

| 対象 | 内容 |
|---|---|
| **既存 product_families → product_materials** | **自動生成しない**（暫定所要量による誤発注防止、監査C-3）。BOMは実ユーザが UC-PROD-1 で明示登録。既存品番は BOM 未登録のまま稼働でき、素材発注時に BOM 必須チェック（MORD-001）で明示登録へ誘導。3FK代表素材は BOM編集時の初期シードに利用（§7.1） |
| 付属・副資材の materials 登録 | 値札/証紙/箱/中敷/面ファスナー等を `materials` に追加（`material_classifications` に「付属」「副資材」分類追加）。オペレーター提供リストで投入。冪等（`ON CONFLICT (code) DO NOTHING`、materials の業務PK=code） |
| 既存26テーブル | **変更なし**（スキーマ破壊ゼロ） |

> リリース手順: `db/init/05-production.sql`（新規5テーブルDDL、冪等 `CREATE TABLE IF NOT EXISTS`）適用 → materials 付属/副資材投入。**既存データ・既存機能への影響なし**（BOM自動生成を行わないため）。

---

## 10. データボリューム（既存 NFR §3 整合）
| エンティティ | 5年想定 |
|---|---|
| product_materials | 約12,000件（2,000品番×平均6素材、BOM登録分） |
| production_instructions | 約5,000件 |
| production_instruction_lines | 約50,000件 |
| material_orders | 約10,000件（1生産指示×複数仕入先） |
| material_order_lines | 約60,000件 |
→ `db.t4g.small` で5年余裕（既存と同オーダー）。

---

## 11. I/F 6視点チェック（データ層）

| # | 視点 | 結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ PostgreSQL(本番14.17互換)＋EF Core 8。`GENERATED STORED`/部分インデックス/`pg_advisory_xact_lock` は 12+ 対応 |
| 2 | ユースケース | ✅ UC-PROD-1〜5 全カバー |
| 3 | ユーザビリティ | ✅ 未/済の部分インデックスで一覧高速化、単価未設定注記 |
| 4 | データ設計上の都合 | ✅ 正規化＋非正規化根拠明示、既存26テーブル無変更、明細 is_deleted 方針を既存と統一 |
| 5 | 型の継承関係 | ✅ Entity→DTO→API 写像、Enum=SMALLINT⇔文字列 |
| 6 | データフロー整合性 | ✅ BOMは独立SoT（3FKと疎結合）、生産指示→素材発注の起点連鎖、未/済はSoT直読・採番は同時実行安全 |

---

## 12. 認可割当（監査C-1/Major-1反映・D-prod-7確定、既存実在トークン・既存ヘルパー再利用）

> **権限値の非単調エンコード注意（監査Major-1反映、最重要）:** RDS権限列は単調増加ではない。`product_ledger_permission`: 0=なし/1=更新可能/2=参照のみ/3=参照のみ(制限)。`purchase_order_create_permission`: 0=なし/1=更新可能/2=参照のみ。**`attendance_permission`（勤怠、Iteration 30 で追加した 5 つ目のカテゴリ）: 0=なし/1=更新可能/2=参照のみ — 既存カテゴリと同じ非単調スケール。**「値が大きい＝高権限」ではない**ため `≥` で read/write を導出してはならない（勤怠権限も同様。`>= 1` と書くと「参照のみ(2)」に書込を許すバグになる）。**本増分は判定ロジックを新発明せず既存実装を再利用する**: write 系は既存 `CheckMasterEditAsync`（`product_ledger_permission`）／`CheckOrderEditAsync`（`purchase_order_create_permission`）をそのまま使う。read 系は既存の参照系エンドポイント同様、認証済アクティブユーザに開放（フロントで編集UIを権限制御）。段階Cで Custom Claims 化する際の write/read 厳密導出（1=更新可能 vs 2=参照のみ の解釈の精緻化）は**既存と歩調を合わせて一括で実施**する（本増分単独では既存の2値運用を踏襲し、独自の値解釈を持ち込まない）。
>
> **2026-07-27 訂正（実装との乖離解消）:** 本注記は当初 write gate を「`ProductLedgerPermission >= 1` を要求（現行MVPの2値簡素化＝Iter1知見#7）」「`PurchaseOrderCreatePermission >= 1`」と記述していたが、**実装は既に `== 1`（更新可能）のみを許可する形へ是正済み**（`src/Backend/Presentation/Endpoints/AuthEndpoints.cs`。旧 `>= 1` は「参照のみ(2/3)」に誤って書込を許していた）。本注記が警告していた非単調エンコード問題そのものであるため、記述を実装に合わせて訂正する。**5 つ目の勤怠権限にも同じ規則が適用される**（write = `CheckAttendanceWriteAsync` が `attendance_permission == 1`、read = `CheckAttendanceReadAsync` が `1 または 2`）。なお勤怠の**管理系**（全員のタイムカード・承認/却下・休暇付与・勤怠ルール設定）は、**勤怠参照権限（1 または 2）かつオーナー権限 `process_record_permission >= 1`** の **AND** で判定する（`CheckAttendanceAdminAsync` が参照権限のチェックを内包する）。`process_record_permission` は 0/1 の 2 値なので `>= 1` で正しい（非単調ではない）。**オーナーであることだけでは足りない**点に注意（2026-07-27 訂正。当初は「勤怠権限では判定せず、オーナー権限に集約」と記載していた）。

| 機能 | 認可（既存トークン/ヘルパー） |
|---|---|
| BOM 参照（B-02, GET materials/requirements） | 認証済アクティブユーザ（既存read系と同等）＝`product:read` 相当 |
| BOM 更新（B-01, PUT materials） | 既存 `CheckMasterEditAsync`（`product_ledger_permission == 1`。2026-07-27 訂正: 旧記載 `>= 1`）を再利用＝`product:write` 相当 |
| 生産指示 参照（PI-02/03 GET, /excel） | 認証済（既存read系と同等）＝`purchase_order:read` 相当 |
| 生産指示 更新（PI-01/03 POST/PATCH/issue/complete/cancel） | 既存 `CheckOrderEditAsync`（`purchase_order_create_permission == 1`。2026-07-27 訂正: 旧記載 `>= 1`）を再利用＝`purchase_order:write` 相当 |
| 素材発注 参照（金額なし: MO-02一覧マスク, prepare） | 認証済＝`purchase_order:read` 相当 |
| **素材発注 金額開示（GET 詳細/一覧 ?include_amount, /excel）** | 上記 ＋ `price:read`（既存仕入単価マスクと同方式、Custom Claims。段階Bでの price 強制は既存 api-design.md §2.5 の price 運用に追従）。`MaterialPrice.View` 監査（§6 ブロッキング） |
| **素材発注 更新（単価設定含む: MO-01/03）** | `CheckOrderEditAsync`（write）＋ `price:write` |
| 未/済 バッジ（PS-01, products?`include=production_status`） | **`purchase_order:read` 相当**（生産手配情報のため。`product:read` のみのユーザには生産バッジ列を出さない＝情報非対称・導線403を回避、監査Major-2） |

> **割当根拠:** 生産指示・素材発注は作成/編集を伴う書込操作のため、既存「発注書作成権限」(`purchase_order_create`) の write gate（`CheckOrderEditAsync`）を再利用（既存パターン、原則3）。BOMは品番属性のため「品番台帳管理権限」(`product_ledger`) の `CheckMasterEditAsync` を再利用。素材単価は既存 `price` 権限と AND（既存仕入単価と同一保護、監査C-2）。**`purchase_order_info`/`process_record` への変更もオペレーター確認で可**（D-prod-7）。
> **PS-01 の権限整合（監査Major-2反映）:** 商品一覧本体は `product:read`。生産手配バッジ（`include=production_status`）は `purchase_order:read` 保有時のみ付与。「未」バッジからの作成画面導線は `purchase_order:write` 保有時のみ活性（非保有時は `aria-disabled`＋理由ツールチップ、403 を未然防止、screen §2.7）。

---

## 13. 変更履歴
| 日付 | 内容 |
|---|---|
| 2026-06-22 | 初版（5テーブル） |
| 2026-06-22 v2 | 独立レビュー1周目反映: 明細 is_deleted 非保持を明記し未/済クエリ修正（C-1）/ 認可を既存実在トークンに是正＋§12新設（監査C-1）/ 素材単価のprice権限AND・デフォルトマスク・MaterialPrice.Viewブロッキング監査（監査C-2/M-4）/ 移行はBOM自動生成せず誤発注防止（監査C-3）/ 採番をadvisory lock+リトライ・複数発注は独立Tx（監査C-4/M-1）/ BOM↔3FK疎結合化・差分upsert・単一Tx（監査M-3）/ ロス率を任意DEFAULT0でMVP基本式統一（M-1）/ 出力履歴はaudit_logs集約を明記（M-2）/ subtotal精度根拠（Mi-1）/ NULL単価subtotal=0意味論＋注記（Mi-2）/ CHECK括弧明示（Mi-3）/ ERDにlast_exported_at（Ni-1） |
| 2026-06-22 v3 | 独立レビュー2周目反映: §12 権限値の非単調エンコード是正＝既存 CheckMasterEditAsync/CheckOrderEditAsync 再利用（SA Major-1）/ PS-01生産バッジ権限を purchase_order:read に整合（SA Major-2）/ MaterialPrice.View を既存 data-design.md §6.1/§9 へ追記＋新設理由（CR Major-1）/ ブロッキング監査の読取系=明示サービス層INSERT＋2sタイムアウト＋AUDIT-001（SA Major-3）/ idx_mo_active 廃止＋§7.2 想定実行計画（CR Major-2）/ 採番リトライ上限 PINST-005/MORD-004（SA Minor-3）/ product_family_id NULL ロールアップ仕様（CR Minor-2）/ 採番Tx最小化・EC2単一実体注記（SA Minor-1） |
| 2026-07-27 | §12 権限値の非単調エンコード注意に **5 つ目の権限カテゴリ `attendance_permission`（0=なし/1=更新可能/2=参照のみ、同じ非単調スケール）を追加**。あわせて write gate の記述を実装に合わせて `>= 1` → `== 1` へ訂正（`CheckMasterEditAsync` / `CheckOrderEditAsync`）。勤怠の管理系はオーナー権限 `process_record_permission >= 1`（2 値、非単調ではない）に集約する旨を明記 |
| 2026-06-22 v4 | 独立レビュー3周目（収束確認）反映: §5 採番の実行基盤表記を中立化（「App Runner複数インスタンス」→「実行基盤のインスタンス数に依らず／本番EC2単一・将来複数とも直列化」、CR Nit-1/SA INFO-1）。3周目で CR=Crit0/Maj0/Min0、SA=リリースOK Crit0/Maj0/Min0 を確認、本修正で唯一の残差解消＝収束 |
