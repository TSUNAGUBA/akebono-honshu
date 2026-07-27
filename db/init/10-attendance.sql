-- Iteration 30: 勤怠管理・タイムカード 6 テーブル (akebono-office からの移植)
-- 前提: 01-schema.sql (tenant / users) 〜 09-updated-at-triggers.sql まで投入済
-- プラットフォーム統合規約: tenant_id (uuid) + RLS + TIMESTAMPTZ(UTC) + uuid PK + deleted_at
--
-- ■ 実行順序に関する注意 (本ファイル固有・重要)
--   db/init/*.sql は find|sort の番号順に適用されるため、本ファイル (10) は
--   08-tenancy-rls.sql (RLS 配線) と 09-updated-at-triggers.sql (updated_at トリガ配線) より
--   **後**に実行される。したがって新規 6 テーブルの RLS ポリシーと updated_at トリガは
--   08 / 09 の一括配線では拾われない。本ファイル末尾で自ら配線する (§4 / §5)。
--   08-tenancy-rls.sql 側にも同じ 6 テーブルを「存在する場合のみ適用」で追記してあり、
--   08 を単体で再実行した場合にも同じ状態に収束する (冪等)。
--
-- ■ 時刻の扱い (src/Backend/Domain/Common/SystemTime.cs が SoT)
--   打刻時刻 punch_records.at は TIMESTAMPTZ に **UTC** を格納する。
--   業務日付 punch_records.date / leave_*.date は **JST の日付** を DATE で持つ。
--   深夜帯 (22時〜5時) 判定・時刻表示はアプリ層で SystemTime.ToJst を通して行う。
--   (db/migration/iter4-tz-to-jst-naive.sql は廃止済みの旧方式。従わないこと)
--
-- ■ 記録系の保護 (CLAUDE.md 原則 2)
--   punch_records は **追記のみ**。訂正は打刻修正申請の承認による論理置換
--   (source=2(Fix) の行を追記し fixed_from に旧打刻の at を入れる) で行い、元打刻は削除しない。
--   そのため updated_at 列を持たず、アプリロールから UPDATE/DELETE を剥奪する (§3)。

SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (tenant_id 列の DEFAULT がこの GUC から解決される)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- ═════════════════════════════════════════════════
-- §1 テーブル定義
-- ═════════════════════════════════════════════════

-- ─────────────────────────────────────────────────
-- §1.1 attendance_rules — 勤務体系マスタ (所定労働時間・法定休日曜日・締め日・フレックス)
-- office の flex JSON は平坦化して列で持つ (jsonb を使わない)。
-- is_default はテナント内で高々 1 件 (排他制御は AttendanceRuleService 側)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS attendance_rules (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id              UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    name                   VARCHAR(128) NOT NULL,
    work_start             VARCHAR(5)   NOT NULL DEFAULT '09:00',   -- 'HH:mm' (JST 壁時計)
    work_end               VARCHAR(5)   NOT NULL DEFAULT '18:00',   -- 'HH:mm' (JST 壁時計)
    break_minutes          INTEGER      NOT NULL DEFAULT 60,        -- 所定休憩 (分)
    flex_enabled           BOOLEAN      NOT NULL DEFAULT FALSE,
    flex_core_start        VARCHAR(5)   NULL,                       -- コアタイム開始 'HH:mm'
    flex_core_end          VARCHAR(5)   NULL,                       -- コアタイム終了 'HH:mm'
    flex_settlement_months INTEGER      NOT NULL DEFAULT 1,         -- 清算期間 (月)
    closing_day            INTEGER      NOT NULL DEFAULT 31,        -- 締め日 (31=月末)
    legal_holiday_weekday  INTEGER      NOT NULL DEFAULT 0,         -- 法定休日 (0=日曜 〜 6=土曜)
    is_default             BOOLEAN      NOT NULL DEFAULT FALSE,
    is_active              BOOLEAN      NOT NULL DEFAULT TRUE,
    deleted_at             TIMESTAMPTZ  NULL,
    created_at             TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_ar_break_minutes          CHECK (break_minutes BETWEEN 0 AND 240),
    CONSTRAINT chk_ar_closing_day            CHECK (closing_day BETWEEN 1 AND 31),
    CONSTRAINT chk_ar_legal_holiday_weekday  CHECK (legal_holiday_weekday BETWEEN 0 AND 6),
    CONSTRAINT chk_ar_flex_settlement_months CHECK (flex_settlement_months BETWEEN 1 AND 3)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_attendance_rules_tenant_name
    ON attendance_rules (tenant_id, name) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_attendance_rules_tenant ON attendance_rules (tenant_id);

COMMENT ON TABLE attendance_rules IS '勤務体系マスタ (所定労働時間・法定休日曜日・締め日・フレックス)';

-- ─────────────────────────────────────────────────
-- §1.2 users への勤怠列 (db/migration/iter30-attendance.sql §1 と等価)
--
-- すべて末尾追加 + DEFAULT 付きで下位互換 (CLAUDE.md 原則7)。既存ユーザは
-- attendance_permission=1 / punch_required=TRUE により移行直後からそのまま打刻できる。
--   attendance_permission: 0=なし / 1=更新可能 / 2=参照のみ
--                          既存 4 権限と同じ **非単調スケール**。書込判定は必ず == 1 で行う
--                          (>= 1 は「参照のみ(2)」に書込を許してしまうバグ)
--   punch_required       : 打刻対象か (役員・外注等は false)
--   attendance_rule_id   : 個別に割り当てた勤務体系 (NULL=既定ルール)
--   hire_date            : 入社日 (有給の周期自動付与の起算日)
--   weekly_days/hours    : 週所定日数・時間 (有給の比例付与判定)
--
-- 01-schema.sql ではなく本ファイルで追加する理由:
--   (1) attendance_rule_id の FK 先 attendance_rules が本ファイルで作られる
--   (2) 列の並び順を iter30 適用後の既存 DB と一致させる (02-masters.sql の users 拡張列より後)
-- ─────────────────────────────────────────────────
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS attendance_permission SMALLINT     NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS punch_required        BOOLEAN      NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS attendance_rule_id    UUID         NULL,
    ADD COLUMN IF NOT EXISTS hire_date             DATE         NULL,
    ADD COLUMN IF NOT EXISTS weekly_days           NUMERIC(3,1) NOT NULL DEFAULT 5,
    ADD COLUMN IF NOT EXISTS weekly_hours          NUMERIC(4,1) NOT NULL DEFAULT 40;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_attendance_permission') THEN
        ALTER TABLE users ADD CONSTRAINT chk_users_attendance_permission
            CHECK (attendance_permission BETWEEN 0 AND 2);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_weekly_days') THEN
        ALTER TABLE users ADD CONSTRAINT chk_users_weekly_days
            CHECK (weekly_days BETWEEN 0 AND 7);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_weekly_hours') THEN
        ALTER TABLE users ADD CONSTRAINT chk_users_weekly_hours
            CHECK (weekly_hours BETWEEN 0 AND 168);
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_users_attendance_rule'
    ) THEN
        ALTER TABLE users
            ADD CONSTRAINT fk_users_attendance_rule
            FOREIGN KEY (attendance_rule_id) REFERENCES attendance_rules(id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_users_attendance_rule
    ON users (attendance_rule_id) WHERE attendance_rule_id IS NOT NULL;

-- ─────────────────────────────────────────────────
-- §1.3 punch_records — 打刻 (記録系・追記のみ)
-- kind: 0=In 1=Out 2=BreakStart 3=BreakEnd / source: 0=Web 1=Mobile 2=Fix
-- at は UTC 格納、date は JST の業務日付。updated_at 列は持たない (追記のみのため)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS punch_records (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    date                DATE         NOT NULL,                      -- JST の業務日付
    kind                SMALLINT     NOT NULL,
    at                  TIMESTAMPTZ  NOT NULL,                      -- UTC 格納
    source              SMALLINT     NOT NULL DEFAULT 0,
    fixed_from          TIMESTAMPTZ  NULL,                          -- 置換した旧打刻の at (UTC)
    fix_reason          VARCHAR(512) NULL,
    approved_by_user_id UUID         NULL REFERENCES users(id),
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_pr_kind   CHECK (kind BETWEEN 0 AND 3),
    CONSTRAINT chk_pr_source CHECK (source BETWEEN 0 AND 2)
);
CREATE INDEX IF NOT EXISTS idx_punch_records_user_date ON punch_records (tenant_id, user_id, date);
CREATE INDEX IF NOT EXISTS idx_punch_records_date      ON punch_records (tenant_id, date);

COMMENT ON TABLE punch_records IS '打刻 (記録系・追記のみ。訂正は source=2(Fix) の追記による論理置換で行い元打刻は削除しない)';

-- ─────────────────────────────────────────────────
-- §1.4 attendance_fix_requests — 打刻修正申請
-- status: 0=Pending 1=Approved 2=Rejected
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS attendance_fix_requests (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    date                DATE         NOT NULL,                      -- JST の業務日付
    kind                SMALLINT     NOT NULL,
    requested_at        TIMESTAMPTZ  NOT NULL,                      -- 修正後の打刻時刻 (UTC)
    reason              VARCHAR(512) NOT NULL,
    status              SMALLINT     NOT NULL DEFAULT 0,
    decided_by_user_id  UUID         NULL REFERENCES users(id),
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_afr_kind   CHECK (kind BETWEEN 0 AND 3),
    CONSTRAINT chk_afr_status CHECK (status BETWEEN 0 AND 2)
);
CREATE INDEX IF NOT EXISTS idx_afr_status    ON attendance_fix_requests (tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_afr_user_date ON attendance_fix_requests (tenant_id, user_id, date);

COMMENT ON TABLE attendance_fix_requests IS '打刻修正申請 (承認時に punch_records へ修正打刻を追記する)';

-- ─────────────────────────────────────────────────
-- §1.5 leave_types — 休暇種別マスタ
-- grant_method: 0=Periodic (周期自動付与) 1=Manual (手動付与)
-- is_statutory=true (法定有給) の種別は作成・編集・論理削除を禁止 (改竄防止、アプリ層で 409)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS leave_types (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    name          VARCHAR(64)  NOT NULL,
    grant_method  SMALLINT     NOT NULL DEFAULT 1,
    expiry_months INTEGER      NULL,                                -- NULL = 無期限
    is_statutory  BOOLEAN      NOT NULL DEFAULT FALSE,
    description   VARCHAR(255) NOT NULL DEFAULT '',
    display_order INTEGER      NOT NULL DEFAULT 1,
    is_active     BOOLEAN      NOT NULL DEFAULT TRUE,
    deleted_at    TIMESTAMPTZ  NULL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_lt_grant_method  CHECK (grant_method BETWEEN 0 AND 1),
    CONSTRAINT chk_lt_expiry_months CHECK (expiry_months IS NULL OR expiry_months BETWEEN 1 AND 120)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_leave_types_tenant_name
    ON leave_types (tenant_id, name) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_leave_types_tenant ON leave_types (tenant_id);

COMMENT ON TABLE leave_types IS '休暇種別マスタ (法定有給 is_statutory=true は変更不可)';

-- ─────────────────────────────────────────────────
-- §1.6 leave_grants — 休暇の付与 (個別 / 一括 / 周期自動)
-- kind: 0=Normal (通常付与) 1=Proportional (比例付与) 2=Special (特別付与)
-- 冪等制約 UNIQUE (tenant_id, user_id, leave_type_id, grant_date) が周期自動付与の
-- 二重実行を防ぐ (CLAUDE.md 原則2)。付与は挿入のみで既存行を更新・削除しない。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS leave_grants (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    leave_type_id       UUID         NOT NULL REFERENCES leave_types(id),
    grant_date          DATE         NOT NULL,                      -- JST の業務日付
    days                NUMERIC(4,1) NOT NULL,
    kind                SMALLINT     NOT NULL DEFAULT 2,
    expire_date         DATE         NOT NULL,                      -- 無期限は 9999-12-31
    granted_by_user_id  UUID         NULL REFERENCES users(id),     -- NULL = 周期自動付与
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_lg_days CHECK (days > 0),
    CONSTRAINT chk_lg_kind CHECK (kind BETWEEN 0 AND 2),
    CONSTRAINT uq_leave_grants_tenant_user_type_date
        UNIQUE (tenant_id, user_id, leave_type_id, grant_date)
);
CREATE INDEX IF NOT EXISTS idx_leave_grants_user ON leave_grants (tenant_id, user_id, leave_type_id);

COMMENT ON TABLE leave_grants IS '休暇の付与 (UNIQUE(tenant_id,user_id,leave_type_id,grant_date) が周期自動付与の冪等性を担保)';

-- ─────────────────────────────────────────────────
-- §1.7 leave_requests — 休暇申請 (1 行 = 1 日分)
-- unit: 0=Full (全日 1.0 日) 1=Half (半日 0.5 日)
-- status: 0=Pending 1=Approved 2=Rejected。残数を消化するのは Approved のみ。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS leave_requests (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    leave_type_id       UUID         NOT NULL REFERENCES leave_types(id),
    date                DATE         NOT NULL,                      -- JST の業務日付
    unit                SMALLINT     NOT NULL DEFAULT 0,
    status              SMALLINT     NOT NULL DEFAULT 0,
    reason              VARCHAR(255) NOT NULL DEFAULT '',
    decided_by_user_id  UUID         NULL REFERENCES users(id),
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_lr_unit   CHECK (unit BETWEEN 0 AND 1),
    CONSTRAINT chk_lr_status CHECK (status BETWEEN 0 AND 2)
);
CREATE INDEX IF NOT EXISTS idx_leave_requests_user_date ON leave_requests (tenant_id, user_id, date);
CREATE INDEX IF NOT EXISTS idx_leave_requests_status    ON leave_requests (tenant_id, status);

COMMENT ON TABLE leave_requests IS '休暇申請 (残数を消化するのは status=1(Approved) のみ)';

-- ═════════════════════════════════════════════════
-- §2 シード: 法定有給の休暇種別 (全テナント・冪等)
--   name='有給休暇', grant_method=0(Periodic), expiry_months=24 (時効2年), is_statutory=true
-- ═════════════════════════════════════════════════
DO $$
DECLARE
    t RECORD;
BEGIN
    FOR t IN SELECT tenant_id FROM tenant LOOP
        PERFORM set_config('app.tenant_id', t.tenant_id::text, true);

        INSERT INTO leave_types (tenant_id, name, grant_method, expiry_months, is_statutory, description, display_order)
        SELECT t.tenant_id, '有給休暇', 0, 24, TRUE, '労働基準法 39 条の年次有給休暇 (時効 2 年)', 1
        WHERE NOT EXISTS (
            -- ガードは不変な is_statutory を基準にする (name / deleted_at は可変)。
            -- name 基準にすると改名後の再実行で、deleted_at 条件を付けると DB 直操作で
            -- 論理削除した後の再実行で、それぞれ 2 行目が入る。既存 leave_grants が旧 id を
            -- 指したまま新 id が有効になり有給残数が分裂するうえ、部分 UNIQUE 索引
            -- uq_leave_types_tenant_name により復元 (LeaveService.RestoreTypeAsync) も 409 で塞がれる。
            SELECT 1 FROM leave_types
             WHERE tenant_id = t.tenant_id AND is_statutory
        );
    END LOOP;
END $$;

-- ═════════════════════════════════════════════════
-- §3 アプリロールへの権限付与
--   08-tenancy-rls.sql の GRANT ... ON ALL TABLES は実行時点のテーブルに対する
--   スナップショットのため、本ファイルで作成したテーブルには明示的に付与する。
--   punch_records は記録系 (追記のみ) のため UPDATE/DELETE を剥奪する (audit_logs と同方針)。
-- ═════════════════════════════════════════════════
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'akebono_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON
            attendance_rules, attendance_fix_requests, leave_types, leave_grants, leave_requests
            TO akebono_app;
        GRANT SELECT, INSERT ON punch_records TO akebono_app;
        REVOKE UPDATE, DELETE ON punch_records FROM akebono_app;
    END IF;
END $$;

-- ═════════════════════════════════════════════════
-- §4 RLS 配線 (本ファイルは 08-tenancy-rls.sql より後に実行されるため自ら配線する)
--   標準形: USING + WITH CHECK / FORCE ROW LEVEL SECURITY / GUC 未設定はフェイルクローズ
-- ═════════════════════════════════════════════════
DO $$
DECLARE
    t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'attendance_rules', 'punch_records', 'attendance_fix_requests',
        'leave_types', 'leave_grants', 'leave_requests'
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
-- §5 updated_at トリガ配線 (本ファイルは 09-updated-at-triggers.sql より後に実行されるため)
--   punch_records は updated_at 列を持たないため対象外 (追記のみの記録系)。
--   冪等: DROP TRIGGER IF EXISTS → CREATE TRIGGER。
-- ═════════════════════════════════════════════════
DO $$
DECLARE
    t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'attendance_rules', 'attendance_fix_requests',
        'leave_types', 'leave_grants', 'leave_requests'
    ]
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS %I ON public.%I',
                       'trg_' || t || '_set_updated_at', t);
        EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION set_updated_at()',
                       'trg_' || t || '_set_updated_at', t);
    END LOOP;
END $$;
