# Phase 5 成果物: 画面設計

> **作成日:** 2026-05-19
> **状態:** ドラフト v1（オペレーターレビュー前）
> **依存:** Phase 3 機能要件（21機能 + UX 要件 P-04 カードビュー ARIA 仕様等）
>          + Phase 4 確定スタック（Nuxt 3 SPA + TailwindCSS + Reka UI + lucide-icons）
>          + `architecture.md` / `data-design.md` / `api-design.md`
> **方針:** サイトマップ + 21画面 + 共通レイアウトの機能定義。
>          UI 細部の最終確定は Phase 6 で業務担当者フィードバックを得てから。Phase 5 では構造・データ表現・操作の整合を主目的とする。
>          Phase 5 ゲート条件「サイトマップが作成されている」「画面ごとの機能定義がある」を充足する。

---

## 1. サイトマップ

### 1.1 ルート構造

```
[未認証]
├─ /login                                  ログイン（C-01）

[認証済 - 共通ヘッダ + サイドナビ常設]
├─ /                                        ホーム（ダッシュボード簡易版）
│
├─ /products/                               商品（P-04 一覧、デフォルト = カードビュー）
│   ├─ /products/new                        商品マスタ新規登録 P-01〜P-03
│   │   ├─ [Step 1] 基本情報             →  仮品番プレビュー (P-01)
│   │   ├─ [Step 2] サイズ展開           →  色×サイズ SKU 生成 (P-02)
│   │   ├─ [Step 3] 仕入先・単価         →  マルチ仕入先単価 (P-03)
│   │   └─ [Step 4] 確認 → 登録 → /orders/new?from_family={id} へ遷移
│   ├─ /products/families/{familyId}        商品詳細 (P-05) + 画像管理 (P-06)
│   └─ /products/families/{familyId}/edit   商品編集 (P-05)
│
├─ /orders/                                 発注書（O-03 一覧、デフォルト = テーブルビュー）
│   ├─ /orders/new                          発注書新規 (O-01: 新規企画から / O-02: 既存品番から)
│   ├─ /orders/{orderId}                    発注書詳細・修正 (O-04)
│   ├─ /orders/{orderId}/revisions/new      改訂 (O-04 枝番採番)
│   └─ /orders/{orderId}/excel              Excel 出力 (O-06、ダウンロード)
│
├─ /masters/                                マスタ管理 (M-01 一覧 hub)
│   ├─ /masters/sizes                       サイズマスタ
│   ├─ /masters/brands                      ブランドマスタ
│   ├─ /masters/functions                   機能マスタ
│   ├─ /masters/countries                   国マスタ
│   ├─ /masters/suppliers                   仕入先マスタ（工場兼用）
│   ├─ /masters/departments                 事業部マスタ
│   ├─ /masters/product-types               商品タイプマスタ
│   ├─ /masters/product-seasons             商品季節マスタ
│   ├─ /masters/product-groups              商品群マスタ
│   ├─ /masters/colors                      色マスタ
│   ├─ /masters/materials                   素材マスタ
│   ├─ /masters/material-classifications    素材分類マスタ
│   ├─ /masters/warehouses                  倉庫コードマスタ
│   ├─ /masters/delivery-destinations       納品先マスタ（取引先名フィールド含む）
│   ├─ /masters/document-template-purchases     連絡文書定型・発注書 (M-05)
│   ├─ /masters/document-template-confirmations 連絡文章・確認表 (M-05)
│   ├─ /masters/document-text-purchases         連絡文章・発注書 (M-05)
│   └─ /masters/users                       ユーザマスタ (M-03)
│
└─ /me                                       自身のプロフィール（パスワード変更導線）
```

### 1.2 画面数の集計

| カテゴリ | 画面数 |
|---|---|
| 認証 | 1 (`/login`) |
| ホーム | 1 (`/`) |
| 商品 | 3 (`/products`, `/products/new`, `/products/families/{id}` (詳細=編集兼用)) |
| 発注 | 3 (`/orders`, `/orders/new`, `/orders/{id}` (詳細=編集兼用)) |
| マスタ | 18 (一覧 hub `/masters` + 17マスタ + ユーザマスタ) |
| プロフィール | 1 (`/me`) |
| **合計** | **27 画面**（21機能を 27 画面に展開）|

### 1.3 機能 → 画面マッピング

