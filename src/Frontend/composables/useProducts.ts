/**
 * 商品関連 (P-01〜P-06) API ラッパー。
 * Backend /api/maker/v1/products/families/** の各 endpoint を叩く。
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
   * Local 時は absolute URL、S3 時は Pre-signed URL (TTL 15min)。
   * 画像未登録時 / URL 取得失敗時 (S3 throttling 等) は null。
   * `img.src` に直接渡す。null の場合は `v-if` でプレースホルダーにフォールバックする
   * (Iter 4 段階 C-1 reviewer 指摘 M3/M6 対応、原則 4 非ブロッキング)。
   */
  primaryImageUrl: string | null
  currentMinPrice: number | null
  currentMaxPrice: number | null
  currencyCode: string
  updatedAt: string
  // 一覧の SPLIT フィルタ用 ID / 値 (P-04 商品一覧の絞込をクライアント側で行うために追加)。
  // *Name は表示用、以下の *Id は MasterSelect の数値 ID と突合してフィルタ一致判定に使う。
  /** 商品タイプ (product_types.id)。フィルタ「商品タイプ」用 */
  productTypeId: number
  /** 商品季節 (product_seasons.id)。フィルタ「商品季節」用 */
  productSeasonId: number
  /** 仕入先 = 工場 (suppliers.id)。フィルタ「仕入先」用 */
  factorySupplierId: number
  /** ブランド (brands.id)。フィルタ「ブランド」用 */
  brandId: number
  /** 商品年度 (9999=通年、Phase A)。フィルタ「商品年度」用 (前方/部分一致)。未設定時 null */
  productYear: number | null
  /** 仮番号 (Phase A)。フィルタ「仮番号」用 (部分一致)。未設定時 null */
  provisionalNumber: string | null
  /** 企画者 (users.id、Phase A)。フィルタ「企画者」用。未設定時 null */
  plannerUserId: number | null
  /** 企画者名 (表示用、Phase A)。未設定時 null */
  plannerName: string | null
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
  /** 画像区分 (§2a)。0=企画画像 / 1=本番画像。 */
  imageCategory: number
  s3Key: string
  thumbS3Key: string | null
  mimeType: string
  fileSizeBytes: number
  originalFilename: string | null
  /**
   * 画像配信用 URL (Iter 4 段階 C 追加)。
   * Local 時は `${apiOrigin}/uploads/...` の absolute URL、S3 時は Pre-signed URL (TTL 15min)。
   * URL 取得失敗時 (S3 throttling 等) は null、`v-if` でプレースホルダーにフォールバック
   * (Iter 4 段階 C-1 reviewer 指摘 M3/M6、原則 4 非ブロッキング)。
   * Pre-signed URL は実行ごとに署名が変わるため long-term cache 不可。
   */
  url: string | null
}

export interface CurrentSupplierPrice {
  id: number
  supplierId: number
  supplierCode: string
  supplierName: string
  unitPrice: number
  currencyCode: string
  exchangeRate: number | null
  effectiveFrom: string
  effectiveTo: string | null
  decidedAt: string
  // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
  /** 見積単価 */
  estimateUnitPrice: number | null
  /** 見積単価受領日 (YYYY-MM-DD) */
  estimateReceivedDate: string | null
  /** 見積原価 */
  estimateCost: number | null
  /** 見積利益率(%) */
  estimateMarginRate: number | null
  /** 仕入原価 */
  purchaseCost: number | null
  /** 仕入利益率(%) */
  purchaseMarginRate: number | null
  /** ロス費 */
  lossCost: number | null
  /** ドレー代 (旧「トレー代」、設計判断Q6) */
  drayageCost: number | null
  /** 税率(%) */
  taxRate: number | null
  // サイズ別仕入単価 (PR2、末尾追加 = 下位互換)
  /** サイズ (sizes.id)。null = 全サイズ共通の既定単価 */
  sizeId: number | null
  /** サイズ名 (表示用)。全サイズ共通 (sizeId=null) のときは null */
  sizeName: string | null
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
  // 旧 品番台帳 項目 (Phase A、全て任意)
  /** 商品年度 (9999=通年) */
  productYear: number | null
  /** 管理季節 (product_seasons.id) */
  managementSeasonId: number | null
  /** 管理季節名 (表示用) */
  managementSeasonName: string | null
  /** 企画者 (users.id) */
  plannerUserId: number | null
  /** 企画者名 (表示用) */
  plannerName: string | null
  /** 仮番号 */
  provisionalNumber: string | null
  /** サンプル合格日 (YYYY-MM-DD) */
  sampleApprovalDate: string | null
  /** 小売価格 */
  retailPrice: number | null
  /** 納品価格 */
  deliveryPrice: number | null
  /** 企画費 */
  planningCost: number | null
  /** ブランド費 */
  brandCost: number | null
  /** 版権対象 (1=小売価格, 2=納品価格) */
  royaltyTarget: number | null
  /** 版権料率(%) */
  royaltyRate: number | null
  // 旧 品番台帳 項目 追補 (PR1、全て任意)
  /** 商品本体 備考 (spec No.39) */
  remark: string | null
  /** 備考（色）(spec No.33、商品単位の単一テキスト) */
  colorRemark: string | null
  // 登録者/最終更新者 表示 (PR1、spec No.27/28)。display_name を join 解決。未解決時は null。
  /** 登録者名 (created_by_user_id の display_name) */
  createdByUserName: string | null
  /** 最終更新者名 (updated_by_user_id の display_name) */
  updatedByUserName: string | null
}

