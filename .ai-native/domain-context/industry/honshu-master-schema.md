# 17マスタ仕様（既存システム由来）

> **出典:** ヒアリング時に提供された「System Context & Database Master Schema Specifications」（3枚の画像形式・原文英語）
> **位置づけ:** 既存システムのマスタスキーマを 100% 反映したスペック。**AS-IS（現状）の正規仕様** であり、新システムでもベース継承する。
> **最終更新:** Phase 0 ヒアリング時点
> **状態:** 仕様書の原文を Markdown 化。新発見の要点は §3-§4 に整理。

---

## 1. アーキテクチャ規約（全マスタ共通）

すべてのマスタは以下の3列を基底フィールドとして物理レイアウトに含む（既存システムのグリッドアーキテクチャに準拠）。

| カラム | 型 | 役割 |
|--------|-----|------|
| `code` | `VARCHAR(3)` PK | `000`〜`999` のゼロパディング連番 |
| `name` | `VARCHAR` or `TEXT` | コアラベル / ペイロードデータ |
| `delete_flag` | `BOOLEAN` | UI「削除」チェックボックスのマッピング。**論理削除必須**（過去取引データの整合性保護のため） |

→ **設計上の含意:** マスタはすべて論理削除であり、物理削除は禁止。過去取引が削除済みマスタを参照する状況を許容する設計。

---

## 2. マスタ別詳細定義（全17件）

### 2.1 サイズマスタ `size`

**Business Rules & Context:** 大人サイズ（S/M/L）と子供サイズ（110cm-160cm）を **単一のフラット階層** で扱う。コード範囲はソート順序を維持するように戦略的に割り当てること。

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `item_conversion_code` | `string` | | **品番変換コード:** SKU/バーコード生成用の英数字フォーマット（例: '110cm' → `110c`, 'アソート' → `AS`） |
| `delete_flag` | `boolean` | | Standard core field |

### 2.2 ブランドマスタ `brand`

**Business Rules & Context:** 現行・廃止ライセンスブランドを管理（廃止は論理削除フラグで表現）。

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `delete_flag` | `boolean` | | Standard core field |

### 2.3 機能マスタ `function`

**Business Rules & Context:** フットウェア/スリッパ特有の機能特性を管理（例: 静音、脱げにくい、足つぼ、超軽量、洗濯可能）。

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `delete_flag` | `boolean` | | Standard core field |

### 2.4 国マスタ `country`

**Business Rules & Context:** 生産国を管理。単一ルーティングとして管理する場合は複数国を結合可能（例: 'ミャンマー・カンボジア' を `004` として扱う）。

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `delete_flag` | `boolean` | | Standard core field |

### 2.5 仕入先マスタ `supplier`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `item_conversion_code` | `string(1)` | | **品番変換コード:** 単一文字を品番システムにマップ（例: 'Z', 'M', 'T'） |
| `official_name` | `string` | | **正式名:** 法的書面・B2B調達書・請求書で使用する正式社名 |
| `country_code` | `string(3)` | FK → `country.code` | **国:** 国マスタへの外部キー |
| `supplier_type` | `integer` | | **仕入区分:** 分類インジケータ。0 = 国内, 1 = 海外/輸入 |
| `alert_target` | `integer` | | **アラート対象:** 納期/品質追跡用のリスク管理・保留フラグ |
| `delete_flag` | `boolean` | | Standard core field |

### 2.6 事業部マスタ `department`

**Business Rules & Context:** 社内事業単位を管理（例: 第1事業部、第2事業部）。

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `delete_flag` | `boolean` | | Standard core field |

### 2.7 商品タイプマスタ `product_type`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | フットウェア構造とターゲット属性を組み合わせる（例: '吊込W底 婦人', '外縫 紳士'） |
| `item_conversion_code` | `string(1)` | | **品番変換コード:** 構造スタイルを品番に埋め込む構造文字（例: 'A', 'B', 'C'） |
| `size_demographic_code` | `string(1)` | | **サイズコード:** ターゲット分類識別子（例: 'R' = Regular/Women, 'M' = Men, 'J' = Junior） |
| `delete_flag` | `boolean` | | Standard core field |