| 機能 ID | 機能名 | 画面 |
|---|---|---|
| C-01 | ログイン | `/login` |
| C-02 | 権限制御 | 全画面横串（Pinia store + composable） |
| C-03 | 監査ログ記録 | バックエンド自動（UI なし、Post-MVP で閲覧画面追加検討）|
| M-01 | マスタ一覧（共通）| `/masters/{master}` の一覧部 |
| M-02 | マスタ編集（共通）| `/masters/{master}` の編集モーダル |
| M-03 | ユーザマスタ管理 | `/masters/users`（固有レイアウト） |
| M-04 | 仕入先マスタ管理 | `/masters/suppliers`（固有拡張カラム）|
| M-05 | 連絡文章テンプレート管理 | `/masters/document-*` 3画面 |
| P-01 | 商品マスタ新規登録 | `/products/new` Step 1 |
| P-02 | サイズ展開 | `/products/new` Step 2 |
| P-03 | マルチ仕入先単価入力 | `/products/new` Step 3 + `/products/families/{id}` 内 |
| P-04 | 商品マスタ一覧・検索 | `/products` |
| P-05 | 商品マスタ詳細・修正 | `/products/families/{id}` |
| P-06 | 商品画像管理 | `/products/families/{id}` 内パネル |
| O-01 | 発注書作成（新規企画から）| `/orders/new?from_family={id}` |
| O-02 | 発注書作成（既存品番から）| `/orders/new`（既定モード） |
| O-03 | 発注書一覧・検索 | `/orders` |
| O-04 | 発注書修正・改訂 | `/orders/{id}` + `/orders/{id}/revisions/new` |
| O-05 | 発注中止 | `/orders/{id}` 内アクション |
| O-06 | 発注書 Excel 出力 | `/orders/{id}` 内アクション + `/orders/{id}/excel` ダウンロード |
| O-07 | 連絡文章選択 | `/orders/{id}` 内モーダル |

---

## 2. 共通レイアウト

### 2.1 認証後の標準レイアウト

```
┌─────────────────────────────────────────────────────────────┐
│ ヘッダ (h-14, sticky)                                        │
│  [LOGO] あけぼの本州           [Q 検索] [🔔][👤 ユーザ名 ▾] │
├──────────┬──────────────────────────────────────────────────┤
│ サイド    │  パンくず: ホーム > 商品 > 婦人サンダル A          │
│ ナビ      │  ┌────────────────────────────────────────────┐  │
│ (w-56)   │  │                                              │  │
│          │  │       メインコンテンツエリア                 │  │
│ 🏠 ホーム │  │                                              │  │
│ 👟 商品   │  │                                              │  │
│ 📝 発注   │  │                                              │  │
│ ⚙ マスタ │  │                                              │  │
│          │  └────────────────────────────────────────────┘  │
│          │  トーストエリア (右下、Reka UI Toast)              │
└──────────┴──────────────────────────────────────────────────┘
```

| 要素 | 実装 |
|---|---|
| ヘッダ | `<header>` sticky top-0、Reka UI `DropdownMenu` でユーザメニュー |
| サイドナビ | `<aside>`、現在ルートをハイライト、権限に応じてメニュー項目を表示制御（`v-if="hasPermission(...)"`） |
| パンくず | Reka UI `Breadcrumb`、Nuxt route の階層から自動生成 |
| トースト | Reka UI `Toast`、Pinia `ui` store からトリガ |
| アイコン | lucide-icons（`House`, `Footprints`, `FileText`, `Settings` 等）|

### 2.2 モバイル対応（CLAUDE.md 原則8）

| ブレークポイント | レイアウト |
|---|---|
| `lg` 以上 (1024px+) | サイドナビ常設 |
| `md` (768〜1023px) | サイドナビは Reka UI `Sheet`（ハンバーガーで開閉）、テーブル → 横スクロール |
| `sm` 以下 (〜767px) | サイドナビは Sheet、**P-04 / O-03 一覧はカードビュー固定**（テーブルは無効化）、フォームは 1 カラム縦並び |

> **MVP 想定:** 業務 LAN PC 利用が主（NFR §3）。モバイル対応は最低限の閲覧 + 簡易操作を担保（リーダー目線）。Phase 6 で業務担当者の利用シーンを再確認。

### 2.3 共通 UI 部品（components/ui/、Reka UI ラッパ）

| 部品 | 元 | 用途 |
|---|---|---|
| `Button` | `<button>` + Tailwind | プライマリ/セカンダリ/危険 3 variant |
| `Modal` | Reka UI `Dialog` | フォーカストラップ + ARIA |
| `ConfirmDialog` | Reka UI `AlertDialog` | 削除・中止等の確認 |
| `Toast` | Reka UI `Toast` | 成功/失敗通知（5秒自動消去）|
| `DataTable` | 自作 + Reka UI 部品 | ソート・ページング・列定義 |
| `Card` | div + Tailwind | P-04 カードビュー |
| `Combobox` | Reka UI `Combobox` | マスタ選択（オートコンプリート）|
| `Tabs` | Reka UI `Tabs` | 詳細画面のタブ |
| `Toast` | Reka UI `Toast` | エラーコード → 日本語メッセージ |
| `LoadingSpinner` | div + Tailwind animate | ローディング |
| `ErrorBoundary` | Nuxt `<NuxtErrorBoundary>` | 致命的エラーキャッチ |
| `EmptyState` | div + Tailwind + lucide-icon | データなし表示 |
| `FormField` | label + input + error 文 | フォーム部品の標準ラッパ（FluentValidation エラー連動） |

---

## 3. 画面ごとの機能定義

### 3.1 `/login` — ログイン

