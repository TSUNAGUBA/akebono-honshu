-- Iteration 33: 勤怠承認経路 (承認経路 + 直行/直帰申請 + 打刻修正の多段承認) — akebono-office からの移植
-- 前提: 01-schema.sql (tenant / users) 〜 10-attendance.sql (勤怠 6 テーブル) まで投入済
-- プラットフォーム統合規約: tenant_id (uuid) + RLS + TIMESTAMPTZ(UTC) + uuid PK + deleted_at
--
-- ■ 背景 (なぜ必要か)
--   akebono-office で勤怠管理に「承認経路 (承認経路の各ステップの承認者を 役職/ロール/個人 から選ぶ)」
--   と「直行/直帰申請」が追加され、打刻修正申請も経路による多段承認へ拡張された。
--   本ファイルはその勤怠部分を honshu へ移植する。office は稟議 (ringi) でも同じ経路モデルを使うが
--   honshu に稟議は無いため、勤怠のみを対象とする。
--
-- ■ office との対応 (踏襲) と honshu への適応
--   - 承認者の指定方法 approver_type = 役職(title) / ロール(role) / 個人(member)。office の
--     PermissionRule.subjectKind と同じ 3 種。
--   - 個人(member) = users.id。役職(title) = users.title (本ファイルで users へ追加する新列)。
--   - ロール(role) は honshu の権限モデルへ写像する。office の admin ロールは honshu の
--     「オーナー (勤怠管理者)」= process_record_permission >= 1 かつ 勤怠権限 1/2 に相当する
--     (AuthEndpoints.CheckAttendanceAdminAsync。office の hr 中間ロールは honshu に無いため作らない)。
--     したがって approver_role は当面 0=Owner の 1 値のみ (列挙は将来拡張の余地を残す)。
--   - office は steps を jsonb で持つが、honshu は jsonb を使わない方針のため、経路ステップ /
--     申請時に凍結する経路スナップショット (route_snapshot) を子テーブルとして正規化して持つ。
--
-- ■ 実行順序 (本ファイル固有・重要)
--   db/init/*.sql は番号順に適用されるため、本ファイル (11) は 08-tenancy-rls.sql / 09 の
--   一括配線より **後** に実行される。新規 4 テーブルの RLS ポリシー・updated_at トリガ・
--   アプリロール権限は 08/09 では拾われないため、本ファイル末尾で自ら配線する (§5〜§7)。
--   08-tenancy-rls.sql 側にも同じ 4 テーブルを「存在する場合のみ適用」で追記してあり、
--   08 を単体で再実行した場合にも同じ状態に収束する (冪等)。
--
-- ■ 時刻の扱い (src/Backend/Domain/Common/SystemTime.cs が SoT)
--   direct_requests.date は **JST の業務日付** を DATE で持つ。created_at / updated_at は
--   TIMESTAMPTZ に UTC を格納する (10-attendance.sql と同方針)。
--
-- ■ 記録系の保護 / 下位互換 (CLAUDE.md 原則2・原則7)
--   - attendance_fix_requests への追加列 (current_step / direct_request_id) はすべて DEFAULT 付き /
--     NULL 許容の末尾追加。既存行は current_step=1 (経路未設定 = 従来の管理者単段承認) に収束する。
--   - status CHECK を 0..2 → 0..3 へ広げ in_review(3) を許可する。既存の 0..2 の行は妥当なまま。
--   - users.title は NULL 許容の末尾追加。既存ユーザは NULL (役職未設定) のままで壊れない。

SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (tenant_id 列の DEFAULT がこの GUC から解決される)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- ═════════════════════════════════════════════════
-- §1 users への役職 (title) 列
--   承認経路の approver_type='title' (役職) が参照する。NULL 許容 = 役職未設定。
--   01-schema.sql ではなく本ファイルで追加する理由: 勤怠承認経路機能の一部であり、
--   init 経路と migration 経路 (iter33) で列の並び順を揃えるため (10-attendance.sql と同方針)。
-- ═════════════════════════════════════════════════
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS title VARCHAR(64) NULL;

COMMENT ON COLUMN users.title IS '役職 (承認経路の approver_type=title が参照。NULL=未設定)';

-- ═════════════════════════════════════════════════
-- §2 attendance_fix_requests へ多段承認列を追加
--   current_step     : 経路承認の現在ステップ (1..n)。経路未設定時は 1 (管理者単段フォールバック)。
--   direct_request_id : 直行/直帰申請との紐付け (直行/直帰起因の打刻修正のみ設定。NULL=通常の修正)。
--   下位互換: 追加列のみ + status CHECK を広げるだけ。既存行・既存列は変更しない。
-- ═════════════════════════════════════════════════
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

-- status に in_review(3) を許可する。既存 CHECK (0..2) を張り替える (冪等: 名前で判定)。
DO $$
BEGIN
    ALTER TABLE attendance_fix_requests DROP CONSTRAINT IF EXISTS chk_afr_status;
    ALTER TABLE attendance_fix_requests
        ADD CONSTRAINT chk_afr_status CHECK (status BETWEEN 0 AND 3);
END $$;

-- ═════════════════════════════════════════════════
-- §3 attendance_routes — 勤怠承認経路 (区分ごとの承認経路。金額帯は持たない)
--   category: 0=Direct (直行/直帰申請) 1=Fix (打刻修正申請)
--   同一区分に複数の有効経路がある場合はステップ数が多い経路を採用する (AttendanceRouteResolver)。
--   論理削除 + 復元に対応 (勤務体系マスタ attendance_rules と同方針)。
-- ═════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS attendance_routes (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID        NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    category    SMALLINT    NOT NULL,
    is_active   BOOLEAN     NOT NULL DEFAULT TRUE,
    deleted_at  TIMESTAMPTZ NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_atr_category CHECK (category BETWEEN 0 AND 1)
);
CREATE INDEX IF NOT EXISTS idx_attendance_routes_category
    ON attendance_routes (tenant_id, category) WHERE deleted_at IS NULL;

COMMENT ON TABLE attendance_routes IS '勤怠承認経路 (区分ごとの多段承認経路。0=直行/直帰 1=打刻修正)';

-- ─────────────────────────────────────────────────
-- §3.1 attendance_route_steps — 承認経路のステップ定義
--   approver_type: 0=Title(役職) 1=Role(ロール) 2=Member(個人)
--   approver_role: 0=Owner (勤怠管理者。approver_type=role のときのみ有効。NULL 可)
--   approver_title: 役職ラベル (approver_type=title のときのみ有効。NULL 可)
--   approver_user_id: 個人指定の users.id (approver_type=member のときのみ有効。NULL 可)
--   mode: 0=Serial 1=All 2=Majority (office 踏襲。解決は現状 Serial 単承認者として扱う)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS attendance_route_steps (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        UUID        NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    route_id         UUID        NOT NULL REFERENCES attendance_routes(id) ON DELETE CASCADE,
    step_order       INTEGER     NOT NULL,
    approver_type    SMALLINT    NOT NULL,
    approver_role    SMALLINT    NULL,
    approver_title   VARCHAR(64) NULL,
    approver_user_id UUID        NULL REFERENCES users(id),
    mode             SMALLINT    NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_ars_step_order    CHECK (step_order >= 1),
    CONSTRAINT chk_ars_approver_type CHECK (approver_type BETWEEN 0 AND 2),
    CONSTRAINT chk_ars_approver_role CHECK (approver_role IS NULL OR approver_role = 0),  -- 0=Owner (現状唯一)
    CONSTRAINT chk_ars_mode          CHECK (mode BETWEEN 0 AND 2),
    CONSTRAINT uq_ars_route_order     UNIQUE (route_id, step_order)
);
CREATE INDEX IF NOT EXISTS idx_ars_route ON attendance_route_steps (tenant_id, route_id);

COMMENT ON TABLE attendance_route_steps IS '勤怠承認経路のステップ定義 (承認者を 役職/ロール/個人 で指定)';

-- ═════════════════════════════════════════════════
-- §4 direct_requests — 直行/直帰申請 (承認系)
--   type:   0=Chokkou(直行) 1=Chokki(直帰) 2=Both(直行直帰)
--   status: 0=Pending 1=InReview 2=Approved 3=Rejected 4=Withdrawn
--   承認された日について打刻修正 (出勤/退勤) を申請できるようになる (AttendanceService の AKO-ATT-005 ゲート)。
--   経路 (category=0) で多段承認。経路未設定時は管理者単段 (スナップショット 0 件)。
-- ═════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS direct_requests (
    id                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id            UUID         NOT NULL REFERENCES users(id),
    date               DATE         NOT NULL,                  -- JST の業務日付
    type               SMALLINT     NOT NULL,
    reason             VARCHAR(512) NOT NULL,
    status             SMALLINT     NOT NULL DEFAULT 0,
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

-- attendance_fix_requests.direct_request_id の soft reference 先が確定したので FK を張る
-- (§2 で列を追加した時点では direct_requests 未作成のため、ここで張る)。
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

-- ─────────────────────────────────────────────────
-- §4.1 attendance_request_steps — 申請時に凍結する経路スナップショット (直行/直帰・打刻修正 共通)
--   申請作成時に有効な経路のステップをコピーして凍結する (以後の経路編集は進行中の申請に影響しない)。
--   request_kind: 0=Direct(direct_requests) 1=Fix(attendance_fix_requests)
--   request_id: 申請の id (2 つのテーブルを跨ぐため FK は張らず soft reference とする。
--               申請と同一トランザクションで挿入し、request_kind とセットで一意に解決する)。
--   列の意味は attendance_route_steps と同一 (承認者 役職/ロール/個人 + mode)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS attendance_request_steps (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        UUID        NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    request_kind     SMALLINT    NOT NULL,
    request_id       UUID        NOT NULL,
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

-- ═════════════════════════════════════════════════
-- §5 アプリロール (akebono_app) への権限付与
--   すべて設定系・申請系 (追記のみの記録系ではない) のため SELECT/INSERT/UPDATE/DELETE を付与する
--   (08-tenancy-rls.sql の GRANT ... ON ALL TABLES は実行時点のスナップショットのため本ファイルで明示付与)。
-- ═════════════════════════════════════════════════
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'akebono_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON
            attendance_routes, attendance_route_steps, direct_requests, attendance_request_steps
            TO akebono_app;
    END IF;
END $$;

-- ═════════════════════════════════════════════════
-- §6 RLS 配線 (本ファイルは 08-tenancy-rls.sql より後に実行されるため自ら配線する)
--   標準形: USING + WITH CHECK / FORCE ROW LEVEL SECURITY / GUC 未設定はフェイルクローズ
-- ═════════════════════════════════════════════════
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

-- ═════════════════════════════════════════════════
-- §7 updated_at トリガ配線 (本ファイルは 09-updated-at-triggers.sql より後に実行されるため)
--   updated_at 列を持つ設定系 (attendance_routes) / 申請系 (direct_requests) のみが対象。
--   *_steps は挿入時凍結で updated_at を持たないため対象外。
-- ═════════════════════════════════════════════════
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

-- ═════════════════════════════════════════════════
-- §8 デモ用の役職シード (全テナント・冪等)
--   承認経路の approver_type=title (役職) を体験できるよう、既存デモユーザに役職を設定する。
--   記録系ではないが、明示的に「未設定のときのみ」設定して既存の役職を上書きしない (原則2)。
-- ═════════════════════════════════════════════════
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
