-- Iteration 0 (ローカル開発環境) 用 最小スキーマ
-- Phase 5 data-design.md §3.13 users / §6.1 audit_logs の最小サブセット
-- 本番では EF Core マイグレーションに置換 (Iteration 1 以降)
--
-- プラットフォーム統合改修 (あけぼの SCM プラットフォーム / akebono-maker 母体化):
--   1. tenant テーブル新設 + 全テーブルへ tenant_id uuid NOT NULL を導入 (AKB-DOC-13 §10.3 M1/M2)
--   2. 全 timestamp カラムを TIMESTAMPTZ (UTC) に統一 (ADR-006、旧 JST-naive 方式を廃止)
--   3. RLS (Row Level Security) は 08-tenancy-rls.sql で一括配線 (シード投入後に有効化)
-- プラットフォーム統合 第二段階: uuid PK / deleted_at 統一 / 監査パーティション
--
-- tenant_id の DEFAULT はセッション GUC app.tenant_id から解決する。
-- GUC 未設定時は NULLIF(...) が NULL を返し NOT NULL 違反で失敗する (フェイルクローズ)。
-- アプリ (EF Core) は常に明示的に tenant_id を設定するため DEFAULT はシード投入・
-- レガシー取込 (raw SQL) 用の補助経路である。

-- タイムゾーン: プラットフォーム標準は timestamptz (UTC)。
-- セッション/DB 既定を UTC に固定し、表示変換はアプリケーション層 (JST) で行う。
-- DB 名はハードコードせず current_database() で解決する (検証用 DB / 別名環境でも安全)。
DO $$
BEGIN
    EXECUTE format('ALTER DATABASE %I SET timezone TO ''UTC''', current_database());
END $$;
SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (Honshu 既定テナント)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- updated_at を DB 側で強制する共通トリガ関数 (プラットフォーム標準 AKB-DOC-13)
CREATE OR REPLACE FUNCTION set_updated_at() RETURNS trigger AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END $$ LANGUAGE plpgsql;

-- 各テーブルへのトリガ配線は 09-updated-at-triggers.sql で一括実施
-- (information_schema 走査により updated_at 列を持つ全 BASE TABLE を自動カバー)。
-- ただし自動カバーの対象は **09 より前に実行されるファイルで作られたテーブル** に限る。
-- 09 より後に実行されるファイル (10-attendance.sql 等) で作られるテーブルは、09 の走査時点では
-- まだ存在しないため対象外。その場合は自ファイル内で set_updated_at() の BEFORE UPDATE トリガを
-- 配線すること (実装例: 10-attendance.sql §5)。

