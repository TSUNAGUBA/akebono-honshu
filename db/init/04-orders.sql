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
    inspection_shipping_date        DATE         NULL,                                      -- 検品所出荷日
    overseas_departure_date         DATE         NULL,                                      -- 海外出港日
    warehouse2_id                   BIGINT       NULL REFERENCES warehouses(id),            -- 納入倉庫2
    warehouse3_id                   BIGINT       NULL REFERENCES warehouses(id),            -- 納入倉庫3

    communication_text              TEXT         NULL,
    first_exported_at               TIMESTAMP    NULL,
    last_exported_at                TIMESTAMP    NULL,

    created_at                      TIMESTAMP    NOT NULL DEFAULT NOW(),
    created_by_user_id              BIGINT       NOT NULL REFERENCES users(id),
    updated_at                      TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id              BIGINT       NOT NULL REFERENCES users(id),
    legacy_id                       VARCHAR(64)  NULL,

    CONSTRAINT chk_po_status         CHECK (status IN (0, 1)),
    CONSTRAINT chk_po_last_after_first CHECK (last_exported_at IS NULL OR first_exported_at IS NOT NULL),
    CONSTRAINT chk_po_cancelled_consistency CHECK ((status = 1) = (cancelled_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_po_mgmt    ON purchase_orders (mgmt_no);
CREATE UNIQUE INDEX IF NOT EXISTS idx_po_order_no ON purchase_orders (order_no) WHERE order_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_po_status  ON purchase_orders (status, due_date);
CREATE INDEX IF NOT EXISTS idx_po_supplier ON purchase_orders (supplier_id);
CREATE INDEX IF NOT EXISTS idx_po_dest    ON purchase_orders (delivery_destination_id);
CREATE INDEX IF NOT EXISTS idx_po_dates   ON purchase_orders (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_po_unexported
    ON purchase_orders (first_exported_at) WHERE first_exported_at IS NULL AND status = 0;

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