### 2.8 商品季節マスタ `product_season`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `item_conversion_code` | `string(1)` | | **品番変換コード:** 季節を品番に注入する数値（例: '1'=通年, '2'=春夏, '3'=秋冬） |
| `conversion_order` | `string` | | **変換順位:** Markdown/季節追跡用の周期や動的ロジックサイクルをマッピングするカンマ区切り配列（例: '2,4' または '1,6,7,8,9'） |
| `delete_flag` | `boolean` | | Standard core field |

### 2.9 商品群マスタ `product_group`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | 衣料カテゴリではなく **商業ポジショニング** を追跡（例: '第1プロパー', '第1定番スリッパ', '第1バーゲン'） |
| `planning_fee` | `numeric` | | **企画費:** 原価計算シミュレーション時に自動適用されるコストパラメータ/乗数 |
| `delete_flag` | `boolean` | | Standard core field |

### 2.10 色マスタ `color`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | アソートマルチパック（'アソート 標準'）と個別カラーウェイ（'レッド', 'ピンク'）の両方を含む |
| `item_conversion_code` | `string(2)` | | **品番変換コード:** SKU/バーコードの末尾に使う標準2桁識別子（例: '10', '11'） |
| `delete_flag` | `boolean` | | Standard core field |

### 2.11 素材マスタ `material`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | 洗濯ラベル・組成表示用の原料組成（例: '綿', 'ポリ塩化ビニル'） |
| `material_classification_code` | `string(3)` | FK → `material_classification.code` | **素材分類:** 素材分類マスタへの外部キー |
| `delete_flag` | `boolean` | | Standard core field |

### 2.12 素材分類マスタ `material_classification`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | 広義の繊維/素材カテゴリ（例: '化学繊維', 'プラスチック', '天然素材', 'サンダル底'） |
| `delete_flag` | `boolean` | | Standard core field |

### 2.13 倉庫コードマスタ `warehouse`

**Business Rules & Context:** ルーティングポイント / 物流ノードを追跡するシンプルなシステム。

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | Standard core field |
| `delete_flag` | `boolean` | | Standard core field |

### 2.14 納品先マスタ `delivery_destination`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string` | | 小売流通センター/チャネル（例: 'しまむらセンター', 'KEYUCA', 'AEON'） |
| `remark_1` | `string` | | **備考1:** 物流発送先住所データ（例: 郵便番号・住所） |
| `remark_2` | `string` | | **備考2:** 配送拠点の通信チャネル（例: 電話番号） |
| `remark_3` | `string` | | **備考3:** 副次的な物流詳細（例: FAX 番号） |
| `delete_flag` | `boolean` | | Standard core field |

### 2.15 連絡文書定型・発注書 `document_template_purchase`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string/text` | | 法務・製造・B2B 金融指示を含む本文テキスト |
| `delete_flag` | `boolean` | | Standard core field |

### 2.16 連絡文章・確認表 `document_template_confirmation`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string/text` | | 品質保証・検証テキストパラメータ・処理指示 |
| `standard_print_flag` | `integer` | | **標準印字:** 1 = 新規取引フォームに自動プリポピュレート, 0 = 手動追加 |
| `delete_flag` | `boolean` | | Standard core field |

### 2.17 連絡文章・発注書 `document_text_purchase`

| Field Name | Type | Key/Relation | Purpose / UI Description |
|------------|------|--------------|--------------------------|
| `code` | `string(3)` | PK | Standard core field |
| `name` | `string/text` | | 特定の調達条件・動的条項・国際電信規則（例: TT送金） |
| `standard_print_flag` | `integer` | | **標準印字:** 1 = 新規発注書に自動プリポピュレート, 0 = 手動追加 |
| `delete_flag` | `boolean` | | Standard core field |

