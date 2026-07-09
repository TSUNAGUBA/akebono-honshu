-- ============================================================================
-- 07-ops-data.sql — 業務拡張モジュール (販売管理/出荷/在庫管理) 正規化層 + サンプルデータ
-- ----------------------------------------------------------------------------
-- 目的: ホームの新メニュー (販売管理・出荷・在庫管理) の各画面が将来参照する
--       業務テーブル群。プラットフォーム規約 (AKB-DOC-14 app_maker /
--       AKB-DOC-13 §5 型・命名規約・§8 データ分類) に準拠した正規化層。
--
-- 全面再設計 (プラットフォーム統合 第二段階):
--   旧 12 テーブル (sales_orders, billing_invoices, payment_receipts,
--   accounts_receivable, shipping_receipts, picking_lists, report_outputs,
--   asn_transmissions, inbound_records, outbound_records, stock_adjustments,
--   stocktaking_adjustments) は日本語 VARCHAR ステータス・自然キー参照 (得意先名/
--   倉庫名の文字列埋込) のアンチパターンだったため、以下の正規形へ置換する。
--     - id UUID PRIMARY KEY DEFAULT gen_random_uuid() / テーブル名は単数形
--     - 区分値は text + CHECK (英小文字 snake_case)。表示訳はアプリケーション層
--     - 得意先は customer マスタへ FK 正規化 (将来 core.party role=customer へ写像)
--     - 倉庫は既存 warehouses マスタへ FK 正規化
--     - 金額 NUMERIC(14,2) / 数量 NUMERIC(12,2) / 時刻 TIMESTAMPTZ (UTC)
--       ※AKB-DOC-13 §5.3 標準 (金額 18,2 / 数量 18,3) からの意図的縮小 (設計判断):
--         本アプリの業務レンジ (国内アパレル、単価×数量とも 10^12 未満) に合わせた。
--         共通列 created_by/updated_by/attributes (§5.4) は既存 05 以前のテーブル群が
--         *_by_user_id 方式のため導入せず、app_maker 一般化写像時に吸収する。
--     - データ3分類 (設定系/記録系/確定系 + 導出) を COMMENT ON TABLE で宣言。
--       記録系は追記専用 (updated_at を持たない。UPDATE/DELETE は 08 で REVOKE)
--     - 旧 accounts_receivable (導出値の実体表) は廃止し、SoT (billing_invoice /
--       payment_allocation) からの導出 VIEW に置換
--     - 旧 在庫 4 テーブル (inbound/outbound/stock_adj/stocktaking_adj) は
--       追記専用の inventory_movement 1 本に統合し、時点残高は導出キャッシュ
--       inventory_balance (SoT = inventory_movement) に分離
--
-- 冪等: CREATE TABLE IF NOT EXISTS + INSERT ... ON CONFLICT DO NOTHING
--       (CLAUDE.md 原則2。再実行で重複生成・巻き戻りなし)。
--
-- 下位互換 (原則7): 旧 12 テーブルは閲覧用プロトタイプ (バックエンド EF 未参照・
--   FE はモック定数表示) でありシード以外のデータを持たないため、本ファイル冒頭の
--   レガシー清掃でそのまま DROP する (復元が必要なデータは存在しない)。
--   稼働中 DB への適用は db/migration の台帳管理で行う (iter6/iter7 と同方式)。
--
-- 実行順: 01 (tenant/users)・02 (warehouses) の後、08 (RLS 配線) の前。
--         updated_at トリガは 09-updated-at-triggers.sql が updated_at 列を持つ
--         全 BASE TABLE へ自動付与する (本ファイルでの個別配線は不要)。
-- ============================================================================

SET TIMEZONE = 'UTC';

-- シード用テナントコンテキスト (tenant_id 列の DEFAULT がこの GUC から解決される)
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';

-- ────────────────────────────────────────────────────────────────────────────
-- レガシー清掃: 旧プロトタイプ 12 テーブルの撤去
-- (新規 DB では no-op。旧 accounts_receivable は本ファイルで同名 VIEW を作るため、
--  「通常テーブルとして存在する場合のみ」DROP する — VIEW 化後の再実行で誤 DROP しない)
-- ────────────────────────────────────────────────────────────────────────────
DROP TABLE IF EXISTS sales_orders;
DROP TABLE IF EXISTS billing_invoices;
DROP TABLE IF EXISTS payment_receipts;
DROP TABLE IF EXISTS shipping_receipts;
DROP TABLE IF EXISTS picking_lists;
DROP TABLE IF EXISTS report_outputs;
DROP TABLE IF EXISTS asn_transmissions;
DROP TABLE IF EXISTS inbound_records;
DROP TABLE IF EXISTS outbound_records;
DROP TABLE IF EXISTS stock_adjustments;
DROP TABLE IF EXISTS stocktaking_adjustments;
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND c.relname = 'accounts_receivable' AND c.relkind = 'r'
    ) THEN
        DROP TABLE accounts_receivable;
    END IF;
