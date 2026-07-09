-- ============================================================================
-- 07-ops-data.sql — 業務拡張モジュール(販売管理/出荷/在庫管理)のテーブル + サンプルデータ
-- ----------------------------------------------------------------------------
-- 目的: ホームに追加する新メニュー（販売管理・出荷・在庫管理）の各画面が参照する
--       軽量な業務テーブルを新設し、表示用サンプルデータを投入する。
--       販売管理は 売上(sales_orders) / 請求(billing_invoices) / 入金(payment_receipts) /
--       債権(accounts_receivable) の 4 画面で構成する。
--       （分析メニューは既存 API の集計のため専用テーブル不要。）
--
-- 設計方針:
--  - これらは表示中心の運用ログ系テーブル。コア業務テーブル(product_families 等)とは
--    独立し、監査 FK(created_by_user_id 等)は持たない軽量構成とする（段階導入の骨格）。
--  - 冪等: CREATE TABLE IF NOT EXISTS + INSERT ... ON CONFLICT(自然キー) DO NOTHING
--    （CLAUDE.md 原則2。再実行で重複生成・巻き戻りなし）。
--  - 既存テーブルには一切変更を加えない（原則7 下位互換）。
--
-- 適用: 新規 DB は db/init で自動投入。既存 DB へは db/migration/iter7-ops-data.sql を
--       migrate で適用（06/iter6 と同方式、\ir で本ファイルを取り込む単一 SoT）。
-- ============================================================================
-- プラットフォーム統合改修: tenant_id (uuid) 導入・UNIQUE を (tenant_id, ...) へ差替・TIMESTAMPTZ(UTC) 化

SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (tenant_id 列の DEFAULT がこの GUC から解決される)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- ────────────────────────────────────────────────────────────────────────────
-- 販売管理: 受注/売上
-- ────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS sales_orders (
    id            BIGSERIAL PRIMARY KEY,
    tenant_id     UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    order_no      VARCHAR(16)   NOT NULL,
    customer_name VARCHAR(255)  NOT NULL,
    order_date    DATE          NOT NULL,
    total_amount  NUMERIC(14,2) NOT NULL DEFAULT 0,
    status        VARCHAR(16)   NOT NULL DEFAULT '受注',
    created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_sales_orders_tenant_order_no UNIQUE (tenant_id, order_no)
);
CREATE INDEX IF NOT EXISTS idx_sales_orders_tenant ON sales_orders (tenant_id);
INSERT INTO sales_orders (order_no, customer_name, order_date, total_amount, status) VALUES
    ('SO-26-0001', 'しまむら',  DATE '2026-06-01', 1842000.00, '出荷済'),
    ('SO-26-0002', 'AEON',     DATE '2026-06-05',  996000.00, '受注'),
    ('SO-26-0003', 'KEYUCA',   DATE '2026-06-08',  423500.00, '引当済'),
    ('SO-26-0004', 'しまむら',  DATE '2026-06-12', 2310000.00, '受注'),
    ('SO-26-0005', 'ベルメゾン', DATE '2026-06-15',  587400.00, '出荷済'),
    ('SO-26-0006', 'AEON',     DATE '2026-06-18',  134200.00, '取消')
ON CONFLICT (tenant_id, order_no) DO NOTHING;

-- 請求: 請求書発行/入金予定
CREATE TABLE IF NOT EXISTS billing_invoices (
    id             BIGSERIAL PRIMARY KEY,
    tenant_id      UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    invoice_no     VARCHAR(16)   NOT NULL,
    customer_name  VARCHAR(255)  NOT NULL,
    invoice_date   DATE          NOT NULL,
    invoice_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    due_date       DATE          NOT NULL,   -- 入金予定日
    status         VARCHAR(16)   NOT NULL DEFAULT '請求済',
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_billing_invoices_tenant_invoice_no UNIQUE (tenant_id, invoice_no)
);
CREATE INDEX IF NOT EXISTS idx_billing_invoices_tenant ON billing_invoices (tenant_id);
INSERT INTO billing_invoices (invoice_no, customer_name, invoice_date, invoice_amount, due_date, status) VALUES
    ('INV-26-0501', 'しまむら',  DATE '2026-05-31', 3652000.00, DATE '2026-06-30', '入金済'),
    ('INV-26-0502', 'AEON',     DATE '2026-05-31', 1130200.00, DATE '2026-06-30', '一部入金'),
    ('INV-26-0601', 'しまむら',  DATE '2026-06-30', 4152000.00, DATE '2026-07-31', '請求済'),
    ('INV-26-0602', 'KEYUCA',   DATE '2026-06-30',  423500.00, DATE '2026-07-31', '請求済'),
    ('INV-26-0603', 'ベルメゾン', DATE '2026-06-30',  587400.00, DATE '2026-07-31', '発行待ち')