| 観点 | 内容 |
|---|---|
| 機能 ID | C-01 |
| 認証 | 不要 |
| レイアウト | 中央 1 カラム、ロゴ + フォーム + 注意文 |
| 入力 | Email, Password |
| アクション | Firebase JS SDK `signInWithEmailAndPassword` → 成功時 `POST /auth/sync` → `/` 遷移 |
| エラー | AUTH-001（ID 未登録）、AUTH-002（パスワード不一致）、AUTH-003（削除済ユーザ）、USR-001（Firebase 登録ありで RDS なし）、USR-002（無効化）|
| 表示 | エラーは form 下にインライン表示、回数超過時は Firebase Auth の `auth/too-many-requests` を「しばらくしてから再試行してください」と日本語化 |
| パスワードリセット | 「パスワードをお忘れの方」リンク → Firebase `sendPasswordResetEmail`（MVP は Email 送付のみ）|
| 表示要件 | エラーは `aria-live="polite"`、フォーカスは Email フィールドに初期配置 |

### 3.2 `/` — ホーム（ダッシュボード簡易版）

| 観点 | 内容 |
|---|---|
| 機能 ID | （横串）|
| レイアウト | カード 4 つ（最近の発注 / 進行中 Draft / 自分の担当 / 通知）|
| API | `GET /purchase-orders?sort=-updated_at&per_page=5`、`GET /purchase-orders?filter[status]=Draft&filter[orderer_user_id]=me`、他 |
| 権限 | 全権限ユーザに表示。権限なし機能はカード非表示 |
| MVP 簡易化 | カウントとリスト最初の 5 件のみ。グラフ・KPI は Post-MVP |

### 3.3 `/products` — 商品マスタ一覧・検索（P-04）

| 観点 | 内容 |
|---|---|
| 機能 ID | P-04 |
| 認可 | `product:read` |
| API | `GET /api/v1/products?q=...&filter[...]=...&sort=...&page=...&per_page=...` |
| ヘッダ部 | タイトル「商品マスタ」+ 件数バッジ + 検索ボックス + フィルタチップ + **ビュー切替トグル (Card / Table)** + 「+ 新規登録」ボタン（`product:write`）|
| 既定ビュー | **カード**（Phase 3 P-04 仕様） |
| ビュー保持 | セッション中保持（Pinia `ui` store）|
| カードビュー (P-04.a) | 1 行 4 カード（lg）/ 2 カード（md）/ 1 カード（sm）|
| テーブルビュー (P-04.b) | フラットなテーブル、列ソート可、デフォルト最終更新日降順 |
| カード内構成 | ヒーロー画像（前/次カルーセル + N/M インデクサ）+ サムネ帯 + 11桁品番 + 商品名 + サブテキスト + 3 指標（価格レンジ / SKU バリエ数 / 状態）|
| アクセシビリティ | サムネ帯 = `role="tablist"`、ナビボタン = `aria-label`、インデクサ = `aria-live="polite"`、全画像 `alt` 必須、矢印キー操作対応 |
| ページング | `per_page=50` 既定、無限スクロール / ボタン式は Phase 6 で確定（MVP はボタン式）|
| 行クリック / カードクリック | `/products/families/{familyId}` へ遷移 |
| 性能 | 初期表示 500ms (NFR §1.1)、N+1 回避は `data-design.md` §7.2 で確認 |

### 3.4 `/products/new` — 商品マスタ新規登録（P-01 / P-02 / P-03）

| 観点 | 内容 |
|---|---|
| 機能 ID | P-01 + P-02 + P-03（4ステップウィザード）|
| 認可 | `product:write` |
| 構造 | Reka UI `Stepper`（自作）で 4 ステップを横並び表示、現在ステップをハイライト |

#### Step 1: 基本情報（P-01）

| 要素 | 内容 |
|---|---|
| 入力 | 商品タイプ・季節・機能・ブランド・甲皮素材・中底素材・底素材・商品群・工場・商品名1/2 |
| UI | 各マスタは `Combobox`（オートコンプリート）、`material` は同じマスタを 3 つの Combobox で参照 |
| プレビュー | 上部に「仮品番プレビュー」エリア（リアルタイム更新、9桁の頭部分 + 4-6桁目は「---」表示） |
| API | 入力途中で `GET /products/families/{familyId}/preview-sku` は不要（フロントだけで構成可能、`item_conversion_code` をマスタレスポンスから取得済）|
| 次へボタン | 必須項目すべて入力完了で活性化 |
| エラー | PROD-002（必須項目未入力）|

#### Step 2: サイズ展開（P-02）

| 要素 | 内容 |
|---|---|
| 入力 | 色（複数チェック）、サイズ（複数チェック）。マスタの `delete_flag=false` のみ表示 |
| 表示 | 色×サイズマトリクスをグリッド表示（チェック → SKU 生成プレビュー）|
| アクション | 「F9 サイズ展開」相当のボタン → `POST /products/families/{id}/expand` |
| 結果 | 生成された SKU 一覧をテーブル表示 |
| 性能 | 50 SKU 生成で 500ms (NFR §1.1) |
| エラー | PROD-003（重複）、PROD-002（色/サイズ未指定）|

