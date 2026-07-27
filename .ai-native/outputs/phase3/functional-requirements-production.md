# Phase 3 成果物: 機能要件（生産管理拡張）

> **作成日:** 2026-06-22 / **改訂:** 2026-06-22 v2（独立レビュー1周目 反映: エラーコード接頭辞独立化・認可を既存実在トークンへ・ロス率任意化・観測可能性）
> **状態:** ドラフト v2（再レビュー前）
> **位置づけ:** 既存 `functional-requirements.md`（21機能 M/P/O/C）への**増分**。
> **依存:** `usecases-production.md`、`data-design-production.md` v2、オペレーター確定4判断
> **非機能:** 既存 `non-functional-requirements.md` を継承。追記は §5。

---

## 1. 機能一覧（増分）

機能ID: `B-NN`(BOM) / `PI-NN`(生産指示) / `MO-NN`(素材発注) / `PS-NN`(生産手配ステータス)

| ID | カテゴリ | 機能名 | UC | スコープ |
|----|---------|--------|----|---------|
| **B-01** | BOM | 素材構成（BOM）登録・編集 | UC-PROD-1 | ✅ 直近詳細 |
| **B-02** | BOM | BOM所要量展開プレビュー | UC-PROD-3 | ✅ 直近詳細 |
| **PI-01** | 生産指示 | 生産指示書 新規作成 | UC-PROD-2 | ✅ 直近詳細 |
| **PI-02** | 生産指示 | 生産指示書 一覧・検索 | UC-PROD-2/4 | ✅ 直近詳細 |
| **PI-03** | 生産指示 | 生産指示書 編集・発行・完了・中止 | UC-PROD-2 | ✅ 直近詳細 |
| **PI-04** | 生産指示 | 生産指示書 Excel出力 | UC-PROD-2 | ✅ 直近詳細 |
| **MO-01** | 素材発注 | 素材発注書 新規作成（BOM展開→仕入先別） | UC-PROD-3 | ✅ 直近詳細 |
| **MO-02** | 素材発注 | 素材発注書 一覧・検索 | UC-PROD-3 | ✅ 直近詳細 |
| **MO-03** | 素材発注 | 素材発注書 編集・発注確定・中止 | UC-PROD-3 | ✅ 直近詳細 |
| **MO-04** | 素材発注 | 素材発注書 Excel出力 | UC-PROD-3 | ✅ 直近詳細 |
| **PS-01** | 生産手配 | 品番ごと未/済 2軸バッジ一覧・フィルタ | UC-PROD-4 | ✅ 直近詳細 |

**追加機能数: 11**（既存21＋本増分11＝計32）

---

## 2. 機能別 入出力定義

> **エラーコード接頭辞（独立化、コードレビュアーC-2反映）:** BOM系=`BOM-NNN`、生産指示系=`PINST-NNN`、素材発注系=`MORD-NNN`、Excel出力=既存 `EXPORT-NNN` 再利用。既存 `PROD-NNN` は商品マスタ専用のため流用しない。`ErrorCodes.cs` に追加、Phase 3 非機能 §7 接頭辞リストに追記。
> **認可（既存実在トークンのみ、監査C-1反映 / D-prod-7確定）:** BOM=`product:*`、生産指示/素材発注=`purchase_order:*`、素材単価開示/設定=`price:read/write` を AND（`data-design-production.md §12`）。

### 2.1 BOM（B-NN）

#### B-01: 素材構成（BOM）登録・編集
| 観点 | 内容 |
|------|------|
| 入力 | 品番(product_family_id)、各行: 部位(甲皮/中底/底/付属/副資材), 素材(material_id), 1足あたり所要量, 単位, 推奨仕入先(任意), ロス率(任意,DEFAULT 0), 備考 |
| 処理 | 単一トランザクションで `product_materials` 差分upsert（既存行ID保持、削除は is_deleted）。**3FK列へは書き戻さない**（疎結合、data-design §7.1）。重複(部位×素材)チェック |
| 出力 | 保存通知、BOM一覧再表示 |
| 業務ルール | 同一部位に複数素材可。所要量>0。論理削除のみ。BOM未登録の品番でも稼働可（素材発注時に必須化） |
| エラー | `BOM-001`(所要量・単位不正), `BOM-002`(部位×素材重複) |
| 認可 | `product:write` |