-- ─────────────────────────────────────────────────
-- tenant — テナントレジストリ (ローカル投影)
--
-- SoT 宣言: テナントのライフサイクル (契約・プラン・ステータス) の SoT は
-- プラットフォームの akebono-backoffice (AKB-DOC-09)。本テーブルはその投影
-- (キャッシュ) であり、本アプリはテナントの新規発行・契約状態の変更を行わない。
-- MVP 段階ではプラットフォームのプロビジョニング未接続のため、シードで
-- Honshu テナント 1 件を投入する (接続後は backoffice からの同期で更新)。
-- ─────────────────────────────────────────────────
CREATE TABLE tenant (
    tenant_id    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_code  VARCHAR(64)  NOT NULL UNIQUE,   -- グローバル一意 (テナント横断で数少ない例外)
    name         VARCHAR(255) NOT NULL,
    status       VARCHAR(16)  NOT NULL DEFAULT 'active'
        CONSTRAINT chk_tenant_status CHECK (status IN ('trial', 'active', 'suspended', 'terminating', 'terminated')),
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE tenant IS 'テナントレジストリのローカル投影 (SoT = akebono-backoffice)';

-- Honshu 既定テナント (固定 UUID。シード・ローカル開発・プロビジョニング前の暫定)
INSERT INTO tenant (tenant_id, tenant_code, name) VALUES
    ('00000000-0000-4000-8000-000000000001', 'honshu', 'Honshu（ホンシュ）');

-- ─────────────────────────────────────────────────
-- users (Phase 5 §3.13、Iteration 0 用に最小列のみ)
--
-- RLS 適用除外テーブル (08-tenancy-rls.sql 参照): firebase_uid → tenant_id の
-- 解決が認証時 (テナントコンテキスト確立前) に必要な認証エントリポイントのため。
-- テナント分離はアプリケーション層 (EF Core グローバルクエリフィルタ) で担保する。
-- firebase_uid / email はグローバル一意 (MVP 制約: 1 Firebase アカウント = 1 テナント所属)。
-- ─────────────────────────────────────────────────
CREATE TABLE users (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
                                     REFERENCES tenant(tenant_id),
    employee_no         VARCHAR(16)  NOT NULL,
    login_id            VARCHAR(64)  NOT NULL,
    display_name        VARCHAR(255) NOT NULL,
    is_active           BOOLEAN      NOT NULL DEFAULT TRUE,
    deleted_at          TIMESTAMPTZ  NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_users_tenant_employee_no UNIQUE (tenant_id, employee_no),
    CONSTRAINT uq_users_tenant_login_id    UNIQUE (tenant_id, login_id)
);

CREATE INDEX idx_users_tenant ON users (tenant_id);
CREATE INDEX idx_users_active ON users (is_active) WHERE deleted_at IS NULL;

-- users の拡張列について (追加場所の索引):
--   §3.18 の Phase 5 全カラム (firebase_uid / email / 権限 4 列 / 監査列 / legacy_id) は
--     02-masters.sql の ALTER TABLE users で追加する。
--   勤怠 (Iteration 30) の 6 列 (attendance_permission / punch_required / attendance_rule_id /
--     hire_date / weekly_days / weekly_hours) は 10-attendance.sql で追加する。
--     ※ ここ (01) ではなく 10 で追加する理由: attendance_rule_id の FK 先 attendance_rules が
--        10 で作られること、および **列の並び順を db/migration/iter30-attendance.sql を適用した
--        既存 DB と一致させる**ため (既存 DB では 02 の列より後ろにしか追加できない)。
--        init 経路と migration 経路でスキーマが等価になる。

-- ─────────────────────────────────────────────────
-- audit_logs (Phase 5 §6.1、Iteration 0 用に最小列のみ)
-- Phase 5 設計: INSERT 専用、UPDATE/DELETE は DB ロール権限で REVOKE
-- (08-tenancy-rls.sql でアプリロールから UPDATE/DELETE を剥奪)
--
-- RLS 適用除外テーブル: 認証拒否イベント (UidUnboundProbe 等) はテナント
-- コンテキスト確立前に記録されるため tenant_id は NULL 許容。テナント確定後の
-- 業務操作ログは GUC 経由の DEFAULT または明示指定で tenant_id が入る。
--
-- 第二段階: occurred_at による月次 RANGE パーティション化 (AKB-DOC-14)。
-- PK はパーティションキーを含む必要があるため (id, occurred_at) の複合 PK。
-- entity_id は対象エンティティの id 値 (FK なし・型のみ uuid に追随)。
-- ─────────────────────────────────────────────────
CREATE TABLE audit_logs (
    id                  UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id           UUID         NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
                                     REFERENCES tenant(tenant_id),
    occurred_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    actor_user_id       UUID         NULL REFERENCES users(id),
    action              VARCHAR(64)  NOT NULL,
    entity_type         VARCHAR(64)  NULL,
    entity_id           UUID         NULL,
    result              SMALLINT     NOT NULL DEFAULT 0,  -- 0=Success, 1=Failure
    note                VARCHAR(512) NULL,
    PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

-- 索引はパーティション親に定義 (各パーティションへ自動伝播)
CREATE INDEX idx_audit_logs_tenant ON audit_logs (tenant_id, occurred_at DESC);
CREATE INDEX idx_audit_logs_occurred ON audit_logs (occurred_at DESC);
CREATE INDEX idx_audit_logs_actor ON audit_logs (actor_user_id, occurred_at DESC);

-- DEFAULT パーティション (安全網): 月次パーティション未作成の期間に落ちた行を受ける。
-- 運用上は ensure_audit_log_partitions() の定期実行で月次パーティションを先行作成し、
-- 本パーティションには行が入らない状態を正とする。
CREATE TABLE audit_logs_default PARTITION OF audit_logs DEFAULT;

-- ─────────────────────────────────────────────────
-- ensure_audit_log_partitions — 監査ログ月次パーティションの先行作成
--
-- 当月から p_months_ahead か月先までの月次パーティション audit_logs_yYYYYmMM を、
-- 存在しなければ作成する (冪等)。
--
-- SECURITY DEFINER の理由: パーティション追加 (CREATE TABLE ... PARTITION OF) は
-- 親テーブル audit_logs の所有者権限が必要であり、アプリロールは親の所有者では
-- ないため、所有者 (定義者) 権限で実行する。
-- SET search_path = public: SECURITY DEFINER 関数のセキュリティ必須設定
-- (呼出側 search_path 経由のオブジェクト差替え攻撃を防止)。
-- アプリロールへの EXECUTE 権限付与は 08-tenancy-rls.sql 側で行う。
-- ─────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION ensure_audit_log_partitions(p_months_ahead int DEFAULT 3)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_from date;
    v_to   date;
    v_name text;
BEGIN
    -- SECURITY DEFINER + アプリロール実行可のため、過大値による大量パーティション作成を
    -- 防ぐガード (呼出は保守ジョブの 3 が既定。0〜24 か月にクランプ)。
    p_months_ahead := LEAST(GREATEST(p_months_ahead, 0), 24);
    FOR i IN 0..p_months_ahead LOOP
        v_from := (date_trunc('month', now()) + make_interval(months => i))::date;
        v_to   := (v_from + INTERVAL '1 month')::date;
        v_name := format('audit_logs_y%sm%s', to_char(v_from, 'YYYY'), to_char(v_from, 'MM'));
        IF to_regclass('public.' || v_name) IS NULL THEN
            EXECUTE format(
                'CREATE TABLE %I PARTITION OF audit_logs FOR VALUES FROM (%L) TO (%L)',
                v_name, v_from, v_to
            );
        END IF;
    END LOOP;
END $$;

-- 初期パーティション作成 (当月〜3 か月先)
SELECT ensure_audit_log_partitions(3);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 0 動作確認用のサンプルユーザ 3 件
-- (tenant_id はセッション GUC app.tenant_id の DEFAULT で Honshu テナントに解決)
-- ─────────────────────────────────────────────────
INSERT INTO users (employee_no, login_id, display_name) VALUES
    ('001', 'owner',   '今尾 雅広'),
    ('002', 'planner', '南 雄介'),
    ('003', 'sales',   '斉藤 摂次');
