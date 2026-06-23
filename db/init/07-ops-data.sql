-- ============================================================================
-- 07-ops-data.sql — 業務拡張モジュール(販売管理/出荷/在庫管理)のテーブル + サンプルデータ
-- ----------------------------------------------------------------------------
-- 目的: ホームに追加する新メニュー（販売管理・出荷・在庫管理）の各画面が参照する
--       軽量な業務テーブルを新設し、表示用サンプルデータを投入する。
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

SET TIMEZONE = 'Asia/Tokyo';

-- ────────────────────────────────────────────────────────────────────────────
-- 販売管理: 受注/売上
-- ────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS sales_orders (
    id            BIGSERIAL PRIMARY KEY,
    order_no      VARCHAR(16)   NOT NULL UNIQUE,
    customer_name VARCHAR(255)  NOT NULL,
    order_date    DATE          NOT NULL,
    total_amount  NUMERIC(14,2) NOT NULL DEFAULT 0,
    status        VARCHAR(16)   NOT NULL DEFAULT '受注',
    created_at    TIMESTAMP     NOT NULL DEFAULT NOW()
);
INSERT INTO sales_orders (order_no, customer_name, order_date, total_amount, status) VALUES
    ('SO-26-0001', 'しまむら',  DATE '2026-06-01', 1842000.00, '出荷済'),
    ('SO-26-0002', 'AEON',     DATE '2026-06-05',  996000.00, '受注'),
    ('SO-26-0003', 'KEYUCA',   DATE '2026-06-08',  423500.00, '引当済'),
    ('SO-26-0004', 'しまむら',  DATE '2026-06-12', 2310000.00, '受注'),
    ('SO-26-0005', 'ベルメゾン', DATE '2026-06-15',  587400.00, '出荷済'),
    ('SO-26-0006', 'AEON',     DATE '2026-06-18',  134200.00, '取消')
ON CONFLICT (order_no) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- 出荷: データ受信 / ピッキングリスト / 帳票出力 / ASN送信
-- ────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shipping_receipts (
    id           BIGSERIAL PRIMARY KEY,
    receipt_no   VARCHAR(20)  NOT NULL UNIQUE,
    source       VARCHAR(64)  NOT NULL,   -- EDI種別/送信元
    received_at  TIMESTAMP    NOT NULL,
    record_count INTEGER      NOT NULL DEFAULT 0,
    status       VARCHAR(16)  NOT NULL DEFAULT '受信済'
);
INSERT INTO shipping_receipts (receipt_no, source, received_at, record_count, status) VALUES
    ('RCV-260619-001', 'しまむら Web-EDI', TIMESTAMP '2026-06-19 06:30', 128, '取込済'),
    ('RCV-260619-002', 'AEON EOS',         TIMESTAMP '2026-06-19 07:10',  64, '取込済'),
    ('RCV-260620-001', 'しまむら Web-EDI', TIMESTAMP '2026-06-20 06:28', 142, '受信済'),
    ('RCV-260620-002', 'KEYUCA CSV',       TIMESTAMP '2026-06-20 09:05',  31, 'エラー'),
    ('RCV-260621-001', 'AEON EOS',         TIMESTAMP '2026-06-21 07:02',  58, '受信済')