END $$;

-- ────────────────────────────────────────────────────────────────────────────
-- 販売管理: 得意先 / 受注 / 請求 / 入金 / 消込 / 債権 (導出 VIEW)
-- ────────────────────────────────────────────────────────────────────────────

-- customer — 得意先マスタ
CREATE TABLE IF NOT EXISTS customer (
    id          UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    code        VARCHAR(8)    NOT NULL,
    name        VARCHAR(255)  NOT NULL,
    deleted_at  TIMESTAMPTZ   NULL,
    created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_customer_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX IF NOT EXISTS idx_customer_tenant ON customer (tenant_id);
COMMENT ON TABLE customer IS
    '設定系マスタ: 得意先。SoT = 本テーブル (将来 core.party role=customer へ写像予定。旧テーブル群の customer_name/destination 文字列の正規化先)';

INSERT INTO customer (code, name) VALUES
    ('001', 'しまむら'),
    ('002', 'AEON'),
    ('003', 'KEYUCA'),
    ('004', 'ベルメゾン'),
    ('005', 'ニッセン')
ON CONFLICT (tenant_id, code) DO NOTHING;

-- sales_order — 受注ヘッダ (旧 sales_orders)
-- 旧ステータス写像: 受注→received / 引当済→allocated / 出荷済→shipped / 取消→cancelled
CREATE TABLE IF NOT EXISTS sales_order (
    id               UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    order_no         VARCHAR(16)   NOT NULL,
    customer_id      UUID          NOT NULL REFERENCES customer(id),
    order_date       DATE          NOT NULL,
    product_summary  VARCHAR(255)  NOT NULL,
    quantity         NUMERIC(12,2) NOT NULL,
    amount           NUMERIC(14,2) NOT NULL,
    status           TEXT          NOT NULL
        CONSTRAINT chk_sales_order_status CHECK (status IN ('received', 'allocated', 'shipped', 'cancelled')),
    created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_sales_order_tenant_order_no UNIQUE (tenant_id, order_no)
);
CREATE INDEX IF NOT EXISTS idx_sales_order_tenant   ON sales_order (tenant_id);
CREATE INDEX IF NOT EXISTS idx_sales_order_customer ON sales_order (customer_id);
COMMENT ON TABLE sales_order IS
    '設定系: 受注ヘッダ。SoT = 本テーブル。quantity/amount は明細 (sales_order_line) 合計と整合させる';

INSERT INTO sales_order (order_no, customer_id, order_date, product_summary, quantity, amount, status)
SELECT s.order_no, c.id, s.order_date, s.product_summary, s.quantity, s.amount, s.status
FROM (VALUES
    ('SO-26-0001', '001', DATE '2026-06-01', '婦人コンフォートサンダル 他', 900.00, 1842000.00, 'shipped'),
    ('SO-26-0002', '002', DATE '2026-06-05', '紳士ビジネスサンダル',        400.00,  996000.00, 'received'),
    ('SO-26-0003', '003', DATE '2026-06-08', 'ルームシューズ ボア',         350.00,  423500.00, 'allocated'),
    ('SO-26-0004', '001', DATE '2026-06-12', '婦人スタイリッシュパンプス 他', 980.00, 2310000.00, 'received'),
    ('SO-26-0005', '004', DATE '2026-06-15', 'クッションマット',            220.00,  587400.00, 'shipped'),
    ('SO-26-0006', '002', DATE '2026-06-18', 'ルームシューズ ボア',         110.00,  134200.00, 'cancelled')
) AS s(order_no, customer_code, order_date, product_summary, quantity, amount, status)
JOIN customer c
  ON c.code = s.customer_code
 AND c.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, order_no) DO NOTHING;

-- sales_order_line — 受注明細
CREATE TABLE IF NOT EXISTS sales_order_line (
    id              UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    sales_order_id  UUID          NOT NULL REFERENCES sales_order(id) ON DELETE CASCADE,
    line_no         SMALLINT      NOT NULL,
    product_name    VARCHAR(255)  NOT NULL,
    quantity        NUMERIC(12,2) NOT NULL,
    amount          NUMERIC(14,2) NOT NULL,
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_sales_order_line_tenant_order_line UNIQUE (tenant_id, sales_order_id, line_no)
);
CREATE INDEX IF NOT EXISTS idx_sales_order_line_tenant ON sales_order_line (tenant_id);
CREATE INDEX IF NOT EXISTS idx_sales_order_line_order  ON sales_order_line (sales_order_id);
COMMENT ON TABLE sales_order_line IS
    '設定系: 受注明細。SoT = 本テーブル。quantity/amount の合計はヘッダ (sales_order) と整合';

