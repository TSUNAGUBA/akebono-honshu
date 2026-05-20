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

export const orderStatusLabel = (s: number): string =>
  s === 1 ? 'Cancelled (中止)' : 'Active (有効)'

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
  }[]
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

  /** Excel ダウンロード (O-06)。Blob を取得して a タグで download。 */
  const downloadExcel = async (id: number): Promise<void> => {
    const { user } = useAuth()
    if (!user.value) throw new Error('未認証')
    const response = await $fetch.raw<Blob>(
      `${config.public.apiBase}/orders/${id}/export.xlsx`,
      {
        method: 'GET',
        headers: { Authorization: `Bearer ${user.value.token}` },
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

  const communicationSuggestions = async (): Promise<CommunicationSuggestion[]> => {
    const res = await apiFetch<{ data: CommunicationSuggestion[] }>('/orders/communication-suggestions')
    return res.data
  }

  return { list, get, create, update, cancel, downloadExcel, communicationSuggestions }
}