#### B-02: BOM所要量展開プレビュー
| 観点 | 内容 |
|------|------|
| 入力 | 品番(or 生産指示id), 生産数量（色×サイズ別 or 合計） |
| 処理 | `Σ(所要量 × 数量)` を素材別集計（loss_rate 設定行のみ `×(1+loss_rate)`）。推奨仕入先別グルーピング。**金額は返さない** |
| 出力 | 素材別・仕入先別の推奨数量リスト（素材発注のプリセット） |
| 業務ルール | BOM未登録時は `MORD-001`（素材発注へ進めない） |
| 認可 | `product:read` |
| 性能 | 1品番展開 300ms以内 |

### 2.2 生産指示（PI-NN）

#### PI-01: 生産指示書 新規作成
| 観点 | 内容 |
|------|------|
| 入力 | 品番, 加工先(factory_supplier_id), 希望納期, 色×サイズ別生産数量, 連絡文章 |
| 処理 | `production_instructions`(Draft)＋`production_instruction_lines` 生成、`instruction_no`(YY-PI-NNNNN)を advisory lock+リトライで採番、planned_quantity＝明細合計を整合 |
| 出力 | 生産指示書プレビュー、詳細へ |
| 業務ルール | 数量>0、同一SKU重複不可。Idempotency-Key で二重作成防止 |
| エラー | `PINST-001`(数量0), `PINST-002`(SKU重複), `PINST-003`(品番にSKU未展開/family不一致), `PINST-005`(採番リトライ上限超過) |
| 認可 | `purchase_order:write` |

#### PI-02: 生産指示書 一覧・検索
| 観点 | 内容 |
|------|------|
| 入力 | フリーワード(指示番号/品番/品名/加工先)、絞込(status, 加工先, 納期, 期間)、ソート、ビュー |
| 処理 | `production_instructions` 検索（加工先・品番 Include、N+1回避） |
| 出力 | 一覧（指示番号, 品番, 品名, 加工先, 生産数量, 納期, ステータスバッジ, 出力バッジ） |
| 認可 | `purchase_order:read` |
| 性能 | 一覧初期表示 500ms |

#### PI-03: 生産指示書 編集・発行・完了・中止
| 観点 | 内容 |
|------|------|
| 入力 | 指示id, 編集値 / 発行 / 完了 / 中止(理由) |
| 処理 | status遷移 Draft→Issued(`instructed_at`)→Completed / →Cancelled。Draft/Issued のみ編集可 |
| 出力 | 更新通知、ステータスバッジ更新 |
| 業務ルール | 発行で品番の「生産指示=済」。中止は物理削除しない。Excel出力は中止後も可 |
| エラー | `PINST-004`(中止済の編集不可) |
| 認可 | `purchase_order:write` |

#### PI-04: 生産指示書 Excel出力
| 観点 | 内容 |
|------|------|
| 入力 | 指示id |
| 処理 | ClosedXML流し込み。初回時のみ加工先名/コード/品番のスナップショット凍結＋`first_exported_at`、毎回`last_exported_at`。出力履歴は `audit_logs(Excel.Export, entity=ProductionInstruction)`（専用テーブルなし、data-design §7.3） |
| 出力 | 生産指示書 Excel |
| 業務ルール | テンプレは新規設計（既存帳票無し、実帳票入手後に体裁調整、§ヒアリング#6） |
| 認可 | `purchase_order:read` |
| 性能 | 1指示(50明細)5秒以内 |
| エラー | `EXPORT-001`(テンプレ不正)/`EXPORT-002`(生成失敗)。**500系は監視対象**（observability、§5） |

### 2.3 素材発注（MO-NN） ※素材単価＝機密

