# Phase 5 Index

## ドキュメント一覧

| ファイル | 最終更新 | 概要 | 行数 |
|---------|---------|------|---|
| architecture.md | 2026-07-27 | アーキテクチャ設計 (デプロイ構成 / レイヤー / 5シナリオ ドキュメントトレース / 横断的関心事 / **5 権限ポリシー**) | 489 |
| data-design.md | 2026-07-28 | データ設計 (18マスタ + 商品4 + 発注3 + 監査1 + **勤怠6** = **32テーブル**, 正規化, 機密度, ボリューム見積) | 956 |
| api-design.md | 2026-07-28 | API 設計 (21機能 × REST エンドポイント + **勤怠 27 エンドポイント (§2.7)**, 共通規約, 10主要フロー検証) | 1729 |
| screen-design.md | 2026-07-28 | 画面設計 (サイトマップ + **29画面** + 共通レイアウト + レスポンシブ + アクセシビリティ) | 1282 |
| legacy-field-parity.md | 2026-06-23 | 旧システム(/refference 189列等) ↔ 現画面 項目パリティ分析。欠落項目の棚卸しと Phase A〜D 段階導入計画。autocomplete化・副担当者・為替レート追加を記録 | (本ファイル作成済) |
| legacy-field-spec.md | 2026-06-23 | 旧システム完全項目仕様（オペレーター提供キャプチャ由来）。商品マスタ/発注(国内・海外)の全項目・マスタ確定値の実装 SoT。Phase A〜D に確定値を付与 | (本ファイル作成済) |

> **2026-07-27 集計更新（Iteration 30: 勤怠管理・タイムカードの移植）:**
>
> | 集計 | 更新前 | 更新後 | 内訳 |
> |---|---|---|---|
> | テーブル数 | 26 | **32** | 18マスタ + 商品4 + 発注3 + 監査1 + **勤怠6**（`attendance_rules` / `punch_records` / `attendance_fix_requests` / `leave_types` / `leave_grants` / `leave_requests`。`data-design.md §14`）|
> | 画面数 | 27 | **29** | 初版 27 + **勤怠 2**（`/attendance`（8 タブ）/ `/attendance/timecard`。`screen-design.md §1.2`）|
> | 権限カテゴリ | 4 | **5** | 初版 4 + **勤怠 `attendance_permission`**（`data-design.md §3.18`）|
> | 勤怠 API | – | **27 本** | `api-design.md §2.7`（#1〜#14 打刻・集計・修正申請・勤怠ルール / #15〜#27 休暇）|
>
> 勤怠 6 テーブルは `users` への列追加（勤怠列 6 本）を伴う。DDL の SoT は `db/init/10-attendance.sql`。
> 生産管理・販売管理等の拡張モジュールの集計は `*-production.md` 側で別管理（本表の対象外）。

## Phase 状態

- **現在ステータス:** COMPLETED (全レビュー観点 36件すべてクローズ、Phase 5 確定)
  - **Iteration 30 (勤怠移植、2026-07-27) で登載した残課題**（`screen-design.md §3.16` が SoT）:
    - **決着済み（2026-07-27 オペレーター判断）:** **C-1**（日跨ぎ夜勤の退勤打刻不可）= 夜勤運用が無いため
      **制約として受け入れ** / **OD-1**（BOM 空上書きによるデータ消失）= **記録のみ**（起票せず、
      BOM 改修時の申し送りとして残す）
    - **決着済み（2026-07-28 オペレーター判断・対応完了）:**
      **C-2**（打刻修正の対象が同種の先頭 1 件に固定）= **対応済み**（Iteration 31。`attendance_fix_requests.target_punch_id`
      を追加し対象打刻を選べるようにした。無指定は先頭フォールバックで下位互換）/
      **C-3**（締め日・フレックスが集計に未反映）= **制約として受け入れ ＋ 説明文訂正済み**（記録用の設定である旨を
      設定タブ・ルール編集モーダルに明記。集計は暦月固定のまま）/
      **C-4**（週 40 時間超の法定外残業が未計上）= **実装済み**（労基法標準。週 40h 超を法定外残業へ計上・
      日 8h 超と二重計上なし・36 協定へ反映）/
      **OD-2**（発注書テンプレの「（選択しない）」で無言上書き）= **修正済み**（`eb6b778`。`if (optionValue === '') return`）
- **進行方針:** 順次進行 (architecture → data → api → screen) + ドキュメント上のトレース検証 (Phase5 着手時にオペレーター合意)
- **ゲート判定:** 7条件すべて PASS (オペレーターレビュー反映済)
- **次フェーズ:** Phase 6 (プロトタイプベースのフィードバック)
  - サイトマップ作成 ✅ (screen §1)
  - 画面ごとの機能定義 ✅ (screen §3, **29画面**。初版 27 + 勤怠 2)
  - I/F 設計 6 視点チェック ✅ (4成果物の各 §で実施)
  - データ設計正規化 ✅ (data §11, 非正規化は根拠記録)
  - API 設計に癒着なし ✅ (api §4)
  - プロトタイプ動作 ✅ (ドキュメントトレース 5+10 シナリオ, 合意済代替形式)
  - 全データフロー I/F 検証 ✅ (各成果物 §4-6)

## オペレーターレビュー進捗

