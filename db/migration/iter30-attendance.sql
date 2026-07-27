-- ════════════════════════════════════════════════════════════════════
-- Iteration 30: 勤怠管理・タイムカード (akebono-office からの移植) — 6 テーブル + users 拡張
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   akebono-office の勤怠機能 (打刻・勤怠集計・36 協定アラート・打刻修正申請・休暇) を
--   honshu へ移植する。db/init/10-attendance.sql (users 拡張列 + 6 テーブル + シード +
--   RLS + トリガ) に反映済だが、db/init/*.sql は「空 DB の初期化」
--   (run-migrations.sh action=init / docker 初回起動) でのみ適用される。既に init 済の
--   本番 RDS には反映されないため、本マイグレーション (action=migrate) で追加適用する。
--   ※ users の勤怠 6 列も 10-attendance.sql §1.2 で追加される。01-schema.sql に
--     ALTER TABLE users は無く、10 で追加する旨の案内コメントがあるだけである。
--     init 経路との差分検証は 10-attendance.sql と本ファイルの間で行うこと (原則5)。
--
-- 内容 (init 側と等価):
--   §1 users への勤怠列 6 本の追加 (すべて DEFAULT 付き = 既存行は自動で妥当な値になる)
--   §2 attendance_rules / punch_records / attendance_fix_requests /
--      leave_types / leave_grants / leave_requests の作成
--   §3 users.attendance_rule_id → attendance_rules(id) の FK
--   §4 法定有給 (有給休暇) の休暇種別シード (全テナント)
--   §5 アプリロール権限 (punch_records は追記のみ = UPDATE/DELETE を剥奪)
--   §6 RLS ポリシー配線 (標準形: USING + WITH CHECK + FORCE)
--   §7 updated_at トリガ配線
--
-- 時刻の扱い (src/Backend/Domain/Common/SystemTime.cs が SoT):
--   punch_records.at は TIMESTAMPTZ に **UTC** を格納する。業務日付 (date / grant_date 等) は
--   **JST の日付** を DATE で持つ。深夜帯 (22時〜5時) 判定・表示はアプリ層で JST 変換して行う。
--   ※ db/migration/iter4-tz-to-jst-naive.sql は廃止済みの旧方式であり、本ファイルは従わない。
--
-- 下位互換 (CLAUDE.md 原則7):
--   - users への追加列はすべて末尾追加 + NOT NULL DEFAULT。既存行は DEFAULT で埋まるため
--     データ更新パッチは不要 (attendance_permission=1 / punch_required=TRUE により、
--     既存ユーザは移行直後からそのまま打刻できる)。
--   - 既存テーブル・既存列・既存データは一切変更しない (追加のみ)。
--
-- 冪等性 (CLAUDE.md 原則 2):
--   - ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS /
--     DROP POLICY IF EXISTS → CREATE POLICY / DROP TRIGGER IF EXISTS → CREATE TRIGGER。
--   - CHECK 制約と FK は pg_constraint を見て未作成のときだけ追加する。
--   - シードは NOT EXISTS ガード付き INSERT。再実行しても既存の休暇種別を更新・削除しない。
--   - 記録系 (punch_records / leave_grants) への UPDATE・DELETE は本ファイルでは一切行わない。
--
-- RLS 注意 (iter26 / iter29 と同じ):
--   テナントスコープ表へのデータ投入は FORCE ROW LEVEL SECURITY の対象になるため、
--   tenant ごとに app.tenant_id を設定してから行う (tenant_id 列の DEFAULT もこの GUC から解決)。
--   tenant 表は RLS 対象外 (レジストリ投影・読取のみ)。
--
-- 適用方法 (自動・推奨):
--   GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行する。
--   run-migrations.sh が db/migration/*.sql を find|sort で自動探索し、schema_migrations 台帳で
--   二重適用を防止する (前進専用)。psql メタコマンド (\ir 等) は使っていないため
--   pgAdmin 等 GUI からそのまま実行することもできる。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ════════════════════════════════════════════════════════════════════
-- §1 users への勤怠列 (db/init/10-attendance.sql §1.2 と等価)
--   attendance_permission: 0=なし / 1=更新可能 / 2=参照のみ
--     既存 4 権限と同じ **非単調スケール**。書込判定は必ず == 1 で行うこと
--     (>= 1 は「参照のみ(2)」に書込を許してしまうバグ)。
--   管理系 (全員のタイムカード・承認・設定・付与) は既存のオーナー権限
--   process_record_permission >= 1 に集約する (新しい列は増やさない)。
-- ════════════════════════════════════════════════════════════════════
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

-- ════════════════════════════════════════════════════════════════════
-- §2 テーブル作成 (db/init/10-attendance.sql と等価)
-- ════════════════════════════════════════════════════════════════════

-- ── attendance_rules — 勤務体系マスタ ──
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

-- ── punch_records — 打刻 (記録系・追記のみ。updated_at 列を持たない) ──
CREATE TABLE IF NOT EXISTS punch_records (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    date                DATE         NOT NULL,                      -- JST の業務日付
    kind                SMALLINT     NOT NULL,                      -- 0=In 1=Out 2=BreakStart 3=BreakEnd
    at                  TIMESTAMPTZ  NOT NULL,                      -- UTC 格納
    source              SMALLINT     NOT NULL DEFAULT 0,            -- 0=Web 1=Mobile 2=Fix
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

-- ── attendance_fix_requests — 打刻修正申請 ──
CREATE TABLE IF NOT EXISTS attendance_fix_requests (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    date                DATE         NOT NULL,                      -- JST の業務日付
    kind                SMALLINT     NOT NULL,
    requested_at        TIMESTAMPTZ  NOT NULL,                      -- 修正後の打刻時刻 (UTC)
    reason              VARCHAR(512) NOT NULL,
    status              SMALLINT     NOT NULL DEFAULT 0,            -- 0=Pending 1=Approved 2=Rejected
    decided_by_user_id  UUID         NULL REFERENCES users(id),
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_afr_kind   CHECK (kind BETWEEN 0 AND 3),
    CONSTRAINT chk_afr_status CHECK (status BETWEEN 0 AND 2)
);
CREATE INDEX IF NOT EXISTS idx_afr_status    ON attendance_fix_requests (tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_afr_user_date ON attendance_fix_requests (tenant_id, user_id, date);

COMMENT ON TABLE attendance_fix_requests IS '打刻修正申請 (承認時に punch_records へ修正打刻を追記する)';

-- ── leave_types — 休暇種別マスタ ──
CREATE TABLE IF NOT EXISTS leave_types (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    name          VARCHAR(64)  NOT NULL,
    grant_method  SMALLINT     NOT NULL DEFAULT 1,                  -- 0=Periodic 1=Manual
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

-- ── leave_grants — 休暇の付与 (個別 / 一括 / 周期自動) ──
CREATE TABLE IF NOT EXISTS leave_grants (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    leave_type_id       UUID         NOT NULL REFERENCES leave_types(id),
    grant_date          DATE         NOT NULL,                      -- JST の業務日付
    days                NUMERIC(4,1) NOT NULL,
    kind                SMALLINT     NOT NULL DEFAULT 2,            -- 0=Normal 1=Proportional 2=Special
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

-- ── leave_requests — 休暇申請 (1 行 = 1 日分) ──
CREATE TABLE IF NOT EXISTS leave_requests (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    user_id             UUID         NOT NULL REFERENCES users(id),
    leave_type_id       UUID         NOT NULL REFERENCES leave_types(id),
    date                DATE         NOT NULL,                      -- JST の業務日付
    unit                SMALLINT     NOT NULL DEFAULT 0,            -- 0=Full 1=Half
    status              SMALLINT     NOT NULL DEFAULT 0,            -- 0=Pending 1=Approved 2=Rejected
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

-- ════════════════════════════════════════════════════════════════════
-- §3 users.attendance_rule_id → attendance_rules(id) の FK
--   (attendance_rules 作成後でないと張れないため §2 の後で行う)
-- ════════════════════════════════════════════════════════════════════
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_users_attendance_rule') THEN
        ALTER TABLE users
            ADD CONSTRAINT fk_users_attendance_rule
            FOREIGN KEY (attendance_rule_id) REFERENCES attendance_rules(id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_users_attendance_rule
    ON users (attendance_rule_id) WHERE attendance_rule_id IS NOT NULL;

-- ════════════════════════════════════════════════════════════════════
-- §4 シード: 法定有給の休暇種別 (全テナント・冪等)
--   name='有給休暇', grant_method=0(Periodic), expiry_months=24 (時効2年), is_statutory=true
--   既存行があれば何もしない (更新も削除もしない = 記録系保護と同じ非破壊方針)。
-- ════════════════════════════════════════════════════════════════════
DO $$
DECLARE
    t RECORD;
BEGIN
    FOR t IN SELECT tenant_id FROM tenant LOOP
        PERFORM set_config('app.tenant_id', t.tenant_id::text, true);

        INSERT INTO leave_types (tenant_id, name, grant_method, expiry_months, is_statutory, description, display_order)
        SELECT t.tenant_id, '有給休暇', 0, 24, TRUE, '労働基準法 39 条の年次有給休暇 (時効 2 年)', 1
        WHERE NOT EXISTS (
            -- ガードは 2 条件の OR。どちらか片方だけでは冪等性が破れる。
            --   (1) is_statutory (不変列): 統制有給が既にあれば入れない。name / deleted_at は
            --       可変なので、name 基準だと改名後の再実行で、deleted_at 条件を付けると DB 直操作で
            --       論理削除した後の再実行で、それぞれ 2 行目が入る。既存 leave_grants が旧 id を
            --       指したまま新 id が有効になり有給残数が分裂するうえ、部分 UNIQUE 索引
            --       uq_leave_types_tenant_name により復元 (LeaveService.RestoreTypeAsync) も 409 で塞がれる。
            --   (2) name = '有給休暇' AND deleted_at IS NULL: 部分 UNIQUE 索引
            --       uq_leave_types_tenant_name (tenant_id, name) WHERE deleted_at IS NULL への保護。
            --       (1) だけにすると「統制行が無く、かつ同名の非統制行が有効で存在する」テナントで
            --       INSERT が索引違反を起こし、スクリプトが中断する (後続の GRANT / RLS / トリガ配線が
            --       丸ごと未実行になる)。API は同名の非統制種別の作成を禁じていないため到達し得る
            --       (LeaveService.ValidateTypeName は空文字と 64 文字超しか弾かない)。
            SELECT 1 FROM leave_types
             WHERE tenant_id = t.tenant_id
               AND (is_statutory OR (name = '有給休暇' AND deleted_at IS NULL))
        );
    END LOOP;
END $$;

-- ════════════════════════════════════════════════════════════════════
-- §5 アプリロール (akebono_app) への権限付与
--   punch_records は記録系 (追記のみ) のため UPDATE/DELETE を剥奪する
--   (audit_logs / payment_receipt 等と同方針。CLAUDE.md 原則2)。
-- ════════════════════════════════════════════════════════════════════
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

-- ════════════════════════════════════════════════════════════════════
-- §6 RLS 配線 (db/init/08-tenancy-rls.sql と同一の標準形)
-- ════════════════════════════════════════════════════════════════════
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

-- ════════════════════════════════════════════════════════════════════
-- §7 updated_at トリガ配線 (db/init/09-updated-at-triggers.sql と同じ方式)
--   punch_records は updated_at 列を持たないため対象外 (追記のみの記録系)。
-- ════════════════════════════════════════════════════════════════════
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

COMMIT;
