// 業務拡張モジュール（販売管理/出荷/在庫管理）の表示用データ定義。
//
// 注: 各データは DB の対応テーブル（db/init/07-ops-data.sql で新設、サンプル投入済）と
// 同じ内容を保持する。現時点では表示用に本定数を参照する（バックエンド API 連携は次段階）。
// API 連携時は OpsTable をフェッチに差し替えるだけでよいよう、列定義（columns）と
// 行（rows）の形を API レスポンス想定に合わせている。各 dataset の `table` が参照先テーブル名。

export interface OpsColumn {
  key: string
  label: string
  align?: 'right'
}

export interface OpsDataset {
  title: string
  note: string
  /** 対応する DB テーブル名（db/init/07-ops-data.sql） */
  table: string
  columns: OpsColumn[]
  rows: Record<string, string | number>[]
}

export const OPS_DATASETS: Record<string, OpsDataset> = {
  // ── 販売管理: 売上 ─────────────────────────────────
  sales: {
    title: '売上',
    note: '取引先別の受注・売上計上を管理します。',
    table: 'sales_orders',
    columns: [
      { key: 'order_no', label: '受注番号' },
      { key: 'customer_name', label: '得意先' },
      { key: 'order_date', label: '売上日' },
      { key: 'total_amount', label: '売上金額', align: 'right' },
      { key: 'status', label: '状態' },
    ],
    rows: [
      { order_no: 'SO-26-0001', customer_name: 'しまむら', order_date: '2026-06-01', total_amount: '¥1,842,000', status: '出荷済' },
      { order_no: 'SO-26-0002', customer_name: 'AEON', order_date: '2026-06-05', total_amount: '¥996,000', status: '受注' },
      { order_no: 'SO-26-0003', customer_name: 'KEYUCA', order_date: '2026-06-08', total_amount: '¥423,500', status: '引当済' },
      { order_no: 'SO-26-0004', customer_name: 'しまむら', order_date: '2026-06-12', total_amount: '¥2,310,000', status: '受注' },
      { order_no: 'SO-26-0005', customer_name: 'ベルメゾン', order_date: '2026-06-15', total_amount: '¥587,400', status: '出荷済' },
      { order_no: 'SO-26-0006', customer_name: 'AEON', order_date: '2026-06-18', total_amount: '¥134,200', status: '取消' },
    ],
  },

  // ── 販売管理: 請求 ─────────────────────────────────
  'sales-billing': {
    title: '請求',
    note: '得意先への請求書発行・入金予定を管理します。',
    table: 'billing_invoices',
    columns: [
      { key: 'invoice_no', label: '請求書番号' },
      { key: 'customer_name', label: '得意先' },
      { key: 'invoice_date', label: '請求日' },
      { key: 'invoice_amount', label: '請求金額', align: 'right' },
      { key: 'due_date', label: '入金予定日' },
      { key: 'status', label: 'ステータス' },
    ],
    rows: [
      { invoice_no: 'INV-26-0501', customer_name: 'しまむら', invoice_date: '2026-05-31', invoice_amount: '¥3,652,000', due_date: '2026-06-30', status: '入金済' },
      { invoice_no: 'INV-26-0502', customer_name: 'AEON', invoice_date: '2026-05-31', invoice_amount: '¥1,130,200', due_date: '2026-06-30', status: '一部入金' },
      { invoice_no: 'INV-26-0601', customer_name: 'しまむら', invoice_date: '2026-06-30', invoice_amount: '¥4,152,000', due_date: '2026-07-31', status: '請求済' },
      { invoice_no: 'INV-26-0602', customer_name: 'KEYUCA', invoice_date: '2026-06-30', invoice_amount: '¥423,500', due_date: '2026-07-31', status: '請求済' },
      { invoice_no: 'INV-26-0603', customer_name: 'ベルメゾン', invoice_date: '2026-06-30', invoice_amount: '¥587,400', due_date: '2026-07-31', status: '発行待ち' },
    ],
  },

  // ── 販売管理: 入金 ─────────────────────────────────
  'sales-payments': {
    title: '入金',
    note: '得意先からの入金実績と請求消込の状況を管理します。',
    table: 'payment_receipts',
    columns: [
      { key: 'payment_no', label: '入金番号' },
      { key: 'payment_date', label: '入金日' },
      { key: 'customer_name', label: '得意先' },
      { key: 'payment_amount', label: '入金額', align: 'right' },
      { key: 'method', label: '入金方法' },
      { key: 'status', label: '消込状況' },
    ],
    rows: [
      { payment_no: 'PAY-26-0601', payment_date: '2026-06-30', customer_name: 'しまむら', payment_amount: '¥3,652,000', method: '銀行振込', status: '消込済' },
      { payment_no: 'PAY-26-0602', payment_date: '2026-06-30', customer_name: 'AEON', payment_amount: '¥600,000', method: '銀行振込', status: '一部消込' },
      { payment_no: 'PAY-26-0603', payment_date: '2026-06-28', customer_name: 'KEYUCA', payment_amount: '¥381,200', method: '手形', status: '消込済' },
      { payment_no: 'PAY-26-0604', payment_date: '2026-06-25', customer_name: 'ベルメゾン', payment_amount: '¥220,000', method: '相殺', status: '未消込' },
    ],
  },

  // ── 販売管理: 債権（売掛） ──────────────────────────
  'sales-receivables': {
    title: '債権',
    note: '得意先別の売掛残高と回収期日・滞留状況を管理します。',
    table: 'accounts_receivable',
    columns: [
      { key: 'customer_name', label: '得意先' },
      { key: 'balance', label: '売掛残高', align: 'right' },
      { key: 'due_date', label: '期日' },
      { key: 'overdue_days', label: '滞留日数', align: 'right' },
      { key: 'status', label: 'ステータス' },
    ],
    rows: [
      { customer_name: 'しまむら', balance: '¥4,152,000', due_date: '2026-07-31', overdue_days: 0, status: '正常' },
      { customer_name: 'AEON', balance: '¥530,200', due_date: '2026-06-30', overdue_days: 0, status: '正常' },
      { customer_name: 'KEYUCA', balance: '¥423,500', due_date: '2026-07-31', overdue_days: 0, status: '正常' },
      { customer_name: 'ベルメゾン', balance: '¥367,400', due_date: '2026-05-31', overdue_days: 24, status: '滞留' },
      { customer_name: 'ニッセン', balance: '¥88,000', due_date: '2026-04-30', overdue_days: 55, status: '督促中' },
    ],
  },

  // ── 出荷 ──────────────────────────────────────────
  'shipping-receive': {
    title: 'データ受信',
    note: '取引先 EDI / EOS からの出荷データ受信状況。',
    table: 'shipping_receipts',
    columns: [
      { key: 'receipt_no', label: '受信番号' },
      { key: 'source', label: '送信元' },
      { key: 'received_at', label: '受信日時' },
      { key: 'record_count', label: '件数', align: 'right' },
      { key: 'status', label: '状態' },
    ],
    rows: [
      { receipt_no: 'RCV-260619-001', source: 'しまむら Web-EDI', received_at: '2026-06-19 06:30', record_count: 128, status: '取込済' },
      { receipt_no: 'RCV-260619-002', source: 'AEON EOS', received_at: '2026-06-19 07:10', record_count: 64, status: '取込済' },
      { receipt_no: 'RCV-260620-001', source: 'しまむら Web-EDI', received_at: '2026-06-20 06:28', record_count: 142, status: '受信済' },
      { receipt_no: 'RCV-260620-002', source: 'KEYUCA CSV', received_at: '2026-06-20 09:05', record_count: 31, status: 'エラー' },
      { receipt_no: 'RCV-260621-001', source: 'AEON EOS', received_at: '2026-06-21 07:02', record_count: 58, status: '受信済' },
    ],
  },
  'shipping-picking': {
    title: 'ピッキングリスト',
    note: '倉庫別のピッキング指示と進捗。',
    table: 'picking_lists',
    columns: [
      { key: 'list_no', label: 'リスト番号' },
      { key: 'warehouse', label: '倉庫' },
      { key: 'item_count', label: '明細数', align: 'right' },
      { key: 'total_qty', label: '総数量', align: 'right' },
      { key: 'status', label: '状態' },
      { key: 'created_at', label: '作成日時' },
    ],
    rows: [
      { list_no: 'PCK-260620-01', warehouse: '本社倉庫', item_count: 24, total_qty: 860, status: '完了', created_at: '2026-06-20 08:00' },
      { list_no: 'PCK-260620-02', warehouse: 'しまむらセンター', item_count: 18, total_qty: 540, status: 'ピッキング中', created_at: '2026-06-20 08:30' },
      { list_no: 'PCK-260621-01', warehouse: '本社倉庫', item_count: 31, total_qty: 1240, status: '未着手', created_at: '2026-06-21 08:00' },
      { list_no: 'PCK-260621-02', warehouse: '本社倉庫', item_count: 9, total_qty: 210, status: '未着手', created_at: '2026-06-21 08:15' },
    ],
  },
  'shipping-reports': {
    title: '帳票出力',
    note: '納品書・出荷指示書・荷札等の出力履歴。',
    table: 'report_outputs',
    columns: [
      { key: 'output_no', label: '出力番号' },
      { key: 'report_name', label: '帳票名' },
      { key: 'format', label: '形式' },
      { key: 'operator', label: '担当者' },
      { key: 'output_at', label: '出力日時' },
    ],
    rows: [
      { output_no: 'RPT-260620-001', report_name: '納品書', format: 'PDF', operator: '今尾 雅広', output_at: '2026-06-20 10:15' },
      { output_no: 'RPT-260620-002', report_name: '出荷指示書', format: 'PDF', operator: '南 雄介', output_at: '2026-06-20 10:20' },
      { output_no: 'RPT-260620-003', report_name: '荷札ラベル', format: 'PDF', operator: '今尾 雅広', output_at: '2026-06-20 10:40' },
      { output_no: 'RPT-260621-001', report_name: '出荷一覧表', format: 'Excel', operator: '斉藤 摂次', output_at: '2026-06-21 11:00' },
    ],
  },
  'shipping-asn': {
    title: 'ASN送信',
    note: '取引先への事前出荷明細（ASN）送信状況。',
    table: 'asn_transmissions',
    columns: [
      { key: 'asn_no', label: 'ASN番号' },
      { key: 'destination', label: '送信先' },
      { key: 'slip_count', label: '伝票数', align: 'right' },
      { key: 'sent_at', label: '送信日時' },
      { key: 'status', label: '状態' },
    ],
    rows: [
      { asn_no: 'ASN-260619-01', destination: 'しまむら', slip_count: 12, sent_at: '2026-06-19 15:00', status: '送信済' },
      { asn_no: 'ASN-260620-01', destination: 'AEON', slip_count: 8, sent_at: '2026-06-20 15:10', status: '送信済' },
      { asn_no: 'ASN-260620-02', destination: 'しまむら', slip_count: 15, sent_at: '2026-06-20 16:30', status: '送信済' },
      { asn_no: 'ASN-260621-01', destination: 'KEYUCA', slip_count: 4, sent_at: '2026-06-21 14:20', status: '送信待ち' },
    ],
  },

  // ── 在庫管理 ──────────────────────────────────────
  'inventory-inbound': {
    title: '入荷情報',
    note: '倉庫への入荷実績。',
    table: 'inbound_records',
    columns: [
      { key: 'slip_no', label: '伝票番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'quantity', label: '数量', align: 'right' },
      { key: 'warehouse', label: '倉庫' },
      { key: 'received_at', label: '入荷日' },
    ],
    rows: [
      { slip_no: 'IN-260615-01', product_name: '婦人コンフォートサンダル', quantity: 540, warehouse: '本社倉庫', received_at: '2026-06-15' },
      { slip_no: 'IN-260616-01', product_name: '婦人ショートブーツ', quantity: 420, warehouse: '本社倉庫', received_at: '2026-06-16' },
      { slip_no: 'IN-260617-01', product_name: '紳士ビジネスサンダル', quantity: 300, warehouse: '本社倉庫', received_at: '2026-06-17' },
      { slip_no: 'IN-260618-01', product_name: 'ルームシューズ ボア', quantity: 640, warehouse: '本社倉庫', received_at: '2026-06-18' },
    ],
  },
  'inventory-outbound': {
    title: '出荷情報',
    note: '倉庫からの出荷実績。',
    table: 'outbound_records',
    columns: [
      { key: 'slip_no', label: '伝票番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'quantity', label: '数量', align: 'right' },
      { key: 'destination', label: '納品先' },
      { key: 'shipped_at', label: '出荷日' },
    ],
    rows: [
      { slip_no: 'OUT-260619-01', product_name: '婦人コンフォートサンダル', quantity: 220, destination: 'しまむらセンター', shipped_at: '2026-06-19' },
      { slip_no: 'OUT-260619-02', product_name: '婦人スタイリッシュパンプス', quantity: 180, destination: 'AEONセンター', shipped_at: '2026-06-19' },
      { slip_no: 'OUT-260620-01', product_name: '婦人ショートブーツ', quantity: 160, destination: 'しまむらセンター', shipped_at: '2026-06-20' },
      { slip_no: 'OUT-260621-01', product_name: '婦人ヘルスウォーカー', quantity: 90, destination: 'AEONセンター', shipped_at: '2026-06-21' },
    ],
  },
  'inventory-adjustment': {
    title: '在庫調整',
    note: '不良・返品・訂正等による在庫の増減。',
    table: 'stock_adjustments',
    columns: [
      { key: 'adjust_no', label: '調整番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'delta', label: '増減', align: 'right' },
      { key: 'reason', label: '理由' },
      { key: 'adjusted_at', label: '調整日' },
    ],
    rows: [
      { adjust_no: 'ADJ-260618-01', product_name: '婦人コンフォートサンダル', delta: '-6', reason: '不良品廃棄', adjusted_at: '2026-06-18' },
      { adjust_no: 'ADJ-260619-01', product_name: '紳士ビジネスサンダル', delta: '+12', reason: '返品入庫', adjusted_at: '2026-06-19' },
      { adjust_no: 'ADJ-260620-01', product_name: 'クッションマット', delta: '-3', reason: '汚損', adjusted_at: '2026-06-20' },
      { adjust_no: 'ADJ-260620-02', product_name: '婦人ショートブーツ', delta: '+4', reason: '数量訂正', adjusted_at: '2026-06-20' },
    ],
  },
  'inventory-stocktaking': {
    title: '棚卸調整',
    note: '棚卸の帳簿在庫と実地在庫の差異。',
    table: 'stocktaking_adjustments',
    columns: [
      { key: 'count_no', label: '棚卸番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'book_qty', label: '帳簿在庫', align: 'right' },
      { key: 'actual_qty', label: '実地在庫', align: 'right' },
      { key: 'diff', label: '差異', align: 'right' },
      { key: 'counted_at', label: '棚卸日' },
    ],
    rows: [
      { count_no: 'CNT-260630-01', product_name: '婦人コンフォートサンダル', book_qty: 318, actual_qty: 315, diff: '-3', counted_at: '2026-06-30' },
      { count_no: 'CNT-260630-02', product_name: '婦人スタイリッシュパンプス', book_qty: 252, actual_qty: 252, diff: '0', counted_at: '2026-06-30' },
      { count_no: 'CNT-260630-03', product_name: '婦人ショートブーツ', book_qty: 196, actual_qty: 200, diff: '+4', counted_at: '2026-06-30' },
      { count_no: 'CNT-260630-04', product_name: 'ルームシューズ ボア', book_qty: 480, actual_qty: 472, diff: '-8', counted_at: '2026-06-30' },
    ],
  },
}
