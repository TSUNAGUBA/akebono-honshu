-- Iteration 0 (ローカル開発環境) 用 最小スキーマ
-- Phase 5 data-design.md §3.13 users / §6.1 audit_logs の最小サブセット
-- 本番では EF Core マイグレーションに置換 (Iteration 1 以降)

-- Iter 4 段階 B: 全 timestamp カラムを TIMESTAMP (without time zone) で JST naive 保存に統一。
-- DB レベルの timezone を 'Asia/Tokyo' に永続化することで DEFAULT NOW() / CURRENT_TIMESTAMP も
-- JST 値で確定される。SET TIMEZONE はセッション内設定 (本ファイル init 実行中のため必須)、
-- ALTER DATABASE は永続設定 (次回以降の接続全てに適用)。
ALTER DATABASE akebono_honshu SET timezone TO 'Asia/Tokyo';
SET TIMEZONE = 'Asia/Tokyo';

-- ─────────────────────────────────────────────────
-- users (Phase 5 §3.13、Iteration 0 用に最小列のみ)
-- ─────────────────────────────────────────────────
CREATE TABLE users (
    id                  BIGSERIAL PRIMARY KEY,
    employee_no         VARCHAR(16)  NOT NULL UNIQUE,
    login_id            VARCHAR(64)  NOT NULL UNIQUE,
    display_name        VARCHAR(255) NOT NULL,
    is_active           BOOLEAN      NOT NULL DEFAULT TRUE,
    is_deleted          BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMP    NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_active ON users (is_active) WHERE is_deleted = FALSE;

-- ─────────────────────────────────────────────────
-- audit_logs (Phase 5 §6.1、Iteration 0 用に最小列のみ)
-- Phase 5 設計: INSERT 専用、UPDATE/DELETE は DB ロール権限で REVOKE
-- (Iteration 0 ではロール権限分離は未実装、Iteration 4 Hardening で対応)
-- ─────────────────────────────────────────────────
CREATE TABLE audit_logs (
    id                  BIGSERIAL PRIMARY KEY,
    occurred_at         TIMESTAMP    NOT NULL DEFAULT NOW(),
    actor_user_id       BIGINT       NULL REFERENCES users(id),
    action              VARCHAR(64)  NOT NULL,
    entity_type         VARCHAR(64)  NULL,
    entity_id           BIGINT       NULL,
    result              SMALLINT     NOT NULL DEFAULT 0,  -- 0=Success, 1=Failure
    note                VARCHAR(512) NULL
);

CREATE INDEX idx_audit_logs_occurred ON audit_logs (occurred_at DESC);
CREATE INDEX idx_audit_logs_actor ON audit_logs (actor_user_id, occurred_at DESC);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 0 動作確認用のサンプルユーザ 3 件
-- ─────────────────────────────────────────────────
INSERT INTO users (employee_no, login_id, display_name) VALUES
    ('001', 'owner',   '今尾 雅広'),
    ('002', 'planner', '南 雄介'),
    ('003', 'sales',   '斉藤 摂次');
