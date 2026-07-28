-- ════════════════════════════════════════════════════════════════════
-- Iteration 31: 打刻修正申請に「対象打刻」を追加 (C-2) — attendance_fix_requests.target_punch_id
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   打刻修正の承認は従来「同種の先頭 1 件」を置換対象にしていた (C-2)。同種の打刻が複数ある日
--   (休憩を複数回とった日など) では 2 回目以降を直せず、2 回目を直そうとすると 1 回目が
--   無効化される。punch_records は UPDATE/DELETE を剥奪 (追記のみ) しているため巻き戻せない。
--   申請時に「どの打刻を直すか」を指定できるよう、対象打刻の id を保持する列を追加する。
--
--   db/init/10-attendance.sql には target_punch_id を含めた形で反映済 (空 DB の初期化用)。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate) で追加する。
--   init 経路との差分検証は 10-attendance.sql と本ファイルの間で行うこと (原則5)。
--
-- 内容 (init 側と等価):
--   attendance_fix_requests に target_punch_id UUID NULL を追加する (soft reference。FK は張らない)。
--   NULL 許容のため既存行はそのまま (承認時に「同種の先頭 1 件」へフォールバック = 下位互換)。
--
-- 冪等性 (原則2): ADD COLUMN IF NOT EXISTS。再実行しても既存データを壊さない。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する
--   (ACTION=migrate deploy/db/run-migrations.sh)。run-migrations.sh が db/migration/*.sql を
--   find|sort で自動探索し、schema_migrations 台帳で二重適用を防止する (前進専用)。
--   psql メタコマンドを使っていないため pgAdmin 等 GUI からもそのまま実行できる。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

ALTER TABLE attendance_fix_requests
    ADD COLUMN IF NOT EXISTS target_punch_id UUID NULL;

COMMIT;
