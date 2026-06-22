# 生産管理拡張 設計サマリ（索引）

> **作成日:** 2026-06-22
> **対象:** akebono-honshu に「生産管理（素材発注・生産指示・未/済）」を追加する設計
> **背景:** フットウェア/ホームウェアOEM「ホンシュ」の現行5システム（販売管理アラジン／生産管理SQL Server／伝発名人／勘定奉行／EOS名人）からSaaSへリプレースする案件。既存MVP（商品マスタ→完成品発注書）の上流に位置する生産軸を追加する。
> **資料根拠:** `refference/` 配下 全件精読（ベンダー提案書 Apparel-ZONE 全69頁／生産管理CSV／販売管理アラジンCSV・PDF／品番コードExcel・DB情報）。

---

## 1. スコープと確定事項

### 直近（詳細設計・実装対象）
1. **商品マスタ登録 ＋ BOM（素材構成・所要量）** — 既存 product_families を BOM 拡張
2. **生産指示書（加工指図書）の出力** — 商品マスタ＋生産数量を起点に工場へ
3. **素材の発注書（生地材料発注）の出力** — BOM×生産数量で所要量展開、素材仕入先へ
4. **品番ごとの未/済ステータス**（素材発注／生産指示の2軸バッジ）

### その他（ロードマップ＋骨格）
工程実績・仕入/購買3段消込・在庫・受注・売上・債権債務・EDI/会計連携 → 段階導入ロードマップとデータモデル骨格（`architecture-production.md` §B）。

### オペレーター確定4判断（2026-06-22）
| # | 論点 | 確定 |
|---|---|---|
| 1 | 生産指示・素材発注の起点 | 商品マスタ(品番)＋生産数量 |
| 2 | 素材発注数量の算出 | BOMを商品マスタに登録（生産数量×所要量。ロス率は任意DEFAULT 0、設定時のみ加味） |
| 3 | 未/済の単位 | 品番ごと2軸バッジ |
| 4 | スコープ外要素の深さ | ロードマップ＋データモデル骨格 |

---

## 2. 成果物一覧

| 文書 | 内容 |
|---|---|
| `domain-context/business-flow/production-management-flow.md` | AS-IS/To-Be業務フロー、用語対応、現行事実、確定判断、未確認事項 |
| `outputs/phase2/usecases-production.md` | UC-PROD-1〜5（BOM登録/生産指示/素材発注/未済/素材マスタ） |
| `outputs/phase3/functional-requirements-production.md` | 11機能（B/PI/MO/PS）、業務ルールBR-P1〜10、権限、非機能追記 |
| `outputs/phase5/data-design-production.md` | 追加5テーブル、既存26テーブル無変更、正規化、移行パッチ |
| `outputs/phase5/api-design-production.md` | REST API（BOM/生産指示/素材発注/未済）、癒着回避、フロー検証 |
| `outputs/phase5/screen-design-production.md` | サイトマップ追加、画面定義、レスポンシブ/アクセシビリティ |
| `outputs/phase5/architecture-production.md` | モジュール配置＋全体ロードマップ＋骨格＋移行・リスク |

---

## 3. 設計の要点

- **既存26テーブルはスキーマ変更ゼロ**（下位互換、CLAUDE.md原則7）。追加5テーブルのみ。
- **既存の完成品発注書（purchase_orders）と素材発注書（material_orders）は別系統**（提案書も製品発注/生地材料発注を分離）。
- **BOMは新規データ**（現行は素材コードのみで所要量なし）。3部位代表素材FK(既存)とBOM(product_materials)は**疎結合**（BOMが所要量SoT、3FKは表示用・書戻しなし）。
- **未/済は派生算出**（denormalized列を持たず EXISTS＋部分インデックス、同期バグ回避。明細 is_deleted 非参照）。
- **既存パターンを最大流用**: ヘッダ＋明細＋スナップショット凍結＋first/last_exported_at（発注書と同型）、ClosedXML Excel、色×サイズマトリクスUI、カード/テーブル一覧、4権限。

---

## 4. 主要な未確認事項（実ユーザヒアリング）

`production-management-flow.md` §7 に集約。特に重要:
- 支給材モデルの有無（素材を工場へ支給 or 工場調達）
- 1足あたり所要量の実値・単位・ロス率
- 生産指示と完成品発注（既存）の併用実態
- 素材/製品仕入先の区分（仕入先分類の意味）
- 生産指示書・素材発注書の既存帳票（あればテンプレ入手）

---

## 5. レビュー状況

CLAUDE.md 原則9／方法論SP-8に基づき、独立ロール（コードレビュアー＋システム監査官）による設計レビューを指摘ゼロまで反復。結果は本書末尾に追記。