#### MO-01: 素材発注書 新規作成（BOM展開→仕入先別）
| 観点 | 内容 |
|------|------|
| 入力 | 起点の生産指示id（or 品番＋数量直接指定）、調整後の素材別数量・単価・納期、素材仕入先 |
| 処理 | B-02展開を初期値に、素材仕入先別に `material_orders`(Draft)＋`material_order_lines` 生成、`order_no`(YY-MO-NNNNN)を advisory lock+リトライ採番。各明細に由来品番(`product_family_id`)保持。**1リクエスト=1発注=1採番の独立トランザクション**（複数仕入先は逐次POST、監査C-4/M-1） |
| 出力 | 素材発注書プレビュー（仕入先別に複数生成され得る）、詳細へ |
| 業務ルール | BOM未登録は `MORD-001`。数量>0、単価≥0。Idempotency-Key 必須 |
| エラー | `MORD-001`(BOM未登録), `MORD-002`(数量/単価不正), `MORD-004`(採番リトライ上限超過) |
| 認可 | `purchase_order:write` **AND** `price:write`（単価設定を伴うため、既存仕入単価と同一保護、監査C-2） |

#### MO-02: 素材発注書 一覧・検索
| 観点 | 内容 |
|------|------|
| 入力 | フリーワード(発注番号/素材仕入先/素材名)、絞込(status, 素材仕入先, 納期, 由来品番, 期間)、ソート、ビュー |
| 処理 | `material_orders` 検索（素材仕入先 Include） |
| 出力 | 一覧（発注番号, 素材仕入先, 明細素材数, **合計金額＝デフォルトマスク "***"**, 納期, ステータス, 出力バッジ） |
| 業務ルール | 合計金額は `?include_amount=true` ＋ `price:read` 保有時のみ実値＋`MaterialPrice.View` 監査（既存発注一覧と同方式、監査C-2） |
| 認可 | `purchase_order:read`（金額開示は ＋`price:read`） |

#### MO-03: 素材発注書 編集・発注確定・中止
| 観点 | 内容 |
|------|------|
| 入力 | 発注id, 編集値 / 発注確定 / 中止(理由) |
| 処理 | status遷移 Draft→Ordered(`instructed_at`)/→Cancelled。Draftのみ全編集、Orderedは限定編集 |
| 出力 | 更新通知 |
| 業務ルール | 発注確定で品番の「素材発注=済」。中止は物理削除しない |
| エラー | `MORD-003`(中止済の編集不可) |
| 認可 | `purchase_order:write` **AND** `price:write`（単価編集を伴うため） |

#### MO-04: 素材発注書 Excel出力
| 観点 | 内容 |
|------|------|
| 入力 | 発注id |
| 処理 | ClosedXML流し込み。初回時に仕入先名/コードのスナップショット凍結＋`first_exported_at` |
| 出力 | 素材発注書 Excel（単価含む） |
| 業務ルール | 単価を帳票に含むため開示扱い。`Excel.Export`＋`MaterialPrice.View` 監査を**ブロッキング**で記録（監査M-4） |
| 認可 | `purchase_order:read` **AND** `price:read` |
| 性能 | 5秒以内 |
| エラー | `EXPORT-001`/`EXPORT-002`（監視対象）, `AUDIT-001`（機密閲覧監査の記録失敗時に出力拒否） |

### 2.4 生産手配ステータス（PS-NN）

