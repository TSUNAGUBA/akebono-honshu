// 業務拡張モジュール（販売管理/出荷/在庫管理）の表示用データ定義。
//
// 注: 各データは DB の対応テーブル（db/init/07-ops-data.sql、正規化再設計後の 07 プロトタイプ層）と
// 同じ内容を保持する。現時点では表示用に本定数を参照する（バックエンド API 連携は次段階）。
// API 連携時は OpsTable をフェッチに差し替えるだけでよいよう、列定義（columns）と
// 行（rows）の形を API レスポンス想定に合わせている。各 dataset の `table` が参照先テーブル名。
//
// 正規化再設計 (07 プロトタイプ層) の反映点:
// - customer マスタ新設 (OPS_CUSTOMERS)。各行は customer_code を持ち、customer_name は
//   マスタから解決した値 (JOIN 相当) を保持する。
// - ステータス/区分は英 snake_case のコードで保持し、表示は対応表 (××_LABELS) を
//   ラベル関数 opsCodeLabel() 経由で日本語ラベルに変換する (列の codeLabels に対応表を指定)。
// - 旧 inbound/outbound/stock_adjustments/stocktaking_adjustments の 4 テーブルは
//   inventory_movement (movement_type + 符号付き数量) に統合された。在庫系 4 ページは
//   movement_type でフィルタした INVENTORY_MOVEMENTS を表示する (ページ構成・URL は維持)。
// - accounts_receivable は「請求 − 消込」の導出ビューになった (下記 sales-receivables 参照)。

export interface OpsColumn {
  key: string
  label: string
  align?: 'right'
  /**
   * コード→日本語ラベルの対応表 (ステータス・入金方法等)。
   * 指定した列は opsCodeLabel() 経由でコードをラベルに変換して表示する。
   */
  codeLabels?: Record<string, string>
}

export interface OpsDataset {
  title: string
  note: string
  /** 対応する DB テーブル名（db/init/07-ops-data.sql） */
  table: string
  columns: OpsColumn[]
  rows: Record<string, string | number>[]
}

// ── customer マスタ（新設） ─────────────────────────
export interface OpsCustomer {
  code: string
  name: string
}

export const OPS_CUSTOMERS: OpsCustomer[] = [
  { code: '001', name: 'しまむら' },
  { code: '002', name: 'AEON' },
  { code: '003', name: 'KEYUCA' },
  { code: '004', name: 'ベルメゾン' },
  { code: '005', name: 'ニッセン' },
]

/** customer_code → customer_name 解決 (JOIN 相当)。未知コードはコードのまま返す (原則4)。 */
export const customerNameOf = (code: string): string =>
  OPS_CUSTOMERS.find((c) => c.code === code)?.name ?? code

// 行に customer_code + customer_name (マスタ解決値) を持たせる共通ヘルパー。
const customerRow = (code: string): { customer_code: string; customer_name: string } => ({
  customer_code: code,
  customer_name: customerNameOf(code),
})

// ── ステータス コード → 日本語ラベル 対応表 ─────────────
// DB はコード (英 snake_case) の SoT。表示ラベルはフロント側の本対応表が持つ。
export const SALES_ORDER_STATUS_LABELS: Record<string, string> = {
  received: '受注',
  allocated: '引当済',
  shipped: '出荷済',
  cancelled: '取消',
}

export const BILLING_INVOICE_STATUS_LABELS: Record<string, string> = {
  draft: '発行待ち',
  issued: '請求済',
  partially_paid: '一部入金',
  paid: '入金済',
}

export const PAYMENT_RECEIPT_METHOD_LABELS: Record<string, string> = {
  bank_transfer: '銀行振込',
  promissory_note: '手形',
  offset: '相殺',
}

export const PAYMENT_RECEIPT_STATUS_LABELS: Record<string, string> = {
  unallocated: '未消込',
  partially_allocated: '一部消込',
  allocated: '消込済',
}

export const SHIPPING_RECEIPT_STATUS_LABELS: Record<string, string> = {
  received: '受信済',
  imported: '取込済',
  error: 'エラー',
}