| 周回 | コードレビュアー | システム監査官 | 状態 |
|---|---|---|---|
| 1周目 | ISSUE: Critical 2 / Major 2 / Minor 3 / Nit 2 | FAIL: Critical 2 / Major 5 / INFO 5 | **全指摘を反映済**（data/api/functional/screen/arch/flow/index）。下記§6 |
| 2周目 | ISSUE: Critical 1 / Major 2 / Minor 2 / Nit 1 | 条件付きOK: Critical 0 / Major 3 | **全指摘を反映済**。下記§7 |
| 3周目 | CLEAR相当: Critical 0 / Major 0 / Minor 0（Nit 1=文書表記） | **リリースOK**: Critical 0 / Major 0 / Minor 0（INFO 1=同上） | **収束**。両者が指摘した唯一の残差（data §5 のApp Runner表記）を中立表現に修正し解消 |

> **収束（2026-06-22）:** 独立コードレビュアー・システム監査官による3周のレビュー/監査を経て、Critical/Major/Minor 指摘ゼロに到達。3周目で両者が共通指摘した唯一の Nit/INFO（data §5 の実行基盤表記の不一致）を修正済。システム監査官は**リリースOK（安全ゲートPASS）**判定。設計品質は収束（CLAUDE.md 原則9 / SP-8 充足）。

### 6. 1周目 指摘と対応（要約）

| 指摘 | ロール | 対応 |
|---|---|---|
| 未/済EXISTSが存在しない列 `mol.is_deleted` 参照 | CR C-1 | 明細テーブルは親CASCADEで is_deleted 非保持と明記、クエリから該当述語を削除（data §2/§7.2、api §2.4）|
| エラーコード `PROD-` が既存商品ドメインと衝突 | CR C-2 | 生産系を `BOM-`/`PINST-`/`MORD-`＋Excelは既存`EXPORT-`再利用に独立化（functional/api/usecases/screen 全置換）|
| ロス率の確定判断が文書間で矛盾 | CR M-1 | `loss_rate` を任意DEFAULT 0、MVP基本式= `所要量×数量`、設定時のみ加味 で全文書統一 |
| 出力履歴テーブルなしで「発注書と同型」表現 | CR M-2 | 出力履歴は `audit_logs(Excel.Export)` 集約と明記（purchase_order_export_logs と異なる、data §7.3）|
| subtotal精度差/NULL単価=0/CHECK括弧/ERD/見出し番号 | CR Mi-1〜3,Ni-1〜2 | 根拠明記・注記追加・括弧明示・ERD補記・画面見出し番号是正 |
| 認可トークン `production_info:*` が不在 | SA C-1 | 既存実在トークン（product/purchase_order/price）へ全件是正＋data §12 で割当確定（D-prod-7解消）|
| 素材単価の機密保護がAPI契約に無い | SA C-2 | `price:read/write` AND・一覧デフォルトマスク・MaterialPrice.View 監査をAPI契約化 |
| 移行パッチが制約と矛盾・誤発注リスク | SA C-3 | BOM初期自動生成を廃止、BOM未登録は素材発注をブロック（MORD-001）|
| 採番の同時実行/リトライ未設計 | SA C-4/M-1 | `pg_advisory_xact_lock`＋UNIQUE＋自動リトライ、複数発注は独立Tx逐次 |
| CI Lintがポリシー名実在を検証しない | SA M-2 | ポリシー名の登録集合存在検証を追加 |
| BOM→3FK同期の失敗時/全置換のトレース寸断 | SA M-3 | 疎結合化（書戻しなし）＋差分upsert＋単一Tx |
| 機密閲覧監査の非ブロッキング化リスク | SA M-4 | MaterialPrice.View/Excel.Export はブロッキング監査、一般C/U/Dは非ブロッキング |
| 新エラーコードの監視未登録 | SA M-5 | CloudWatch メトリクス/Alarm 対象に追加（arch RP-9）|

### 7. 2周目 指摘と対応（要約）

| 指摘 | ロール | 対応 |
|---|---|---|
| UC-PROD-1 だけ旧「3FK同期」記述が残存 | CR Crit-1 | UC-PROD-1 を疎結合（差分upsert・書戻しなし・初期シード）に修正、全文書で一貫 |
| 権限値が非単調(1=更新可能/2=参照のみ)なのに `≥1` 誤マッピング | SA Major-1 | data §12 を是正＝既存 `CheckMasterEditAsync`/`CheckOrderEditAsync`(write gate `>=1`)を再利用、独自の値解釈を持ち込まない |
| PS-01バッジ(product:read)と生産指示(purchase_order:read)の権限非対称・導線403 | SA Major-2 | 生産バッジは `purchase_order:read` 保有時のみ、導線は `purchase_order:write` 保有時のみ活性 |
| MaterialPrice.View が既存 data-design.md の action定義に未追記 | CR Major-1 | 既存 §6.1/§9 に追記＋新設理由を明記 |
| ブロッキング監査の読取系実装・可用性 | SA Major-3 | 明示サービス層INSERT＋短命Tx＋2sタイムアウト＋`AUDIT-001`、可用性トレードオフをオペレーター確認可と明記 |
| idx_mo_active が EXISTS で効かない | CR Major-2 | 当該インデックス廃止、想定実行計画（idx_mol_family＋PK lookup）を明記 |
| 移行でBOM未生成→全件「未」/採番リトライ上限/product_family_id NULL/App Runner表記 | Minor各 | BOM未登録 第3状態表示、`PINST-005`/`MORD-004`/`AUDIT-001` 追加、ロールアップ仕様明記、EC2実体注記 |

