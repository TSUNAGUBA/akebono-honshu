/**
 * 発注書 (O-01〜O-07) API ラッパー + Excel ダウンロード。
 */

export type EditReason = 'quantity' | 'deadline' | 'supplier' | 'typo' | 'other'

export const editReasonLabel = (r: EditReason): string => {
  switch (r) {
    case 'quantity': return '数量変更'
    case 'deadline': return '納期変更'
    case 'supplier': return '仕入先変更'
    case 'typo': return '誤入力修正'
    case 'other': return 'その他'
  }
}

export interface OrderListItem {
  id: number
  mgmtNo: string
  orderNo: string | null
  status: number
  supplierCode: string
  supplierName: string
  deliveryDestinationName: string
  dueDate: string
  ordererName: string | null
  lineCount: number
  totalAmount: number
  currencyCode: string
  firstExportedAt: string | null
  lastExportedAt: string | null
  createdAt: string
  updatedAt: string
  // 発注区分 国内/海外 (Phase B、is_overseas)。一覧でのタブ絞込・区分バッジ表示用。
  isOverseas: boolean
  // 発注状態 5 値モデル (#3a)。納品完了/発注削除 導出 + 一覧 SPLIT フィルタ用フィールド。
  deliveredAt: string | null
  isDeleted: boolean
  supplierId: number
  ordererUserId: number
  customerName: string | null
  hasUndecidedPrice: boolean
}

export interface OrderLineDetail {
  id: number
  lineNo: number
  productId: number
  sku: string
  productName: string
  colorName: string
  sizeName: string
  quantity: number
  unitPriceSnapshot: number
  currencyCodeSnapshot: string
  subtotal: number
  // 旧 発注明細 項目 (Phase B)
  packQuantity: number | null
  estimateUnitPrice: number | null
  provisionalNumberSnapshot: string | null
  // 発注明細 備考 (spec 明細 No.26)
  remark: string | null
}

export interface OrderDetail {
  id: number
  mgmtNo: string
  orderNo: string | null
  status: number
  cancelledAt: string | null
  cancelReason: string | null
  supplierId: number
  supplierCode: string
  supplierName: string
  supplierOfficialNameSnapshot: string | null
  supplierCodeSnapshot: string | null
  deliveryDestinationId: number
  deliveryDestinationName: string
  customerNameSnapshot: string | null
  departmentId: number
  departmentName: string
  warehouseId: number
  warehouseName: string
  dueDate: string
  ordererUserId: number
  ordererName: string
  managerUserId: number
  managerName: string
  subOrderer1UserId: number | null
  subOrderer2UserId: number | null
  subOrderer3UserId: number | null
  subOrderer4UserId: number | null
  subOrderer5UserId: number | null
  subOrderer6UserId: number | null
  communicationText: string | null
  firstExportedAt: string | null
  lastExportedAt: string | null
  createdAt: string
  updatedAt: string
  lines: OrderLineDetail[]
  // 旧 発注書 国内/海外 項目 (Phase B)
  isOverseas: boolean
  landingPlace: string | null
  customerRef: string | null
  factoryShippingDate: string | null
  deliveryPlaceShippingDate: string | null
  overseasDepartureDate: string | null
  warehouse2Id: number | null
  warehouse2Name: string | null
  warehouse3Id: number | null
  warehouse3Name: string | null
  // 発注状態 5 値モデル (#3a)。納品完了/発注削除 の状態表示・操作可否判定に使う。
  deliveredAt: string | null
  isDeleted: boolean
  deletedAt: string | null
}