INSERT INTO sales_order_line (sales_order_id, line_no, product_name, quantity, amount)
SELECT so.id, s.line_no, s.product_name, s.quantity, s.amount
FROM (VALUES
    ('SO-26-0001', 1::smallint, '婦人コンフォートサンダル',   600.00, 1080000.00),
    ('SO-26-0001', 2::smallint, '婦人ショートブーツ',         300.00,  762000.00),
    ('SO-26-0002', 1::smallint, '紳士ビジネスサンダル',       400.00,  996000.00),
    ('SO-26-0003', 1::smallint, 'ルームシューズ ボア',        350.00,  423500.00),
    ('SO-26-0004', 1::smallint, '婦人スタイリッシュパンプス', 700.00, 1540000.00),
    ('SO-26-0004', 2::smallint, '婦人ヘルスウォーカー',       280.00,  770000.00),
    ('SO-26-0005', 1::smallint, 'クッションマット',           220.00,  587400.00),
    ('SO-26-0006', 1::smallint, 'ルームシューズ ボア',        110.00,  134200.00)
) AS s(order_no, line_no, product_name, quantity, amount)
JOIN sales_order so
  ON so.order_no = s.order_no
 AND so.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, sales_order_id, line_no) DO NOTHING;

-- billing_invoice — 請求書 (旧 billing_invoices)
-- 旧ステータス写像: 発行待ち→draft / 請求済→issued / 一部入金→partially_paid / 入金済→paid
CREATE TABLE IF NOT EXISTS billing_invoice (
    id           UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id    UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    invoice_no   VARCHAR(16)   NOT NULL,
    customer_id  UUID          NOT NULL REFERENCES customer(id),
    issue_date   DATE          NOT NULL,
    due_date     DATE          NOT NULL,
    amount       NUMERIC(14,2) NOT NULL,
    status       TEXT          NOT NULL
        CONSTRAINT chk_billing_invoice_status CHECK (status IN ('draft', 'issued', 'partially_paid', 'paid')),
    created_at   TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_billing_invoice_tenant_invoice_no UNIQUE (tenant_id, invoice_no)
);
CREATE INDEX IF NOT EXISTS idx_billing_invoice_tenant   ON billing_invoice (tenant_id);
CREATE INDEX IF NOT EXISTS idx_billing_invoice_customer ON billing_invoice (customer_id);
COMMENT ON TABLE billing_invoice IS
    '確定系: 請求書。SoT = 本テーブル。発行後 (issued 以降) の金額・明細は不変とし、訂正は貸方 (マイナス) 行の追記で行う方針。status のみ入金消込に応じて遷移する';

INSERT INTO billing_invoice (invoice_no, customer_id, issue_date, due_date, amount, status)
SELECT s.invoice_no, c.id, s.issue_date, s.due_date, s.amount, s.status
FROM (VALUES
    ('INV-26-0501', '001', DATE '2026-05-31', DATE '2026-06-30', 3652000.00, 'paid'),
    ('INV-26-0502', '002', DATE '2026-05-31', DATE '2026-06-30', 1130200.00, 'partially_paid'),
    ('INV-26-0601', '001', DATE '2026-06-30', DATE '2026-07-31', 4152000.00, 'issued'),
    ('INV-26-0602', '003', DATE '2026-06-30', DATE '2026-07-31',  423500.00, 'issued'),
    ('INV-26-0603', '004', DATE '2026-06-30', DATE '2026-07-31',  587400.00, 'draft')
) AS s(invoice_no, customer_code, issue_date, due_date, amount, status)
JOIN customer c
  ON c.code = s.customer_code
 AND c.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, invoice_no) DO NOTHING;

-- payment_receipt — 入金 (旧 payment_receipts)
-- 旧写像: method 銀行振込→bank_transfer / 手形→promissory_note / 相殺→offset、
--         status 未消込→unallocated / 一部消込→partially_allocated / 消込済→allocated
CREATE TABLE IF NOT EXISTS payment_receipt (
    id             UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    payment_no     VARCHAR(16)   NOT NULL,
    customer_id    UUID          NOT NULL REFERENCES customer(id),
    received_date  DATE          NOT NULL,
    amount         NUMERIC(14,2) NOT NULL,
    method         TEXT          NOT NULL
        CONSTRAINT chk_payment_receipt_method CHECK (method IN ('bank_transfer', 'promissory_note', 'offset')),
    status         TEXT          NOT NULL
        CONSTRAINT chk_payment_receipt_status CHECK (status IN ('unallocated', 'partially_allocated', 'allocated')),
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_payment_receipt_tenant_payment_no UNIQUE (tenant_id, payment_no)
);
CREATE INDEX IF NOT EXISTS idx_payment_receipt_tenant   ON payment_receipt (tenant_id);
CREATE INDEX IF NOT EXISTS idx_payment_receipt_customer ON payment_receipt (customer_id);
COMMENT ON TABLE payment_receipt IS
    '記録系: 入金実績 (追記専用・updated_at なし。UPDATE/DELETE は 08 でアプリロールから REVOKE)。SoT = 本テーブル。消込内訳は payment_allocation';