---

## 3. 17マスタの構造的特徴（重要）

### 3.1 5階層に分類できる

| 階層 | 該当マスタ | 役割 |
|------|-----------|------|
| **品番構成系**（11桁 SKU 生成に直接寄与） | `size`, `product_type`, `product_season`, `color`, `supplier` | `item_conversion_code` を持ち、品番自動生成のソース |
| **商品属性系**（品番には出ないが商品を特徴づける） | `brand`, `function`, `material`, `material_classification`, `product_group` | 検索・分類・原価計算用 |
| **組織・取引先系** | `department`, `country`, `delivery_destination` | 業務組織と取引先情報 |
| **物流系** | `warehouse` | ルーティングポイント管理 |
| **文書テンプレート系** | `document_template_purchase`, `document_template_confirmation`, `document_text_purchase` | 発注書・確認表の定型文 |

### 3.2 `item_conversion_code` を持つマスタは 5 件のみ

- `size.item_conversion_code` (string)
- `product_type.item_conversion_code` (string(1))
- `product_season.item_conversion_code` (string(1))
- `color.item_conversion_code` (string(2))
- `supplier.item_conversion_code` (string(1))

→ 11桁品番ルール（`honshu-product-code-rule.md` §3）における各桁のソースは、上記5マスタ + **未定義の「工場マスタ」+「年式」コード** で構成される。仕入先マスタの `item_conversion_code` は工場コード（7桁目）相当だが、工場と仕入先は同一概念か別概念かは要確認。

### 3.3 すべて論理削除（物理削除禁止）

過去取引データの参照整合性を保護するため、削除は `delete_flag = TRUE` で表現する。新システムでもこの方針は継承必須。

### 3.4 性別（婦人/紳士/子供）の処理方法が判明

`product_type.size_demographic_code`（'R'=Regular/Women, 'M'=Men, 'J'=Junior）で性別を識別する。
→ 同じ `item_conversion_code` (例: 'A' = 吊込W底) の商品タイプレコードが性別ごとに **3 件存在** することを意味する。性別ごとの適用可能サイズ範囲（婦人=22-25, 紳士=24-28, 子供=110c-160c 等）を `product_type` 経由で制御する設計と推測される。

### 3.5 商品季節の複数コード問題が解決

`product_season.conversion_order` フィールド（例: '1,6,7,8,9'）が、同じ意味（通年）の複数コードをマッピングする機能を担う。
→ `honshu-product-code-rule.md` §3.3 の「なぜ通年=1/6/7/8/9 と複数あるか」の答えがここにある。各通年コード（1, 6, 7, 8, 9）はそれぞれ別レコードで存在し、`conversion_order` で関連付ける構造と推測。

---

## 4. 17マスタ仕様に **欠落** している要素（新システムで補完必須）

既存仕様には以下が含まれていないため、Phase 1 でテーブル定義を作成する際に追加考慮する。

