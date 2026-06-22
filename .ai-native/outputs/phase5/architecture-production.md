# Phase 5 成果物: アーキテクチャ＋全体ロードマップ（生産管理拡張）

> **作成日:** 2026-06-22
> **状態:** ドラフト v1（独立レビュー前）
> **位置づけ:** 既存 `architecture.md` への**増分**。(A) 生産管理モジュールの配置、(B) スコープ外要素の全体ロードマップ＋データモデル骨格（オペレーター確定#4「ロードマップ＋骨格」）。
> **依存:** 既存 `architecture.md`（レイヤー構造・SoT境界・横断的関心事）、`data-design-production.md`、現行5システム分析、提案書モジュール構成。
> **方針:** 既存アーキ（Nuxt SPA＋.NET8 Vertical Slice＋RDS PostgreSQL＋Firebase Auth＋S3、AWS Tokyo / EC2 docker compose 実体）を変更しない。生産管理は既存モノリスに機能スライスとして追加。

---

## A. 生産管理モジュールの配置

### A.1 バックエンド（既存 Vertical Slice に追加）

```
src/Backend/
├─ Domain/
│   ├─ Products/
│   │   └─ ProductMaterial.cs            … 🆕 BOM エンティティ
│   └─ Production/                        … 🆕 生産管理ドメイン
│       ├─ ProductionInstruction.cs
│       ├─ ProductionInstructionLine.cs
│       ├─ MaterialOrder.cs
│       ├─ MaterialOrderLine.cs
│       ├─ ProductionInstructionStatus.cs (enum)
│       └─ MaterialOrderStatus.cs        (enum)
├─ Application/ (or Service 層、既存方式に追従)
│   ├─ ProductMaterialService     … BOM CRUD＋3FK同期＋所要量展開
│   ├─ ProductionInstructionService … 採番・status遷移・スナップショット凍結
│   ├─ MaterialOrderService       … prepare(BOM展開)・採番・status遷移
│   └─ ProductionStatusQuery      … 未/済 EXISTS 算出（商品一覧 include）
├─ Infrastructure/
│   ├─ Persistence/                … AkebonoDbContext に5エンティティ＋計算列(subtotal)マッピング追加
│   └─ Excel/
│       ├─ IProductionInstructionExcelService / 実装  … 生産指示書（ClosedXML）
│       └─ IMaterialOrderExcelService / 実装          … 素材発注書（ClosedXML）
└─ Presentation/Endpoints/
    ├─ ProductEndpoints.cs (既存に BOM・production_status を追加)
    ├─ ProductionInstructionEndpoints.cs … 🆕
    └─ MaterialOrderEndpoints.cs          … 🆕
```
- 依存方向は既存通り（Presentation→Application→Domain←Infrastructure）。
- 権限: 既存4権限ポリシーに `production_info:read/write`（発注情報管理権限）を割当（§機能要件 §4）。`[Authorize]` 必須化の既存CI Lint（R-6）対象に新エンドポイントを含める。
- 監査: 既存 `audit.LogAsync` / Interceptor に生産系 action（§data-design §6）を追加。
- Excel: 既存 `IPurchaseOrderExcelService`（ClosedXML）と同パターンで2サービス追加。テンプレ未入手のため当面は動的生成（既存 Iter3 仮テンプレ同様）、実帳票入手後にテンプレファイル方式へ差替（I/F不変、Iter3知見#5）。

### A.2 フロントエンド（既存 Nuxt 構成に追加）
- `pages/production/*`（§screen-design-production §1）、サイドナビに「生産管理」グループ。
- 既存 composable（`useApi`/`usePermission`）・既存コンポーネント（色×サイズマトリクス、一覧カード/テーブル、Excelダウンロード）を再利用。新規ドメイン部品: `BomEditor`, `ProductionInstructionForm`, `MaterialOrderPrepare`, `ProductionStatusBadge`。

### A.3 DB（既存 `db/init` に追加）
- `db/init/05-production.sql`（新規5テーブルDDL、冪等 `CREATE TABLE IF NOT EXISTS`）。
- `db/migration/` に既存 product_families→product_materials 初期生成パッチ（§data-design §9）。
- 本番は生SQL冪等適用方式（既存 `deploy/db/run-migrations.sh`、EF Migrationではない）に従う。