-- PAY-26-0603 (KEYUCA) は移行前システムで消込済 (対象請求書は移行対象外) のため、
-- status=allocated だが payment_allocation にシード行を持たない (移行時の設計判断)。
INSERT INTO payment_receipt (payment_no, customer_id, received_date, amount, method, status)
SELECT s.payment_no, c.id, s.received_date, s.amount, s.method, s.status
FROM (VALUES
    ('PAY-26-0601', '001', DATE '2026-06-30', 3652000.00, 'bank_transfer',   'allocated'),
    ('PAY-26-0602', '002', DATE '2026-06-30',  600000.00, 'bank_transfer',   'partially_allocated'),
    ('PAY-26-0603', '003', DATE '2026-06-28',  381200.00, 'promissory_note', 'allocated'),
    ('PAY-26-0604', '004', DATE '2026-06-25',  220000.00, 'offset',          'unallocated')
) AS s(payment_no, customer_code, received_date, amount, method, status)
JOIN customer c
  ON c.code = s.customer_code
 AND c.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, payment_no) DO NOTHING;

-- payment_allocation — 消込 (入金 ↔ 請求書の充当)
CREATE TABLE IF NOT EXISTS payment_allocation (
    id                  UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    payment_receipt_id  UUID          NOT NULL REFERENCES payment_receipt(id),
    billing_invoice_id  UUID          NOT NULL REFERENCES billing_invoice(id),
    amount              NUMERIC(14,2) NOT NULL,
    allocated_at        TIMESTAMPTZ   NOT NULL,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    -- 同一 (入金, 請求書) ペアの充当は 1 行に集約する運用 (シード冪等化も兼ねる)
    CONSTRAINT uq_payment_allocation_tenant_pair UNIQUE (tenant_id, payment_receipt_id, billing_invoice_id)
);
CREATE INDEX IF NOT EXISTS idx_payment_allocation_tenant  ON payment_allocation (tenant_id);
CREATE INDEX IF NOT EXISTS idx_payment_allocation_receipt ON payment_allocation (payment_receipt_id);
CREATE INDEX IF NOT EXISTS idx_payment_allocation_invoice ON payment_allocation (billing_invoice_id);
COMMENT ON TABLE payment_allocation IS
    '記録系: 消込 (追記専用・updated_at なし。UPDATE/DELETE は 08 でアプリロールから REVOKE)。SoT = 本テーブル。取消は逆仕訳 (マイナス行) の追記で行う';

-- paid の請求書 (INV-26-0501) には全額消込、partially_paid (INV-26-0502) には一部消込
INSERT INTO payment_allocation (payment_receipt_id, billing_invoice_id, amount, allocated_at)
SELECT pr.id, bi.id, s.amount, s.allocated_at
FROM (VALUES
    ('PAY-26-0601', 'INV-26-0501', 3652000.00, TIMESTAMPTZ '2026-06-30 10:00+09'),
    ('PAY-26-0602', 'INV-26-0502',  600000.00, TIMESTAMPTZ '2026-06-30 10:05+09')
) AS s(payment_no, invoice_no, amount, allocated_at)
JOIN payment_receipt pr
  ON pr.payment_no = s.payment_no
 AND pr.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
JOIN billing_invoice bi
  ON bi.invoice_no = s.invoice_no
 AND bi.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, payment_receipt_id, billing_invoice_id) DO NOTHING;

-- accounts_receivable — 債権 (得意先別売掛残) 導出 VIEW
--
-- 旧 accounts_receivable テーブル (残高の実体保持 = SoT 二重化) は廃止。
-- 導出ビュー: SoT は billing_invoice / payment_allocation。残高はいつでも再導出可能。
-- security_invoker = true (PG15+) により基底テーブルの RLS が「呼出し側の権限・
-- テナントコンテキスト」で適用される (ビュー所有者権限での RLS 素通りを防ぐ)。
CREATE OR REPLACE VIEW accounts_receivable WITH (security_invoker = true) AS
WITH invoice_allocated AS (
    SELECT billing_invoice_id, SUM(amount) AS allocated_amount
    FROM payment_allocation
    GROUP BY billing_invoice_id
)
SELECT
    bi.tenant_id,
    bi.customer_id,
    c.code                                                   AS customer_code,
    c.name                                                   AS customer_name,
    SUM(bi.amount)                                           AS invoiced_amount,
    COALESCE(SUM(ia.allocated_amount), 0)                    AS allocated_amount,
    SUM(bi.amount) - COALESCE(SUM(ia.allocated_amount), 0)   AS balance,
    MIN(bi.due_date) FILTER (
        WHERE bi.amount > COALESCE(ia.allocated_amount, 0)
    )                                                        AS oldest_open_due_date