| # | 欠落要素 | 根拠 | MVP 解消方針 |
|---|---------|------|-------------|
| 1 | **工場マスタ** | Excel「ホンシュ_品番設定 コード一覧.xlsx」に 27 件超の工場リストが存在。11桁品番の 7 桁目を構成。 | **MVP では `supplier` マスタを一旦兼用**（スポンサー判断）。`supplier_type` 等のディスクリミネータで工場/商社/原材料供給を区別する想定。将来分離の余地は残す。 |
| 2 | **甲皮素材マスタ** | 既存「品番台帳入力」画面に「甲皮素材」項目あり。17マスタには素材（中底/底素材想定）のみ。 | **既存の `material` マスタを参照する** ことで対応（スポンサー判断）。商品マスタ側の「甲皮素材」「中底素材」「底素材」フィールドはいずれも `material.code` への FK となる。 |
| 3 | **色のサブ分類**（値札 1-5、色相 -/+） | Excel 色コード表に存在するが、17マスタの `color` には `name` と `item_conversion_code` しか持たない。これらをどう表現するか要設計（追加カラム or 親子テーブル化）。 | Phase 3 で設計 |
| 4 | **商品マスタ本体テーブル** | 17マスタは「マスタ」のみで、品番（SKU）レコードを格納する **取引上の本体テーブル** が含まれない。11桁品番1件ごとのレコードを保持する `product` テーブルが必要。 | Phase 3 で `product_family` / `product` を新設 |
| 5 | **年式コードマスタ**（または変換ロジック） | 11桁品番の1桁目（年式）は文字（A-K, N, Z）で表現されるが、17マスタには対応マスタなし。コードロジックとして実装するか、専用マスタを設けるかの判断が必要。 | Phase 3 で設計（コードロジック有力） |
| 6 | **Departures（自社工場）の扱い** | ユーザー発言に「Departures（自社工場）」あり。工場マスタの 1 エントリか、別概念（事業部マスタとの組み合わせ）か要確認。 | MVP では `supplier` マスタの1エントリ扱い（§4-1 と統合） |
| 7 | **ユーザマスタ** | 既存「利用者マスタメンテナンス」画面で確認済み。発注担当者・副担当者・発注管理者の選択肢、および権限制御の母体。 | **MVP で新規マスタとして追加。**詳細仕様は §7 参照。 |

### 4.1 MVP 追加マスタの確定

| マスタ | 状態 | 備考 |
|--------|------|------|
| `user`（ユーザ） | ✅ **新規追加** | §7 参照 |
| `factory`（工場） | ❌ 追加せず | `supplier` を兼用（MVP 暫定判断） |
| `upper_material`（甲皮素材） | ❌ 追加せず | `material` の参照用途と判明 |

**結論:** MVP マスタ数 = **既存 17 + 新規 1（user）= 18マスタ**

---

## 5. 設計上の含意（Phase 1 への申し送り）

### 5.1 マスタ → トランザクション/エンティティの 2 層構造

17マスタはあくまで **マスタ（参照データ）** であり、ビジネスの主体（商品・受注・発注など）を表現するトランザクションテーブルは別途設計が必要。Phase 1 で以下のテーブル群を新設する想定:

| カテゴリ | 候補テーブル |
|---------|-------------|
| 商品（最優先） | `product` (11桁品番ごとに1レコード), `product_family` (商品企画レベルの親) |
| 取引 | `purchase_order`, `sales_order` |
| 在庫 | `inventory` |
| 集計 | `cost_calculation`, `pricing` |

### 5.2 マスタ間の外部キー整理

仕様書から確認できる FK 関係:
- `material.material_classification_code` → `material_classification.code`
- `supplier.country_code` → `country.code`

新システムでは以下の追加 FK が必要になる:
- `product.supplier_code` → `supplier.code`
- `product.country_code` → `country.code`（または `supplier` 経由で間接参照）
- `product.product_type_code` → `product_type.code`
- ...等

### 5.3 既存システムの命名規則を尊重

仕様書は英語ベース（`code`, `name`, `delete_flag`, `item_conversion_code` 等）。新システムでも同じ命名規則を採用するか、和文化するかは Phase 1 で決定。**移行コストを下げるなら英語踏襲を推奨**。

### 5.4 文書テンプレート（3マスタ）の業務上の使い分け

| マスタ | 用途 |
|--------|------|
| `document_template_purchase` | 発注書の定型本文（法務・製造・金融指示） |
| `document_template_confirmation` | 確認表の定型本文（QA・検証・処理指示） |
| `document_text_purchase` | 発注書の動的条項（TT送金等の調達条件） |

→ `template`（定型・複数選択肢）と `text`（動的条項・個別追加）の 2 種類があり、それぞれ標準印字フラグ（自動プリポピュレート）で挙動制御される設計。

