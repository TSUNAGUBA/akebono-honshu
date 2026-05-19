# Phase 7 Index

> **作成日:** 2026-05-19
> **状態:** Phase 6 暫定完了を受けて Phase 7 着手準備中、Iteration 0 (基盤構築) 開始可

## ドキュメント一覧

| ファイル | 最終更新 | 概要 |
|---------|---------|------|
| iteration-plan.md | 2026-05-19 | Iteration 分割計画 (0-4)、各 Iteration のスコープ・ゲート条件・期間目安、Post-MVP 計画 |
| pre-iteration-tasks.md | 2026-05-19 | Iteration 1 着手前タスク (T-1 不要化済 F-21 廃止 / T-2 国内用 Excel サンプル調達、画像 3 枚受領済 / .xlsx は Iteration 1 開始 1 週間前まで) |

## Phase 7 進行状態

- **現在ステータス:** **Iteration 0 完了 (2026-05-19)**、Iteration 1 (認証 + マスタ管理基盤) 着手可
- **MVP スコープ:** Phase 3 §1 21 機能 (C-01〜03, M-01〜05, P-01〜06, O-01〜07)
- **目安期間:** 約 9-13 週間 (約 2-3 ヶ月)、Iteration 0-4 の 5 段階
- **品質運用:** 各 Iteration 完了時に独立 2 ロール (コードレビュアー + システム監査官) 反復レビュー (CLAUDE.md 原則 9 / SP-8)

## Iteration 概要

| Iteration | 期間 | 機能数 | 主要スコープ |
|---|---|---|---|
| 0 | **完了 2026-05-19** | – | ローカル開発環境 (PostgreSQL + .NET 8 Backend + Nuxt 3 Frontend + ダミー認証 + ログイン/ユーザ一覧)、オペレーター環境で疎通確認済 |
| 1 | 2-3 週間 | 8 | 認証 (C-01〜03、ローカルダミー認証で動作) + マスタ管理 (M-01〜05) |
| 2 | 2-3 週間 | 6 | 商品マスタ (P-01〜06) |
| 3 | 3 週間 | 7 | 発注書 (O-01〜07、Excel 出力含む、MVP のクリティカルパス) |
| 4 | 2-3 週間 | – | Hardening + **AWS インフラ構築 + Firebase 本番認証切替** + CI/CD + UAT 準備 |

## Iteration 1 着手前運用残タスク

| # | 内容 | 期限 / 担当 |
|---|---|---|
| H-3 (T-2) | 国内用 Excel サンプル .xlsx 取得 (画像 3 枚は受領済) | Iteration 1 開始 1 週間前まで / オペレーター |
| H-1 / H-2 | Post-Phase6 実フィードバックセッション調整・実施 | Phase 7 と並行 / オペレーター |
| H-7 | キーパーソン承認取得 (Phase 6 正式完了印) | Phase 7 ゲート前 / オペレーター |

## Phase 6 確定事項の Iteration への振り分け

| Phase 6 確定 | 対象 Iteration | 適用箇所 |
|---|---|---|
| F-06 単一バルク登録 | Iteration 2 | P-01〜03 統合エンドポイント |
| F-10 状態モデル 2 値 + 出力バッジ | Iteration 3 | O-03 一覧 + O-04 編集 |
| F-11 Excel 出力概念廃止 | Iteration 3 | O-06 設計 |
| F-12 MVP は ① 国内用のみ | Iteration 3 | O-06 テンプレ |
| F-14 色×サイズマトリクスダイアログ | Iteration 3 | O-02 UI |
| F-16 編集理由 5 値 Enum 必須 + ORDER-005 | Iteration 3 | O-04 編集ダイアログ |
| F-18 FK ネスト返却 + IMasterUsage | Iteration 1 | M-01 共通テンプレート + suppliers/materials |
| F-20 usage API + 削除ダイアログ | Iteration 1 | M-01 削除フロー |
| F-21 F-key 廃止 → 画面ボタン | Iteration 1 以降全体 | 全 UI |
| F-22 supplier.official_name + 御中 + code + 発注印手押し | Iteration 1 (準備) + Iteration 3 (実装) | M-04 マスタ + O-06 帳票 |
| F-15 audit_logs.changes JSONB データ基盤 | Iteration 1 | C-03 監査ログ + Iteration 3 O-04 編集 |
| F-07 / F-15 / F-23 / F-24 | – | Post-MVP (Later カテゴリ) |

## 関連ドキュメント

- Phase 6 (暫定完了): `.ai-native/outputs/phase6/{_index,feedback-log}.md`
- Phase 5 設計: `.ai-native/outputs/phase5/{architecture,data-design,api-design,screen-design}.md`
- Phase 3 機能要件: `.ai-native/outputs/phase3/functional-requirements.md`
- 方法論 Phase 7 定義: `.ai-native/methodology/common/phase-definitions.md`