ON CONFLICT (tenant_id, invoice_no) DO NOTHING;

-- 入金: 入金実績/消込
CREATE TABLE IF NOT EXISTS payment_receipts (
    id             BIGSERIAL PRIMARY KEY,
    tenant_id      UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    payment_no     VARCHAR(16)   NOT NULL,
    payment_date   DATE          NOT NULL,
    customer_name  VARCHAR(255)  NOT NULL,
    payment_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    method         VARCHAR(16)   NOT NULL,   -- 入金方法(銀行振込/手形/相殺 等)
    status         VARCHAR(16)   NOT NULL DEFAULT '未消込',   -- 消込状況
    CONSTRAINT uq_payment_receipts_tenant_payment_no UNIQUE (tenant_id, payment_no)
);
CREATE INDEX IF NOT EXISTS idx_payment_receipts_tenant ON payment_receipts (tenant_id);
INSERT INTO payment_receipts (payment_no, payment_date, customer_name, payment_amount, method, status) VALUES
    ('PAY-26-0601', DATE '2026-06-30', 'しまむら',  3652000.00, '銀行振込', '消込済'),
    ('PAY-26-0602', DATE '2026-06-30', 'AEON',      600000.00, '銀行振込', '一部消込'),
    ('PAY-26-0603', DATE '2026-06-28', 'KEYUCA',    381200.00, '手形',     '消込済'),
    ('PAY-26-0604', DATE '2026-06-25', 'ベルメゾン',  220000.00, '相殺',     '未消込')
ON CONFLICT (tenant_id, payment_no) DO NOTHING;

-- 債権: 得意先別売掛残高/滞留
CREATE TABLE IF NOT EXISTS accounts_receivable (
    id            BIGSERIAL PRIMARY KEY,
    tenant_id     UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    customer_name VARCHAR(255)  NOT NULL,
    balance       NUMERIC(14,2) NOT NULL DEFAULT 0,   -- 売掛残高
    due_date      DATE          NOT NULL,             -- 期日
    overdue_days  INTEGER       NOT NULL DEFAULT 0,   -- 滞留日数
    status        VARCHAR(16)   NOT NULL DEFAULT '正常',
    CONSTRAINT uq_accounts_receivable_tenant_customer_name UNIQUE (tenant_id, customer_name)
);
CREATE INDEX IF NOT EXISTS idx_accounts_receivable_tenant ON accounts_receivable (tenant_id);
INSERT INTO accounts_receivable (customer_name, balance, due_date, overdue_days, status) VALUES
    ('しまむら',  4152000.00, DATE '2026-07-31',  0, '正常'),
    ('AEON',      530200.00, DATE '2026-06-30',  0, '正常'),
    ('KEYUCA',    423500.00, DATE '2026-07-31',  0, '正常'),
    ('ベルメゾン',  367400.00, DATE '2026-05-31', 24, '滞留'),
    ('ニッセン',    88000.00, DATE '2026-04-30', 55, '督促中')
ON CONFLICT (tenant_id, customer_name) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- 出荷: データ受信 / ピッキングリスト / 帳票出力 / ASN送信
-- ────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shipping_receipts (
    id           BIGSERIAL PRIMARY KEY,
    tenant_id    UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    receipt_no   VARCHAR(20)  NOT NULL,
    source       VARCHAR(64)  NOT NULL,   -- EDI種別/送信元
    received_at  TIMESTAMPTZ  NOT NULL,
    record_count INTEGER      NOT NULL DEFAULT 0,
    status       VARCHAR(16)  NOT NULL DEFAULT '受信済',
    CONSTRAINT uq_shipping_receipts_tenant_receipt_no UNIQUE (tenant_id, receipt_no)
);
CREATE INDEX IF NOT EXISTS idx_shipping_receipts_tenant ON shipping_receipts (tenant_id);
INSERT INTO shipping_receipts (receipt_no, source, received_at, record_count, status) VALUES
    ('RCV-260619-001', 'しまむら Web-EDI', TIMESTAMPTZ '2026-06-19 06:30+09', 128, '取込済'),
    ('RCV-260619-002', 'AEON EOS',         TIMESTAMPTZ '2026-06-19 07:10+09',  64, '取込済'),
    ('RCV-260620-001', 'しまむら Web-EDI', TIMESTAMPTZ '2026-06-20 06:28+09', 142, '受信済'),
    ('RCV-260620-002', 'KEYUCA CSV',       TIMESTAMPTZ '2026-06-20 09:05+09',  31, 'エラー'),
    ('RCV-260621-001', 'AEON EOS',         TIMESTAMPTZ '2026-06-21 07:02+09',  58, '受信済')