---

## 6. 未確認・要確認事項

| # | 項目 | 確認方法 |
|---|------|----------|
| 1 | ~~仕入先マスタ `item_conversion_code` と工場マスタの関係~~ | **解消:** MVP では `supplier` マスタを工場兼用とする判断（§4-1）。`item_conversion_code` が工場コード（7桁目）に対応する想定で運用 |
| 2 | `supplier.alert_target` の具体的な業務上の発火条件 | 現場ヒアリング |
| 3 | 商品季節マスタの `conversion_order` 値の正確な解釈（同一 name の関連付け or マークダウン進行順？） | 現場ヒアリング |
| 4 | 商品群マスタの `planning_fee` の単位（円/%/倍率） | 現場ヒアリング |
| 5 | 文書テンプレート 3 マスタの実運用パターン（どの取引でどれが発行されるか） | 現場ヒアリング |
| 6 | Departures（自社工場）の正体 | 現場ヒアリング |
| 7 | 17マスタの code 採番順序ルール（戦略的ソート順のための割り当てルール） | 現場ヒアリング |
| 8 | 倉庫マスタと納品先マスタの使い分け（自社倉庫 vs 取引先納品先？） | 現場ヒアリング |

---

## 7. ユーザマスタ詳細仕様（既存「利用者マスタメンテナンス」画面より観察）

> **出典:** 既存システム「利用者マスタメンテナンス」画面スクリーンショット（2026-05-18 取得）
> **位置づけ:** MVP 追加マスタ（§4-7）の正規仕様

### 7.1 フィールド構成

| # | 列名（観察） | 推定型 | 値域 / 例 | 備考 |
|---|------------|--------|----------|------|
| 1 | 社員番号 | string(3) PK | 001, 008, 011, ..., 123 | 3桁数字、ユニーク |
| 2 | ユーザID | string | owner, HeadOffice_008, HeadOffice_A00, design_111, hs-sales-012, HSsales_097, YM note, 0119 | ログイン ID。命名規則は部署系プレフィックス |
| 3 | ユーザ名 | string | 今尾 雅広, 佐藤 浩始, 古川智惠, ... | 表示用日本語名 |
| 4 | 企画担当 | boolean | true / false | チェックボックス |
| 5 | 営業担当 | boolean | true / false | チェックボックス |
| 6 | 品番台帳管理権限 | enum(4) | なし / 更新可能 / 参照のみ / 参照のみ(制限) | 商品マスタ画面の操作レベル |
| 7 | 発注書作成権限 | enum(3) | なし / 更新可能 / 参照のみ | 発注書作成画面の操作レベル |
| 8 | 発注情報管理権限 | enum(2) | なし / あり | 発注情報の管理権限（詳細用途は要確認） |
| 9 | 工程実績管理権限 | enum(2) | なし / あり | 工程実績画面の権限（**MVP 対象外**機能だが、ユーザマスタは保持）。**MVP 実装では「オーナー権限」として利用者マスタ管理・勤怠管理の gate も兼ねる**（`CheckUserAdminAsync` / `CheckAttendanceAdminAsync`）|
| 10 | 削除 | boolean | true / false | 論理削除フラグ |
| 11 | 勤怠権限 | enum(3) | なし / 更新可能 / 参照のみ | **AS-IS には存在しない。MVP 実装で追加した 5 つ目の権限カテゴリ**（`users.attendance_permission`、Iteration 30 / 2026-07-27、akebono-office からの移植）。DB 既定は `1`（更新可能）= 全従業員が移行直後から打刻できる必要があるため |

> **2026-07-27 追記（AS-IS と MVP 実装の差分）:** 上表 #1〜#10 は既存システム（AS-IS）の観察結果。
> MVP 実装ではこれに加えて **勤怠権限（#11）と勤怠付随列**（`punch_required` / `attendance_rule_id` /
> `hire_date` / `weekly_days` / `weekly_hours`）を `users` に持つ。列定義の SoT は
> `.ai-native/outputs/phase5/data-design.md §3.18`、エンティティは `src/Backend/Domain/Entities/User.cs`。