FROM billing_invoice bi
JOIN customer c ON c.id = bi.customer_id
LEFT JOIN invoice_allocated ia ON ia.billing_invoice_id = bi.id
WHERE bi.status IN ('issued', 'partially_paid', 'paid')   -- draft は債権未発生
GROUP BY bi.tenant_id, bi.customer_id, c.code, c.name
HAVING SUM(bi.amount) - COALESCE(SUM(ia.allocated_amount), 0) > 0;

COMMENT ON VIEW accounts_receivable IS
    '導出ビュー: 得意先別売掛残 (残高 > 0 のみ)。SoT = billing_invoice / payment_allocation。security_invoker により基底 RLS が呼出し側コンテキストで適用される';

-- ────────────────────────────────────────────────────────────────────────────
-- 出荷: データ受信 / ピッキングリスト / 帳票出力 / ASN送信
-- ────────────────────────────────────────────────────────────────────────────

-- shipping_receipt — 出荷データ受信 (EDI 受信 = 統合設計方針の経路B の一例)
-- 旧ステータス写像: 受信済→received / 取込済→imported / エラー→error
CREATE TABLE IF NOT EXISTS shipping_receipt (
    id             UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    receipt_no     VARCHAR(20)  NOT NULL,
    source_system  VARCHAR(64)  NOT NULL,   -- 送信元システム識別 (データ値は表示名のまま保持可)
    received_at    TIMESTAMPTZ  NOT NULL,
    line_count     INTEGER      NOT NULL,
    status         TEXT         NOT NULL
        CONSTRAINT chk_shipping_receipt_status CHECK (status IN ('received', 'imported', 'error')),
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_shipping_receipt_tenant_receipt_no UNIQUE (tenant_id, receipt_no)
);
CREATE INDEX IF NOT EXISTS idx_shipping_receipt_tenant ON shipping_receipt (tenant_id);
COMMENT ON TABLE shipping_receipt IS
    '記録系: EDI 出荷データ受信ログ (経路B。追記専用・updated_at なし。UPDATE/DELETE は 08 で REVOKE)。SoT = 送信元 EDI システム (本テーブルは受信の記録)';

INSERT INTO shipping_receipt (receipt_no, source_system, received_at, line_count, status) VALUES
    ('RCV-260619-001', 'しまむら Web-EDI', TIMESTAMPTZ '2026-06-19 06:30+09', 128, 'imported'),
    ('RCV-260619-002', 'AEON EOS',         TIMESTAMPTZ '2026-06-19 07:10+09',  64, 'imported'),
    ('RCV-260620-001', 'しまむら Web-EDI', TIMESTAMPTZ '2026-06-20 06:28+09', 142, 'received'),
    ('RCV-260620-002', 'KEYUCA CSV',       TIMESTAMPTZ '2026-06-20 09:05+09',  31, 'error'),
    ('RCV-260621-001', 'AEON EOS',         TIMESTAMPTZ '2026-06-21 07:02+09',  58, 'received')
ON CONFLICT (tenant_id, receipt_no) DO NOTHING;

-- picking_list — ピッキングリスト (旧 picking_lists)
-- 旧写像: 倉庫名文字列 '本社倉庫'→warehouses code '007' / 'しまむらセンター'→code '717'、
--         status 未着手→not_started / ピッキング中→picking / 完了→completed
CREATE TABLE IF NOT EXISTS picking_list (
    id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    list_no       VARCHAR(20)  NOT NULL,
    warehouse_id  UUID         NOT NULL REFERENCES warehouses(id),
    picking_date  DATE         NOT NULL,
    line_count    INTEGER      NOT NULL,
    status        TEXT         NOT NULL
        CONSTRAINT chk_picking_list_status CHECK (status IN ('not_started', 'picking', 'completed')),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_picking_list_tenant_list_no UNIQUE (tenant_id, list_no)
);
CREATE INDEX IF NOT EXISTS idx_picking_list_tenant    ON picking_list (tenant_id);
CREATE INDEX IF NOT EXISTS idx_picking_list_warehouse ON picking_list (warehouse_id);
COMMENT ON TABLE picking_list IS
    '設定系: ピッキングリスト。SoT = 本テーブル。倉庫は warehouses マスタへ FK 正規化';