#### PS-01: 品番ごと未/済 2軸バッジ一覧・フィルタ
| 観点 | 内容 |
|------|------|
| 入力 | 既存商品一覧（P-04）への列追加 or 専用「生産手配」一覧。フィルタ(素材発注 未/済, 生産指示 未/済) |
| 処理 | 品番ごとに**派生算出**（data-design §7.2 のEXISTS、**明細 is_deleted は参照せず**親 status・is_deleted で判定、部分インデックス利用、N+1なし） |
| 出力 | 各品番行に「素材発注: 未/済」「生産指示: 未/済」バッジ。**BOM未登録品番は「BOM未登録（手配前）」を別表示**し手配漏れ（手配可なのに未）と区別（監査Minor）。フィルタで未手配のみ抽出 |
| 業務ルール | 済判定: 生産指示=Issued以上(status 1/2)が存在 / 素材発注=Ordered(status 1)の発注明細が当該品番に存在 |
| 認可 | `product:read`（一覧本体）＋ **`purchase_order:read`（生産バッジ付与の条件、監査Major-2）** |
| 性能 | 一覧（2,000品番）500ms。部分インデックスで担保 |
| 遷移 | 「未」バッジクリック→該当作成画面（PI-01/MO-01）。導線は `purchase_order:write` 保有時のみ活性（非保有は aria-disabled＋理由、監査Major-2） |

---

## 3. 業務ルール（増分 BR）

| # | ルール | 適用 |
|---|--------|------|
| BR-P1 | BOMは品番(product_family)単位。1足あたり所要量＋単位を持つ | B-01 |
| BR-P2 | 3部位代表素材(既存3FK)とBOM(product_materials)は**疎結合**。BOMが所要量SoT、3FKは表示用（書戻しなし）。BOM編集時に3FKを初期シードのみ利用 | B-01 |
| BR-P3 | 生産指示は品番×生産1回単位。色×サイズ別数量を明細で持つ | PI-01 |
| BR-P4 | 素材発注数量(推奨)= `Σ(所要量×生産数量)`。**ロス率は任意(DEFAULT 0)、設定時のみ `×(1+loss_rate)`**（M-1反映）。手調整可 | MO-01 |
| BR-P5 | 素材発注は素材仕入先別にヘッダ、明細に由来品番を保持 | MO-01 |
| BR-P6 | 未/済は品番ごと派生算出（生産指示=Issued以上、素材発注=Ordered。明細is_deleted非参照）。素材発注の品番ロールアップは `material_order_lines.product_family_id` に依存（prepare経由は充足、完全手動NULL明細は寄与しない、監査CR Minor-2） | PS-01 |
| BR-P7 | 採番: 生産指示=YY-PI-NNNNN、素材発注=YY-MO-NNNNN。**advisory lock+リトライで同時実行安全**（監査C-4） | PI-01/MO-01 |
| BR-P8 | 生産指示・素材発注とも論理削除＋中止状態。Excel出力は中止後も可 | PI-03/MO-03 |
| BR-P9 | 帳票宛名は初回Excel出力時にスナップショット凍結（既存F-22と同方針） | PI-04/MO-04 |
| BR-P10 | 監査: BOM/生産指示/素材発注の C/U/D＋Excel出力＋素材単価閲覧。**機密閲覧(MaterialPrice.View)/Excel.Exportは明示サービス層INSERTで短命Tx＋2sタイムアウトのブロッキング監査、永続失敗時 `AUDIT-001` で開示拒否**（一般C/U/Dは非ブロッキング、監査M-4/Major-3。`MaterialPrice.View` は既存 data-design.md §6.1 へ追記） | C-03拡張 |
| BR-P11 | BOM未登録の品番は素材発注の所要量展開をブロック（MORD-001）。誤発注防止（監査C-3） | MO-01 |

---

## 4. ユーザー権限（既存4権限の適用、D-prod-7確定・監査C-1反映）

> `production_info:*` は存在しないトークンであり**不使用**。既存実在の権限カテゴリ・トークンに割当（`data-design-production.md §12`）。
> **2026-07-27 追記:** 既存の権限カテゴリは Iteration 30 で 4 → **5**（勤怠 `attendance_permission` を追加）。本増分が使うのは品番台帳・発注書作成の 2 つで割当に変更はない。

