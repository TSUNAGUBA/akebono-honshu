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
  // 第二段階契約: エンティティ ID は uuid 文字列 (JSON では文字列で受け渡し)
  id: string
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
  // 発注状態 4 値モデル (§3b)。orderedAt(発注済)/status(発注中止)/isDeleted(発注削除) から導出。
  // deliveredAt は §3b で状態導出から除外 (後方互換のため残すが未使用)。
  orderedAt: string | null
  deliveredAt: string | null
  isDeleted: boolean
  supplierId: string
  ordererUserId: string
  customerName: string | null
  hasUndecidedPrice: boolean
}

// 分納×倉庫の多次元明細 (PR5b)。1 発注明細を「(倉庫 × 納期) の分納行」の集合で多次元化する。
// warehouseId / deliveryDate は null 許容 (倉庫未指定 / 発注明細日未指定)。seq は表示順。
export interface OrderLineDeliverySummary {
  id: string
  warehouseId: string | null
  warehouseName: string | null
  deliveryDate: string | null
  quantity: number
  packQuantity: number | null
  seq: number
}

export interface OrderLineDetail {
  id: string
  lineNo: number
  productId: string
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
  // 分納×倉庫の多次元明細 (PR5b)。空配列 = 分納なし (単一明細、従来挙動)。
  deliveries: OrderLineDeliverySummary[]
}

export interface OrderDetail {
  id: string
  mgmtNo: string
  orderNo: string | null
  // 帳票出力フォーム 手入力項目 (発注日 / 出荷指示番号)。出力フォームの初期表示に使う。
  orderDate: string | null
  shippingInstructionNo: string | null
  status: number
  cancelledAt: string | null
  cancelReason: string | null
  supplierId: string
  supplierCode: string
  supplierName: string
  supplierOfficialNameSnapshot: string | null
  supplierCodeSnapshot: string | null
  deliveryDestinationId: string
  deliveryDestinationName: string
  customerNameSnapshot: string | null
  departmentId: string
  departmentName: string
  warehouseId: string
  warehouseName: string
  dueDate: string
  ordererUserId: string
  ordererName: string
  managerUserId: string
  managerName: string
  subOrderer1UserId: string | null
  subOrderer2UserId: string | null
  subOrderer3UserId: string | null
  subOrderer4UserId: string | null
  subOrderer5UserId: string | null
  subOrderer6UserId: string | null
  communicationText: string | null
  // 連絡文書 6 行 (構造化、PR6)。新フローの SoT。6 列が全て空の旧発注は communicationText を
  // 改行分割してブリッジ表示する (編集ロード時、フロント側で実施)。
  communicationLine1: string | null
  communicationLine2: string | null
  communicationLine3: string | null
  communicationLine4: string | null
  communicationLine5: string | null
  communicationLine6: string | null
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
  warehouse2Id: string | null
  warehouse2Name: string | null
  warehouse3Id: string | null
  warehouse3Name: string | null
  // 発注状態 4 値モデル (§3b)。orderedAt(発注済)/status(発注中止)/isDeleted(発注削除) の状態表示・操作可否判定に使う。
  // deliveredAt は §3b で状態導出から除外 (後方互換のため残すが未使用)。
  orderedAt: string | null
  deliveredAt: string | null
  isDeleted: boolean
  deletedAt: string | null
}

export interface CreateOrderPayload {
  // 発注書番号 (§5)。作成時に手入力可能 (任意)。空欄 (null) は初回 Excel 出力時に自動採番される。
  // 編集 (UpdateOrderPayload) では送らないため optional。
  orderNo?: string | null
  supplierId: string
  deliveryDestinationId: string
  departmentId: string
  warehouseId: string
  dueDate: string
  ordererUserId: string
  managerUserId: string
  subOrderer1UserId: string | null
  subOrderer2UserId: string | null
  subOrderer3UserId: string | null
  subOrderer4UserId: string | null
  subOrderer5UserId: string | null
  subOrderer6UserId: string | null
  communicationText: string | null
  lines: {
    productId: string
    quantity: number
    unitPriceSnapshot: number
    currencyCodeSnapshot: string
    // 旧 発注明細 項目 (Phase B、任意)
    packQuantity: number | null
    estimateUnitPrice: number | null
    // 発注明細 備考 (spec 明細 No.26、任意)
    remark: string | null
    // 分納×倉庫の多次元明細 (PR5b、任意)。null/空 = 分納なし (単一明細、従来挙動)。
    // 1 件以上あればサーバ側で line.quantity = 分納 quantity 合計 (SUM) に再計算される。
    deliveries?: {
      warehouseId: string | null
      deliveryDate: string | null
      quantity: number
      packQuantity: number | null
    }[] | null
  }[]
  // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
  isOverseas: boolean
  landingPlace: string | null
  customerRef: string | null
  factoryShippingDate: string | null
  deliveryPlaceShippingDate: string | null
  overseasDepartureDate: string | null
  warehouse2Id: string | null
  warehouse3Id: string | null
  // 連絡文書 6 行 (構造化、PR6)。新フローは本 6 列を SoT として送る。各行は空欄なら null。
  // communicationText は新フロントから書かない (旧データのみ保持) が、I/F 互換のため従来値を
  // そのまま送り返す (作成時は null)。
  communicationLine1: string | null
  communicationLine2: string | null
  communicationLine3: string | null
  communicationLine4: string | null
  communicationLine5: string | null
  communicationLine6: string | null
}

