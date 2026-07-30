-- ════════════════════════════════════════════════════════════════════
-- Iteration 33: 勤怠承認経路 (承認経路 + 直行/直帰申請 + 打刻修正の多段承認) — akebono-office からの移植
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   akebono-office で勤怠管理に「承認経路 (各ステップの承認者を 役職/ロール/個人 から選ぶ)」と
--   「直行/直帰申請」が追加され、打刻修正申請も経路による多段承認へ拡張された。その勤怠部分を
--   honshu へ移植する。db/init/11-attendance-approval-routing.sql に反映済だが、db/init/*.sql は
--   「空 DB の初期化」でのみ適用される。既に init 済の本番 RDS には反映されないため、本
--   マイグレーション (action=migrate) で追加適用する。
--   ※ init 経路との差分検証は 11-attendance-approval-routing.sql と本ファイルの間で行うこと (原則5)。
--
-- 内容 (init 側 §1〜§8 と等価):
--   §1 users.title (役職) 列の追加 (NULL 許容の末尾追加)
--   §2 attendance_fix_requests へ current_step / direct_request_id の追加 + status CHECK を 0..3 へ拡張
--   §3 attendance_routes / attendance_route_steps の作成
--   §4 direct_requests / attendance_request_steps (経路スナップショット) の作成 + FK
--   §5 アプリロール権限
--   §6 RLS ポリシー配線 (標準形: USING + WITH CHECK + FORCE)
--   §7 updated_at トリガ配線
--   §8 デモ用の役職シード (全テナント・冪等)
--
-- 下位互換 (CLAUDE.md 原則7):
--   - users.title / attendance_fix_requests の追加列はすべて末尾追加 + NULL 許容 or NOT NULL DEFAULT。
--     既存行は current_step=1 (経路未設定 = 従来の管理者単段承認) / title=NULL に収束する。
--   - status CHECK を 0..2 → 0..3 へ広げる (既存の 0..2 の行は妥当なまま)。
--   - 既存テーブル・既存列・既存データは一切変更しない (追加・制約緩和のみ)。
--
-- 冪等性 (CLAUDE.md 原則2):
--   - ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS /
--     DROP CONSTRAINT IF EXISTS → ADD CONSTRAINT / DROP POLICY IF EXISTS → CREATE POLICY /
--     DROP TRIGGER IF EXISTS → CREATE TRIGGER。CHECK / FK は pg_constraint を見て未作成時のみ追加。
--   - §8 の役職シードは「未設定のときのみ」UPDATE で、既存の役職を上書きしない。
--
-- RLS 注意 (iter30 と同じ):
--   テナントスコープ表への UPDATE は FORCE ROW LEVEL SECURITY の対象になるため、tenant ごとに
--   app.tenant_id を設定してから行う (§8)。tenant 表は RLS 対象外。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。run-migrations.sh が
--   db/migration/*.sql を find|sort で自動探索し、schema_migrations 台帳で二重適用を防止する (前進専用)。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ════════════════════════════════════════════════════════════════════
-- §1 users.title (役職) 列
-- ════════════════════════════════════════════════════════════════════
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS title VARCHAR(64) NULL;

COMMENT ON COLUMN users.title IS '役職 (承認経路の approver_type=title が参照。NULL=未設定)';

-- ════════════════════════════════════════════════════════════════════
-- §2 attendance_fix_requests へ多段承認列 + status CHECK 拡張
-- ════════════════════════════════════════════════════════════════════
ALTER TABLE attendance_fix_requests
    ADD COLUMN IF NOT EXISTS current_step      INTEGER NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS direct_request_id UUID    NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_afr_current_step') THEN
        ALTER TABLE attendance_fix_requests
            ADD CONSTRAINT chk_afr_current_step CHECK (current_step >= 1);
    END IF;
END $$;

DO $$
BEGIN
    ALTER TABLE attendance_fix_requests DROP CONSTRAINT IF EXISTS chk_afr_status;
    ALTER TABLE attendance_fix_requests
        ADD CONSTRAINT chk_afr_status CHECK (status BETWEEN 0 AND 3);
END $$;

-- ════════════════════════════════════════════════════════════════════
-- §3 attendance_routes / attendance_route_steps
-- ════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS attendance_routes (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID        NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    category    SMALLINT    NOT NULL,                  -- 0=Direct 1=Fix
    is_active   BOOLEAN     NOT NULL DEFAULT TRUE,
    deleted_at  TIMESTAMPTZ NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_atr_category CHECK (category BETWEEN 0 AND 1)
);
CREATE INDEX IF NOT EXISTS idx_attendance_routes_category
    ON attendance_routes (tenant_id, category) WHERE deleted_at IS NULL;

COMMENT ON TABLE attendance_routes IS '勤怠承認経路 (区分ごとの多段承認経路。0=直行/直帰 1=打刻修正)';

CREATE TABLE IF NOT EXISTS attendance_route_steps (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        UUID        NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    route_id         UUID        NOT NULL REFERENCES attendance_routes(id) ON DELETE CASCADE,
    step_order       INTEGER     NOT NULL,
    approver_type    SMALLINT    NOT NULL,             -- 0=Title 1=Role 2=Member
    approver_role    SMALLINT    NULL,                 -- 0=Owner (approver_type=role のみ)
    approver_title   VARCHAR(64) NULL,                 -- 役職ラベル (approver_type=title のみ)
    approver_user_id UUID        NULL REFERENCES users(id),  -- 個人指定 (approver_type=member のみ)
    mode             SMALLINT    NOT NULL DEFAULT 0,   -- 0=Serial 1=All 2=Majority
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_ars_step_order    CHECK (step_order >= 1),
    CONSTRAINT chk_ars_approver_type CHECK (approver_type BETWEEN 0 AND 2),
    CONSTRAINT chk_ars_approver_role CHECK (approver_role IS NULL OR approver_role = 0),  -- 0=Owner (現状唯一)
    CONSTRAINT chk_ars_mode          CHECK (mode BETWEEN 0 AND 2),
    CONSTRAINT uq_ars_route_order     UNIQUE (route_id, step_order)
);
CREATE INDEX IF NOT EXISTS idx_ars_route ON attendance_route_steps (tenant_id, route_id);

COMMENT ON TABLE attendance_route_steps IS '勤怠承認経路のステップ定義 (承認者を 役職/ロール/個人 で指定)';

-- ════════════════════════════════════════════════════════════════════
-- §4 direct_requests / attendance_request_steps
-- ════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS direct_requests (
    id                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id            UUID         NOT NULL REFERENCES users(id),
    date               DATE         NOT NULL,                  -- JST の業務日付
    type               SMALLINT     NOT NULL,                  -- 0=Chokkou 1=Chokki 2=Both
    reason             VARCHAR(512) NOT NULL,
    status             SMALLINT     NOT NULL DEFAULT 0,        -- 0=Pending 1=InReview 2=Approved 3=Rejected 4=Withdrawn
    current_step       INTEGER      NOT NULL DEFAULT 1,
    decided_by_user_id UUID         NULL REFERENCES users(id),
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_dr_type         CHECK (type BETWEEN 0 AND 2),
    CONSTRAINT chk_dr_status       CHECK (status BETWEEN 0 AND 4),
    CONSTRAINT chk_dr_current_step CHECK (current_step >= 1)
);
CREATE INDEX IF NOT EXISTS idx_direct_requests_user_date ON direct_requests (tenant_id, user_id, date);
CREATE INDEX IF NOT EXISTS idx_direct_requests_status    ON direct_requests (tenant_id, status);

COMMENT ON TABLE direct_requests IS '直行/直帰申請 (承認された日は打刻修正を申請できる。多段承認)';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_afr_direct_request') THEN
        ALTER TABLE attendance_fix_requests
            ADD CONSTRAINT fk_afr_direct_request
            FOREIGN KEY (direct_request_id) REFERENCES direct_requests(id);
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS idx_afr_direct_request
    ON attendance_fix_requests (direct_request_id) WHERE direct_request_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS attendance_request_steps (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        UUID        NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    request_kind     SMALLINT    NOT NULL,             -- 0=Direct 1=Fix
    request_id       UUID        NOT NULL,             -- soft reference (2 テーブルを跨ぐため FK 無し)
    step_order       INTEGER     NOT NULL,
    approver_type    SMALLINT    NOT NULL,
    approver_role    SMALLINT    NULL,
    approver_title   VARCHAR(64) NULL,
    approver_user_id UUID        NULL REFERENCES users(id),
    mode             SMALLINT    NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_aqs_request_kind  CHECK (request_kind BETWEEN 0 AND 1),
    CONSTRAINT chk_aqs_step_order    CHECK (step_order >= 1),
    CONSTRAINT chk_aqs_approver_type CHECK (approver_type BETWEEN 0 AND 2),
    CONSTRAINT chk_aqs_approver_role CHECK (approver_role IS NULL OR approver_role = 0),  -- 0=Owner (現状唯一)
    CONSTRAINT chk_aqs_mode          CHECK (mode BETWEEN 0 AND 2),
    CONSTRAINT uq_aqs_request_order   UNIQUE (request_kind, request_id, step_order)
);
CREATE INDEX IF NOT EXISTS idx_aqs_request
    ON attendance_request_steps (tenant_id, request_kind, request_id);

COMMENT ON TABLE attendance_request_steps IS '申請時に凍結する承認経路スナップショット (直行/直帰・打刻修正 共通。soft reference)';

-- ════════════════════════════════════════════════════════════════════
-- §5 アプリロール (akebono_app) への権限付与
-- ════════════════════════════════════════════════════════════════════
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'akebono_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON
            attendance_routes, attendance_route_steps, direct_requests, attendance_request_steps
            TO akebono_app;
    END IF;
END $$;

-- ════════════════════════════════════════════════════════════════════
-- §6 RLS 配線 (db/init/08-tenancy-rls.sql と同一の標準形)
-- ════════════════════════════════════════════════════════════════════
DO $$
DECLARE
    t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'attendance_routes', 'attendance_route_steps', 'direct_requests', 'attendance_request_steps'
    ]
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %I '
            'USING (tenant_id = (NULLIF(current_setting(''app.tenant_id'', TRUE), ''''))::uuid) '
            'WITH CHECK (tenant_id = (NULLIF(current_setting(''app.tenant_id'', TRUE), ''''))::uuid)',
            t);
    END LOOP;
END $$;

-- ════════════════════════════════════════════════════════════════════
-- §7 updated_at トリガ配線 (db/init/09-updated-at-triggers.sql と同じ方式)
--   *_steps は挿入時凍結で updated_at を持たないため対象外。
-- ════════════════════════════════════════════════════════════════════
DO $$
DECLARE
    t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY['attendance_routes', 'direct_requests']
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS %I ON public.%I',
                       'trg_' || t || '_set_updated_at', t);
        EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION set_updated_at()',
                       'trg_' || t || '_set_updated_at', t);
    END LOOP;
END $$;

-- ════════════════════════════════════════════════════════════════════
-- §8 デモ用の役職シード (全テナント・冪等。未設定のときのみ設定し既存の役職を上書きしない)
-- ════════════════════════════════════════════════════════════════════
DO $$
DECLARE
    t RECORD;
BEGIN
    FOR t IN SELECT tenant_id FROM tenant LOOP
        PERFORM set_config('app.tenant_id', t.tenant_id::text, true);
        UPDATE users SET title = '代表取締役'
         WHERE tenant_id = t.tenant_id AND login_id = 'owner'   AND title IS NULL;
        UPDATE users SET title = 'マネージャー'
         WHERE tenant_id = t.tenant_id AND login_id = 'planner' AND title IS NULL;
    END LOOP;
END $$;

COMMIT;
