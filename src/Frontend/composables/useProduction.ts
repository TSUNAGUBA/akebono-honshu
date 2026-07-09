/**
 * 生産管理拡張 (BOM / 生産指示書 / 素材発注書 / 未済) API ラッパー + Excel ダウンロード。
 */

export const materialRoleLabel = (r: number): string =>
  ['甲皮', '中底', '底', '付属', '副資材'][r] ?? `区分${r}`

export const piStatusLabel = (s: number): string => {
  switch (s) {
    case 0: return '下書き'
    case 1: return '指示済'
    case 2: return '完了'
    case 9: return '中止'
    default: return `状態${s}`
  }
}

export const moStatusLabel = (s: number): string => {
  switch (s) {
    case 0: return '下書き'
    case 1: return '発注済'
    case 9: return '中止'
    default: return `状態${s}`
  }
}

// ── BOM ─────────────────────────────────────────
// 第二段階契約: エンティティ ID (id / xxxId) は uuid 文字列 (JSON では文字列で受け渡し)
export interface ProductMaterialItem {
  id: string
  materialRole: number
  materialId: string
  materialName: string
  requiredQtyPerUnit: number
  unit: string
  recommendedSupplierId: string | null
  recommendedSupplierName: string | null
  lossRate: number
  remark: string | null
}

export interface BomLineInput {
  materialRole: number
  materialId: string
  requiredQtyPerUnit: number
  unit: string
  recommendedSupplierId: string | null
  lossRate: number
  remark: string | null
}

export interface MaterialRequirementLine {
  materialId: string
  materialName: string
  materialRole: number
  requiredQuantity: number
  unit: string
}
export interface MaterialRequirementGroup {
  recommendedSupplierId: string | null
  recommendedSupplierName: string | null
  lines: MaterialRequirementLine[]
}
export interface MaterialRequirements {
  familyId: string
  totalQuantity: number
  groups: MaterialRequirementGroup[]
}

// ── 生産指示書 ─────────────────────────────────────
export interface PiListItem {
  id: string
  instructionNo: string
  productSku9: string
  productName: string
  factoryCode: string
  factoryName: string
  plannedQuantity: number
  dueDate: string
  status: number
  exportState: string
  firstExportedAt: string | null
  createdAt: string
  updatedAt: string
}
export interface PiLineDetail {
  id: string
  lineNo: number
  productId: string
  sku: string
  productName: string
  colorName: string
  sizeName: string
  quantity: number
}
export interface PiDetail {
  id: string
  instructionNo: string
  productFamilyId: string
  productSku9: string
  productName: string
  factorySupplierId: string
  factoryCode: string
  factoryName: string
  plannedQuantity: number
  dueDate: string
  status: number
  instructedAt: string | null
  completedAt: string | null
  cancelledAt: string | null
  cancelReason: string | null
  communicationText: string | null
  firstExportedAt: string | null
  lastExportedAt: string | null
  createdAt: string
  updatedAt: string
  lines: PiLineDetail[]
}
export interface CreatePiPayload {
  productFamilyId: string
  factorySupplierId: string
  dueDate: string
  communicationText: string | null
  lines: { productId: string; quantity: number }[]
}
export interface UpdatePiPayload {
  factorySupplierId: string
  dueDate: string
  communicationText: string | null
  lines: { productId: string; quantity: number }[]
}