#### Step 3: 仕入先・単価（P-03、アイテム単位）

> **Phase 6 修正:** 仕入単価はアイテム (product_family) 単位で管理。色違い・サイズ違いの SKU はすべて同一単価。入力は **アイテム単位 × 仕入先数** のみ（通常 1〜3 行程度）。

| 要素 | 内容 |
|---|---|
| 入力 | **アイテム（product_family）に対して** 仕入先・単価・通貨・有効開始日・決定日のセット（複数可、行追加ボタン）|
| UI | DataTable 形式、行追加 / 削除アクション。仕入先 Combobox は `suppliers` から |
| API | 「保存」で各行 `POST /products/families/{familyId}/supplier-prices` を順次呼出（行数は通常 1-3 で軽量）|
| 認可 | `product:write` + `price:write` |
| エラー | PRICE-001（重複）、PRICE-002（金額不正）|
| 設計上の注記 | 旧設計（SKU 単位）では 50 SKU × 2 仕入先 = 100 行入力で大規模だったが、アイテム単位確定により通常 1-3 行に縮小。F-05 一括設定機能の必要性消滅 |

#### Step 4: 確認 → 登録（バルク登録）

> **Phase 6 修正:** F-06 対応で、Step 1〜3 の入力はクライアント側状態管理（Pinia store）のみとし、Step 4 の「登録」ボタンで **単一バルク API** `POST /api/v1/products/families/complete` を呼出して 1 トランザクション完結。途中失敗時の中途半端な DB データを排除。

| 要素 | 内容 |
|---|---|
| 表示 | Step 1〜3 の入力サマリ（企画情報・色×サイズ生成プレビュー・仕入単価一覧）|
| アクション | 「登録」→ `POST /api/v1/products/families/complete` を `Idempotency-Key` 付きで呼出 → 成功時 `/orders/new?from_family={id}` へ遷移、失敗時はエラー表示 + Step 1-3 入力データはクライアント側に保持（再試行可能）|
| キャンセル | 確認ダイアログ → 戻る（Step 1-3 入力データ破棄）|
| 中断時 | ブラウザ閉じ・タブ切替で Pinia store の入力データが消失するため、注意喚起（離脱確認ダイアログを `beforeunload` で実装）|
| エラーハンドリング | バリデーション失敗（422）は該当 Step へ自動戻り + フィールド別エラー表示。SKU 重複（409 PROD-003）はサーバ側で sequence_no 自動リトライ |

### 3.5 `/products/families/{familyId}` — 商品詳細・修正（P-05 + P-06）

| 観点 | 内容 |
|---|---|
| 機能 ID | P-05 + P-06 |
| 認可 | `product:read`（閲覧）/ `product:write`（編集）|
| API | `GET /api/v1/products/families/{familyId}` で family + products + images + supplier_prices を 1 リクエスト取得 |
| レイアウト | 上部: ヒーロー画像（カルーセル）+ 基本情報、下部: タブ（SKU 一覧 / 仕入単価 / 画像管理 / 履歴）|
| タブ「SKU 一覧」| 配下 SKU をテーブル表示、論理削除可（発注紐付ありの SKU は削除不可、PROD-004）|
| タブ「仕入単価」| 仕入先別 × 履歴を表示。`price:read` なしのユーザには金額マスク（"***"）|
| タブ「画像管理」(P-06) | 最大 5 枚、ドラッグ&ドロップアップロード、並び替え、代表画像指定 |
| タブ「履歴」| audit_logs の関連レコード（Post-MVP、MVP では非表示） |
| アクション | 「編集」（モーダル）/ 「論理削除」（ConfirmDialog）/ 「発注書作成」（既存品番から） |
| エラー | PROD-004（発注紐付ありで削除不可）|

#### 画像管理（P-06）詳細

| 操作 | UI / API |
|---|---|
| アップロード | ドラッグ&ドロップ or ファイル選択 → `POST /images/upload-url` → S3 PUT → `POST /images` |
| 並び替え | Reka UI `Sortable` でドラッグ → `PATCH /images/reorder` |
| 代表画像指定 | order_no=1 に並び替え |
| 削除 | カード上に × ボタン → `DELETE /images/{imageId}` |
| プレビュー | クリックで Modal で拡大表示 |
| 上限 | 5 枚（既存有効枚数を表示）|
| エラー | IMAGE-001（容量超過）、IMAGE-002（形式不正）、IMAGE-003（保存失敗）、IMAGE-004（上限超過）|

### 3.6 `/orders` — 発注書一覧・検索（O-03）