### 7.2 ユーザID 命名規則（観察ベース、要確認）

| プレフィックス | 部署 / 役割 |
|---------------|-----------|
| `owner` | 特権ユーザ |
| `HeadOffice_NNN` | 本社系 |
| `design_NNN` | デザイン / 企画系 |
| `hs-sales-NNN`, `HSsales_NNN` | 営業系 |
| その他（`YM note`, `0119`） | 例外的、要確認 |

### 7.3 権限モデルの観察

- **4つの独立した権限カテゴリ**を組み合わせて制御（**AS-IS 観察時点。MVP 実装では勤怠を加えた 5 カテゴリ**、下記追記参照）
- 各権限カテゴリで **権限の粒度（レベル数）が異なる**:
  - 品番台帳: 4レベル（「参照のみ(制限)」が独自）
  - 発注書作成: 3レベル
  - 発注情報・工程実績: 2レベル（あり/なし）
  - **勤怠: 3レベル（MVP 実装で追加、Iteration 30）**

> **2026-07-27 追記（MVP 実装の権限カテゴリは 5 件）:** 上記 4 カテゴリに `attendance_permission`（勤怠）を加えた
> **計 5 カテゴリ**が実装の実態。**勤怠権限の値も既存カテゴリと同じ非単調エンコード**
> （`0=なし / 1=更新可能 / 2=参照のみ`）であり、**「値が大きい = 高権限」ではない**。
> 書込判定は必ず `== 1` で行うこと（`>= 1` は「参照のみ(2)」に書込を許すバグになる）。
> 勤怠の管理系操作（全員のタイムカード・承認/却下・休暇付与・勤怠ルール設定）は勤怠権限では判定せず、
> **オーナー権限 `process_record_permission >= 1`**（工程実績管理権限、2 値なので `>= 1` で正しい）に集約している。
> SoT: `src/Backend/Domain/Entities/User.cs` / `src/Backend/Presentation/Endpoints/AuthEndpoints.cs`。
- 担当属性（企画担当 / 営業担当）と権限は **直交した別概念**
- 論理削除あり（17マスタの `delete_flag` と同じ運用）

### 7.4 MVP 設計への含意

| 観点 | 反映 |
|------|------|
| 認証・権限分離 | **要確認状態は解消**。AS-IS で4権限カテゴリ × レベル分けが実装済み、MVP は踏襲する想定。**実装結果（2026-07-27）: AS-IS の 4 カテゴリを踏襲したうえで、勤怠権限を 5 つ目のカテゴリとして追加した（§7.3 追記）** |
| 工程実績管理権限 | MVP 対象外機能だが、ユーザマスタのフィールドとしては保持（将来拡張のため） |
| 発注書の担当者選択 | 「発注担当者」「発注者・副1〜6」「発注管理者」の選択肢は本マスタからユーザを引いてくる |
| 企画担当 / 営業担当 | 商品マスタ登録時の「企画者」表示に使用される可能性。Phase 3 で要確認 |

### 7.5 未確認事項

| # | 項目 | 確認方法 |
|---|------|----------|
| 1 | パスワード管理方式（DB 保持? SSO? 既存システムの方式は？） | ヒアリング + ログイン画面確認 |
| 2 | 「発注情報管理権限」と「発注書作成権限」の業務上の使い分け | 生産管理部ヒアリング |
| 3 | 「参照のみ(制限)」の制限内容 | 現場確認 |
| 4 | ユーザID の正規命名規則の根拠（部署再編時の運用等） | 運用ルールヒアリング |
| 5 | 工程実績管理権限を MVP のユーザマスタに含めるか省略するか | スポンサー判断 |