// ── 素材発注書 ─────────────────────────────────────
export interface MaterialOrderListItem {
  id: string
  orderNo: string
  materialSupplierId: string
  materialSupplierCode: string
  materialSupplierName: string
  productionInstructionId: string | null
  dueDate: string
  status: number
  lineCount: number
  totalAmount: number
  currencyCode: string
  exportState: string
  firstExportedAt: string | null
  createdAt: string
  updatedAt: string
}
export interface MaterialOrderLineDetail {
  id: string
  lineNo: number
  materialId: string
  materialName: string
  productFamilyId: string | null
  requiredQuantity: number
  unit: string
  unitPrice: number | null
  currencyCode: string
  subtotal: number
}
export interface MaterialOrderDetail {
  id: string
  orderNo: string
  materialSupplierId: string
  materialSupplierCode: string
  materialSupplierName: string
  supplierOfficialNameSnapshot: string | null
  supplierCodeSnapshot: string | null
  productionInstructionId: string | null
  productionInstructionNo: string | null
  dueDate: string
  status: number
  instructedAt: string | null
  cancelledAt: string | null
  cancelReason: string | null
  communicationText: string | null
  firstExportedAt: string | null
  lastExportedAt: string | null
  createdAt: string
  updatedAt: string
  lines: MaterialOrderLineDetail[]
}
export interface CreateMaterialOrderPayload {
  materialSupplierId: string
  productionInstructionId: string | null
  dueDate: string
  communicationText: string | null
  lines: {
    materialId: string
    productFamilyId: string | null
    sourcePiLineId: string | null
    requiredQuantity: number
    unit: string
    unitPrice: number | null
    currencyCode: string
  }[]
}

// ── 未/済 ──────────────────────────────────────────
export interface ProductionStatusRow {
  familyId: string
  sku9: string
  productName: string
  materialOrder: string         // done / undone
  productionInstruction: string
  bomRegistered: boolean
}