| 観点 | 内容 |
|---|---|
| 機能 ID | O-03 |
| 認可 | `purchase_order:read` |
| API | `GET /api/v1/purchase-orders?...` |
| ヘッダ部 | タイトル「発注書」+ 件数バッジ + 検索ボックス + フィルタチップ（期間・状態・取引先）+ ビュー切替（Card / Table）+ 「+ 新規」ボタン |
| 既定ビュー | **テーブル**（Phase 3 O-03 仕様、情報密度重視）|
| カードビュー (O-03.a) | ヘッダ帯（状態バッジ + 発注番号）+ 取引先名・納入日 + 担当者 + 明細品番数・仕入合計（マスク or 開示）・中止フラグ + サムネスタック |
| テーブルビュー (O-03.b) | 作成管理番号 / 発注番号 / 取引先 / 納品先 / 発注先 / 取引先納入日 / 明細品番数 / 状態 / 最終更新者 / 最終更新日 |
| 金額表示 | デフォルトマスク。`?include_amount=true` トグルで開示（`price:read` 保有時、audit_logs に `Price.View` 記録）|
| ソート | デフォルト最終更新日降順 |
| 行クリック | `/orders/{orderId}` へ遷移 |
| 性能 | 初期表示 500ms |

### 3.7 `/orders/new` — 発注書新規（O-01 / O-02）

| 観点 | 内容 |
|---|---|
| 機能 ID | O-01 + O-02 |
| 認可 | `purchase_order:write` |
| URL パラメータ | `?from_family={id}`（O-01: 新規企画から、明細を自動転記）/ なし（O-02: 既存品番から、検索選択モード）|
| レイアウト | 1 ページ縦長フォーム（ヘッダ情報 → 明細 → 連絡文章 → 確認）|

#### ヘッダ情報セクション

| 入力 | UI |
|---|---|
| 発注先（仕入先/工場）| `Combobox` ← suppliers |
| 納品先 | `Combobox` ← delivery_destinations、選択で `customer_name` が表示プレビューされる |
| 発注事業部 | `Combobox` ← departments |
| 納入倉庫 | `Combobox` ← warehouses |
| 取引先納入日 | `<input type="date">` |
| 発注担当者 | `Combobox` ← users |
| 副担当者 1〜6 | `Combobox` × 6（NULL 許容）|
| 発注管理者 | `Combobox` ← users |

#### 明細セクション

| モード | UI |
|---|---|
| O-01（from_family）| 配下 SKU 全件を自動展開、行ごとに数量入力 |
| O-02（既存品番）| 「+ 商品追加」ボタン → 商品検索モーダル（Combobox + 簡易検索）→ 行追加 |
| 共通 | 各行: 品番 / 商品名 / 仕入先（自動）/ 単価（自動引当て、マスクトグル）/ 数量 / 小計（GENERATED 列、フロント表示時はクライアント側で再計算）|
| 行操作 | 数量変更、行削除 |

#### 連絡文章セクション（O-07）

| 操作 | UI / API |
|---|---|
| 「テンプレートから選択」ボタン | モーダル → `GET /masters/document-text-purchases` 一覧表示 → 選択で `communication_text` に複写 |
| 「標準印字取込」ボタン | `?filter[standard_print_flag]=true` の全件を改行区切りで一括投入 |
| 自由編集 | テキストエリア、最大 6 行（Application 層で検証）|

#### 確認・登録

| アクション | API |
|---|---|
| 「下書き保存」| `POST /purchase-orders`（status=Draft、Idempotency-Key で 2重送信防止）|
| 「キャンセル」| ConfirmDialog → `/orders` へ戻る |
| エラー | ORDER-001（必須欠落）/ ORDER-002（unit_price 未設定）/ ORDER-004（過去日付）|

### 3.8 `/orders/{orderId}` — 発注書詳細・修正（O-04 + O-05 + O-06 + O-07）

| 観点 | 内容 |
|---|---|
| 機能 ID | O-04 + O-05 + O-06 + O-07 |
| 認可 | `purchase_order:read` / `purchase_order:write` |
| API | `GET /api/v1/purchase-orders/{id}` で全データ取得 |
| レイアウト | 上部: ステータスバッジ + 発注番号 + 主要メタ、下部: タブ（明細 / 履歴・改訂 / 連絡文章）|
| アクション領域 | 状態に応じてボタンを出し分け |

#### 状態別アクション

| 状態 | ボタン |
|---|---|
| Draft | 「修正」（PATCH /orders/{id}） / 「Excel 出力＝発注確定」（GET /orders/{id}/excel、Idempotency-Key） / 「中止」（POST /orders/{id}/cancel）|
| Submitted | 「改訂」（→ /orders/{id}/revisions/new）/ 「Excel 再出力」/ 「中止」 |
| Revised | 「Excel 再出力」/ 「中止」 |
| Cancelled | アクションなし（参照のみ）。「ステータス: 中止済」+ 理由表示 |

#### タブ「明細」

| 表示 | 内容 |
|---|---|
| 列 | 行番号 / 品番（snapshot）/ 商品名（snapshot）/ 仕入先（snapshot）/ 単価（snapshot、マスク or 開示）/ 数量 / 小計 / 通貨 |
| 編集 | Draft 時のみ、行追加 / 削除 / 数量変更 |
| サムネ | 行頭に商品代表画像（S3 Pre-signed）|

#### タブ「履歴・改訂」

| 表示 | 内容 |
|---|---|
| 改訂チェーン | 親 → 改訂版 1 → 改訂版 2 ... を縦並び、各版へリンク |
| 表示元 | `purchase_order_revisions` + audit_logs |

