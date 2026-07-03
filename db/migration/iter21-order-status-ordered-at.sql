-- ════════════════════════════════════════════════════════════════════
-- Iteration 21: 発注状態 4 値モデル — purchase_orders へ ordered_at / ordered_by_user_id 追加 (§3b)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   発注状態を 4 値 (未発注 / 発注済 / 発注中止 / 発注削除) に再設計する。従来は「発注済 (正式発注済)」を
--   Excel 出力 (first_exported_at) の有無から導出していたが、要件により「ダウンロードでは状態を変えず、
--   ユーザー操作でのみ状態を変更する」。そのため「発注済」を出力とは独立した明示列 ordered_at で持つ。
--     - ordered_at: 発注を「発注済にする」操作で SET、「未発注に戻す」で NULL。出力 (first_exported_at) とは独立。
--     - ordered_by_user_id: 発注済にした操作者 (users.id)。cancelled_by_user_id と同じ scalar 配置。
--   旧「納品完了 (delivered_at)」状態は廃止 (列は後方互換のため保持するが、状態導出・UI では使用しない)。
--
--   これは db/init/04-orders.sql の CREATE TABLE に反映済だが、db/init/*.sql は「空 DB の初期化」でのみ
--   適用されるため、既に init 済の本番 RDS には本マイグレーション (action=migrate) で ALTER TABLE により
--   追加適用する。新規列のみ = 既存データ非破壊。
--
-- 既存データの移行 (壁打ち結果: 既存の出力済み発注は「発注済」へ一度だけ移行):
--   これまで「初回出力済 = 正式発注済」と見なしていたため、first_exported_at が設定済の発注 (= 従来
--   発注済相当) に ordered_at = first_exported_at を一度だけ埋め、現状の見え方を維持する。
--   発注中止 (status=1) / 発注削除 (is_deleted) は導出優先順位が上のため、ordered_at を埋めても表示は変わらない。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。run-migrations.sh が
--   db/migration/*.sql を find|sort で自動探索し、schema_migrations 台帳で二重適用を防止する (前進専用)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS でガード。再実行しても追加のみで既存データ・スキーマ非破壊。
--   - データ移行 UPDATE は `ordered_at IS NULL` でガードするため、再実行しても既に埋めた値・
--     ユーザーが手動変更した値を上書きしない (原則 2 状態保護)。
--   - 下位互換 (原則7): ordered_at / ordered_by_user_id は NULL 許容の新規列。first_exported_at・
--     delivered_at 等の既存列は一切変更しない。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── purchase_orders 発注状態 4 値モデル列追加 (NULL 許容、ADD COLUMN IF NOT EXISTS で冪等)。 ──
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS ordered_at         TIMESTAMP NULL;                      -- 発注済日時 (NULL=未発注)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS ordered_by_user_id BIGINT    NULL REFERENCES users(id); -- 発注済にした操作者

-- ── 既存の出力済み発注を「発注済」へ一度だけ移行 (壁打ち結果)。ordered_at IS NULL でガード = 冪等・状態保護。 ──
UPDATE purchase_orders
   SET ordered_at         = first_exported_at,
       ordered_by_user_id = updated_by_user_id
 WHERE first_exported_at IS NOT NULL
   AND ordered_at IS NULL;

COMMIT;