| 機能 | 認可トークン | RDS権限列（段階B直読） |
|------|---------|------|
| B-01/B-02（BOM） | `product:write` / `product:read` | product_ledger_permission |
| PI-01〜04（生産指示） | `purchase_order:write` / `purchase_order:read` | purchase_order_create_permission |
| MO-01/03（素材発注 更新・単価設定） | `purchase_order:write` AND `price:write` | purchase_order_create + 価格権限 |
| MO-02/04（素材発注 参照・金額開示） | `purchase_order:read`（金額開示は AND `price:read`） | 同上 |
| PS-01（未済一覧・生産バッジ） | `product:read`（一覧本体）＋ `purchase_order:read`（生産バッジ付与の条件、監査Major-2） | product_ledger ＋ purchase_order_create |

> **割当根拠:** 生産指示・素材発注は作成/編集を伴う書込操作のため、既存「発注書作成権限」(`purchase_order_create`) の write gate（既存 `CheckOrderEditAsync`、`>= 1`）を**再利用**。BOMは品番属性のため `CheckMasterEditAsync`（`product_ledger_permission >= 1`）を再利用。**権限値は非単調（1=更新可能/2=参照のみ）のため `≥` で read/write を導出せず、既存実装の判定をそのまま使う**（監査Major-1、詳細 data §12）。素材単価は既存 `price` 権限と AND。`purchase_order_info`/`process_record` への変更もオペレーター確認で可（D-prod-7）。
> **CI Lint（監査M-2）:** 既存R-6（全API `[Authorize]` 必須）に加え、**指定ポリシー名が登録済みポリシー集合に存在するか**を検証（属性有無だけでなく名前解決）。新エンドポイント（prepare/material-requirements 含む）のポリシー名は実在トークンへ。

---

## 5. 非機能要件 追記（既存継承＋追加分）

| # | 項目 | 値 |
|---|------|-----|
| 性能 | 未/済一覧（2,000品番、EXISTS×2） | 500ms以内（部分インデックス担保） |
| 性能 | BOM所要量展開（1品番） | 300ms以内 |
| 性能 | 生産指示書/素材発注書 Excel出力 | 5秒以内 |
| セキュリティ | 素材単価 | 中-高（既存仕入単価と同等: KMS暗号化＋`price`権限AND＋監査ログ金額マスク＋MaterialPrice.Viewブロッキング監査） |
| セキュリティ | BOM（歩留り情報） | 中（`product`権限＋監査。閲覧範囲はオペレーター確認、D-prod-8） |
| 観測可能性（監査M-5） | 新エラーコード | `EXPORT-001/002`等の500系を CloudWatch メトリクスフィルタ＋Alarm 対象に追加（既存5xx監視に新action含む）。構造化ログに ErrorCode 付与、Excel生成失敗率をダッシュボード可視化。BOM未登録(MORD-001)・Excel失敗時のユーザ復帰導線（screen §5）と監視を連動 |
| データ量 | production_instructions / material_orders / product_materials | 約5,000 / 10,000 / 12,000件（5年、既存と同オーダー） |

---

## 6. Phase 3 ゲート（増分・自己評価）

| # | 条件 | 状態 | 根拠 |
|---|-----------|------|------|
| 3-1 | 業務要件が機能一覧に整理 | ✅ | §1（11機能） |
| 3-2 | 各機能の入出力定義 | ✅ | §2 |
| 3-3 | 非機能要件が数値/基準 | ✅ | §5 |
| 3-4 | データ機密度特定 | ✅ | §5（素材単価=中-高、BOM=中） |

---

## 7. 変更履歴
| 日付 | 内容 |
|---|---|
| 2026-06-22 | 初版（11機能） |
| 2026-06-22 v2 | エラーコード接頭辞をBOM/PINST/MORD＋EXPORT再利用に独立化（C-2）/ 認可を既存実在トークン product・purchase_order・price へ是正＋§4確定（監査C-1, D-prod-7）/ 素材単価のprice権限AND・マスク・ブロッキング監査（監査C-2/M-4）/ ロス率任意DEFAULT0でMVP基本式統一（M-1）/ 採番advisory lock（監査C-4）/ BOM疎結合BR-P2（監査M-3）/ BOM未登録ガードBR-P11（監査C-3）/ CI Lintポリシー名検証（監査M-2）/ 観測可能性（監査M-5）|