export interface CreateOrderPayload {
  supplierId: number
  deliveryDestinationId: number
  departmentId: number
  warehouseId: number
  dueDate: string
  ordererUserId: number
  managerUserId: number
  subOrderer1UserId: number | null
  subOrderer2UserId: number | null
  subOrderer3UserId: number | null
  subOrderer4UserId: number | null
  subOrderer5UserId: number | null
  subOrderer6UserId: number | null
  communicationText: string | null
  lines: {
    productId: number
    quantity: number
    unitPriceSnapshot: number
    currencyCodeSnapshot: string
    // 旧 発注明細 項目 (Phase B、任意)
    packQuantity: number | null
    estimateUnitPrice: number | null
    // 発注明細 備考 (spec 明細 No.26、任意)
    remark: string | null
  }[]
  // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
  isOverseas: boolean
  landingPlace: string | null
  customerRef: string | null
  factoryShippingDate: string | null
  deliveryPlaceShippingDate: string | null
  overseasDepartureDate: string | null
  warehouse2Id: number | null
  warehouse3Id: number | null
}

export interface UpdateOrderPayload extends CreateOrderPayload {
  editReason: EditReason
  editNote: string | null
}

export interface CommunicationSuggestion {
  body: string
  standardPrintFlag: boolean
  sourceLabel: string
}

// サイズ別仕入単価 (PR2)。発注明細の unit_price_snapshot 入力補助 (size-aware サジェスト)。
// SKU (productId) の size に対応する現単価を「(family, supplier, SKUのsize) → 無ければ
// (…, NULL-size 既定)」のフォールバックで解決。現単価が無ければ found=false。
export interface PriceSuggestion {
  found: boolean
  unitPrice: number | null
  currencyCode: string | null
  exchangeRate: number | null
  /** 解決に使われた行のサイズ (sizes.id)。全サイズ既定で解決された場合は null */
  resolvedSizeId: number | null
  /** size 専用単価で解決されたか (false = 全サイズ既定 fallback) */
  isSizeSpecific: boolean
}