#### タブ「連絡文章」（O-07）

| 表示 | テキストエリア + 「テンプレートから選択」「標準印字取込」ボタン |
| 編集 | Draft 時のみ、`PATCH /orders/{id}` で `communication_text` 更新 |

#### 中止アクション（O-05）

| UI | ConfirmDialog「本当に中止しますか？」+ 理由入力（任意）+ 「中止する」ボタン |
| API | `POST /purchase-orders/{id}/cancel` |
| 副作用 | 状態が Cancelled に遷移、再開不可（業務ルール、§13 確認事項 S-1）|

#### Excel 出力アクション（O-06）

| UI | 「Excel 出力（発注確定）」ボタン → 確認ダイアログ |
| 警告 | 初回出力時は「この操作で発注番号が確定し、以降は改訂のみ可能になります」と明示 |
| API | `GET /api/v1/purchase-orders/{id}/excel` + `Idempotency-Key` ヘッダ |
| 完了 | ブラウザ標準ダウンロードダイアログ → 一覧の order_no と status が更新 |

### 3.9 `/orders/{orderId}/revisions/new` — 発注書改訂作成（O-04）

| 観点 | 内容 |
|---|---|
| 認可 | `purchase_order:write` |
| 構成 | `/orders/new` と同レイアウト、ただし初期値は親発注書から複写 |
| 入力 | revised_reason（改訂理由、任意）+ 通常の明細・連絡文章編集 |
| API | `POST /purchase-orders/{id}/revisions` |
| 完了 | 新発注書 ID で `/orders/{newId}` へ遷移、ステータス Draft（再度 Excel 出力で枝番採番 `Snnnn-01`） |

### 3.10 `/masters` — マスタ管理 hub

| 観点 | 内容 |
|---|---|
| 認可 | `master:read` |
| 表示 | 18 マスタを 4 つのカテゴリ（品番構成系 / 商品属性系 / 組織・取引先系 / 文書テンプレ系 / ユーザ）に分けてカード表示 |
| 各カード | マスタ名 + 件数バッジ + アイコン（lucide）+ クリックで個別画面へ |
| MVP 簡易化 | カード並びは固定（カスタマイズなし）|

### 3.11 `/masters/{master}` — マスタ個別画面（M-01 / M-02 共通テンプレート）

> 17マスタ + ユーザ = 18画面で共通実装。固有カラムは各画面でフォームを拡張。

| 観点 | 内容 |
|---|---|
| 機能 ID | M-01（一覧）+ M-02（編集）|
| 認可 | `master:read` / `master:write` |
| API | `GET/POST/PATCH/DELETE /api/v1/masters/{master}` |
| レイアウト | 上部: タイトル + 検索 + 「+ 新規」+ 「論理削除済を表示」トグル / 下部: DataTable |
| 編集 | 行クリック → 編集モーダル（Reka UI Dialog）|
| 新規 | ヘッダ「+ 新規」→ 同じモーダル（空フォーム）|
| 削除 | 行末アクション → ConfirmDialog → `DELETE /masters/{master}/{id}` |
| 復元 | 論理削除済を表示中のみ「復元」アクション → `POST /masters/{master}/{id}/restore` |
| バリデーション | コード重複 → MASTER-001、FK 不整合 → MASTER-002 |
| ソート | デフォルト `code` 昇順 |

#### 拡張ポイント（マスタ別）

| マスタ | 拡張カラム / 特殊 UI |
|---|---|
| sizes | `item_conversion_code` 入力 |
| suppliers (M-04) | `official_name`, `item_conversion_code`, `country_id` (Combobox), `supplier_type` (Select), `alert_target` |
| product_types | `item_conversion_code`, `size_demographic_code` (R/M/J) |
| product_seasons | `item_conversion_code`, `conversion_order`（カンマ区切り）|
| product_groups | `planning_fee` (numeric) |
| colors | `item_conversion_code` (2桁) |
| materials | `material_classification_id` (Combobox) |
| delivery_destinations | `customer_name`, `remark_1/2/3` |
| document_template_purchases | `name`（ラベル）+ `body`（テキストエリア、大）|
| document_template_confirmations | 同 + `standard_print_flag` (Checkbox) |
| document_text_purchases | 同 + `standard_print_flag` |

### 3.12 `/masters/users` — ユーザマスタ管理（M-03）

| 観点 | 内容 |
|---|---|
| 認可 | `user:read` / `user:write` |
| 一覧列 | 社員番号 / ユーザID / ユーザ名 / 品番台帳権限 / 発注書作成権限 / 発注情報権限 / 有効/無効 / 論理削除 |
| MVP UI 露出フィールド (Phase 3 §M-03 BR-07) | 7 件（社員番号, ユーザID, ユーザ名, 品番台帳権限, 発注書作成権限, 発注情報権限, 論理削除）|
| DB 保持 / UI 非表示 (3件) | 企画担当, 営業担当, 工程実績管理権限 |
| 新規追加 | Firebase Auth + RDS 両側に作成（API §2.2 POST /users）。初期パスワードを生成 → 画面に 1 回だけ表示（コピー可、再表示不可）|
| 権限変更 | 編集モーダル内、`PATCH /users/{id}/permissions` |
| 無効化 / 有効化 | 行末アクション、`POST /users/{id}/deactivate` / `/activate` |
| 論理削除 | 「削除」アクション、`DELETE /users/{id}` |
| 警告表示 | Firebase 同期失敗時（USR-003）は「Firebase Auth 側との同期に失敗しました。管理者に連絡してください」+ 失敗ログ参照リンク |