| 観点 | クローズ済 | 残 |
|---|---|---|
| Phase5-Arch (Arch-1〜Arch-6) | 全6件「進めてください」で確定 | 0 |
| Phase5-Data (D-1〜D-10) | 全10件「すべて推奨案で OK」で確定 | 0 |
| Phase5-Api (API-1〜API-10) | 全10件「すべて推奨案で OK」で確定 | 0 |
| Phase5-Screen (S-1〜S-10) | 全10件「すべて推奨案で OK」で確定 | 0 |
| **合計** | **36 / 36 件クローズ** | **0** |

## 確定事項サマリ

### アーキテクチャ (Arch-1〜Arch-6 確定)
- Vertical Slice + 軽量レイヤード採用
- DbContext per-request Scoped
- 状態管理: Pinia (グローバル) + Composable (ローカル)
- プロトタイプ検証シナリオ 5件で十分
- preview 環境のバック側は Phase 5 後半 or Phase 7 で確定
- アーキ図フォーマット (ASCII vs Mermaid) は README 化時判断

### データ設計 (D-1〜D-10 確定)
- 取引先 = delivery_destinations.customer_name で対応 (独立マスタ追加せず)
- ~~purchase_order_revisions 新設~~ → **Phase 6 で廃止**（状態モデル簡素化により改訂概念廃止）。代わりに purchase_order_export_logs 新設
- ~~is_cancelled と status=Cancelled 二重保持~~ → **Phase 6 で解消**（status を Active/Cancelled の 2 値に統一、is_cancelled 削除）
- users.is_deleted と is_active 二重保持 (意味が異なる)
- 商品画像 S3 物理削除は 90日後 Lifecycle
- 仕入単価 pgcrypto 採否は Phase 5 後半で再評価
- パーティショニングは audit_logs のみ
- 文書テンプレートは name (ラベル) + body (本文) に分離
- enum は SMALLINT + Application 層解釈
- EF Core Migration は per-PR 1 Migration + script レビュー

### 画面設計 (S-1〜S-10 確定)
- 発注中止後の再開不可 (最終アクション)
- /products/new Step 4 → /orders/new 自動遷移 (戻るボタン併設)
- カード/テーブル切替は Pinia セッション中保持 (localStorage は Phase 6 で判断)
- 仕入単価マスク開示は X-Include-Amount ヘッダ + price:read 権限保有時のみトグル
- ホームのダッシュボードは MVP 最小 (リスト + 未出力発注 + 通知)、グラフは Post-MVP（Phase 6 で「Draft」を「未出力」に変更）
- 監査ログ閲覧 UI は Post-MVP
- ~~Excel 出力時の発注確定警告を明示~~ → **Phase 6 で解消**（発注確定概念廃止、初回出力時のみ業務通知ダイアログに変更）
- サブナビは権限なし機能を非表示
- モバイル時 /products/new は Stepper 縦配置 (Phase 6 で実用性検証)
- ローディング表現は Skeleton

### API 設計 (API-1〜API-10 確定)
- URL バージョニング /api/v1/
- Problem Details (RFC 7807) + Phase 3 §10 エラーコード
- マスタ CRUD ジェネリックテンプレート (IMaster)
- 商品詳細 1リクエスト方針
- 発注一覧 total_amount デフォルトマスク
- Excel 出力時の発注番号採番 (GET + 副作用) + Idempotency-Key
- OpenAPI YAML は Phase 5 後半に Swashbuckle で自動生成
- Idempotency-Key 必須: POST orders, confirm, GET excel
- レート制限は MVP なし、Phase 7 で WAF 検討
- API ドキュメント は stg で Swagger UI 社内公開、prod 非公開

## 主要キーワード

Phase 5 基本設計, 29画面 (初版27 + 勤怠2), 18マスタ + 商品4 + 発注3 + 監査1 + 勤怠6 = 32テーブル, 21機能 REST エンドポイント + 勤怠27エンドポイント, 5権限カテゴリ (初版4 + 勤怠), サイトマップ, Vertical Slice, 軽量レイヤード, EF Core 8 per-request Scoped, Nuxt 3 SPA, TailwindCSS, Reka UI Headless, lucide-icons, Pinia (UI/auth), Composable (ローカル状態), Firebase Auth + RDS users SoT 分離, Custom Claims キャッシュ, Reconciler 日次, KMS Storage Encryption, 仕入単価マスク監査, S3 Pre-signed URL 画像 5MB×5枚, RFC 7807 Problem Details, AUTH-NNN/PROD-NNN/ORDER-NNN/PRICE-NNN, OpenAPI 3.0 + Swashbuckle, openapi-typescript 型生成, Idempotency-Key 採番系冪等性, /api/v1/ URL バージョニング, マスタ CRUD ジェネリック (IMaster), Step ウィザード P-01〜P-03, P-04 カードビュー ARIA tablist, O-03 デフォルトテーブル, 発注スナップショット (customer_name/sku/product_name/unit_price), 改訂枝番 Snnnn-NN, 月次パーティション audit_logs, S3 Glacier IR 3年, レスポンシブ (sm カード固定/md Sheet/lg サイドナビ), アクセシビリティ WCAG AA, 5シナリオ ドキュメントトレース (A〜E), 10主要フロー API 検証 (A〜J)
