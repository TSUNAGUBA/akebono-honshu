-- Iteration 3: 発注関連 3 テーブル (Phase 5 data-design.md §5.1-5.3)
-- 前提: 03-products.sql まで投入済

SET TIMEZONE = 'Asia/Tokyo';

-- ─────────────────────────────────────────────────
-- §5.1 purchase_orders — 発注書ヘッダ
-- Phase 6 簡素化: status 2 値 (0=Active, 1=Cancelled)、改訂概念廃止
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS purchase_orders (
    id                              BIGSERIAL PRIMARY KEY,
    mgmt_no                         VARCHAR(16)  NOT NULL UNIQUE,
    order_no                        VARCHAR(16)  NULL,
    status                          SMALLINT     NOT NULL DEFAULT 0,
    cancelled_at                    TIMESTAMP    NULL,
    cancelled_by_user_id            BIGINT       NULL REFERENCES users(id),
    cancel_reason                   VARCHAR(255) NULL,

    -- 発注状態 5 値モデル (#3a)。納品完了 (delivered_at IS NOT NULL) / 発注削除 (is_deleted) を新設。
    -- cancelled_at/cancelled_by_user_id と同じ配置・様式でミラー。is_deleted は論理削除フラグ。
    delivered_at                    TIMESTAMP    NULL,
    delivered_by_user_id            BIGINT       NULL REFERENCES users(id),
    is_deleted                      BOOLEAN      NOT NULL DEFAULT FALSE,
    deleted_at                      TIMESTAMP    NULL,
    deleted_by_user_id              BIGINT       NULL REFERENCES users(id),

    supplier_id                     BIGINT       NOT NULL REFERENCES suppliers(id),
    supplier_official_name_snapshot VARCHAR(255) NULL,
    supplier_code_snapshot          VARCHAR(3)   NULL,

    delivery_destination_id         BIGINT       NOT NULL REFERENCES delivery_destinations(id),
    customer_name_snapshot          VARCHAR(255) NULL,

    department_id                   BIGINT       NOT NULL REFERENCES departments(id),
    warehouse_id                    BIGINT       NOT NULL REFERENCES warehouses(id),
    due_date                        DATE         NOT NULL,

    orderer_user_id                 BIGINT       NOT NULL REFERENCES users(id),
    sub_orderer_1_user_id           BIGINT       NULL REFERENCES users(id),
    sub_orderer_2_user_id           BIGINT       NULL REFERENCES users(id),
    sub_orderer_3_user_id           BIGINT       NULL REFERENCES users(id),
    sub_orderer_4_user_id           BIGINT       NULL REFERENCES users(id),
    sub_orderer_5_user_id           BIGINT       NULL REFERENCES users(id),
    sub_orderer_6_user_id           BIGINT       NULL REFERENCES users(id),
    manager_user_id                 BIGINT       NOT NULL REFERENCES users(id),

    -- 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外 NULL 許容)
    is_overseas                     BOOLEAN      NOT NULL DEFAULT FALSE,                    -- 発注区分 (国内=false/海外=true)
    landing_place                   VARCHAR(128) NULL,                                      -- 荷揚地 / Port of entry
    customer_ref                    VARCHAR(128) NULL,                                      -- 得意先 / 受注先
    factory_shipping_date           DATE         NULL,                                      -- 工場出荷日
    delivery_place_shipping_date    DATE         NULL,                                      -- 納品所出荷日 (旧名: 検品所出荷日、設計判断Q6)
    overseas_departure_date         DATE         NULL,                                      -- 海外出港日
    warehouse2_id                   BIGINT       NULL REFERENCES warehouses(id),            -- 納入倉庫2
    warehouse3_id                   BIGINT       NULL REFERENCES warehouses(id),            -- 納入倉庫3

    communication_text              TEXT         NULL,
    -- 連絡文書 6 行 (構造化、PR6)。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」(選択式) に対応。
    -- 各行はテンプレ選択式 (連絡文書サジェスト再利用) + 自由編集可。新フローは本 6 列を SoT とする。
    -- 既存 communication_text 列は削除しない (後方互換)。6 列が全 NULL の旧発注は Excel/編集ロードで
    -- communication_text にフォールバックする (既存表示を壊さない)。全 NULL 許容 = 既存行は不変。
    communication_line_1            TEXT         NULL,                                      -- 連絡文書01行 (spec 明細 No.27)
    communication_line_2            TEXT         NULL,                                      -- 連絡文書02行 (spec 明細 No.28)
    communication_line_3            TEXT         NULL,                                      -- 連絡文書03行 (spec 明細 No.29)
    communication_line_4            TEXT         NULL,                                      -- 連絡文書04行 (spec 明細 No.30)
    communication_line_5            TEXT         NULL,                                      -- 連絡文書05行 (spec 明細 No.31)
    communication_line_6            TEXT         NULL,                                      -- 連絡文書06行 (spec 明細 No.32)
    first_exported_at               TIMESTAMP    NULL,
    last_exported_at                TIMESTAMP    NULL,

    created_at                      TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id              BIGINT       NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id              BIGINT       NOT NULL REFERENCES users(id),
    legacy_id                       VARCHAR(64)  NULL,

    CONSTRAINT chk_po_status         CHECK (status IN (0, 1)),
    CONSTRAINT chk_po_last_after_first CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL),
    CONSTRAINT chk_po_cancelled_consistency CHECK ((status = 1) = (cancelled_at IS NOT NULL)),
    -- 発注状態 5 値モデル (#3a): is_deleted と deleted_at の整合 (chk_po_cancelled_consistency の踏襲)。
    -- 既存行は is_deleted=FALSE / deleted_at=NULL で TRUE=TRUE を満たす。
    CONSTRAINT chk_po_deleted_consistency CHECK (is_deleted = (deleted_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_po_mgmt    ON purchase_orders (mgmt_no);
CREATE UNIQUE INDEX IF NOT EXISTS idx_po_order_no ON purchase_orders (order_no) WHERE order_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_po_status  ON purchase_orders (status, due_date);
CREATE INDEX IF NOT EXISTS idx_po_supplier ON purchase_orders (supplier_id);
CREATE INDEX IF NOT EXISTS idx_po_dest    ON purchase_orders (delivery_destination_id);
CREATE INDEX IF NOT EXISTS idx_po_dates   ON purchase_orders (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_po_unexported
    ON purchase_orders (first_exported_at) WHERE first_exported_at IS NULL AND status = 0;
-- 発注状態 5 値モデル (#3a): 後方互換の「非中止・未削除」一覧パス (ListAsync の
-- includeCancelled=false 分岐: WHERE status=0 AND is_deleted=FALSE) を高速化する部分索引。
-- 新フロントは includeCancelled=true で全状態を取得し client-side 絞込するためこの索引は使わない。
CREATE INDEX IF NOT EXISTS idx_po_not_deleted
    ON purchase_orders (created_at DESC) WHERE is_deleted = FALSE;

-- ─────────────────────────────────────────────────
-- §5.2 purchase_order_lines — 発注明細
-- スナップショット (sku/name/unit_price/currency) で発注時点を凍結
-- subtotal は GENERATED ALWAYS AS で DB 側計算
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS purchase_order_lines (
    id                              BIGSERIAL PRIMARY KEY,
    purchase_order_id               BIGINT        NOT NULL REFERENCES purchase_orders(id) ON DELETE CASCADE,
    line_no                         SMALLINT      NOT NULL,
    product_id                      BIGINT        NOT NULL REFERENCES products(id),
    sku_snapshot                    VARCHAR(11)   NOT NULL,
    product_name_snapshot           VARCHAR(255)  NOT NULL,
    quantity                        INTEGER       NOT NULL,
    unit_price_snapshot             NUMERIC(12,2) NOT NULL,
    currency_code_snapshot          CHAR(3)       NOT NULL,
    -- 旧 発注明細 項目 (Phase B、全 NULL 許容)
    pack_quantity                   INTEGER       NULL,                                     -- 倉庫1入数 / 入数
    estimate_unit_price             NUMERIC(12,2) NULL,                                     -- 見積単価
    provisional_number_snapshot     VARCHAR(64)   NULL,                                     -- 仮番号 (商品 family からコピー)
    remark                          TEXT          NULL,                                     -- 発注明細 備考 (行レベル、spec 明細 No.26)
    subtotal                        NUMERIC(14,2) GENERATED ALWAYS AS (quantity * unit_price_snapshot) STORED,
    created_at                      TIMESTAMP     NOT NULL DEFAULT NOW(),
    created_by_user_id              BIGINT        NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMP     NOT NULL DEFAULT NOW(),
    updated_by_user_id              BIGINT        NOT NULL REFERENCES users(id),

    CONSTRAINT chk_pol_quantity     CHECK (quantity > 0),
    CONSTRAINT chk_pol_unit_price   CHECK (unit_price_snapshot >= 0),
    CONSTRAINT uq_pol_order_line    UNIQUE (purchase_order_id, line_no)
);
CREATE INDEX IF NOT EXISTS idx_pol_order   ON purchase_order_lines (purchase_order_id);
CREATE INDEX IF NOT EXISTS idx_pol_product ON purchase_order_lines (product_id);

-- ─────────────────────────────────────────────────
-- §5.2b purchase_order_line_deliveries — 分納×倉庫の多次元明細 (PR5b)
-- 1 発注明細 (purchase_order_lines) を「(倉庫 × 納期) の分納行」の集合で多次元化する。
-- 旧 spec 発注明細 No.6「発注明細日1-11」/ No.7-17「発注明細数1〜11」/ No.18-23「倉庫1〜3 入数/発注数」
-- に対応 (設計判断 Q1=可変の分納テーブル、Q2=明細を倉庫×納期で多次元化)。
--
-- 下位互換 (最重要): 分納は明細あたり任意 (0 件可)。
--   分納 0 件 = 従来どおりの単一明細。purchase_order_lines.quantity をそのまま単一数量として使う
--     (既存発注・既存フローは完全に不変)。
--   分納 N 件 = その明細は分納で構成。purchase_order_lines.quantity には分納数量の合計 (SUM) を設定する
--     (既存の subtotal GENERATED 列 = quantity * unit_price_snapshot がそのまま正しい金額になる)。
-- 既存 purchase_order_lines.quantity / pack_quantity 列は削除しない (後方互換)。pack_quantity は
-- 単一明細時の入数として温存し、分納時は分納行 (本テーブル) の pack_quantity を使う (二重表現)。
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS purchase_order_line_deliveries (
    id                       BIGSERIAL PRIMARY KEY,
    purchase_order_line_id   BIGINT       NOT NULL REFERENCES purchase_order_lines(id) ON DELETE CASCADE,
    warehouse_id             BIGINT       NULL REFERENCES warehouses(id),          -- 倉庫 (NULL=倉庫未指定)
    delivery_date            DATE         NULL,                                     -- 納期 / 発注明細日 (NULL=発注明細日未指定)
    quantity                 INTEGER      NOT NULL,                                 -- 発注明細数 (分納数量)
    pack_quantity            INTEGER      NULL,                                     -- 倉庫別入数
    seq                      SMALLINT     NOT NULL DEFAULT 1,                       -- 表示順 (配列順で採番)
    created_at               TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id       BIGINT       NOT NULL REFERENCES users(id),
    updated_at               TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id       BIGINT       NOT NULL REFERENCES users(id),
    CONSTRAINT chk_pold_quantity CHECK (quantity > 0)
);
CREATE INDEX IF NOT EXISTS idx_pold_line ON purchase_order_line_deliveries (purchase_order_line_id);

-- ─────────────────────────────────────────────────
-- §5.3 purchase_order_export_logs — Excel 出力履歴 (監査用、非 UI 露出)
-- ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS purchase_order_export_logs (
    id                              BIGSERIAL PRIMARY KEY,
    purchase_order_id               BIGINT       NOT NULL REFERENCES purchase_orders(id),
    exported_at                     TIMESTAMP    NOT NULL DEFAULT NOW(),
    exported_by_user_id             BIGINT       NOT NULL REFERENCES users(id),
    is_first_export                 BOOLEAN      NOT NULL,
    excel_template_version          VARCHAR(16)  NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_poel_order_at ON purchase_order_export_logs (purchase_order_id, exported_at DESC);

-- ─────────────────────────────────────────────────
-- Seed: Iteration 3 動作確認用 (発注書 1 件、3 SKU 明細)
-- ─────────────────────────────────────────────────
DO $$
DECLARE
    owner_id    BIGINT;
    supplier_id BIGINT;
    dest_id     BIGINT;
    dept_id     BIGINT;
    wh_id       BIGINT;
    po_id       BIGINT;
    sku_id1     BIGINT;
    sku_id2     BIGINT;
    sku_id3     BIGINT;
BEGIN
    SELECT id INTO owner_id    FROM users WHERE login_id = 'owner';
    SELECT id INTO supplier_id FROM suppliers WHERE code = '336';
    SELECT id INTO dest_id     FROM delivery_destinations WHERE code = '001';
    SELECT id INTO dept_id     FROM departments WHERE code = '001';
    SELECT id INTO wh_id       FROM warehouses WHERE code = '007';
    SELECT id INTO sku_id1     FROM products WHERE sku = 'NA1001A4010';
    SELECT id INTO sku_id2     FROM products WHERE sku = 'NA1001A4011';
    SELECT id INTO sku_id3     FROM products WHERE sku = 'NA1001A9010';

    INSERT INTO purchase_orders (
        mgmt_no, supplier_id, delivery_destination_id, department_id, warehouse_id,
        due_date, orderer_user_id, manager_user_id, communication_text,
        created_by_user_id, updated_by_user_id
    ) VALUES (
        '26-00001', supplier_id, dest_id, dept_id, wh_id,
        '2026-08-31', owner_id, owner_id,
        '初回サンプル発注。納期厳守のほどよろしくお願いいたします。',
        owner_id, owner_id
    ) ON CONFLICT (mgmt_no) DO NOTHING
    RETURNING id INTO po_id;

    IF po_id IS NULL THEN
        SELECT id INTO po_id FROM purchase_orders WHERE mgmt_no = '26-00001';
    END IF;

    -- 明細 3 件 (各 SKU の発注時単価スナップショットを 1500 で凍結)
    INSERT INTO purchase_order_lines (
        purchase_order_id, line_no, product_id, sku_snapshot,
        product_name_snapshot, quantity, unit_price_snapshot, currency_code_snapshot,
        created_by_user_id, updated_by_user_id
    ) VALUES
        (po_id, 1, sku_id1, 'NA1001A4010', 'デモ商品 春夏ベーシック', 100, 1500.00, 'JPY', owner_id, owner_id),
        (po_id, 2, sku_id2, 'NA1001A4011', 'デモ商品 春夏ベーシック', 150, 1500.00, 'JPY', owner_id, owner_id),
        (po_id, 3, sku_id3, 'NA1001A9010', 'デモ商品 春夏ベーシック',  80, 1500.00, 'JPY', owner_id, owner_id)
    ON CONFLICT DO NOTHING;
END $$;