### 3.13 `/me` — 自身のプロフィール

| 観点 | 内容 |
|---|---|
| 認可 | 認証済 |
| 表示 | 自身の情報（display_name, employee_no, login_id, email, 権限）|
| アクション | 「パスワード変更」→ Firebase `sendPasswordResetEmail`（メール送付方式）|
| MVP 簡易化 | 設定変更（display_name 等）はマスタ管理者経由のみ。`/me` からの自己編集は Post-MVP |

---

## 4. レスポンシブ / アクセシビリティ要件

### 4.1 レスポンシブ（CLAUDE.md 原則8）

| 画面 | モバイル特殊化 |
|---|---|
| `/login` | フォーム幅を全幅に、padding 縮小 |
| `/products` | テーブルビュー無効化、カードビュー固定。`per_page=20` に縮小 |
| `/products/new` | Stepper を縦並び（垂直）、Step 2 の色×サイズグリッドは縦スクロール |
| `/products/families/{id}` | タブを Reka UI `Tabs` の `vertical` モードに切替 |
| `/orders` | テーブル → カードビュー固定 |
| `/orders/new` / `/orders/{id}` | 明細テーブルは横スクロール（または行展開）|
| `/masters` | カードグリッドを 1 列に |

### 4.2 アクセシビリティ（USABILITY_STANDARDS U-5 整合）

| 観点 | 実装 |
|---|---|
| キーボード操作 | Reka UI 標準で Tab / Shift+Tab / Enter / Esc / 矢印キーが全コンポーネントで動作 |
| フォーカス可視 | Tailwind `focus:ring-2 focus:ring-blue-500` を全 interactive 要素 |
| ARIA | カードビューのカルーセル（P-04.a 仕様準拠）、Toast の `aria-live`、エラーの `role="alert"` |
| コントラスト | WCAG AA（4.5:1）以上を Tailwind カラーパレットで担保 |
| スクリーンリーダー | 全フォーム要素に `<label>`、画像に `alt`、装飾アイコンに `aria-hidden="true"` |
| ロケール | 日本語のみ（Phase 2 確定）|

### 4.3 視覚デザイン共通

| 要素 | スタイル |
|---|---|
| カラーパレット | Tailwind 標準（gray, blue メイン、red 危険、green 成功、amber 警告）|
| タイポ | システムフォント（業務システムのため可読性優先）、見出しは `font-bold text-lg〜2xl` |
| 間隔 | Tailwind の 4px グリッド（`p-2`, `gap-4`, `space-y-6`）|
| 角丸 | `rounded-md` 統一、カードは `rounded-lg shadow-sm border` |
| アニメ | Reka UI 標準（Modal 開閉等）、過度なアニメは避ける（業務効率優先）|

---

## 5. 状態 / エラー / フィードバック

### 5.1 ローディング状態

| 種別 | UI |
|---|---|
| 一覧初期取得 | テーブル/カード全体に Skeleton 表示（Tailwind `animate-pulse`）|
| アクション処理中 | ボタン → スピナー + disabled |
| 画像アップロード | プログレスバー（S3 PUT 進捗）|

### 5.2 エラー表示

| エラー | UI |
|---|---|
| バリデーション (422) | 該当フォームフィールド下にインライン表示、`useApi` が ProblemDetails の `errors[]` を field 別に展開 |
| 認可 (403) | トースト「この操作の権限がありません」+ 元画面に留まる |
| 認証失効 (401) | トースト「セッションが切れました。再ログインしてください」→ `/login` 遷移 |
| 衝突 (409) | トースト + コードに応じた日本語（例: PROD-001「品番が重複しています」）|
| サーバエラー (500/503) | トースト「処理に失敗しました。時間をおいて再試行してください」+ trace_id 表示（サポート連絡用）|
| ネットワーク失敗 | トースト「ネットワークエラー」+ 再試行ボタン |

### 5.3 成功フィードバック

| 操作 | UI |
|---|---|
| 作成・更新・削除 | トースト（緑系）3 秒表示 |
| Excel 出力 | ダウンロード開始通知 + ダウンロード履歴に記録（ブラウザ標準）|
| 大量操作完了 | トースト + 件数表示（例: 「12 SKU を生成しました」）|

---

## 6. 全データフロー I/F 検証（architecture.md 5 シナリオ → 画面）