ON CONFLICT (receipt_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS picking_lists (
    id          BIGSERIAL PRIMARY KEY,
    list_no     VARCHAR(20)  NOT NULL UNIQUE,
    warehouse   VARCHAR(64)  NOT NULL,
    item_count  INTEGER      NOT NULL DEFAULT 0,
    total_qty   INTEGER      NOT NULL DEFAULT 0,
    status      VARCHAR(16)  NOT NULL DEFAULT '未着手',
    created_at  TIMESTAMP    NOT NULL DEFAULT NOW()
);
INSERT INTO picking_lists (list_no, warehouse, item_count, total_qty, status, created_at) VALUES
    ('PCK-260620-01', '本社倉庫',       24, 860, '完了',   TIMESTAMP '2026-06-20 08:00'),
    ('PCK-260620-02', 'しまむらセンター', 18, 540, 'ピッキング中', TIMESTAMP '2026-06-20 08:30'),
    ('PCK-260621-01', '本社倉庫',       31, 1240, '未着手', TIMESTAMP '2026-06-21 08:00'),
    ('PCK-260621-02', '本社倉庫',        9, 210, '未着手', TIMESTAMP '2026-06-21 08:15')
ON CONFLICT (list_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS report_outputs (
    id          BIGSERIAL PRIMARY KEY,
    output_no   VARCHAR(20)  NOT NULL UNIQUE,
    report_name VARCHAR(128) NOT NULL,
    format      VARCHAR(8)   NOT NULL DEFAULT 'PDF',
    operator    VARCHAR(64)  NOT NULL,
    output_at   TIMESTAMP    NOT NULL
);
INSERT INTO report_outputs (output_no, report_name, format, operator, output_at) VALUES
    ('RPT-260620-001', '納品書',        'PDF',   '今尾 雅広', TIMESTAMP '2026-06-20 10:15'),
    ('RPT-260620-002', '出荷指示書',    'PDF',   '南 雄介',  TIMESTAMP '2026-06-20 10:20'),
    ('RPT-260620-003', '荷札ラベル',    'PDF',   '今尾 雅広', TIMESTAMP '2026-06-20 10:40'),
    ('RPT-260621-001', '出荷一覧表',    'Excel', '斉藤 摂次', TIMESTAMP '2026-06-21 11:00')
ON CONFLICT (output_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS asn_transmissions (
    id          BIGSERIAL PRIMARY KEY,
    asn_no      VARCHAR(20)  NOT NULL UNIQUE,
    destination VARCHAR(64)  NOT NULL,
    slip_count  INTEGER      NOT NULL DEFAULT 0,
    sent_at     TIMESTAMP    NOT NULL,
    status      VARCHAR(16)  NOT NULL DEFAULT '送信済'
);
INSERT INTO asn_transmissions (asn_no, destination, slip_count, sent_at, status) VALUES
    ('ASN-260619-01', 'しまむら', 12, TIMESTAMP '2026-06-19 15:00', '送信済'),
    ('ASN-260620-01', 'AEON',    8,  TIMESTAMP '2026-06-20 15:10', '送信済'),
    ('ASN-260620-02', 'しまむら', 15, TIMESTAMP '2026-06-20 16:30', '送信済'),
    ('ASN-260621-01', 'KEYUCA',  4,  TIMESTAMP '2026-06-21 14:20', '送信待ち')
ON CONFLICT (asn_no) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- 在庫管理: 入荷情報 / 出荷情報 / 在庫調整 / 棚卸調整
-- ────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS inbound_records (
    id           BIGSERIAL PRIMARY KEY,
    slip_no      VARCHAR(20)  NOT NULL UNIQUE,
    product_name VARCHAR(255) NOT NULL,
    quantity     INTEGER      NOT NULL,
    warehouse    VARCHAR(64)  NOT NULL,
    received_at  DATE         NOT NULL
);
INSERT INTO inbound_records (slip_no, product_name, quantity, warehouse, received_at) VALUES
    ('IN-260615-01', '婦人コンフォートサンダル', 540, '本社倉庫', DATE '2026-06-15'),
    ('IN-260616-01', '婦人ショートブーツ',       420, '本社倉庫', DATE '2026-06-16'),
    ('IN-260617-01', '紳士ビジネスサンダル',     300, '本社倉庫', DATE '2026-06-17'),
    ('IN-260618-01', 'ルームシューズ ボア',      640, '本社倉庫', DATE '2026-06-18')
ON CONFLICT (slip_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS outbound_records (
    id           BIGSERIAL PRIMARY KEY,
    slip_no      VARCHAR(20)  NOT NULL UNIQUE,
    product_name VARCHAR(255) NOT NULL,
    quantity     INTEGER      NOT NULL,
    destination  VARCHAR(64)  NOT NULL,
    shipped_at   DATE         NOT NULL
);
INSERT INTO outbound_records (slip_no, product_name, quantity, destination, shipped_at) VALUES
    ('OUT-260619-01', '婦人コンフォートサンダル', 220, 'しまむらセンター', DATE '2026-06-19'),
    ('OUT-260619-02', '婦人スタイリッシュパンプス', 180, 'AEONセンター',   DATE '2026-06-19'),
    ('OUT-260620-01', '婦人ショートブーツ',       160, 'しまむらセンター', DATE '2026-06-20'),
    ('OUT-260621-01', '婦人ヘルスウォーカー',      90, 'AEONセンター',   DATE '2026-06-21')
ON CONFLICT (slip_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS stock_adjustments (
    id           BIGSERIAL PRIMARY KEY,
    adjust_no    VARCHAR(20)  NOT NULL UNIQUE,
    product_name VARCHAR(255) NOT NULL,
    delta        INTEGER      NOT NULL,   -- +入 / -出
    reason       VARCHAR(128) NOT NULL,
    adjusted_at  DATE         NOT NULL
);
INSERT INTO stock_adjustments (adjust_no, product_name, delta, reason, adjusted_at) VALUES
    ('ADJ-260618-01', '婦人コンフォートサンダル', -6, '不良品廃棄',     DATE '2026-06-18'),
    ('ADJ-260619-01', '紳士ビジネスサンダル',     12, '返品入庫',       DATE '2026-06-19'),
    ('ADJ-260620-01', 'クッションマット',         -3, '汚損',           DATE '2026-06-20'),
    ('ADJ-260620-02', '婦人ショートブーツ',        4, '数量訂正',       DATE '2026-06-20')
ON CONFLICT (adjust_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS stocktaking_adjustments (
    id           BIGSERIAL PRIMARY KEY,
    count_no     VARCHAR(20)  NOT NULL UNIQUE,
    product_name VARCHAR(255) NOT NULL,
    book_qty     INTEGER      NOT NULL,   -- 帳簿在庫
    actual_qty   INTEGER      NOT NULL,   -- 実地在庫
    diff         INTEGER      GENERATED ALWAYS AS (actual_qty - book_qty) STORED,
    counted_at   DATE         NOT NULL
);
INSERT INTO stocktaking_adjustments (count_no, product_name, book_qty, actual_qty, counted_at) VALUES
    ('CNT-260630-01', '婦人コンフォートサンダル', 318, 315, DATE '2026-06-30'),
    ('CNT-260630-02', '婦人スタイリッシュパンプス', 252, 252, DATE '2026-06-30'),
    ('CNT-260630-03', '婦人ショートブーツ',       196, 200, DATE '2026-06-30'),
    ('CNT-260630-04', 'ルームシューズ ボア',      480, 472, DATE '2026-06-30')
ON CONFLICT (count_no) DO NOTHING;
