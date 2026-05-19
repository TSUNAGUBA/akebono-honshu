# Phase 5 Index

## ドキュメント一覧

| ファイル | 最終更新 | 概要 | 行数 |
|---------|---------|------|---|
| architecture.md | 2026-05-19 | アーキテクチャ設計 (デプロイ構成 / レイヤー / 5シナリオ ドキュメントトレース / 横断的関心事) | 460 |
| data-design.md | 2026-05-19 | データ設計 (18マスタ + 商品4 + 発注3 + 監査1 = 26テーブル, 正規化, 機密度, ボリューム見積) | 656 |
| api-design.md | 2026-05-19 | API 設計 (21機能 × REST エンドポイント, 共通規約, 10主要フロー検証) | 942 |
| screen-design.md | 2026-05-19 | 画面設計 (サイトマップ + 27画面 + 共通レイアウト + レスポンシブ + アクセシビリティ) | (本ファイル作成済) |

## Phase 状態

- **現在ステータス:** COMPLETED (全レビュー観点 36件すべてクローズ、Phase 5 確定)
- **進行方針:** 順次進行 (architecture → data → api → screen) + ドキュメント上のトレース検証 (Phase5 着手時にオペレーター合意)
- **ゲート判定:** 7条件すべて PASS (オペレーターレビュー反映済)
- **次フェーズ:** Phase 6 (プロトタイプベースのフィードバック)
  - サイトマップ作成 ✅ (screen §1)
  - 画面ごとの機能定義 ✅ (screen §3, 27画面)
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
- purchase_order_revisions 新設
- is_cancelled と status=Cancelled 二重保持
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
- ホームのダッシュボードは MVP 最小 (リスト + Draft + 通知)、グラフは Post-MVP
- 監査ログ閲覧 UI は Post-MVP
- Excel 出力時の発注確定警告を明示
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

Phase 5 基本設計, 27画面, 18マスタ + 商品4 + 発注3 + 監査1 = 26テーブル, 21機能 REST エンドポイント, サイトマップ, Vertical Slice, 軽量レイヤード, EF Core 8 per-request Scoped, Nuxt 3 SPA, TailwindCSS, Reka UI Headless, lucide-icons, Pinia (UI/auth), Composable (ローカル状態), Firebase Auth + RDS users SoT 分離, Custom Claims キャッシュ, Reconciler 日次, KMS Storage Encryption, 仕入単価マスク監査, S3 Pre-signed URL 画像 5MB×5枚, RFC 7807 Problem Details, AUTH-NNN/PROD-NNN/ORDER-NNN/PRICE-NNN, OpenAPI 3.0 + Swashbuckle, openapi-typescript 型生成, Idempotency-Key 採番系冪等性, /api/v1/ URL バージョニング, マスタ CRUD ジェネリック (IMaster), Step ウィザード P-01〜P-03, P-04 カードビュー ARIA tablist, O-03 デフォルトテーブル, 発注スナップショット (customer_name/sku/product_name/unit_price), 改訂枝番 Snnnn-NN, 月次パーティション audit_logs, S3 Glacier IR 3年, レスポンシブ (sm カード固定/md Sheet/lg サイドナビ), アクセシビリティ WCAG AA, 5シナリオ ドキュメントトレース (A〜E), 10主要フロー API 検証 (A〜J)