| シナリオ | 画面遷移 | 検証 |
|---|---|---|
| A. 商品マスタ登録 | `/products` → `/products/new` (Step 1〜4) → `/orders/new?from_family={id}` | ✅ ウィザード遷移 + URL パラメータでデータ連動 |
| B. 仕入単価設定 | `/products/families/{id}` → 「仕入単価」タブ → 行追加モーダル | ✅ 権限 `price:write` チェック + マスク表示 |
| C. 発注書作成 | `/orders/new` → ヘッダ + 明細 + 連絡文章 → 「下書き保存」→ `/orders/{id}` | ✅ Idempotency-Key 適用 |
| D. Excel 出力 | `/orders/{id}` → 「Excel 出力（発注確定）」→ 確認ダイアログ → ダウンロード | ✅ 初回採番警告 UI + 状態遷移可視化 |
| E. 権限変更 | `/masters/users` → 編集モーダル → 権限選択 → 保存 | ✅ Firebase 同期失敗時の USR-003 警告表示 |

---

## 7. I/F 設計 6 視点チェック（UI 層）

| # | 視点 | チェック結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ Nuxt 3 SPA + TailwindCSS + Reka UI + lucide で全画面実装可能 |
| 2 | ユースケース | ✅ UC-1〜UC-4 の全ステップが画面遷移として表現済（§6 で 5 シナリオ検証）|
| 3 | ユーザビリティ | ✅ カード/テーブル切替、Combobox オートコンプリート、スナップショット表示、エラーインライン化、レスポンシブ対応 |
| 4 | データ設計上の都合 | ✅ data-design.md のエンティティ粒度（family / products / images / supplier_prices）が画面構造（タブ）と整合 |
| 5 | 型の継承関係 | ✅ openapi-typescript で API レスポンス型を生成 → Pinia store / props の型は一貫 |
| 6 | データフロー整合性 | ✅ §6 で 5 シナリオの画面遷移と API 呼出順序が整合 |

---

## 8. ゲート条件チェック（Phase 5 全体）

| ゲート条件 | 達成箇所 | 状態 |
|---|---|---|
| サイトマップが作成されている | screen-design.md §1 | ✅ |
| 画面ごとの機能定義 | screen-design.md §3 | ✅（27画面全件）|
| I/F 設計が 6 視点チェック済み | architecture §7 + data-design §12 + api-design §6 + screen-design §7 | ✅（4層全て）|
| データ設計が正規化の原則に従っている | data-design §11 | ✅（非正規化は根拠記録）|
| API 設計に癒着がない | api-design §4 + §5 | ✅ |
| プロトタイプがダミーデータで動作 | architecture §4 + screen-design §6 のドキュメントトレース 5 シナリオ + api-design §5 の 10 主要フロー | ✅（オペレーター合意済の代替形式）|
| 全データフローが I/F レベルで検証済み | 各成果物の §4-§6 で網羅 | ✅ |

**Phase 5 ゲート 7 条件すべて PASS（事前自己評価）。オペレーターレビュー Phase5-Screen 完了後に Phase 5 全体クローズ。**

---

## 9. 設計上の確認事項（オペレーターレビュー Phase5-Screen）

| # | 論点 | 推奨案 |
|---|---|---|
| **S-1** | 発注中止後の **再開（中止解除）**可否 | 不可（推奨）。中止は最終アクション、業務上の意思決定の重みを保つ。再開が必要なら新規発注 |
| S-2 | `/products/new` の Step 4 完了後の自動遷移先（`/orders/new?from_family={id}`）| 採用（UC-1 直結）。ただし「発注書を作らずに戻る」ボタンも提供 |
| S-3 | カード/テーブル切替のセッション保持範囲 | Pinia `ui` store でセッション中保持。永続化（localStorage）は Phase 6 で要否判断 |
| S-4 | 仕入単価マスク → 開示トグル時の挙動 | `?include_amount=true` を URL に付加せず、ヘッダ X-Include-Amount で送信（履歴に残らない）。`price:read` 権限保有時のみトグル表示 |
| S-5 | ホーム画面のダッシュボード簡易版（カード 4 つ）| MVP は最小実装（最近の発注リスト + 自分の Draft + 通知のみ）。グラフは Post-MVP |
| S-6 | 監査ログ閲覧画面（C-03）の MVP 提供 | Post-MVP。MVP は audit_logs 記録のみで閲覧 UI なし |
| S-7 | Excel 出力時の「発注確定」警告表現 | 「この操作で発注番号が確定し、以降は改訂のみ可能になります」と明示（推奨）。誤操作防止 |
| S-8 | サブナビ表示制御（権限なし機能を非表示 vs グレーアウト）| **非表示**（推奨）。情報量削減 + 業務スコープ明確化 |
| S-9 | モバイル時の `/products/new` ウィザード | Stepper を縦配置 + Step 2 グリッド縦スクロール。実用性は Phase 6 で業務担当者検証 |
| S-10 | ローディング Skeleton vs スピナー | Skeleton（推奨）。レイアウトシフト回避 |

---

## 10. 変更履歴

| 日付 | 内容 |
|---|---|
| 2026-05-19 | 初版作成（27画面 + 共通レイアウト + レスポンシブ + アクセシビリティ + 5シナリオ画面トレース）|