INSERT INTO picking_list (list_no, warehouse_id, picking_date, line_count, status)
SELECT s.list_no, w.id, s.picking_date, s.line_count, s.status
FROM (VALUES
    ('PCK-260620-01', '007', DATE '2026-06-20', 24, 'completed'),
    ('PCK-260620-02', '717', DATE '2026-06-20', 18, 'picking'),
    ('PCK-260621-01', '007', DATE '2026-06-21', 31, 'not_started'),
    ('PCK-260621-02', '007', DATE '2026-06-21',  9, 'not_started')
) AS s(list_no, warehouse_code, picking_date, line_count, status)
JOIN warehouses w
  ON w.code = s.warehouse_code
 AND w.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, list_no) DO NOTHING;

-- report_output — 帳票出力履歴 (旧 report_outputs)
-- 旧写像: operator 氏名文字列 → users への FK (今尾 雅広=owner / 南 雄介=planner / 斉藤 摂次=sales)、
--         format PDF→pdf / Excel→excel
CREATE TABLE IF NOT EXISTS report_output (
    id                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    output_no          VARCHAR(20)  NOT NULL,
    report_type        VARCHAR(64)  NOT NULL,
    format             TEXT         NOT NULL
        CONSTRAINT chk_report_output_format CHECK (format IN ('pdf', 'excel')),
    output_at          TIMESTAMPTZ  NOT NULL,
    output_by_user_id  UUID         NULL REFERENCES users(id),
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_report_output_tenant_output_no UNIQUE (tenant_id, output_no)
);
CREATE INDEX IF NOT EXISTS idx_report_output_tenant ON report_output (tenant_id);
COMMENT ON TABLE report_output IS
    '記録系: 帳票出力履歴 (追記専用・updated_at なし。UPDATE/DELETE は 08 で REVOKE)。SoT = 本テーブル';

INSERT INTO report_output (output_no, report_type, format, output_at, output_by_user_id)
SELECT s.output_no, s.report_type, s.format, s.output_at, u.id
FROM (VALUES
    ('RPT-260620-001', '納品書',     'pdf',   TIMESTAMPTZ '2026-06-20 10:15+09', 'owner'),
    ('RPT-260620-002', '出荷指示書', 'pdf',   TIMESTAMPTZ '2026-06-20 10:20+09', 'planner'),
    ('RPT-260620-003', '荷札ラベル', 'pdf',   TIMESTAMPTZ '2026-06-20 10:40+09', 'owner'),
    ('RPT-260621-001', '出荷一覧表', 'excel', TIMESTAMPTZ '2026-06-21 11:00+09', 'sales')
) AS s(output_no, report_type, format, output_at, login_id)
JOIN users u
  ON u.login_id = s.login_id
 AND u.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, output_no) DO NOTHING;