### A.4 SoT 境界（既存§1.2に追加）
| データ | SoT | キャッシュ/派生 |
|---|---|---|
| BOM（素材構成・所要量） | RDS `product_materials` | `product_families` 3FK列（代表素材、編集時同期） |
| 生産指示・素材発注 | RDS `production_instructions`/`material_orders` 系 | — |
| 品番ごと未/済 | （派生）SoT直読 EXISTS | denormalized 列なし（§data-design §7.2） |

---

## B. 全体ロードマップ＋データモデル骨格（スコープ外要素）

> オペレーター確定#4: スコープ外要素（受注・在庫・売上・仕入/購買・EDI等）は**ロードマップ＋データモデル骨格＋段階移行計画**まで設計（実装は直近3機能）。現行5システムの二重入力解消（真の課題）に向けた段階統合の道筋を示す。

### B.1 全体像（現行5システム → 統合SaaS）

```mermaid
flowchart TB
  subgraph SaaS["統合 SaaS (本プロジェクト)"]
    M[商品マスタ/品番<br/>＋BOM] --> PI[生産指示]
    M --> MO[素材発注]
    M --> PO[完成品発注<br/>(既存)]
    SO[受注] --> PI
    PO --> RCV[入荷予定/仕入<br/>3段消込]
    MO --> RCV
    RCV --> INV[在庫<br/>SKU×倉庫]
    SO --> SHIP[出荷指図/ピッキング]
    INV --> SHIP
    SHIP --> SALES[売上/債権]
  end
  ACC[会計 勘定奉行<br/>連携のみ] -.売上/仕入/請求/支払.- SALES
  EDI[EDI 取引先<br/>商品変換] -.受注/出荷.- SO
```

### B.2 段階導入ロードマップ

| 段階 | 領域 | 主テーブル骨格 | 現行の手本 | 状態 |
|---|---|---|---|---|
| 済 | 商品マスタ・完成品発注 | product_families/products/purchase_orders | 品番台帳・ORDER SHEET | 実装済 |
| **本案件** | **BOM・生産指示・素材発注・未/済** | product_materials/production_instructions/material_orders（本設計5テーブル） | 提案書 p-31/32（空白領域） | **詳細設計（本書群）** |
| 次1 | 工程実績（加工先別/品番別 進捗） | `production_processes`（指示の多工程化）/`process_records` | 提案書 工程実績照会 | 骨格（§B.3） |
| 次2 | 仕入/購買 3段消込（完成品＋素材） | `goods_receipts`(入荷予定)/`purchase_receipts`(仕入) ＋ 発注残＝発注−仕入 | アラジン発注明細/入荷予定/仕入、完了区 | 骨格（§B.3） |
| 次3 | 在庫（SKU×倉庫×月次） | `inventory_balances`/`inventory_movements`（12倉庫） | アラジン在庫CSV/生産管理在庫帳票 | 骨格（§B.3） |
| 後1 | 受注・配分・出荷指図・ピッキング | `sales_orders`/`shipping_instructions`/`picking_lists` | 提案書受注管理 | 骨格 |
| 後2 | 売上・債権債務・与信 | `sales`/`receivables`/`credit_limits` | アラジン売上明細/元帳/与信残高 | 骨格 |
| 後3 | EDI連携・会計連携・伝票発行 | `customer_product_conversions`/会計連携I/F | EOS名人/商品変換マスタ/勘定奉行 | 骨格 |

### B.3 データモデル骨格（次段階の主要エンティティ、列は代表のみ）

> 拡張余地確保が目的。詳細設計は各段階着手時に行う。既存命名規約・SoT原則を継承。

**工程実績（次1）** — 生産指示の多工程化:
- `production_processes`(id, production_instruction_id FK, process_no, process_name, planned_date) — 指示内の工程
- `process_records`(id, production_process_id FK, actual_quantity, actual_date, recorded_by) — 工程実績入力
- → 「指図残＝指示数−工程実績」で進捗算出。本案件の未/済バッジを「工程進捗%」へ発展。

**仕入/購買 3段消込（次2）** — 完成品発注（既存）＋素材発注（本案件）に共通:
- `goods_receipts`(id, source_type[PO/MaterialOrder], source_id, expected_date, status[未/完], 納期回答日) — 入荷予定
- `purchase_receipts`(id, goods_receipt_id FK, received_quantity, received_date) — 仕入実績
- → 発注残＝発注数−仕入数、完了区（アラジン手本）。完成品発注・素材発注の入荷を一元消込。

