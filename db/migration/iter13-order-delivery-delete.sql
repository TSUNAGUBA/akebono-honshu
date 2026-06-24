-- ════════════════════════════════════════════════════════════════════
-- Iteration 13: 発注状態 5 値モデル (#3a) — purchase_orders へ納品完了/発注削除 列を追加
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   発注状態を従来の 3 値 (発注依頼中 / 正式発注済 / 発注中止) から 5 値へ拡張する。
--   新設する 2 状態の導出列を purchase_orders に追加する:
--     - 納品完了: delivered_at IS NOT NULL (正式発注済の発注を「納品完了にする」操作で SET)
--     - 発注削除: is_deleted = TRUE (論理削除。物理削除はしない)
--   付随する監査列 (delivered_by_user_id / deleted_at / deleted_by_user_id) も追加する。
--   これらは db/init/04-orders.sql の CREATE TABLE に反映済だが、db/init/*.sql は
--   「空 DB の初期化」(run-migrations.sh action=init / docker 初回起動) でのみ適用される。
--   既に init 済の本番 RDS には反映されないため、本マイグレーション (action=migrate)
--   で ALTER TABLE により追加適用する。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し (ハードコード無し)、
--   schema_migrations 台帳で二重適用を防止する (前進専用)。本ファイルは glob 探索で
--   自動的に対象となるため、ランナー側・csproj 側への登録は不要
--   (iter4〜iter12 と同方式。MIG-3 のみ LegacyImportService が実行時参照するため
--    csproj に EmbeddedResource 登録されているが、スキーマ系 iter*.sql は登録しない)。
--   ※ pgAdmin 等 GUI で手動適用する場合も本ファイルをそのまま実行可能 (\ir 等の
--      psql メタコマンドは使用していない)。
--
-- 冪等性 (CLAUDE.md 原則 2 / 7):
--   - 列追加は ADD COLUMN IF NOT EXISTS、制約 (FK) 追加は pg_constraint を引いた DO ブロックで
--     ガードしているため、再実行しても既存データ・既存スキーマを破壊しない (追加のみ)。
--   - is_deleted は NOT NULL DEFAULT FALSE。既存行は自動的に FALSE (=未削除) が入り、
--     下位互換を維持する (既存発注書は従来どおり一覧に表示される)。
--   - delivered_at / delivered_by_user_id / deleted_at / deleted_by_user_id は NULL 許容。
--     既存行は NULL のまま残り、納品完了/発注削除には該当しない。
--   - 既存 CHECK 制約 chk_po_cancelled_consistency ((status=1)=(cancelled_at IS NOT NULL)) は
--     status / cancelled_at のみ参照しており、本マイグレーションの追加列とは独立。影響なし。
--   注: schema_migrations 台帳の checksum は本ファイル内容に対するもの。後から本ファイルを
--      書き換えても前進専用方針のため再適用されない。スキーマの後続変更は新しい
--      マイグレーションファイルを追加して対応すること (init 側 04-orders.sql が SoT)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── purchase_orders 列追加 (is_deleted のみ NOT NULL DEFAULT FALSE、他は NULL 許容) ──
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS delivered_at         TIMESTAMP    NULL;                    -- 納品完了日時 (NULL=未納品)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS delivered_by_user_id BIGINT       NULL;                    -- 納品完了操作者
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS is_deleted           BOOLEAN      NOT NULL DEFAULT FALSE;  -- 論理削除フラグ (TRUE=発注削除)
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS deleted_at           TIMESTAMP    NULL;                    -- 論理削除日時
ALTER TABLE purchase_orders ADD COLUMN IF NOT EXISTS deleted_by_user_id   BIGINT       NULL;                    -- 論理削除操作者

-- ── 外部キー (納品完了操作者 / 論理削除操作者 → users)。pg_constraint を引いて未存在の
--    ときのみ追加 (冪等)。NULL 許容のため任意参照、cascade なし
--    (cancelled_by_user_id REFERENCES users(id) と同じ任意参照ポリシー)。 ──
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_delivered_by') THEN
        ALTER TABLE purchase_orders
            ADD CONSTRAINT fk_po_delivered_by
            FOREIGN KEY (delivered_by_user_id) REFERENCES users(id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_deleted_by') THEN
        ALTER TABLE purchase_orders
            ADD CONSTRAINT fk_po_deleted_by
            FOREIGN KEY (deleted_by_user_id) REFERENCES users(id);
    END IF;

    -- is_deleted と deleted_at の整合 CHECK (chk_po_cancelled_consistency の踏襲)。
    -- 既存行は is_deleted=FALSE / deleted_at=NULL のため FALSE=(NULL IS NOT NULL)=FALSE で合致し、
    -- ALTER ADD CONSTRAINT の既存行検証を通過する (追加時に失敗しない)。
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_po_deleted_consistency') THEN
        ALTER TABLE purchase_orders
            ADD CONSTRAINT chk_po_deleted_consistency
            CHECK (is_deleted = (deleted_at IS NOT NULL));
    END IF;
END $$;

-- ── 部分索引: 後方互換の「非中止・未削除」一覧パス (ListAsync の includeCancelled=false 分岐:
--    WHERE status=0 AND is_deleted=FALSE) を高速化。新フロントは includeCancelled=true で全状態を
--    取得し client-side 絞込するため使わない。init 側 04-orders.sql と同一定義。 ──
CREATE INDEX IF NOT EXISTS idx_po_not_deleted
    ON purchase_orders (created_at DESC) WHERE is_deleted = FALSE;

COMMIT;