export interface UpdateOrderPayload extends CreateOrderPayload {
  editReason: EditReason
  editNote: string | null
}

// 帳票出力フォーム (旧システム「発注書出力」画面相当)。出力帳票選択 = 発注書 / 管理表 / 発注書+管理表。
export type OrderExportFormat = 'order' | 'management' | 'both'

export interface ExportOrderPayload {
  // 手入力 3 項目 (発注日 / 出荷指示番号 / 発注番号)。空欄は null で送る。
  orderDate: string | null
  shippingInstructionNo: string | null
  orderNo: string | null
  format: OrderExportFormat
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
  resolvedSizeId: string | null
  /** size 専用単価で解決されたか (false = 全サイズ既定 fallback) */
  isSizeSpecific: boolean
}

export const useOrders = () => {
  const { apiFetch, apiData, apiPaged } = useApi()
  const config = useRuntimeConfig()

  // 一覧はキーセットページング (AKB-DOC-12 §7.1)。limit=200 で取得し、
  // page.hasMore の間 nextCursor を渡して「さらに読み込む」で続きを取得する。
  const list = async (includeCancelled = false, cursor: string | null = null): Promise<PagedItems<OrderListItem>> => {
    return await apiPaged<OrderListItem>(`/orders?includeCancelled=${includeCancelled}&${pageQuery(cursor)}`)
  }

  const get = (id: string): Promise<OrderDetail> => apiData<OrderDetail>(`/orders/${id}`)

  // 作成 POST は Idempotency-Key 必須 (AKB-DOC-12 §8.1、欠落は 400 AKB-SYS-004)。
  // 同一ペイロードの再試行には同じキーを使い回し、サーバ側リプレイで二重作成を防ぐ。
  const createIdem = createIdempotencySession()
  const create = async (payload: CreateOrderPayload): Promise<{ id: string; mgmtNo: string }> => {
    const res = await apiData<{ id: string; mgmtNo: string }>('/orders', {
      method: 'POST',
      body: payload,
      headers: { 'Idempotency-Key': createIdem.keyFor(payload) },
    })
    createIdem.complete(payload)
    return res
  }

  const update = async (id: string, payload: UpdateOrderPayload): Promise<{ id: string; mgmtNo: string }> =>
    await apiData<{ id: string; mgmtNo: string }>(`/orders/${id}`, { method: 'PATCH', body: payload })

  const cancel = async (id: string, cancelReason: string): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/cancel`, { method: 'POST', body: { cancelReason } })
  }

  /** 発注済にする (§3b)。未発注 → 発注済 (ordered_at を SET)。ダウンロードとは独立したユーザー操作。 */
  const markOrdered = async (id: string): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/mark-ordered`, { method: 'POST' })
  }

  /** 未発注に戻す (§3b)。発注済 → 未発注 (ordered_at を NULL)。 */
  const unmarkOrdered = async (id: string): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/unmark-ordered`, { method: 'POST' })
  }

  /** 発注削除 (§3b)。論理削除 (deleted_at を SET、第二段階規約)。 */
  const softDelete = async (id: string): Promise<void> => {
    await apiFetch<void>(`/orders/${id}/delete`, { method: 'POST' })
  }

  /**
   * 発注状態の一括変更 (§3c)。チェックした発注を指定状態へ一括変更する。
   * 終端状態で変更できない発注はスキップされ、{ updated, skipped } を返す。
   */
  const bulkStatus = async (
    orderIds: string[],
    targetState: OrderState,
    cancelReason?: string,
  ): Promise<{ updated: number; skipped: number }> =>
    await apiData<{ updated: number; skipped: number }>('/orders/bulk-status', {
      method: 'POST',
      body: { orderIds, targetState, cancelReason: cancelReason ?? null },
    })

  // Blob レスポンス (Excel/ZIP) を Content-Disposition の filename で download する共有ヘルパー。
  // exportOrder / bulkExport で共通利用 (原則3 既存パターン再利用)。
  const saveBlobResponse = (response: { headers: Headers; _data?: unknown }, fallbackName: string): void => {
    const cd = response.headers.get('content-disposition') ?? ''
    const match = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i)
    const filename = match ? decodeURIComponent(match[1]) : fallbackName
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
   * 帳票出力 (旧システム「発注書出力」画面相当)。発注日 / 出荷指示番号 / 発注番号 を手入力し、
   * 出力帳票 (発注書 / 管理表 / 発注書+管理表) を選んで出力する。入力 3 項目はサーバ側で発注に保存される。
   *   - 'order' / 'management' → 単一 .xlsx
   *   - 'both'                 → 発注書+管理表 を ZIP
   */
  const exportOrder = async (id: string, payload: ExportOrderPayload): Promise<void> => {
    const { getIdToken, user } = useAuth()
    const token = await getIdToken()
    if (!token) throw new Error('未認証')
    const tenantId = user.value?.tenantId
    const response = await $fetch.raw<Blob>(
      `${config.public.apiBase}/orders/${id}/export`,
      {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
        },
        body: payload,
        responseType: 'blob',
      },
    )
    const fallbackExt = payload.format === 'both' ? 'zip' : 'xlsx'
    saveBlobResponse(response, `order_${id}.${fallbackExt}`)
  }

  /**
   * 一括ダウンロード (#3b)。チェックした発注を 発注書 / 管理表 / 発注書+管理表 でまとめて取得。
   * exportOrder と同じく saveBlobResponse で Blob を download する。
   *   - 'order'      → 発注書を ZIP で (各発注の初回出力は order_no 採番等の副作用あり)
   *   - 'management' → 管理表を単一 .xlsx で (読み取り専用、副作用なし)
   *   - 'both'       → 管理表 + 各発注の発注書 を ZIP で
   */
  const bulkExport = async (
    orderIds: string[],
    format: 'order' | 'management' | 'both',
  ): Promise<void> => {
    const { getIdToken, user } = useAuth()
    const token = await getIdToken()
    if (!token) throw new Error('未認証')
    const tenantId = user.value?.tenantId
    const response = await $fetch.raw<Blob>(
      `${config.public.apiBase}/orders/bulk-export`,
      {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
        },
        body: { orderIds, format },
        responseType: 'blob',
      },
    )
    // フォールバック名は zip/xlsx を format から推定 (実際の名前は server 側 Content-Disposition が決定)。
    const fallbackExt = format === 'management' ? 'xlsx' : 'zip'
    saveBlobResponse(response, `orders_export.${fallbackExt}`)
  }

  const communicationSuggestions = async (): Promise<CommunicationSuggestion[]> => {
    return await apiData<CommunicationSuggestion[]>('/orders/communication-suggestions')
  }

  /**
   * 単価サジェスト (PR2、size-aware)。SKU と発注先から現単価を解決して返す (入力補助)。
   * サーバ側で snapshot を上書きしないため、本値はフォームの初期値/補完にのみ使う。
   */
  const priceSuggestion = async (productId: string, supplierId: string): Promise<PriceSuggestion> =>
    await apiData<PriceSuggestion>(`/orders/price-suggestion?productId=${productId}&supplierId=${supplierId}`)

  return { list, get, create, update, cancel, markOrdered, unmarkOrdered, softDelete, bulkStatus, exportOrder, bulkExport, communicationSuggestions, priceSuggestion }
}

// ─────────────────────────────────────────────────
// 発注状態 4 値モデル (§3b)。導出は単一ヘルパーに集約 (一覧 index.vue / 詳細 [id].vue 共通)。
// 優先順位: 発注削除 > 発注中止 > 発注済 > 未発注。
//   未発注:   status=0(Active) かつ orderedAt 未設定
//   発注済:   status=0 かつ orderedAt 設定済 (ユーザー操作でのみ設定。ダウンロードでは変わらない)
//   発注中止: status=1(Cancelled)
//   発注削除: isDeleted=true (論理削除)
// 発注済/削除は status と独立した列のため、表示上の優先順位を明示する。
// 「発注済」は Excel 出力 (firstExportedAt) とは独立した ordered_at 列で持つ (§3b)。
// ─────────────────────────────────────────────────
export type OrderState = 'notOrdered' | 'ordered' | 'cancelled' | 'deleted'

export interface OrderStateInput {
  status: number
  orderedAt: string | null
  isDeleted: boolean
}

export const deriveOrderState = (o: OrderStateInput): OrderState => {
  if (o.isDeleted) return 'deleted'
  if (o.status === 1) return 'cancelled'
  if (o.orderedAt) return 'ordered'
  return 'notOrdered'
}

export const orderStateLabel = (s: OrderState): string => {
  switch (s) {
    case 'notOrdered': return '未発注'
    case 'ordered': return '発注済'
    case 'cancelled': return '発注中止'
    case 'deleted': return '発注削除'
  }
}

/** 4 状態バッジの配色 (それぞれ識別しやすい別色)。 */
export const orderStateBadgeClass = (s: OrderState): string => {
  switch (s) {
    case 'notOrdered': return 'bg-gray-100 text-gray-600'
    case 'ordered': return 'bg-green-100 text-green-700'
    case 'cancelled': return 'bg-orange-100 text-orange-700'
    case 'deleted': return 'bg-red-100 text-red-700'
  }
}