ON CONFLICT (tenant_id, receipt_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS picking_lists (
    id          BIGSERIAL PRIMARY KEY,
    tenant_id   UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    list_no     VARCHAR(20)  NOT NULL,
    warehouse   VARCHAR(64)  NOT NULL,
    item_count  INTEGER      NOT NULL DEFAULT 0,
    total_qty   INTEGER      NOT NULL DEFAULT 0,
    status      VARCHAR(16)  NOT NULL DEFAULT '未着手',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_picking_lists_tenant_list_no UNIQUE (tenant_id, list_no)
);
CREATE INDEX IF NOT EXISTS idx_picking_lists_tenant ON picking_lists (tenant_id);
INSERT INTO picking_lists (list_no, warehouse, item_count, total_qty, status, created_at) VALUES
    ('PCK-260620-01', '本社倉庫',       24, 860, '完了',   TIMESTAMPTZ '2026-06-20 08:00+09'),
    ('PCK-260620-02', 'しまむらセンター', 18, 540, 'ピッキング中', TIMESTAMPTZ '2026-06-20 08:30+09'),
    ('PCK-260621-01', '本社倉庫',       31, 1240, '未着手', TIMESTAMPTZ '2026-06-21 08:00+09'),
    ('PCK-260621-02', '本社倉庫',        9, 210, '未着手', TIMESTAMPTZ '2026-06-21 08:15+09')
ON CONFLICT (tenant_id, list_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS report_outputs (
    id          BIGSERIAL PRIMARY KEY,
    tenant_id   UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    output_no   VARCHAR(20)  NOT NULL,
    report_name VARCHAR(128) NOT NULL,
    format      VARCHAR(8)   NOT NULL DEFAULT 'PDF',
    operator    VARCHAR(64)  NOT NULL,
    output_at   TIMESTAMPTZ  NOT NULL,
    CONSTRAINT uq_report_outputs_tenant_output_no UNIQUE (tenant_id, output_no)
);
CREATE INDEX IF NOT EXISTS idx_report_outputs_tenant ON report_outputs (tenant_id);
INSERT INTO report_outputs (output_no, report_name, format, operator, output_at) VALUES
    ('RPT-260620-001', '納品書',        'PDF',   '今尾 雅広', TIMESTAMPTZ '2026-06-20 10:15+09'),
    ('RPT-260620-002', '出荷指示書',    'PDF',   '南 雄介',  TIMESTAMPTZ '2026-06-20 10:20+09'),
    ('RPT-260620-003', '荷札ラベル',    'PDF',   '今尾 雅広', TIMESTAMPTZ '2026-06-20 10:40+09'),
    ('RPT-260621-001', '出荷一覧表',    'Excel', '斉藤 摂次', TIMESTAMPTZ '2026-06-21 11:00+09')
ON CONFLICT (tenant_id, output_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS asn_transmissions (
    id          BIGSERIAL PRIMARY KEY,
    tenant_id   UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    asn_no      VARCHAR(20)  NOT NULL,
    destination VARCHAR(64)  NOT NULL,
    slip_count  INTEGER      NOT NULL DEFAULT 0,
    sent_at     TIMESTAMPTZ  NOT NULL,
    status      VARCHAR(16)  NOT NULL DEFAULT '送信済',
    CONSTRAINT uq_asn_transmissions_tenant_asn_no UNIQUE (tenant_id, asn_no)
);
CREATE INDEX IF NOT EXISTS idx_asn_transmissions_tenant ON asn_transmissions (tenant_id);
INSERT INTO asn_transmissions (asn_no, destination, slip_count, sent_at, status) VALUES
    ('ASN-260619-01', 'しまむら', 12, TIMESTAMPTZ '2026-06-19 15:00+09', '送信済'),
    ('ASN-260620-01', 'AEON',    8,  TIMESTAMPTZ '2026-06-20 15:10+09', '送信済'),
    ('ASN-260620-02', 'しまむら', 15, TIMESTAMPTZ '2026-06-20 16:30+09', '送信済'),
    ('ASN-260621-01', 'KEYUCA',  4,  TIMESTAMPTZ '2026-06-21 14:20+09', '送信待ち')
ON CONFLICT (tenant_id, asn_no) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- 在庫管理: 入荷情報 / 出荷情報 / 在庫調整 / 棚卸調整
-- ────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS inbound_records (
    id           BIGSERIAL PRIMARY KEY,
    tenant_id    UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    slip_no      VARCHAR(20)  NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    quantity     INTEGER      NOT NULL,
    warehouse    VARCHAR(64)  NOT NULL,
    received_at  DATE         NOT NULL,
    CONSTRAINT uq_inbound_records_tenant_slip_no UNIQUE (tenant_id, slip_no)
);
CREATE INDEX IF NOT EXISTS idx_inbound_records_tenant ON inbound_records (tenant_id);
INSERT INTO inbound_records (slip_no, product_name, quantity, warehouse, received_at) VALUES
    ('IN-260615-01', '婦人コンフォートサンダル', 540, '本社倉庫', DATE '2026-06-15'),
    ('IN-260616-01', '婦人ショートブーツ',       420, '本社倉庫', DATE '2026-06-16'),
    ('IN-260617-01', '紳士ビジネスサンダル',     300, '本社倉庫', DATE '2026-06-17'),
    ('IN-260618-01', 'ルームシューズ ボア',      640, '本社倉庫', DATE '2026-06-18')
ON CONFLICT (tenant_id, slip_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS outbound_records (
    id           BIGSERIAL PRIMARY KEY,
    tenant_id    UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    slip_no      VARCHAR(20)  NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    quantity     INTEGER      NOT NULL,
    destination  VARCHAR(64)  NOT NULL,
    shipped_at   DATE         NOT NULL,
    CONSTRAINT uq_outbound_records_tenant_slip_no UNIQUE (tenant_id, slip_no)
);
CREATE INDEX IF NOT EXISTS idx_outbound_records_tenant ON outbound_records (tenant_id);
INSERT INTO outbound_records (slip_no, product_name, quantity, destination, shipped_at) VALUES
    ('OUT-260619-01', '婦人コンフォートサンダル', 220, 'しまむらセンター', DATE '2026-06-19'),
    ('OUT-260619-02', '婦人スタイリッシュパンプス', 180, 'AEONセンター',   DATE '2026-06-19'),
    ('OUT-260620-01', '婦人ショートブーツ',       160, 'しまむらセンター', DATE '2026-06-20'),
    ('OUT-260621-01', '婦人ヘルスウォーカー',      90, 'AEONセンター',   DATE '2026-06-21')
ON CONFLICT (tenant_id, slip_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS stock_adjustments (
    id           BIGSERIAL PRIMARY KEY,
    tenant_id    UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    adjust_no    VARCHAR(20)  NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    delta        INTEGER      NOT NULL,   -- +入 / -出
    reason       VARCHAR(128) NOT NULL,
    adjusted_at  DATE         NOT NULL,
    CONSTRAINT uq_stock_adjustments_tenant_adjust_no UNIQUE (tenant_id, adjust_no)
);
CREATE INDEX IF NOT EXISTS idx_stock_adjustments_tenant ON stock_adjustments (tenant_id);
INSERT INTO stock_adjustments (adjust_no, product_name, delta, reason, adjusted_at) VALUES
    ('ADJ-260618-01', '婦人コンフォートサンダル', -6, '不良品廃棄',     DATE '2026-06-18'),
    ('ADJ-260619-01', '紳士ビジネスサンダル',     12, '返品入庫',       DATE '2026-06-19'),
    ('ADJ-260620-01', 'クッションマット',         -3, '汚損',           DATE '2026-06-20'),
    ('ADJ-260620-02', '婦人ショートブーツ',        4, '数量訂正',       DATE '2026-06-20')
ON CONFLICT (tenant_id, adjust_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS stocktaking_adjustments (
    id           BIGSERIAL PRIMARY KEY,
    tenant_id    UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    count_no     VARCHAR(20)  NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    book_qty     INTEGER      NOT NULL,   -- 帳簿在庫
    actual_qty   INTEGER      NOT NULL,   -- 実地在庫
    diff         INTEGER      GENERATED ALWAYS AS (actual_qty - book_qty) STORED,
    counted_at   DATE         NOT NULL,
    CONSTRAINT uq_stocktaking_adjustments_tenant_count_no UNIQUE (tenant_id, count_no)
);
CREATE INDEX IF NOT EXISTS idx_stocktaking_adjustments_tenant ON stocktaking_adjustments (tenant_id);
INSERT INTO stocktaking_adjustments (count_no, product_name, book_qty, actual_qty, counted_at) VALUES
    ('CNT-260630-01', '婦人コンフォートサンダル', 318, 315, DATE '2026-06-30'),
    ('CNT-260630-02', '婦人スタイリッシュパンプス', 252, 252, DATE '2026-06-30'),
    ('CNT-260630-03', '婦人ショートブーツ',       196, 200, DATE '2026-06-30'),
    ('CNT-260630-04', 'ルームシューズ ボア',      480, 472, DATE '2026-06-30')
ON CONFLICT (tenant_id, count_no) DO NOTHING;