export const useOrders = () => {
  const { apiFetch } = useApi()
  const config = useRuntimeConfig()

  const list = async (includeCancelled = false): Promise<OrderListItem[]> => {
    const res = await apiFetch<{ data: OrderListItem[] }>(
      `/orders?includeCancelled=${includeCancelled}`,
    )
    return res.data
  }

  const get = (id: number): Promise<OrderDetail> => apiFetch<OrderDetail>(`/orders/${id}`)

  const create = async (payload: CreateOrderPayload): Promise<{ id: number; mgmtNo: string }> =>
    await apiFetch<{ id: number; mgmtNo: string }>('/orders', { method: 'POST', body: payload })

  const update = async (id: number, payload: UpdateOrderPayload): Promise<{ id: number; mgmtNo: string }> =>
    await apiFetch<{ id: number; mgmtNo: string }>(`/orders/${id}`, { method: 'PATCH', body: payload })

  const cancel = async (id: number, cancelReason: string): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/cancel`, { method: 'POST', body: { cancelReason } })
  }

  /** 納品完了 (#3a)。正式発注済の発注を納品完了にする。 */
  const markDelivered = async (id: number): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/deliver`, { method: 'POST' })
  }

  /** 発注削除 (#3a)。論理削除 (is_deleted=true)。 */
  const softDelete = async (id: number): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/delete`, { method: 'POST' })
  }

  /** Excel ダウンロード (O-06)。Blob を取得して a タグで download。 */
  const downloadExcel = async (id: number): Promise<void> => {
    const { getIdToken } = useAuth()
    const token = await getIdToken()
    if (!token) throw new Error('未認証')
    const response = await $fetch.raw<Blob>(
      `${config.public.apiBase}/orders/${id}/export.xlsx`,
      {
        method: 'GET',
        headers: { Authorization: `Bearer ${token}` },
        responseType: 'blob',
      },
    )
    // Content-Disposition から filename を抽出 (フォールバック付)
    const cd = response.headers.get('content-disposition') ?? ''
    const match = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i)
    const filename = match ? decodeURIComponent(match[1]) : `PO_${id}.xlsx`
    const url = URL.createObjectURL(response._data as Blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  /**
   * 一括ダウンロード (#3b)。チェックした発注を 発注書 / 管理表 / 発注書+管理表 でまとめて取得。
   * downloadExcel と同じ Blob → a タグ download のパターン。
   *   - 'order'      → 発注書を ZIP で (各発注の初回出力は order_no 採番等の副作用あり)
   *   - 'management' → 管理表を単一 .xlsx で (読み取り専用、副作用なし)
   *   - 'both'       → 管理表 + 各発注の発注書 を ZIP で
   */
  const bulkExport = async (
    orderIds: number[],
    format: 'order' | 'management' | 'both',
  ): Promise<void> => {
    const { getIdToken } = useAuth()
    const token = await getIdToken()
    if (!token) throw new Error('未認証')
    const response = await $fetch.raw<Blob>(
      `${config.public.apiBase}/orders/bulk-export`,
      {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        body: { orderIds, format },
        responseType: 'blob',
      },
    )
    // Content-Disposition から filename を抽出 (フォールバック付。zip/xlsx は server 側が決定)。
    const cd = response.headers.get('content-disposition') ?? ''
    const match = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i)
    const fallbackExt = format === 'management' ? 'xlsx' : 'zip'
    const filename = match ? decodeURIComponent(match[1]) : `orders_export.${fallbackExt}`
    const url = URL.createObjectURL(response._data as Blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  const communicationSuggestions = async (): Promise<CommunicationSuggestion[]> => {
    const res = await apiFetch<{ data: CommunicationSuggestion[] }>('/orders/communication-suggestions')
    return res.data
  }

  /**
   * 単価サジェスト (PR2、size-aware)。SKU と発注先から現単価を解決して返す (入力補助)。
   * サーバ側で snapshot を上書きしないため、本値はフォームの初期値/補完にのみ使う。
   */
  const priceSuggestion = async (productId: number, supplierId: number): Promise<PriceSuggestion> =>
    await apiFetch<PriceSuggestion>(`/orders/price-suggestion?productId=${productId}&supplierId=${supplierId}`)

  return { list, get, create, update, cancel, markDelivered, softDelete, downloadExcel, bulkExport, communicationSuggestions, priceSuggestion }
}

// ─────────────────────────────────────────────────
// 発注状態 5 値モデル (#3a)。導出は単一ヘルパーに集約 (一覧 index.vue / 詳細 [id].vue 共通)。
// 優先順位: 発注削除 > 納品完了 > 発注中止 > 正式発注済 > 発注依頼中。
//   発注依頼中: status=0(Active) かつ firstExportedAt 未設定
//   正式発注済: status=0 かつ firstExportedAt 設定済
//   発注中止:   status=1(Cancelled)
//   納品完了:   deliveredAt 設定済
//   発注削除:   isDeleted=true (論理削除)
// 削除/納品は status と独立した列のため、表示上の優先順位を明示する。
// ─────────────────────────────────────────────────
export type OrderState = 'requested' | 'ordered' | 'cancelled' | 'delivered' | 'deleted'

export interface OrderStateInput {
  status: number
  firstExportedAt: string | null
  deliveredAt: string | null
  isDeleted: boolean
}

export const deriveOrderState = (o: OrderStateInput): OrderState => {
  if (o.isDeleted) return 'deleted'
  if (o.deliveredAt) return 'delivered'
  if (o.status === 1) return 'cancelled'
  if (o.firstExportedAt) return 'ordered'
  return 'requested'
}

export const orderStateLabel = (s: OrderState): string => {
  switch (s) {
    case 'requested': return '発注依頼中'
    case 'ordered': return '正式発注済'
    case 'cancelled': return '発注中止'
    case 'delivered': return '納品完了'
    case 'deleted': return '発注削除'
  }
}

/** 5 状態バッジの配色 (それぞれ識別しやすい別色)。 */
export const orderStateBadgeClass = (s: OrderState): string => {
  switch (s) {
    case 'requested': return 'bg-gray-100 text-gray-600'
    case 'ordered': return 'bg-green-100 text-green-700'
    case 'cancelled': return 'bg-orange-100 text-orange-700'
    case 'delivered': return 'bg-blue-100 text-blue-700'
    case 'deleted': return 'bg-red-100 text-red-700'
  }
}