export const PICKING_LIST_STATUS_LABELS: Record<string, string> = {
  not_started: '未着手',
  picking: 'ピッキング中',
  completed: '完了',
}

export const ASN_TRANSMISSION_STATUS_LABELS: Record<string, string> = {
  pending: '送信待ち',
  sent: '送信済',
}

export const INVENTORY_MOVEMENT_TYPE_LABELS: Record<string, string> = {
  inbound: '入庫',
  outbound: '出庫',
  adjust: '在庫調整',
  stocktake: '棚卸',
}

/**
 * コード → 表示ラベルのラベル関数。ページ (OpsTable) はこの関数経由で表示する。
 * 未知コードはコードのまま返す (表示が壊れないフォールバック、原則4)。
 */
export const opsCodeLabel = (labels: Record<string, string> | undefined, code: string): string =>
  labels?.[code] ?? code

// ── inventory_movement（在庫移動 統合テーブル） ─────────
// 旧 inbound_records / outbound_records / stock_adjustments / stocktaking_adjustments の
// 4 テーブルを movement_type + 符号付き数量で統合。在庫系 4 ページは該当 type のみ表示する。
export type InventoryMovementType = 'inbound' | 'outbound' | 'adjust' | 'stocktake'

export interface InventoryMovementRow {
  movement_no: string
  movement_type: InventoryMovementType
  product_name: string
  /** 符号付き数量 (入庫 +、出庫 −、調整/棚卸は差分)。表示側で絶対値/±表現に整形する。 */
  quantity: number
  warehouse: string
  /** 相手先 (出庫の納品先等)。該当なしは空文字。 */
  counterparty: string
  /** 理由 (調整の事由、棚卸差異等)。該当なしは空文字。 */
  reason: string
  moved_at: string
}