export const useProduction = () => {
  const { apiFetch, apiData, apiPaged } = useApi()
  const config = useRuntimeConfig()

  // 作成 POST の Idempotency-Key セッション (AKB-DOC-12 §8.1)。同一ペイロードの再試行には
  // 同じキーを使い回し、サーバ側リプレイで二重作成を防ぐ (useApi.createIdempotencySession)。
  const piIdem = createIdempotencySession()
  const moIdem = createIdempotencySession()

  const download = async (path: string, fallback: string): Promise<void> => {
    const { getIdToken, user } = useAuth()
    const token = await getIdToken()
    if (!token) throw new Error('未認証')
    const tenantId = user.value?.tenantId
    const response = await $fetch.raw<Blob>(`${config.public.apiBase}${path}`, {
      method: 'GET',
      headers: {
        Authorization: `Bearer ${token}`,
        ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
      },
      responseType: 'blob',
    })
    const cd = response.headers.get('content-disposition') ?? ''
    const match = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i)
    const filename = match ? decodeURIComponent(match[1]) : fallback
    const url = URL.createObjectURL(response._data as Blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  return {
    // BOM
    getBom: async (familyId: string): Promise<ProductMaterialItem[]> =>
      await apiData<ProductMaterialItem[]>(`/products/families/${familyId}/materials`),
    replaceBom: async (familyId: string, materials: BomLineInput[]): Promise<ProductMaterialItem[]> =>
      await apiData<ProductMaterialItem[]>(`/products/families/${familyId}/materials`, {
        method: 'PUT', body: { materials },
      }),
    requirements: (familyId: string, quantity: number): Promise<MaterialRequirements> =>
      apiData<MaterialRequirements>(`/products/families/${familyId}/material-requirements?quantity=${quantity}`),

    // 生産指示書。一覧はキーセットページング (AKB-DOC-12 §7.1)。limit=200 で取得し、
    // page.hasMore の間 nextCursor を渡して「さらに読み込む」で続きを取得する。
    piList: async (includeCancelled = false, cursor: string | null = null): Promise<PagedItems<PiListItem>> =>
      await apiPaged<PiListItem>(`/production-instructions?includeCancelled=${includeCancelled}&${pageQuery(cursor)}`),
    piGet: (id: string): Promise<PiDetail> => apiData<PiDetail>(`/production-instructions/${id}`),
    // 作成 POST は Idempotency-Key 必須 (AKB-DOC-12 §8.1、欠落は 400 AKB-SYS-004)。
    // 同一ペイロードの再試行には同じキーを使い回す (piIdem セッション)。
    piCreate: async (payload: CreatePiPayload): Promise<{ id: string; instructionNo: string }> => {
      const res = await apiData<{ id: string; instructionNo: string }>(`/production-instructions`, {
        method: 'POST', body: payload, headers: { 'Idempotency-Key': piIdem.keyFor(payload) },
      })
      piIdem.complete(payload)
      return res
    },
    piUpdate: (id: string, payload: UpdatePiPayload): Promise<{ id: string; instructionNo: string }> =>
      apiData<{ id: string; instructionNo: string }>(`/production-instructions/${id}`, { method: 'PATCH', body: payload }),
    piIssue: (id: string): Promise<void> => apiFetch(`/production-instructions/${id}/issue`, { method: 'POST' }),
    piComplete: (id: string): Promise<void> => apiFetch(`/production-instructions/${id}/complete`, { method: 'POST' }),
    piCancel: (id: string, reason: string): Promise<void> =>
      apiFetch(`/production-instructions/${id}/cancel`, { method: 'POST', body: { reason } }),
    piDownloadExcel: (id: string, instructionNo: string): Promise<void> =>
      download(`/production-instructions/${id}/export.xlsx`, `PI_${instructionNo}.xlsx`),

    // 素材発注書
    prepareMaterialOrder: (req: { productionInstructionId?: string | null; productFamilyId?: string | null; quantity?: number | null }): Promise<MaterialRequirements> =>
      apiData<MaterialRequirements>(`/material-orders/prepare`, { method: 'POST', body: req }),
    // 素材発注一覧もキーセットページング (piList と同方式)。
    moList: async (includeCancelled = false, cursor: string | null = null): Promise<PagedItems<MaterialOrderListItem>> =>
      await apiPaged<MaterialOrderListItem>(`/material-orders?includeCancelled=${includeCancelled}&${pageQuery(cursor)}`),
    moGet: (id: string): Promise<MaterialOrderDetail> => apiData<MaterialOrderDetail>(`/material-orders/${id}`),
    // 作成 POST は Idempotency-Key 必須 (AKB-DOC-12 §8.1、欠落は 400 AKB-SYS-004)。
    // 同一ペイロードの再試行には同じキーを使い回す (moIdem セッション。仕入先グループごとに
    // ペイロードが異なるため、グループ単位で独立したキーになる)。
    moCreate: async (payload: CreateMaterialOrderPayload): Promise<{ id: string; orderNo: string }> => {
      const res = await apiData<{ id: string; orderNo: string }>(`/material-orders`, {
        method: 'POST', body: payload, headers: { 'Idempotency-Key': moIdem.keyFor(payload) },
      })
      moIdem.complete(payload)
      return res
    },
    moOrder: (id: string): Promise<void> => apiFetch(`/material-orders/${id}/order`, { method: 'POST' }),
    moCancel: (id: string, reason: string): Promise<void> =>
      apiFetch(`/material-orders/${id}/cancel`, { method: 'POST', body: { reason } }),
    moDownloadExcel: (id: string, orderNo: string): Promise<void> =>
      download(`/material-orders/${id}/export.xlsx`, `MO_${orderNo}.xlsx`),

    // 未/済
    statusList: async (f: { materialOrderState?: string; productionInstructionState?: string; bomRegistered?: boolean } = {}): Promise<ProductionStatusRow[]> => {
      const q = new URLSearchParams()
      if (f.materialOrderState) q.set('materialOrderState', f.materialOrderState)
      if (f.productionInstructionState) q.set('productionInstructionState', f.productionInstructionState)
      if (f.bomRegistered !== undefined) q.set('bomRegistered', String(f.bomRegistered))
      const qs = q.toString()
      return await apiData<ProductionStatusRow[]>(`/production/status${qs ? `?${qs}` : ''}`)
    },
  }
}
