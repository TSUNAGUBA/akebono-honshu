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
export interface ProductMaterialItem {
  id: number
  materialRole: number
  materialId: number
  materialName: string
  requiredQtyPerUnit: number
  unit: string
  recommendedSupplierId: number | null
  recommendedSupplierName: string | null
  lossRate: number
  remark: string | null
}

export interface BomLineInput {
  materialRole: number
  materialId: number
  requiredQtyPerUnit: number
  unit: string
  recommendedSupplierId: number | null
  lossRate: number
  remark: string | null
}

export interface MaterialRequirementLine {
  materialId: number
  materialName: string
  materialRole: number
  requiredQuantity: number
  unit: string
}
export interface MaterialRequirementGroup {
  recommendedSupplierId: number | null
  recommendedSupplierName: string | null
  lines: MaterialRequirementLine[]
}
export interface MaterialRequirements {
  familyId: number
  totalQuantity: number
  groups: MaterialRequirementGroup[]
}

// ── 生産指示書 ─────────────────────────────────────
export interface PiListItem {
  id: number
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
  id: number
  lineNo: number
  productId: number
  sku: string
  productName: string
  colorName: string
  sizeName: string
  quantity: number
}
export interface PiDetail {
  id: number
  instructionNo: string
  productFamilyId: number
  productSku9: string
  productName: string
  factorySupplierId: number
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
  productFamilyId: number
  factorySupplierId: number
  dueDate: string
  communicationText: string | null
  lines: { productId: number; quantity: number }[]
}
export interface UpdatePiPayload {
  factorySupplierId: number
  dueDate: string
  communicationText: string | null
  lines: { productId: number; quantity: number }[]
}

// ── 素材発注書 ─────────────────────────────────────
export interface MaterialOrderListItem {
  id: number
  orderNo: string
  materialSupplierId: number
  materialSupplierCode: string
  materialSupplierName: string
  productionInstructionId: number | null
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
  id: number
  lineNo: number
  materialId: number
  materialName: string
  productFamilyId: number | null
  requiredQuantity: number
  unit: string
  unitPrice: number | null
  currencyCode: string
  subtotal: number
}
export interface MaterialOrderDetail {
  id: number
  orderNo: string
  materialSupplierId: number
  materialSupplierCode: string
  materialSupplierName: string
  supplierOfficialNameSnapshot: string | null
  supplierCodeSnapshot: string | null
  productionInstructionId: number | null
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
  materialSupplierId: number
  productionInstructionId: number | null
  dueDate: string
  communicationText: string | null
  lines: {
    materialId: number
    productFamilyId: number | null
    sourcePiLineId: number | null
    requiredQuantity: number
    unit: string
    unitPrice: number | null
    currencyCode: string
  }[]
}

// ── 未/済 ──────────────────────────────────────────
export interface ProductionStatusRow {
  familyId: number
  sku9: string
  productName: string
  materialOrder: string         // done / undone
  productionInstruction: string
  bomRegistered: boolean
}

export const useProduction = () => {
  const { apiFetch, apiData } = useApi()
  const config = useRuntimeConfig()

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
    getBom: async (familyId: number): Promise<ProductMaterialItem[]> =>
      await apiData<ProductMaterialItem[]>(`/products/families/${familyId}/materials`),
    replaceBom: async (familyId: number, materials: BomLineInput[]): Promise<ProductMaterialItem[]> =>
      await apiData<ProductMaterialItem[]>(`/products/families/${familyId}/materials`, {
        method: 'PUT', body: { materials },
      }),
    requirements: (familyId: number, quantity: number): Promise<MaterialRequirements> =>
      apiData<MaterialRequirements>(`/products/families/${familyId}/material-requirements?quantity=${quantity}`),

    // 生産指示書
    piList: async (includeCancelled = false): Promise<PiListItem[]> =>
      await apiData<PiListItem[]>(`/production-instructions?includeCancelled=${includeCancelled}`),
    piGet: (id: number): Promise<PiDetail> => apiData<PiDetail>(`/production-instructions/${id}`),
    // 作成 POST は Idempotency-Key 必須 (AKB-DOC-12、欠落は 400 AKB-SYS-004)。送信試行ごとに新規 UUID を生成する。
    piCreate: (payload: CreatePiPayload): Promise<{ id: number; instructionNo: string }> =>
      apiData<{ id: number; instructionNo: string }>(`/production-instructions`, {
        method: 'POST', body: payload, headers: { 'Idempotency-Key': crypto.randomUUID() },
      }),
    piUpdate: (id: number, payload: UpdatePiPayload): Promise<{ id: number; instructionNo: string }> =>
      apiData<{ id: number; instructionNo: string }>(`/production-instructions/${id}`, { method: 'PATCH', body: payload }),
    piIssue: (id: number): Promise<void> => apiFetch(`/production-instructions/${id}/issue`, { method: 'POST' }),
    piComplete: (id: number): Promise<void> => apiFetch(`/production-instructions/${id}/complete`, { method: 'POST' }),
    piCancel: (id: number, reason: string): Promise<void> =>
      apiFetch(`/production-instructions/${id}/cancel`, { method: 'POST', body: { reason } }),
    piDownloadExcel: (id: number, instructionNo: string): Promise<void> =>
      download(`/production-instructions/${id}/export.xlsx`, `PI_${instructionNo}.xlsx`),

    // 素材発注書
    prepareMaterialOrder: (req: { productionInstructionId?: number | null; productFamilyId?: number | null; quantity?: number | null }): Promise<MaterialRequirements> =>
      apiData<MaterialRequirements>(`/material-orders/prepare`, { method: 'POST', body: req }),
    moList: async (includeCancelled = false): Promise<MaterialOrderListItem[]> =>
      await apiData<MaterialOrderListItem[]>(`/material-orders?includeCancelled=${includeCancelled}`),
    moGet: (id: number): Promise<MaterialOrderDetail> => apiData<MaterialOrderDetail>(`/material-orders/${id}`),
    // 作成 POST は Idempotency-Key 必須 (AKB-DOC-12、欠落は 400 AKB-SYS-004)。送信試行ごとに新規 UUID を生成する。
    moCreate: (payload: CreateMaterialOrderPayload): Promise<{ id: number; orderNo: string }> =>
      apiData<{ id: number; orderNo: string }>(`/material-orders`, {
        method: 'POST', body: payload, headers: { 'Idempotency-Key': crypto.randomUUID() },
      }),
    moOrder: (id: number): Promise<void> => apiFetch(`/material-orders/${id}/order`, { method: 'POST' }),
    moCancel: (id: number, reason: string): Promise<void> =>
      apiFetch(`/material-orders/${id}/cancel`, { method: 'POST', body: { reason } }),
    moDownloadExcel: (id: number, orderNo: string): Promise<void> =>
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