export const INVENTORY_MOVEMENTS: InventoryMovementRow[] = [
  // 入庫 (movement_type=inbound、旧 inbound_records)
  { movement_no: 'IN-260615-01', movement_type: 'inbound', product_name: '婦人コンフォートサンダル', quantity: 540, warehouse: '本社倉庫', counterparty: '', reason: '', moved_at: '2026-06-15' },
  { movement_no: 'IN-260616-01', movement_type: 'inbound', product_name: '婦人ショートブーツ', quantity: 420, warehouse: '本社倉庫', counterparty: '', reason: '', moved_at: '2026-06-16' },
  { movement_no: 'IN-260617-01', movement_type: 'inbound', product_name: '紳士ビジネスサンダル', quantity: 300, warehouse: '本社倉庫', counterparty: '', reason: '', moved_at: '2026-06-17' },
  { movement_no: 'IN-260618-01', movement_type: 'inbound', product_name: 'ルームシューズ ボア', quantity: 640, warehouse: '本社倉庫', counterparty: '', reason: '', moved_at: '2026-06-18' },
  // 出庫 (movement_type=outbound、旧 outbound_records。数量は符号付き −)
  { movement_no: 'OUT-260619-01', movement_type: 'outbound', product_name: '婦人コンフォートサンダル', quantity: -220, warehouse: '本社倉庫', counterparty: 'しまむらセンター', reason: '', moved_at: '2026-06-19' },
  { movement_no: 'OUT-260619-02', movement_type: 'outbound', product_name: '婦人スタイリッシュパンプス', quantity: -180, warehouse: '本社倉庫', counterparty: 'AEONセンター', reason: '', moved_at: '2026-06-19' },
  { movement_no: 'OUT-260620-01', movement_type: 'outbound', product_name: '婦人ショートブーツ', quantity: -160, warehouse: '本社倉庫', counterparty: 'しまむらセンター', reason: '', moved_at: '2026-06-20' },
  { movement_no: 'OUT-260621-01', movement_type: 'outbound', product_name: '婦人ヘルスウォーカー', quantity: -90, warehouse: '本社倉庫', counterparty: 'AEONセンター', reason: '', moved_at: '2026-06-21' },
  // 在庫調整 (movement_type=adjust、旧 stock_adjustments。数量は増減差分)
  { movement_no: 'ADJ-260618-01', movement_type: 'adjust', product_name: '婦人コンフォートサンダル', quantity: -6, warehouse: '本社倉庫', counterparty: '', reason: '不良品廃棄', moved_at: '2026-06-18' },
  { movement_no: 'ADJ-260619-01', movement_type: 'adjust', product_name: '紳士ビジネスサンダル', quantity: 12, warehouse: '本社倉庫', counterparty: '', reason: '返品入庫', moved_at: '2026-06-19' },
  { movement_no: 'ADJ-260620-01', movement_type: 'adjust', product_name: 'クッションマット', quantity: -3, warehouse: '本社倉庫', counterparty: '', reason: '汚損', moved_at: '2026-06-20' },
  { movement_no: 'ADJ-260620-02', movement_type: 'adjust', product_name: '婦人ショートブーツ', quantity: 4, warehouse: '本社倉庫', counterparty: '', reason: '数量訂正', moved_at: '2026-06-20' },
  // 棚卸 (movement_type=stocktake、旧 stocktaking_adjustments。数量 = 実地 − 帳簿 の差異。差異 0 も棚卸実施記録として保持)
  { movement_no: 'CNT-260630-01', movement_type: 'stocktake', product_name: '婦人コンフォートサンダル', quantity: -3, warehouse: '本社倉庫', counterparty: '', reason: '棚卸差異', moved_at: '2026-06-30' },
  { movement_no: 'CNT-260630-02', movement_type: 'stocktake', product_name: '婦人スタイリッシュパンプス', quantity: 0, warehouse: '本社倉庫', counterparty: '', reason: '差異なし', moved_at: '2026-06-30' },
  { movement_no: 'CNT-260630-03', movement_type: 'stocktake', product_name: '婦人ショートブーツ', quantity: 4, warehouse: '本社倉庫', counterparty: '', reason: '棚卸差異', moved_at: '2026-06-30' },
  { movement_no: 'CNT-260630-04', movement_type: 'stocktake', product_name: 'ルームシューズ ボア', quantity: -8, warehouse: '本社倉庫', counterparty: '', reason: '棚卸差異', moved_at: '2026-06-30' },
]

/** movement_type で絞った在庫移動 (在庫系 4 ページの表示元)。 */
export const inventoryMovementsOf = (type: InventoryMovementType): InventoryMovementRow[] =>
  INVENTORY_MOVEMENTS.filter((m) => m.movement_type === type)

// 符号付き数量の表示整形 (従来の見た目を維持: 増減/差異は +12 / -6 / 0)。
const signedQty = (n: number): string => (n > 0 ? `+${n}` : String(n))