-- asn_transmission — ASN 送信 (旧 asn_transmissions)
-- 旧写像: destination 文字列 → customer への FK 正規化、送信済→sent / 送信待ち→pending
--         (pending の transmitted_at は NULL。CHECK で status と整合を強制)
CREATE TABLE IF NOT EXISTS asn_transmission (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID         NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    asn_no          VARCHAR(20)  NOT NULL,
    customer_id     UUID         NOT NULL REFERENCES customer(id),
    transmitted_at  TIMESTAMPTZ  NULL,
    status          TEXT         NOT NULL
        CONSTRAINT chk_asn_transmission_status CHECK (status IN ('pending', 'sent')),
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_asn_transmission_tenant_asn_no UNIQUE (tenant_id, asn_no),
    CONSTRAINT chk_asn_transmission_sent_at CHECK ((status = 'sent') = (transmitted_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_asn_transmission_tenant   ON asn_transmission (tenant_id);
CREATE INDEX IF NOT EXISTS idx_asn_transmission_customer ON asn_transmission (customer_id);
COMMENT ON TABLE asn_transmission IS
    '記録系: ASN (事前出荷通知) 送信ログ (追記専用・updated_at なし。UPDATE/DELETE は 08 で REVOKE)。SoT = 本テーブル';

INSERT INTO asn_transmission (asn_no, customer_id, transmitted_at, status)
SELECT s.asn_no, c.id, s.transmitted_at, s.status
FROM (VALUES
    ('ASN-260619-01', '001', TIMESTAMPTZ '2026-06-19 15:00+09', 'sent'),
    ('ASN-260620-01', '002', TIMESTAMPTZ '2026-06-20 15:10+09', 'sent'),
    ('ASN-260620-02', '001', TIMESTAMPTZ '2026-06-20 16:30+09', 'sent'),
    ('ASN-260621-01', '003', NULL::timestamptz,                 'pending')
) AS s(asn_no, customer_code, transmitted_at, status)
JOIN customer c
  ON c.code = s.customer_code
 AND c.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, asn_no) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- 在庫管理: 在庫移動 (追記専用・SoT) + 時点残高 (導出キャッシュ)
-- ────────────────────────────────────────────────────────────────────────────

-- inventory_movement — 在庫移動 (旧 inbound_records / outbound_records /
--                       stock_adjustments / stocktaking_adjustments の 4 テーブルを統合)
-- 旧写像: 入荷→inbound (+) / 出荷→outbound (−) / 在庫調整→adjust (±) /
--         棚卸調整→stocktake (実地−帳簿。棚卸減は負)
-- 倉庫の写像: 旧 inbound/adjust/stocktake は倉庫列なし → 本社倉庫 '007' とみなす。
--             旧 outbound の destination (しまむらセンター/AEONセンター) は出荷先で
--             あり倉庫ではないため、warehouse_id は出荷元 '007'、出荷先は reason に保持。
CREATE TABLE IF NOT EXISTS inventory_movement (
    id             UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    movement_no    VARCHAR(16)   NOT NULL,
    movement_type  TEXT          NOT NULL
        CONSTRAINT chk_inventory_movement_type CHECK (movement_type IN ('inbound', 'outbound', 'adjust', 'stocktake')),
    product_name   VARCHAR(255)  NOT NULL,
    sku_snapshot   VARCHAR(16)   NULL,      -- 移動時点の SKU 写し (products 未連携の旧データは NULL 可)
    quantity       NUMERIC(12,2) NOT NULL,  -- 符号付き: 入庫・調整増は正 / 出庫・棚卸減は負
    warehouse_id   UUID          NOT NULL REFERENCES warehouses(id),
    moved_at       TIMESTAMPTZ   NOT NULL,
    reason         VARCHAR(255)  NULL,
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_inventory_movement_tenant_movement_no UNIQUE (tenant_id, movement_no)
);
CREATE INDEX IF NOT EXISTS idx_inventory_movement_tenant    ON inventory_movement (tenant_id);
CREATE INDEX IF NOT EXISTS idx_inventory_movement_warehouse ON inventory_movement (warehouse_id);
COMMENT ON TABLE inventory_movement IS
    '記録系: 在庫移動 (追記専用・updated_at なし。UPDATE/DELETE は 08 で REVOKE)。SoT = 本テーブル (在庫数量の唯一の真実)。訂正は逆符号行の追記で行う';

INSERT INTO inventory_movement (movement_no, movement_type, product_name, sku_snapshot, quantity, warehouse_id, moved_at, reason)
SELECT s.movement_no, s.movement_type, s.product_name, s.sku_snapshot, s.quantity, w.id, s.moved_at, s.reason
FROM (VALUES
    -- 旧 inbound_records (4 件)
    ('IN-260615-01',  'inbound',   '婦人コンフォートサンダル',   'FA2071F',  540.00, '007', TIMESTAMPTZ '2026-06-15 10:00+09', NULL),
    ('IN-260616-01',  'inbound',   '婦人ショートブーツ',         'FB3082S',  420.00, '007', TIMESTAMPTZ '2026-06-16 10:00+09', NULL),
    ('IN-260617-01',  'inbound',   '紳士ビジネスサンダル',       'MC1050B',  300.00, '007', TIMESTAMPTZ '2026-06-17 10:00+09', NULL),
    ('IN-260618-01',  'inbound',   'ルームシューズ ボア',        'RS4010B',  640.00, '007', TIMESTAMPTZ '2026-06-18 10:00+09', NULL),
    -- 旧 outbound_records (4 件): 出荷先は reason に保持 (destination の正規化は出荷モジュール本実装時)
    ('OUT-260619-01', 'outbound',  '婦人コンフォートサンダル',   'FA2071F', -220.00, '007', TIMESTAMPTZ '2026-06-19 14:00+09', '出荷先: しまむらセンター'),
    ('OUT-260619-02', 'outbound',  '婦人スタイリッシュパンプス', 'FP2260P', -180.00, '007', TIMESTAMPTZ '2026-06-19 14:30+09', '出荷先: AEONセンター'),
    ('OUT-260620-01', 'outbound',  '婦人ショートブーツ',         'FB3082S', -160.00, '007', TIMESTAMPTZ '2026-06-20 14:00+09', '出荷先: しまむらセンター'),
    ('OUT-260621-01', 'outbound',  '婦人ヘルスウォーカー',       'FW1130H',  -90.00, '007', TIMESTAMPTZ '2026-06-21 14:00+09', '出荷先: AEONセンター'),
    -- 旧 stock_adjustments (4 件)
    ('ADJ-260618-01', 'adjust',    '婦人コンフォートサンダル',   'FA2071F',   -6.00, '007', TIMESTAMPTZ '2026-06-18 16:00+09', '不良品廃棄'),
    ('ADJ-260619-01', 'adjust',    '紳士ビジネスサンダル',       'MC1050B',   12.00, '007', TIMESTAMPTZ '2026-06-19 16:00+09', '返品入庫'),
    ('ADJ-260620-01', 'adjust',    'クッションマット',           'CM0900M',   -3.00, '007', TIMESTAMPTZ '2026-06-20 16:00+09', '汚損'),
    ('ADJ-260620-02', 'adjust',    '婦人ショートブーツ',         'FB3082S',    4.00, '007', TIMESTAMPTZ '2026-06-20 16:30+09', '数量訂正'),
    -- 旧 stocktaking_adjustments (4 件): quantity = 実地 − 帳簿
    ('CNT-260630-01', 'stocktake', '婦人コンフォートサンダル',   'FA2071F',   -3.00, '007', TIMESTAMPTZ '2026-06-30 17:00+09', '棚卸差異 (帳簿 318 / 実地 315)'),
    ('CNT-260630-02', 'stocktake', '婦人スタイリッシュパンプス', 'FP2260P',    0.00, '007', TIMESTAMPTZ '2026-06-30 17:05+09', '棚卸差異なし (帳簿 252 / 実地 252)'),
    ('CNT-260630-03', 'stocktake', '婦人ショートブーツ',         'FB3082S',    4.00, '007', TIMESTAMPTZ '2026-06-30 17:10+09', '棚卸差異 (帳簿 196 / 実地 200)'),
    ('CNT-260630-04', 'stocktake', 'ルームシューズ ボア',        'RS4010B',   -8.00, '007', TIMESTAMPTZ '2026-06-30 17:15+09', '棚卸差異 (帳簿 480 / 実地 472)')
) AS s(movement_no, movement_type, product_name, sku_snapshot, quantity, warehouse_code, moved_at, reason)
JOIN warehouses w
  ON w.code = s.warehouse_code
 AND w.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, movement_no) DO NOTHING;

-- inventory_balance — 時点残高 (導出キャッシュ)
-- 三語峻別 (統合設計方針 §3.1): OLTP の残高キャッシュは inventory_balance を正式名とする。
CREATE TABLE IF NOT EXISTS inventory_balance (
    id            UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID          NOT NULL DEFAULT (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid REFERENCES tenant(tenant_id),
    product_name  VARCHAR(255)  NOT NULL,
    sku_snapshot  VARCHAR(16)   NULL,
    warehouse_id  UUID          NOT NULL REFERENCES warehouses(id),
    quantity      NUMERIC(12,2) NOT NULL,
    as_of         DATE          NOT NULL,
    created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_inventory_balance_tenant    ON inventory_balance (tenant_id);
CREATE INDEX IF NOT EXISTS idx_inventory_balance_warehouse ON inventory_balance (warehouse_id);
-- SKU 単位の一意性 (sku_snapshot NULL は '' に正規化して比較する式インデックス)
CREATE UNIQUE INDEX IF NOT EXISTS uq_inventory_balance_key
    ON inventory_balance (tenant_id, (COALESCE(sku_snapshot, '')), warehouse_id, as_of);
COMMENT ON TABLE inventory_balance IS
    '導出キャッシュ: 時点在庫残高。SoT = inventory_movement (残高は移動履歴の再集計でいつでも復元可能)。SoT 書込 (movement 追記) が先、本キャッシュ更新が後';

-- シードは inventory_movement の累計 (期首残ゼロ前提) と一致させる。
-- 累計が負になる品目 (入荷記録が移行対象外) はキャッシュ行を持たない。
INSERT INTO inventory_balance (product_name, sku_snapshot, warehouse_id, quantity, as_of)
SELECT s.product_name, s.sku_snapshot, w.id, s.quantity, s.as_of
FROM (VALUES
    -- 540 - 6 - 220 - 3 = 311
    ('婦人コンフォートサンダル', 'FA2071F', '007', 311.00, DATE '2026-06-30'),
    -- 420 + 4 - 160 + 4 = 268
    ('婦人ショートブーツ',       'FB3082S', '007', 268.00, DATE '2026-06-30'),
    -- 300 + 12 = 312
    ('紳士ビジネスサンダル',     'MC1050B', '007', 312.00, DATE '2026-06-30'),
    -- 640 - 8 = 632
    ('ルームシューズ ボア',      'RS4010B', '007', 632.00, DATE '2026-06-30')
) AS s(product_name, sku_snapshot, warehouse_code, quantity, as_of)
JOIN warehouses w
  ON w.code = s.warehouse_code
 AND w.tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid
ON CONFLICT (tenant_id, (COALESCE(sku_snapshot, '')), warehouse_id, as_of) DO NOTHING;
