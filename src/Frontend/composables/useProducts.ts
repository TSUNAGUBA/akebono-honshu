/**
 * 商品関連 (P-01〜P-06) API ラッパー。
 * Backend /api/v1/products/families/** の各 endpoint を叩く。
 */

export interface FamilyListItem {
  id: number
  /** @deprecated 命名ミス。実体は 7 桁の品番。`itemNumber` を使用すること。 */
  sku9Digit: string
  /** 品番 7 桁 (例: NA1001A) — 新規企画品の業務キー */
  itemNumber: string
  /** 他品番 6 桁 (例: NA1001) — 商品ファミリーの識別子 */
  itemFamilyNumber: string
  /** 既存システム由来の品番 (例: FA2071F)。新規企画品では null */
  legacyId: string | null
  productName1: string
  productName2: string | null
  brandName: string
  productTypeName: string
  productSeasonName: string
  factorySupplierName: string
  status: number
  skuVariationCount: number
  imageCount: number
  /** 代表画像 (order_no=1 の有効画像) の s3_key。画像未登録時は null */
  primaryImageS3Key: string | null
  /**
   * 代表画像の配信用 URL (Iter 4 段階 C 追加)。
   * Local 時は absolute URL、S3 時は Pre-signed URL (TTL 15min)。画像未登録時は null。
   * `img.src` に直接渡す (URL 組立て規則は Frontend では意識しない)。
   */
  primaryImageUrl: string | null
  currentMinPrice: number | null
  currentMaxPrice: number | null
  currencyCode: string
  updatedAt: string
}

export interface SkuSummary {
  id: number
  sku: string
  colorId: number
  colorCode: string
  colorName: string
  sizeId: number
  sizeCode: string
  sizeName: string
  isDeleted: boolean
}

export interface ImageSummary {
  id: number
  orderNo: number
  s3Key: string
  thumbS3Key: string | null
  mimeType: string
  fileSizeBytes: number
  originalFilename: string | null
  /**
   * 画像配信用 URL (Iter 4 段階 C 追加)。
   * Local 時は `${apiOrigin}/uploads/...` の absolute URL、S3 時は Pre-signed URL (TTL 15min)。
   * `img.src` に直接渡せる形式で Backend が生成・返却する (URL 組立て規則は Frontend では意識しない)。
   * Pre-signed URL は実行ごとに署名が変わるため long-term cache 不可。
   */
  url: string
}

export interface CurrentSupplierPrice {
  id: number
  supplierId: number
  supplierCode: string
  supplierName: string
  unitPrice: number
  currencyCode: string
  effectiveFrom: string
  effectiveTo: string | null
  decidedAt: string
}

export interface FamilyFullInfo {
  id: number
  plannedYearCode: string
  sequenceNo: string
  /** 品番 7 桁 */
  itemNumber: string
  /** 他品番 6 桁 */
  itemFamilyNumber: string
  /** 既存システム由来の品番 (例: FA2071F)。新規企画品では null */
  legacyId: string | null
  productTypeId: number
  productTypeName: string
  productSeasonId: number
  productSeasonName: string
  factorySupplierId: number
  factorySupplierName: string
  brandId: number
  brandName: string
  functionId: number | null
  functionName: string | null
  productGroupId: number
  productGroupName: string
  upperMaterialId: number
  upperMaterialName: string
  insoleMaterialId: number
  insoleMaterialName: string
  outsoleMaterialId: number
  outsoleMaterialName: string
  productName1: string
  productName2: string | null
  status: number
  isDeleted: boolean
  createdAt: string
  updatedAt: string
}

export interface FamilyDetail {
  family: FamilyFullInfo
  products: SkuSummary[]
  images: ImageSummary[]
  currentSupplierPrices: CurrentSupplierPrice[]
}

export interface CompleteFamilyPayload {
  family: {
    plannedYearCode: string
    productTypeId: number
    productSeasonId: number
    factorySupplierId: number
    brandId: number
    functionId: number | null
    productGroupId: number
    upperMaterialId: number
    insoleMaterialId: number
    outsoleMaterialId: number
    productName1: string
    productName2: string | null
  }
  expansion: {
    colorIds: number[]
    sizeIds: number[]
  }
  supplierPrices: {
    supplierId: number
    unitPrice: number
    currencyCode: string
    exchangeRate: number | null
    effectiveFrom: string
    decidedAt: string
  }[]
}

export const productStatusLabel = (status: number): string => {
  switch (status) {
    case 0: return 'Draft (下書き)'
    case 1: return 'Active (有効)'
    case 2: return 'Discontinued (販売終了)'
    default: return `Unknown (${status})`
  }
}

export const useProducts = () => {
  const { apiFetch } = useApi()

  const listFamilies = async (includeDeleted = false): Promise<FamilyListItem[]> => {
    const res = await apiFetch<{ data: FamilyListItem[] }>(
      `/products/families?includeDeleted=${includeDeleted}`,
    )
    return res.data
  }

  const getFamily = async (id: number): Promise<FamilyDetail> => {
    return await apiFetch<FamilyDetail>(`/products/families/${id}`)
  }

  const createComplete = async (payload: CompleteFamilyPayload) => {
    return await apiFetch<{
      family: { id: number; sequenceNo: string; plannedYearCode: string }
      products: SkuSummary[]
      supplierPrices: { id: number; supplierId: number; unitPrice: number; effectiveFrom: string }[]
    }>('/products/families/complete', {
      method: 'POST',
      body: payload,
    })
  }

  const updateFamily = async (id: number, body: {
    brandId: number
    functionId: number | null
    productGroupId: number
    upperMaterialId: number
    insoleMaterialId: number
    outsoleMaterialId: number
    productName1: string
    productName2: string | null
    status: number
  }) => {
    return await apiFetch<FamilyFullInfo>(`/products/families/${id}`, {
      method: 'PATCH',
      body,
    })
  }

  const deleteFamily = async (id: number): Promise<void> => {
    await apiFetch<void>(`/products/families/${id}`, { method: 'DELETE' })
  }

  const addSupplierPrice = async (id: number, body: {
    supplierId: number
    unitPrice: number
    currencyCode: string
    exchangeRate: number | null
    effectiveFrom: string
    decidedAt: string
  }) => {
    return await apiFetch<{ id: number; supplierId: number; unitPrice: number; effectiveFrom: string }>(
      `/products/families/${id}/supplier-prices`,
      { method: 'POST', body },
    )
  }

  const uploadImage = async (familyId: number, file: File): Promise<ImageSummary> => {
    const form = new FormData()
    form.append('file', file)
    const { getIdToken } = useAuth()
    const token = await getIdToken()
    const res = await $fetch<ImageSummary>(
      `${config.public.apiBase}/products/families/${familyId}/images`,
      {
        method: 'POST',
        body: form,
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      },
    )
    return res
  }

  const deleteImage = async (familyId: number, imageId: number): Promise<void> => {
    await apiFetch<void>(`/products/families/${familyId}/images/${imageId}`, {
      method: 'DELETE',
    })
  }

  const reorderImages = async (familyId: number, orderedIds: number[]): Promise<void> => {
    await apiFetch<void>(`/products/families/${familyId}/images/reorder`, {
      method: 'PATCH',
      body: orderedIds,
    })
  }

  return {
    listFamilies,
    getFamily,
    createComplete,
    updateFamily,
    deleteFamily,
    addSupplierPrice,
    uploadImage,
    deleteImage,
    reorderImages,
  }
}