export const OPS_DATASETS: Record<string, OpsDataset> = {
  // ── 販売管理: 売上 ─────────────────────────────────
  sales: {
    title: '売上',
    note: '取引先別の受注・売上計上を管理します。',
    table: 'sales_order',
    columns: [
      { key: 'order_no', label: '受注番号' },
      { key: 'customer_name', label: '得意先' },
      { key: 'order_date', label: '売上日' },
      { key: 'total_amount', label: '売上金額', align: 'right' },
      { key: 'status', label: '状態', codeLabels: SALES_ORDER_STATUS_LABELS },
    ],
    rows: [
      { order_no: 'SO-26-0001', ...customerRow('001'), order_date: '2026-06-01', total_amount: '¥1,842,000', status: 'shipped' },
      { order_no: 'SO-26-0002', ...customerRow('002'), order_date: '2026-06-05', total_amount: '¥996,000', status: 'received' },
      { order_no: 'SO-26-0003', ...customerRow('003'), order_date: '2026-06-08', total_amount: '¥423,500', status: 'allocated' },
      { order_no: 'SO-26-0004', ...customerRow('001'), order_date: '2026-06-12', total_amount: '¥2,310,000', status: 'received' },
      { order_no: 'SO-26-0005', ...customerRow('004'), order_date: '2026-06-15', total_amount: '¥587,400', status: 'shipped' },
      { order_no: 'SO-26-0006', ...customerRow('002'), order_date: '2026-06-18', total_amount: '¥134,200', status: 'cancelled' },
    ],
  },

  // ── 販売管理: 請求 ─────────────────────────────────
  'sales-billing': {
    title: '請求',
    note: '得意先への請求書発行・入金予定を管理します。',
    table: 'billing_invoice',
    columns: [
      { key: 'invoice_no', label: '請求書番号' },
      { key: 'customer_name', label: '得意先' },
      { key: 'invoice_date', label: '請求日' },
      { key: 'invoice_amount', label: '請求金額', align: 'right' },
      { key: 'due_date', label: '入金予定日' },
      { key: 'status', label: 'ステータス', codeLabels: BILLING_INVOICE_STATUS_LABELS },
    ],
    rows: [
      { invoice_no: 'INV-26-0501', ...customerRow('001'), invoice_date: '2026-05-31', invoice_amount: '¥3,652,000', due_date: '2026-06-30', status: 'paid' },
      { invoice_no: 'INV-26-0502', ...customerRow('002'), invoice_date: '2026-05-31', invoice_amount: '¥1,130,200', due_date: '2026-06-30', status: 'partially_paid' },
      { invoice_no: 'INV-26-0601', ...customerRow('001'), invoice_date: '2026-06-30', invoice_amount: '¥4,152,000', due_date: '2026-07-31', status: 'issued' },
      { invoice_no: 'INV-26-0602', ...customerRow('003'), invoice_date: '2026-06-30', invoice_amount: '¥423,500', due_date: '2026-07-31', status: 'issued' },
      { invoice_no: 'INV-26-0603', ...customerRow('004'), invoice_date: '2026-06-30', invoice_amount: '¥587,400', due_date: '2026-07-31', status: 'draft' },
    ],
  },

  // ── 販売管理: 入金 ─────────────────────────────────
  'sales-payments': {
    title: '入金',
    note: '得意先からの入金実績と請求消込の状況を管理します。',
    table: 'payment_receipt',
    columns: [
      { key: 'payment_no', label: '入金番号' },
      { key: 'payment_date', label: '入金日' },
      { key: 'customer_name', label: '得意先' },
      { key: 'payment_amount', label: '入金額', align: 'right' },
      { key: 'method', label: '入金方法', codeLabels: PAYMENT_RECEIPT_METHOD_LABELS },
      { key: 'status', label: '消込状況', codeLabels: PAYMENT_RECEIPT_STATUS_LABELS },
    ],
    rows: [
      { payment_no: 'PAY-26-0601', payment_date: '2026-06-30', ...customerRow('001'), payment_amount: '¥3,652,000', method: 'bank_transfer', status: 'allocated' },
      { payment_no: 'PAY-26-0602', payment_date: '2026-06-30', ...customerRow('002'), payment_amount: '¥600,000', method: 'bank_transfer', status: 'partially_allocated' },
      { payment_no: 'PAY-26-0603', payment_date: '2026-06-28', ...customerRow('003'), payment_amount: '¥381,200', method: 'promissory_note', status: 'allocated' },
      { payment_no: 'PAY-26-0604', payment_date: '2026-06-25', ...customerRow('004'), payment_amount: '¥220,000', method: 'offset', status: 'unallocated' },
    ],
  },

  // ── 販売管理: 債権（売掛） ──────────────────────────
  // accounts_receivable は正規化再設計で「請求 (billing_invoice) − 消込 (payment_allocation)」の
  // 導出ビューになった (テーブル実体を持たない)。本モックは customer 別残高の算出済み値を保持する。
  // status (正常/滞留/督促中) も期日・滞留日数からの表示用導出値 (DB コードではない)。
  'sales-receivables': {
    title: '債権',
    note: '得意先別の売掛残高と回収期日・滞留状況を管理します（請求 − 消込 の導出ビュー）。',
    table: 'accounts_receivable',
    columns: [
      { key: 'customer_name', label: '得意先' },
      { key: 'balance', label: '売掛残高', align: 'right' },
      { key: 'due_date', label: '期日' },
      { key: 'overdue_days', label: '滞留日数', align: 'right' },
      { key: 'status', label: 'ステータス' },
    ],
    rows: [
      { ...customerRow('001'), balance: '¥4,152,000', due_date: '2026-07-31', overdue_days: 0, status: '正常' },
      { ...customerRow('002'), balance: '¥530,200', due_date: '2026-06-30', overdue_days: 0, status: '正常' },
      { ...customerRow('003'), balance: '¥423,500', due_date: '2026-07-31', overdue_days: 0, status: '正常' },
      { ...customerRow('004'), balance: '¥367,400', due_date: '2026-05-31', overdue_days: 24, status: '滞留' },
      { ...customerRow('005'), balance: '¥88,000', due_date: '2026-04-30', overdue_days: 55, status: '督促中' },
    ],
  },

  // ── 出荷 ──────────────────────────────────────────
  'shipping-receive': {
    title: 'データ受信',
    note: '取引先 EDI / EOS からの出荷データ受信状況。',
    table: 'shipping_receipt',
    columns: [
      { key: 'receipt_no', label: '受信番号' },
      { key: 'source', label: '送信元' },
      { key: 'received_at', label: '受信日時' },
      { key: 'record_count', label: '件数', align: 'right' },
      { key: 'status', label: '状態', codeLabels: SHIPPING_RECEIPT_STATUS_LABELS },
    ],
    rows: [
      { receipt_no: 'RCV-260619-001', source: 'しまむら Web-EDI', received_at: '2026-06-19 06:30', record_count: 128, status: 'imported' },
      { receipt_no: 'RCV-260619-002', source: 'AEON EOS', received_at: '2026-06-19 07:10', record_count: 64, status: 'imported' },
      { receipt_no: 'RCV-260620-001', source: 'しまむら Web-EDI', received_at: '2026-06-20 06:28', record_count: 142, status: 'received' },
      { receipt_no: 'RCV-260620-002', source: 'KEYUCA CSV', received_at: '2026-06-20 09:05', record_count: 31, status: 'error' },
      { receipt_no: 'RCV-260621-001', source: 'AEON EOS', received_at: '2026-06-21 07:02', record_count: 58, status: 'received' },
    ],
  },
  'shipping-picking': {
    title: 'ピッキングリスト',
    note: '倉庫別のピッキング指示と進捗。',
    table: 'picking_list',
    columns: [
      { key: 'list_no', label: 'リスト番号' },
      { key: 'warehouse', label: '倉庫' },
      { key: 'item_count', label: '明細数', align: 'right' },
      { key: 'total_qty', label: '総数量', align: 'right' },
      { key: 'status', label: '状態', codeLabels: PICKING_LIST_STATUS_LABELS },
      { key: 'created_at', label: '作成日時' },
    ],
    rows: [
      { list_no: 'PCK-260620-01', warehouse: '本社倉庫', item_count: 24, total_qty: 860, status: 'completed', created_at: '2026-06-20 08:00' },
      { list_no: 'PCK-260620-02', warehouse: 'しまむらセンター', item_count: 18, total_qty: 540, status: 'picking', created_at: '2026-06-20 08:30' },
      { list_no: 'PCK-260621-01', warehouse: '本社倉庫', item_count: 31, total_qty: 1240, status: 'not_started', created_at: '2026-06-21 08:00' },
      { list_no: 'PCK-260621-02', warehouse: '本社倉庫', item_count: 9, total_qty: 210, status: 'not_started', created_at: '2026-06-21 08:15' },
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
    table: 'asn_transmission',
    columns: [
      { key: 'asn_no', label: 'ASN番号' },
      { key: 'destination', label: '送信先' },
      { key: 'slip_count', label: '伝票数', align: 'right' },
      { key: 'sent_at', label: '送信日時' },
      { key: 'status', label: '状態', codeLabels: ASN_TRANSMISSION_STATUS_LABELS },
    ],
    rows: [
      { asn_no: 'ASN-260619-01', destination: customerNameOf('001'), slip_count: 12, sent_at: '2026-06-19 15:00', status: 'sent' },
      { asn_no: 'ASN-260620-01', destination: customerNameOf('002'), slip_count: 8, sent_at: '2026-06-20 15:10', status: 'sent' },
      { asn_no: 'ASN-260620-02', destination: customerNameOf('001'), slip_count: 15, sent_at: '2026-06-20 16:30', status: 'sent' },
      { asn_no: 'ASN-260621-01', destination: customerNameOf('003'), slip_count: 4, sent_at: '2026-06-21 14:20', status: 'pending' },
    ],
  },

  // ── 在庫管理 ──────────────────────────────────────
  // 4 ページとも inventory_movement を movement_type でフィルタして表示する (ページ構成・URL は維持)。
  // 数量は絶対値 (入荷/出荷) または符号付き (調整/棚卸差異) の従来の見た目を維持する。
  'inventory-inbound': {
    title: '入荷情報',
    note: '倉庫への入荷実績（inventory_movement の movement_type=inbound）。',
    table: 'inventory_movement',
    columns: [
      { key: 'slip_no', label: '伝票番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'quantity', label: '数量', align: 'right' },
      { key: 'warehouse', label: '倉庫' },
      { key: 'received_at', label: '入荷日' },
    ],
    rows: inventoryMovementsOf('inbound').map((m) => ({
      slip_no: m.movement_no,
      product_name: m.product_name,
      quantity: Math.abs(m.quantity),
      warehouse: m.warehouse,
      received_at: m.moved_at,
    })),
  },
  'inventory-outbound': {
    title: '出荷情報',
    note: '倉庫からの出荷実績（inventory_movement の movement_type=outbound、数量は絶対値表示）。',
    table: 'inventory_movement',
    columns: [
      { key: 'slip_no', label: '伝票番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'quantity', label: '数量', align: 'right' },
      { key: 'destination', label: '納品先' },
      { key: 'shipped_at', label: '出荷日' },
    ],
    rows: inventoryMovementsOf('outbound').map((m) => ({
      slip_no: m.movement_no,
      product_name: m.product_name,
      quantity: Math.abs(m.quantity),
      destination: m.counterparty,
      shipped_at: m.moved_at,
    })),
  },
  'inventory-adjustment': {
    title: '在庫調整',
    note: '不良・返品・訂正等による在庫の増減（inventory_movement の movement_type=adjust）。',
    table: 'inventory_movement',
    columns: [
      { key: 'adjust_no', label: '調整番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'delta', label: '増減', align: 'right' },
      { key: 'reason', label: '理由' },
      { key: 'adjusted_at', label: '調整日' },
    ],
    rows: inventoryMovementsOf('adjust').map((m) => ({
      adjust_no: m.movement_no,
      product_name: m.product_name,
      delta: signedQty(m.quantity),
      reason: m.reason,
      adjusted_at: m.moved_at,
    })),
  },
  'inventory-stocktaking': {
    title: '棚卸調整',
    note: '棚卸の帳簿在庫と実地在庫の差異（inventory_movement の movement_type=stocktake、数量 = 実地 − 帳簿）。',
    table: 'inventory_movement',
    columns: [
      { key: 'count_no', label: '棚卸番号' },
      { key: 'product_name', label: '商品名' },
      { key: 'diff', label: '差異', align: 'right' },
      { key: 'reason', label: '理由' },
      { key: 'counted_at', label: '棚卸日' },
    ],
    rows: inventoryMovementsOf('stocktake').map((m) => ({
      count_no: m.movement_no,
      product_name: m.product_name,
      diff: signedQty(m.quantity),
      reason: m.reason,
      counted_at: m.moved_at,
    })),
  },
}