---

## 8. 実装（Iteration 5）と実装レビュー（2026-06-22）

設計マージ（PR #7）後、直近スコープを実装（PR #9）。

### 8.1 実装範囲
- **バックエンド**（.NET8）: DB 5テーブル（`db/init/05-production.sql`、既存26テーブル無変更）/ Domain 8型 / Application 4サービス（ProductMaterial・ProductionInstruction・MaterialOrder・ProductionStatusQuery）/ Excel 2サービス / `ProductionEndpoints` / DI・Program 配線。**CI `dotnet build -c Release` + docker build success**
- **フロントエンド**（Nuxt3）: `useProduction.ts` + 8ページ（未/済一覧・生産指示3・素材発注3・BOM編集）+ AppNav。**`nuxt typecheck` 0エラー（ローカル+CI）**

### 8.2 実装レビュー（独立2ロール×2周、CLAUDE.md 原則9）
| 周回 | コードレビュアー | システム監査官 |
|---|---|---|
| 1周目 | ISSUE（Crit1/Maj4/Min5/Nit3） | 条件付OK（Crit1/Maj3/Min4） |
| **2周目** | **CLEAR（Crit0/Maj0、Min1=任意）** | （1周目指摘の解消を2周目で全確認） |

**1周目→2周目で解消した主指摘:** AuditLogger 非ブロッキング化（原則4）＋note 512切詰め / 素材発注 一覧・詳細・Excel に `MaterialPrice.View` 監査（金額マスク）/ 生産指示 List・View 監査の対称化 / BOM の素材存在検証（FK 500→422）/ DTOコメント是正 / 死コード削除。

### 8.3 繰延（オペレーター判断事項）— 素材単価の price 権限ゲート
- **状況:** 設計（api-design-production §2.3）は素材単価の詳細/Excel/一覧開示に `price:read` ゲート＋一覧デフォルトマスク＋`MaterialPrice.View` ブロッキング監査を要求。**実コードには専用 price 権限カラムが存在せず**（`users` は4権限のみ）、**既存の仕入単価（PurchaseOrderService/OrderEndpoints）も同じく price 未ゲート**で平文返却。
- **判定（両レビュー一致）:** 本実装は**既存仕入単価と同水準＝機密保護を退行させていない**。実装可能な是正（`MaterialPrice.View` 監査記録＝金額マスク）は本イテレーションで実施済。
- **要判断:** price 権限ゲート/デフォルトマスク/ブロッキング監査(AUDIT-001) の本実装は、**既存の仕入単価(発注)と素材発注を一括で対象とする横断改修**（新権限カラム＋移行＋既存発注の改修）。直近スコープ外。次イテレーション（段階C）でオペレーター承認の上、既存・新規一括対応を推奨。

### 8.4 デプロイ不具合修正（2026-06-22 ホットフィックス）

- **事象:** 本番アプリの生産管理3画面（生産手配状況 / 生産指示書 / 素材発注書）が全て「取得に失敗しました（0件）」を表示。
- **原因（データフロー不整合・原則6）:** 生産管理5テーブルを `db/init/05-production.sql` のみに定義していた。`db/init/*.sql` は**空 DB 初期化専用**（ローカル `docker-entrypoint-initdb.d` / RDS `action=init`）で、iter-4 で初期化済の本番 RDS には届かない。RDS への差分適用は `db/migration/*.sql`（`action=migrate`）が唯一の経路だが、対応するマイグレーションファイルが無かった。バックエンド（`ProductionEndpoints`）はデプロイ済のため、存在しないテーブルを参照し 500（relation does not exist）→ 一覧3画面が全滅。`db-migrate.yml`（run #5）は成功したが `migrate` は `db/init/` を見ないため何も適用していなかった。
- **修正:**
  1. `db/migration/iter5-production-schema.sql` を追加（既存 RDS への適用経路）。重複定義を避けるため正規定義 `db/init/05-production.sql` を `\ir` で取込む（原則3/5）。全 DDL 冪等（原則2）・追加のみ（原則7）。
  2. `deploy/db/run-migrations.sh` の `init` を `db/init/*.sql` の glob 適用に修正（従来 01–04 ハードコードで 05 を取りこぼし）。新規 init ファイル追加時の付け忘れを防止（原則1）。
- **オペレーター作業:** マージ後に GitHub Actions「DB Init / Migrate (RDS)」を **action=migrate** で実行 → 本番 RDS に5テーブルを追加適用。バックエンド再デプロイは不要（コード変更なし）。
