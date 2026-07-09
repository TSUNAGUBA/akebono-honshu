-- Iteration 0 (ローカル開発環境) 用 最小スキーマ
-- Phase 5 data-design.md §3.13 users / §6.1 audit_logs の最小サブセット
-- 本番では EF Core マイグレーションに置換 (Iteration 1 以降)
--
-- プラットフォーム統合改修 (あけぼの SCM プラットフォーム / akebono-maker 母体化):
--   1. tenant テーブル新設 + 全テーブルへ tenant_id uuid NOT NULL を導入 (AKB-DOC-13 §10.3 M1/M2)
--   2. 全 timestamp カラムを TIMESTAMPTZ (UTC) に統一 (ADR-006、旧 JST-naive 方式を廃止)
--   3. RLS (Row Level Security) は 08-tenancy-rls.sql で一括配線 (シード投入後に有効化)
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
    id                  BIGSERIAL PRIMARY KEY,
    tenant_id           UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
                                     REFERENCES tenant(tenant_id),
    employee_no         VARCHAR(16)  NOT NULL,
    login_id            VARCHAR(64)  NOT NULL,
    display_name        VARCHAR(255) NOT NULL,
    is_active           BOOLEAN      NOT NULL DEFAULT TRUE,
    is_deleted          BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_users_tenant_employee_no UNIQUE (tenant_id, employee_no),
    CONSTRAINT uq_users_tenant_login_id    UNIQUE (tenant_id, login_id)
);

CREATE INDEX idx_users_tenant ON users (tenant_id);
CREATE INDEX idx_users_active ON users (is_active) WHERE is_deleted = FALSE;

-- ─────────────────────────────────────────────────
-- audit_logs (Phase 5 §6.1、Iteration 0 用に最小列のみ)
-- Phase 5 設計: INSERT 専用、UPDATE/DELETE は DB ロール権限で REVOKE
-- (08-tenancy-rls.sql でアプリロールから UPDATE/DELETE を剥奪)
--
-- RLS 適用除外テーブル: 認証拒否イベント (UidUnboundProbe 等) はテナント
-- コンテキスト確立前に記録されるため tenant_id は NULL 許容。テナント確定後の
-- 業務操作ログは GUC 経由の DEFAULT または明示指定で tenant_id が入る。
-- ─────────────────────────────────────────────────
CREATE TABLE audit_logs (
    id                  BIGSERIAL PRIMARY KEY,
    tenant_id           UUID         NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
                                     REFERENCES tenant(tenant_id),
    occurred_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    actor_user_id       BIGINT       NULL REFERENCES users(id),
    action              VARCHAR(64)  NOT NULL,
    entity_type         VARCHAR(64)  NULL,
    entity_id           BIGINT       NULL,
    result              SMALLINT     NOT NULL DEFAULT 0,  -- 0=Success, 1=Failure
    note                VARCHAR(512) NULL
);

CREATE INDEX idx_audit_logs_tenant ON audit_logs (tenant_id, occurred_at DESC);
CREATE INDEX idx_audit_logs_occurred ON audit_logs (occurred_at DESC);
CREATE INDEX idx_audit_logs_actor ON audit_logs (actor_user_id, occurred_at DESC);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 0 動作確認用のサンプルユーザ 3 件
-- (tenant_id はセッション GUC app.tenant_id の DEFAULT で Honshu テナントに解決)
-- ─────────────────────────────────────────────────
INSERT INTO users (employee_no, login_id, display_name) VALUES
    ('001', 'owner',   '今尾 雅広'),
    ('002', 'planner', '南 雄介'),
    ('003', 'sales',   '斉藤 摂次');