**在庫（次3）:**
- `warehouses`（既存マスタ流用、12倉庫）
- `inventory_balances`(id, warehouse_id, product_id, period_ym, opening/in/out/adjust/closing_qty) — 月次残高（アラジン在庫CSVの計算式踏襲）
- `inventory_movements`(id, warehouse_id, product_id, movement_type[入荷/出荷/入庫/出庫/調整/棚卸], quantity, occurred_at, ref) — 増減実績

**受注/売上（後）:**
- `sales_orders`/`sales_order_lines`（得意先×SKU、注文NO）、`shipping_instructions`/`picking_lists`、`sales`/`sales_lines`（原価/粗利/掛率/PS区分）、`customers`（与信限度額・回収条件）、`customer_product_conversions`（取引先商品コード⇔自社品番、EDI）。

### B.4 真の課題（二重入力解消）への寄与
- 本案件で **品番（商品マスタ）を SaaS 単一SoT化** し、生産軸（BOM/生産指示/素材発注）を載せることで、生産管理システム側の品番台帳・発注の二重入力を段階的に解消する第一歩。
- 次段階で仕入/在庫/受注/売上を統合し、アラジン側の重複も吸収（提案書 p-20「1元化」の道筋）。
- 会計（勘定奉行）は連携のみ（リプレース対象外）、EDIは後段で取込（EOS名人廃止の方針と整合）。

---

## C. 移行・並行稼働（既存 MIG 戦略に追従）
- 既存 MIG-3（生産管理CSV 138列→product_families/products）の延長として、品番台帳の素材3部位を `product_materials` 初期行へ（§data-design §9）。
- 所要量・付属/副資材・素材仕入先区分は現行データに無いため**実ユーザ後追い入力**（並行稼働期間で整備、§domain-context §7）。
- 既存26テーブル無変更のため、稼働中の MVP（商品マスタ・完成品発注）に影響なし（下位互換、CLAUDE.md 原則7）。

---

## D. リスク・留意点（本拡張固有）

| # | リスク | 影響 | 緩和 |
|---|---|---|---|
| RP-1 | BOM所要量の実データ不在（現行は数量を持たない） | 素材発注数が当面不正確 | 初期は手調整前提。所要量を実ユーザが整備、ロス率で吸収。展開はプレビューで人が確認 |
| RP-2 | 3部位代表素材(3FK)とBOM(product_materials)の同期ずれ | 表示と実BOMの不整合 | SoT=product_materials、編集時に同期トランザクション。reconcile不要な単方向（BOM→3FK）に限定 |
| RP-3 | 素材/製品仕入先の区分が現行に無い | 仕入先選択時の混乱 | 当面は区分せず選択可＋推奨仕入先で誘導。`仕入先分類`の意味判明後に区分追加（§domain §7-5） |
| RP-4 | 生産指示と完成品発注（既存）の二重発行運用が不明 | 業務重複/混乱 | MVPは独立。ヒアリング（§domain §7-7）後に連携（指示→完成品発注自動生成）を検討 |
| RP-5 | 未/済 EXISTS 算出の性能（一覧） | 一覧遅延 | 部分インデックス（idx_pi_family_active/idx_mol_family）。2,000品番で500ms担保。大規模化時のみ denormalize |
| RP-6 | 素材単価の機密漏洩 | 営業秘密 | 既存仕入単価と同等保護（KMS/アクセス制御/監査マスク） |
| RP-7 | Excel テンプレ未確定（生産指示書/素材発注書の現行帳票なし） | 体裁手戻り | 動的生成で先行、実帳票入手後に差替（I/F不変、既存Iter3知見#5） |

---

## E. I/F 6視点チェック（アーキ層）
| # | 視点 | 結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ 既存スタック・配置に追加スライス、無理なし |
| 2 | ユースケース | ✅ UC-PROD-1〜5 をモジュール配置で実現、ロードマップで全体カバー |
| 3 | ユーザビリティ | ✅ 既存UIパターン再利用で学習コスト最小 |
| 4 | データ設計上の都合 | ✅ 既存26テーブル無変更、5テーブル追加、SoT境界明確 |
| 5 | 型の継承関係 | ✅ 既存 Domain/Service/Endpoint 構成に準拠 |
| 6 | データフロー整合性 | ✅ 起点（商品マスタ）→生産指示→素材発注→未/済の連鎖、真の課題への寄与を明示 |

---

## F. 変更履歴
| 日付 | 内容 |
|---|---|
| 2026-06-22 | 初版（生産管理モジュール配置＋全体ロードマップ＋骨格＋移行・リスク） |