// アソート/セット明細 1 行 (PR3、旧 spec No.37 品番 / No.38 数量)
export interface SetComponentSummary {
  id: number
  /** 子品番 (手入力テキスト、FK なし) */
  childItemNumber: string
  /** 数量 (正の整数) */
  quantity: number
  /** 表示順 (配列順で採番) */
  lineNo: number
}

export interface FamilyDetail {
  family: FamilyFullInfo
  products: SkuSummary[]
  images: ImageSummary[]
  currentSupplierPrices: CurrentSupplierPrice[]
  // アソート/セット明細 (PR3、末尾追加 = 下位互換)。明細なしの商品では空配列。
  // 既存応答との互換のため null/undefined も許容し、画面側で `?? []` で吸収する。
  setComponents?: SetComponentSummary[] | null
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
    // 旧 品番台帳 項目 (Phase A、全て任意)
    productYear: number | null
    managementSeasonId: number | null
    plannerUserId: number | null
    provisionalNumber: string | null
    sampleApprovalDate: string | null
    retailPrice: number | null
    deliveryPrice: number | null
    planningCost: number | null
    brandCost: number | null
    royaltyTarget: number | null
    royaltyRate: number | null
    // 旧 品番台帳 項目 追補 (PR1、全て任意)
    remark: string | null
    colorRemark: string | null
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
    // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
    estimateUnitPrice: number | null
    estimateReceivedDate: string | null
    estimateCost: number | null
    estimateMarginRate: number | null
    purchaseCost: number | null
    purchaseMarginRate: number | null
    lossCost: number | null
    drayageCost: number | null
    taxRate: number | null
    // サイズ別仕入単価 (PR2、null = 全サイズ共通の既定単価)
    sizeId: number | null
  }[]
  // アソート/セット明細 (PR3、末尾追加 = 下位互換)。明細なし (通常商品) の場合は空配列を送る。
  setComponents: {
    childItemNumber: string
    quantity: number
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
  const { apiFetch, apiData } = useApi()

  const listFamilies = async (includeDeleted = false): Promise<FamilyListItem[]> => {
    return await apiData<FamilyListItem[]>(`/products/families?includeDeleted=${includeDeleted}`)
  }

  const getFamily = async (id: number): Promise<FamilyDetail> => {
    return await apiData<FamilyDetail>(`/products/families/${id}`)
  }

  const createComplete = async (payload: CompleteFamilyPayload) => {
    return await apiData<{
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
    // 旧 品番台帳 項目 (Phase A、全て任意)
    productYear: number | null
    managementSeasonId: number | null
    plannerUserId: number | null
    provisionalNumber: string | null
    sampleApprovalDate: string | null
    retailPrice: number | null
    deliveryPrice: number | null
    planningCost: number | null
    brandCost: number | null
    royaltyTarget: number | null
    royaltyRate: number | null
    // 旧 品番台帳 項目 追補 (PR1、全て任意)
    remark: string | null
    colorRemark: string | null
    // アソート/セット明細 (PR3、末尾追加 = 下位互換)。
    // 配列 (空含む) を送ると全置換、undefined を送ると既存明細を保持 (PATCH セマンティクス)。
    // 編集画面からは常に配列を送るため全置換になる。
    setComponents?: { childItemNumber: string; quantity: number }[]
  }): Promise<void> => {
    // PATCH は 204 No Content を返す (PR3 以降、参照循環回避のため body を返さない)。
    // 呼び出し側は更新後に getFamily で再取得する。
    await apiFetch<void>(`/products/families/${id}`, {
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
    // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
    estimateUnitPrice: number | null
    estimateReceivedDate: string | null
    estimateCost: number | null
    estimateMarginRate: number | null
    purchaseCost: number | null
    purchaseMarginRate: number | null
    lossCost: number | null
    drayageCost: number | null
    taxRate: number | null
    // サイズ別仕入単価 (PR2、null = 全サイズ共通の既定単価)
    sizeId: number | null
  }) => {
    return await apiData<{ id: number; supplierId: number; unitPrice: number; effectiveFrom: string; sizeId: number | null }>(
      `/products/families/${id}/supplier-prices`,
      { method: 'POST', body },
    )
  }

  // 画像アップロード (§2a)。category=0(企画)/1(本番) を query で指定 (multipart body は file のみ)。
  // FormData も apiData 経由で送れる (Authorization / X-Tenant-Id を共通付与、応答はエンベロープ unwrap)。
  const uploadImage = async (familyId: number, file: File, category = 0): Promise<ImageSummary> => {
    const form = new FormData()
    form.append('file', file)
    return await apiData<ImageSummary>(
      `/products/families/${familyId}/images?category=${category}`,
      {
        method: 'POST',
        body: form,
      },
    )
  }

  const deleteImage = async (familyId: number, imageId: number): Promise<void> => {
    await apiFetch<void>(`/products/families/${familyId}/images/${imageId}`, {
      method: 'DELETE',
    })
  }

  // 並び順変更 (§2a)。区分ごとに独立して振り直すため category を query で指定。
  const reorderImages = async (familyId: number, orderedIds: number[], category = 0): Promise<void> => {
    await apiFetch<void>(`/products/families/${familyId}/images/reorder?category=${category}`, {
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
