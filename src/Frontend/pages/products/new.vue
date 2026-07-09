<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CompleteFamilyPayload, FamilyListItem, FamilyDetail } from '~/composables/useProducts'

const { canEditMaster } = useAuth()
const { list } = useMasters()
// 参照コピー (PR4): 既存商品の検索 (listFamilies) と詳細取得 (getFamily) を再利用する。
// 商品一覧 P-04 (index.vue) と同じくサーバ側検索 API を増やさず、全件取得 + クライアント側
// 部分一致でコピー元を選ぶ。詳細はバルク登録 (createComplete) と同じ既存 endpoint。
const { createComplete, listFamilies, getFamily, uploadImage } = useProducts()
const { apiData } = useApi()

// マスタ参照データ
const productTypes = ref<MasterItem[]>([])
const productSeasons = ref<MasterItem[]>([])
const suppliers = ref<MasterItem[]>([])
const brands = ref<MasterItem[]>([])
const functions_ = ref<MasterItem[]>([])
const productGroups = ref<MasterItem[]>([])
const materials = ref<MasterItem[]>([])
const colors = ref<MasterItem[]>([])
const sizes = ref<MasterItem[]>([])

// ユーザ候補 (企画者用)。orders/new.vue と同じ /users から取得。
interface UserOption { id: number; loginId: string; displayName: string }
const users = ref<UserOption[]>([])
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))
// 版権対象 (1=小売価格, 2=納品価格)。AutoComplete は string 値で扱う。
const royaltyTargetOptions = [{ value: '1', label: '小売価格' }, { value: '2', label: '納品価格' }]

const loading = ref(true)
const submitting = ref(false)
const errorMessage = ref('')
const successMessage = ref('')

// フォーム
const yearCodes = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'N', 'Z']
const form = ref({
  plannedYearCode: 'N',
  productTypeId: 0,
  productSeasonId: 0,
  factorySupplierId: 0,
  brandId: 0,
  functionId: null as number | null,
  productGroupId: 0,
  upperMaterialId: 0,
  insoleMaterialId: 0,
  outsoleMaterialId: 0,
  productName1: '',
  // 旧 品番台帳 項目 (Phase A、全て任意)
  productYear: null as number | null,
  managementSeasonId: null as number | null,
  plannerUserId: null as number | null,
  provisionalNumber: '',
  sampleApprovalDate: '',
  retailPrice: null as number | null,
  deliveryPrice: null as number | null,
  planningCost: null as number | null,
  // ブランド費 (brandCost) は §2c で自動計算 (版権対象価格 × 版権料率)。手入力欄は廃止し brandCostComputed を送る。
  royaltyTarget: null as number | null,
  royaltyRate: null as number | null,
  // 旧 品番台帳 項目 追補 (PR1、全て任意)
  remark: '',
  colorRemark: '',
})

const expansion = ref({
  colorIds: [] as number[],
  sizeIds: [] as number[],
})

// ブランド費 自動計算 (§2c)。ブランド費 = 版権対象で選んだ価格 × 版権料率(%)。
// 版権対象 1=小売価格(retailPrice), 2=納品価格(deliveryPrice)。対象/料率/基準価格が揃わなければ null。
const brandCostBasePrice = computed<number | null>(() => {
  if (form.value.royaltyTarget === 1) return form.value.retailPrice
  if (form.value.royaltyTarget === 2) return form.value.deliveryPrice
  return null
})
const brandCostComputed = computed<number | null>(() => {
  const base = brandCostBasePrice.value
  const rate = form.value.royaltyRate
  if (base == null || rate == null) return null
  // rate は % 表記なので /100。円は小数第2位まで許容 → 四捨五入で 2 桁に丸める。
  return Math.round(base * rate) / 100
})

// ───────────────────────────────────────────────────────────────────────────
// 商品画像 (P-06 を新規登録フォームに前倒し)。
// 新規フォーム時点では family が未採番のため、ここではファイルを保持・プレビューのみ行い、
// 実アップロードは登録 (createComplete) 成功後、採番された familyId に対して既存 P-06 endpoint
// (uploadImage) で 1 枚ずつ行う 2 段階方式。バックエンド変更なしで詳細画面と同じ制約を満たす
// (原則 3 既存パターン再利用)。制約は詳細画面 (P-05/06) と同一: JPEG/PNG/WebP・各 5MB・最大 5 枚 (BR-10)。
// §2a: 企画画像(0)/本番画像(1) の 2 区分で登録。上限は区分ごとに最大 5 枚 (BR-10)。
const IMAGE_MAX_COUNT = 5
const IMAGE_MAX_BYTES = 5 * 1024 * 1024
const IMAGE_ALLOWED_MIME = ['image/jpeg', 'image/png', 'image/webp']
const IMAGE_CATEGORIES = [
  { key: 0, label: '企画画像' },
  { key: 1, label: '本番画像' },
] as const
interface PendingImage { file: File; previewUrl: string; category: number }
const pendingImages = ref<PendingImage[]>([])
const imageInputs = ref<Record<number, HTMLInputElement | null>>({ 0: null, 1: null })
const setImageInput = (cat: number, el: HTMLInputElement | null) => { imageInputs.value[cat] = el }
const imageError = ref('')
const formatBytes = (b: number) => `${(b / 1024).toFixed(1)} KB`

// 登録成功後に採番された family.id を保持する。二重採番防止 (canSubmit=false) と、
// 画像が一部失敗したときの再アップロード/詳細画面導線に使う (原則 2 状態保護)。
const registeredFamilyId = ref<number | null>(null)
// アップロード成功済み枚数を区分別に保持 (サーバ保存済み枚数)。合計は imageUploadedCount。
const imageUploadedByCat = ref<Record<number, number>>({ 0: 0, 1: 0 })
const imageUploadedCount = computed(() => imageUploadedByCat.value[0] + imageUploadedByCat.value[1])
const retryingImages = ref(false)
// 区分ごとの保留・スロット判定。上限は「保存済み + 保留」で BR-10 (区分ごと最大 5 枚) と整合 (原則 6)。
const pendingByCat = (cat: number): PendingImage[] => pendingImages.value.filter((p) => p.category === cat)
const slotsUsed = (cat: number): number => imageUploadedByCat.value[cat] + pendingByCat(cat).length

const onImageSelect = (event: Event, category: number) => {
  const target = event.target as HTMLInputElement
  if (!target.files?.length) return
  // アップロード実行中 (登録/再送) は pendingImages を変更させない (反復中の競合防止)。
  if (submitting.value || retryingImages.value) { target.value = ''; return }
  imageError.value = ''
  const label = category === 1 ? '本番' : '企画'
  const skipped: string[] = []
  for (const file of Array.from(target.files)) {
    if (slotsUsed(category) >= IMAGE_MAX_COUNT) {
      skipped.push(`${label}画像は最大 ${IMAGE_MAX_COUNT} 枚まで`)
      break
    }
    if (!IMAGE_ALLOWED_MIME.includes(file.type)) {
      skipped.push(`${file.name} (非対応形式)`)
      continue
    }
    if (file.size > IMAGE_MAX_BYTES) {
      skipped.push(`${file.name} (5MB 超過)`)
      continue
    }
    pendingImages.value.push({ file, previewUrl: URL.createObjectURL(file), category })
  }
  if (skipped.length > 0) {
    imageError.value = `次の画像は登録できませんでした — ${skipped.join(' / ')}`
  }
  // 同じファイルを連続選択できるよう input をリセット (詳細画面の onFileSelect と同じ作法)
  target.value = ''
}

// 保留画像を参照 (identity) で削除。区分別グリッドで表示するため index ではなくオブジェクトで指定する。
const removePendingImage = (img: PendingImage) => {
  if (submitting.value || retryingImages.value) return
  const idx = pendingImages.value.indexOf(img)
  if (idx < 0) return
  const [removed] = pendingImages.value.splice(idx, 1)
  if (removed) URL.revokeObjectURL(removed.previewUrl)
}

// 採番済 familyId へ pendingImages を 1 枚ずつアップロードする共通処理 (登録時・再試行で共用)。
// 各画像は自身の区分 (§2a) を付けて送る。成功分は pendingImages から除去 (object URL 解放) し
// imageUploadedByCat を区分別に加算、失敗分は残して再試行可能にする (原則 4 非ブロッキング)。
const uploadPendingImages = async (familyId: number): Promise<{ uploaded: number; failed: number }> => {
  let uploaded = 0
  let lastErrorDetail = ''
  const remaining: PendingImage[] = []
  for (const img of [...pendingImages.value]) {
    try {
      await uploadImage(familyId, img.file, img.category)
      uploaded++
      imageUploadedByCat.value[img.category]++
      URL.revokeObjectURL(img.previewUrl)
    } catch (uploadErr) {
      remaining.push(img)
      const detail = getApiErrorMessage(uploadErr, '')
      if (detail) lastErrorDetail = detail
      console.error('画像アップロードに失敗しました:', img.file.name, uploadErr)
    }
  }
  pendingImages.value = remaining
  imageError.value = remaining.length === 0
    ? ''
    : `画像 ${remaining.length} 枚のアップロードに失敗しました${lastErrorDetail ? `: ${lastErrorDetail}` : ''}。再アップロードできます。`
  return { uploaded, failed: remaining.length }
}

// 部分失敗後の再アップロード。family は採番済のため uploadImage のみ呼ぶ (二重採番なし)。
const retryImageUpload = async () => {
  if (registeredFamilyId.value == null || retryingImages.value || pendingImages.value.length === 0) return
  retryingImages.value = true
  try {
    await uploadPendingImages(registeredFamilyId.value)
    if (pendingImages.value.length === 0) await navigateTo(`/products/${registeredFamilyId.value}`)
  } finally {
    retryingImages.value = false
  }
}

// プレビュー用 object URL のリーク防止 (画面離脱時に解放)
onBeforeUnmount(() => {
  for (const img of pendingImages.value) URL.revokeObjectURL(img.previewUrl)
})

// アソート/セット明細 (PR3、旧 spec No.37/38)。子品番 (手入力テキスト) + 数量の行コレクション。
// 任意 (空でも可)。空 = 通常商品。色サイズ/単価明細と同じ「行追加/削除」パターン。
const setComponents = ref<{ childItemNumber: string; quantity: number }[]>([])
const addSetComponent = () => {
  setComponents.value.push({ childItemNumber: '', quantity: 1 })
}
const removeSetComponent = (idx: number) => {
  setComponents.value.splice(idx, 1)
}

// ⑤ 仕入単価 (§2d/e)。複数の仕入先 × サイズごとに入力できるよう、単一オブジェクトから行コレクションへ変更。
// 通貨は選択した仕入先の適用通貨 (Supplier.currencyCode) から自動決定。為替マスタから適用レートを解決し、
// 円換算・仕入原価・仕入利益率・ドレー代を自動計算する (行には入力せず算出する)。
// 原価明細 (見積単価/ロス費/税率) は登録後の詳細画面で追加する (新規フォームは行を高くしないため省く)。
const today0 = todayJst() // 業務日付は JST 基準
interface PriceRow {
  supplierId: number
  sizeId: number | null   // §2e 全サイズ共通(null) / サイズ別
  unitPrice: number
  effectiveFrom: string
  decidedAt: string
}
const makePriceRow = (): PriceRow => ({ supplierId: 0, sizeId: null, unitPrice: 0, effectiveFrom: today0, decidedAt: today0 })
const supplierPrices = ref<PriceRow[]>([makePriceRow()])
const addPriceRow = () => {
  const row = makePriceRow()
  if (suppliers.value.length) row.supplierId = suppliers.value[0].id
  supplierPrices.value.push(row)
}
const removePriceRow = (idx: number) => { if (supplierPrices.value.length > 1) supplierPrices.value.splice(idx, 1) }

// 為替マスタ (§2f)。年月×通貨の対円レート。onMounted で読み込む。
interface ExchangeRateItem { yearMonth: string; currencyCode: string; rate: number }
const exchangeRates = ref<ExchangeRateItem[]>([])

// 仕入先の適用通貨/ドレー代を参照するヘルパー (suppliers は MasterItem[]、拡張列を動的保持)。
const supplierById = (id: number) => suppliers.value.find((s) => s.id === id)
const currencyOfRow = (row: PriceRow): string => (supplierById(row.supplierId)?.currencyCode as string) || 'JPY'
const drayageOfRow = (row: PriceRow): number | null => {
  const d = supplierById(row.supplierId)?.drayageCost
  return d == null ? null : Number(d)
}
// (年月, 通貨) の適用レートを解決 (§2f)。JPY=1。厳密一致が無ければ対象年月以前の最新レート。未登録は null。
const resolveRate = (yearMonth: string, currency: string): number | null => {
  if (currency === 'JPY') return 1
  const cands = exchangeRates.value.filter((r) => r.currencyCode === currency && r.yearMonth <= yearMonth)
  if (cands.length === 0) return null
  return cands.reduce((best, r) => (r.yearMonth > best.yearMonth ? r : best)).rate
}
const rateOfRow = (row: PriceRow): number | null => resolveRate((row.effectiveFrom || today0).slice(0, 7), currencyOfRow(row))
// 円換算仕入単価 (§2f)。JPY はそのまま、外貨は 単価 × 適用レート。レート未解決は null。
const yenUnitPriceOfRow = (row: PriceRow): number | null => {
  if (currencyOfRow(row) === 'JPY') return Number(row.unitPrice) || 0
  const rate = rateOfRow(row)
  return rate == null ? null : Math.round((Number(row.unitPrice) || 0) * rate * 100) / 100
}
// 仕入原価 (§2g) = 円換算仕入単価 + ブランド費。円換算不能なら null。
const purchaseCostOfRow = (row: PriceRow): number | null => {
  const yen = yenUnitPriceOfRow(row)
  if (yen == null) return null
  return Math.round((yen + (brandCostComputed.value ?? 0)) * 100) / 100
}
// 仕入利益率 (§2h、%) = 仕入原価 ÷ 納品価格 × 100。納品価格未設定/0 は null。
const purchaseMarginOfRow = (row: PriceRow): number | null => {
  const cost = purchaseCostOfRow(row)
  const dp = form.value.deliveryPrice
  if (cost == null || dp == null || dp === 0) return null
  return Math.round((cost / dp) * 10000) / 100
}
// DB 列の桁上限を超える自動計算値は保存できないため、保存時のみ null にフォールバックする
// (review #3、numeric overflow 500 防止)。画面表示は実値のまま、payload の保存値だけ範囲内に収める。
const NUMERIC_5_2_MAX = 999.99       // purchase_margin_rate NUMERIC(5,2)
const NUMERIC_12_2_MAX = 9999999999.99 // purchase_cost NUMERIC(12,2)
const fitOrNull = (v: number | null, maxAbs: number): number | null =>
  v == null ? null : (Math.abs(v) <= maxAbs ? v : null)
const yen = (n: number | null): string => (n == null ? '—' : `¥${n.toLocaleString()}`)

// ───────────────────────────────────────────────────────────────────────────
// 参照コピー登録 (PR4、旧 spec 商品マスタ本体 No.3 参照コード/コピー)
// 既存商品を品番/商品名/legacy_id で検索選択 → 詳細を取得して編集可能フィールドを
// この新規フォームの state にコピー (プリフィル)。コピー後はあくまで新規起票の初期値で、
// 保存は通常どおり POST /complete (新 family として新採番)。参照元は読取のみで一切変更しない。
//
// コピーしない項目: SKU/品番(7桁)/legacy_id/画像/id/監査列/status (新規は採番・既定値)。
const referenceFamilies = ref<FamilyListItem[]>([]) // 検索元 (一覧 API の全件、index.vue と同様)
const referenceSourceId = ref<number | null>(null)   // 選択中のコピー元 family.id
const copying = ref(false)
const copyNotice = ref('')                            // コピー実行後の結果メッセージ (件数等)

// AutoComplete のソース。品番 (itemNumber) / legacy_id / 商品名 1,2 を検索対象に含める。
// MasterSelect は label/code 数値マスタ向けなので、ここは AutoComplete を直接使い
// searchText に検索対象テキストを集約する。value は family.id の文字列。
const referenceOptions = computed(() =>
  referenceFamilies.value.map((f) => ({
    value: String(f.id),
    label: `${f.itemNumber} ${f.productName1}${f.productName2 ? ` / ${f.productName2}` : ''}`,
    // 品番・他品番・legacy_id・商品名 1/2 を検索テキストに集約 (部分一致で引けるように)。
    searchText: [f.itemNumber, f.itemFamilyNumber, f.legacyId ?? '', f.productName1, f.productName2 ?? '']
      .filter((s) => s !== '')
      .join(' '),
  })),
)


// コピー元一覧の読込はコピー機能専用の補助処理。失敗しても新規登録本体は使えるため
// 致命的ではない (原則 4 非ブロッキング)。削除済みは除外 (includeDeleted=false) し、
// 生きている商品のみコピー元にする。
const loadReferenceSources = async () => {
  try {
    referenceFamilies.value = await listFamilies(false)
  } catch (e) {
    // コピー元候補の取得失敗はコピー機能が使えなくなるだけ。新規登録フローには影響しない。
    console.error('参照コピー元の商品一覧取得に失敗しました', e)
  }
}

// 選択中のコピー元の詳細を取得し、編集可能フィールドを新規フォーム state へ反映する。
// 明示ボタン操作時のみ実行 (誤操作で入力済みの値を失わないため)。
const onCopyFromReference = async () => {
  copyNotice.value = ''
  errorMessage.value = ''
  if (referenceSourceId.value == null) {
    errorMessage.value = 'コピー元の商品を選択してください'
    return
  }
  copying.value = true
  try {
    const src: FamilyDetail = await getFamily(referenceSourceId.value)
    const f = src.family

    // (1) family の編集可能項目をコピー (SKU/品番/legacy_id/id/監査列/status は除外)。
    //     新規フォーム state は空文字/null を入力欄で扱うため、null は '' に正規化する
    //     (reload() の編集フォーム初期化と同じ ?? '' 規約)。
    form.value.productTypeId = f.productTypeId
    form.value.productSeasonId = f.productSeasonId
    form.value.factorySupplierId = f.factorySupplierId
    form.value.brandId = f.brandId
    form.value.functionId = f.functionId
    form.value.productGroupId = f.productGroupId
    form.value.upperMaterialId = f.upperMaterialId
    form.value.insoleMaterialId = f.insoleMaterialId
    form.value.outsoleMaterialId = f.outsoleMaterialId
    form.value.productName1 = f.productName1
    form.value.productYear = f.productYear
    form.value.managementSeasonId = f.managementSeasonId
    form.value.plannerUserId = f.plannerUserId
    form.value.provisionalNumber = f.provisionalNumber ?? ''
    form.value.sampleApprovalDate = f.sampleApprovalDate ?? ''
    form.value.retailPrice = f.retailPrice
    form.value.deliveryPrice = f.deliveryPrice
    form.value.planningCost = f.planningCost
    // ブランド費 (§2c) は版権対象/料率から自動計算するため個別コピーしない (royaltyTarget/Rate のコピーで再計算)。
    form.value.royaltyTarget = f.royaltyTarget
    form.value.royaltyRate = f.royaltyRate
    form.value.remark = f.remark ?? ''
    form.value.colorRemark = f.colorRemark ?? ''
    // 年式 (planned_year_code) は family ヘッダ由来でコピー可。新採番は連番のみ。
    form.value.plannedYearCode = f.plannedYearCode

    // (2) 色サイズ展開を SKU 一覧から復元 (distinct colorId / sizeId)。
    //     削除済み SKU (isDeleted) は除外し、生きている組合せの色・サイズのみ採用する。
    const liveProducts = src.products.filter((p) => !p.isDeleted)
    expansion.value.colorIds = [...new Set(liveProducts.map((p) => p.colorId))]
    expansion.value.sizeIds = [...new Set(liveProducts.map((p) => p.sizeId))]

    // (3) 仕入単価明細をコピー (§2d/e サイズ別・複数対応)。現有効な単価行を全て supplierPrices の行に載せる。
    //     通貨/為替/仕入原価/利益率/ドレー代は自動算出のため、コピーするのは supplier/size/単価/日付のみ。
    const prices = src.currentSupplierPrices
    if (prices.length > 0) {
      supplierPrices.value = prices.map((p) => ({
        supplierId: p.supplierId,
        sizeId: p.sizeId,
        unitPrice: p.unitPrice,
        effectiveFrom: p.effectiveFrom,
        decidedAt: p.decidedAt,
      }))
    } else {
      supplierPrices.value = [makePriceRow()]
    }

    // (4) アソート/セット明細をコピー (PR3)。子品番 + 数量。lineNo 昇順は API 側で保証済。
    setComponents.value = (src.setComponents ?? []).map((c) => ({
      childItemNumber: c.childItemNumber,
      quantity: c.quantity,
    }))

    copyNotice.value = `品番 ${f.itemNumber}「${f.productName1}」の内容をコピーしました`
      + ` (色 ${expansion.value.colorIds.length} / サイズ ${expansion.value.sizeIds.length}`
      + ` / 仕入単価 ${supplierPrices.value.length} 件`
      + ` / アソート ${setComponents.value.length} 行)。`
      + ' このまま編集して登録すると新しい品番が採番されます。'
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, 'コピー元の取得に失敗しました')
  } finally {
    copying.value = false
  }
}

onMounted(async () => {
  // 参照コピー元一覧は補助処理。本体マスタ読込 (フォーム表示の前提) とは独立に発火させ、
  // 失敗してもフォームは使えるようにする (原則 4 非ブロッキング、await しない)。
  loadReferenceSources()
  try {
    const [pt, ps, sup, br, fn, pg, mt, co, sz, usrRes, exrRes] = await Promise.all([
      list('product-types'),
      list('product-seasons'),
      list('suppliers'),
      list('brands'),
      list('functions'),
      list('product-groups'),
      list('materials'),
      list('colors'),
      list('sizes'),
      apiData<UserOption[]>('/users'),
      // 為替マスタ (§2f)。失敗しても本体は使える (円換算は「レート未登録」表示にフォールバック)。
      apiData<ExchangeRateItem[]>('/masters/exchange-rates').catch(() => [] as ExchangeRateItem[]),
    ])
    productTypes.value = pt
    productSeasons.value = ps
    suppliers.value = sup
    brands.value = br
    functions_.value = fn
    productGroups.value = pg
    materials.value = mt
    colors.value = co
    sizes.value = sz
    users.value = usrRes
    exchangeRates.value = exrRes

    // 初期値設定
    if (pt.length) form.value.productTypeId = pt[0].id
    if (ps.length) form.value.productSeasonId = ps[0].id
    if (sup.length) {
      form.value.factorySupplierId = sup[0].id
      supplierPrices.value[0].supplierId = sup[0].id
    }
    if (br.length) form.value.brandId = br[0].id
    if (pg.length) form.value.productGroupId = pg[0].id
    if (mt.length) {
      form.value.upperMaterialId = mt[0].id
      form.value.insoleMaterialId = mt[0].id
      form.value.outsoleMaterialId = mt[0].id
    }
  } catch (e) {
    errorMessage.value = 'マスタ情報の取得に失敗しました'
  } finally {
    loading.value = false
  }
})

const toggleColor = (id: number) => {
  const i = expansion.value.colorIds.indexOf(id)
  if (i >= 0) expansion.value.colorIds.splice(i, 1)
  else expansion.value.colorIds.push(id)
}

const toggleSize = (id: number) => {
  const i = expansion.value.sizeIds.indexOf(id)
  if (i >= 0) expansion.value.sizeIds.splice(i, 1)
  else expansion.value.sizeIds.push(id)
}

const skuCount = computed(() => expansion.value.colorIds.length * expansion.value.sizeIds.length)

// ⑤ 仕入単価行の妥当性 (review 対応)。問題があればユーザ向けメッセージ、無ければ null。
// (a) 同一 (仕入先, サイズ, 有効開始日) の重複行は DB 一意制約違反になるため事前に弾く (review #2)。
// (b) 外貨仕入先の行は当該年月の為替レートが必要 (未登録だと円換算/原価/利益率が算出できない、review #4)。
const priceRowIssue = computed<string | null>(() => {
  const seen = new Set<string>()
  for (const r of supplierPrices.value) {
    if (r.supplierId <= 0 || Number(r.unitPrice) <= 0) continue
    const key = `${r.supplierId}|${r.sizeId ?? 'all'}|${r.effectiveFrom}`
    if (seen.has(key)) {
      return '仕入単価に「仕入先 × サイズ × 有効開始日」が重複した行があります。サイズ・有効開始日・仕入先のいずれかを変えるか、重複行を削除してください。'
    }
    seen.add(key)
    if (currencyOfRow(r) !== 'JPY' && rateOfRow(r) == null) {
      const sup = supplierById(r.supplierId)
      return `外貨 (${currencyOfRow(r)}) の仕入先「${sup?.name ?? ''}」の行に、${(r.effectiveFrom || today0).slice(0, 7)} 以前の為替レートが登録されていません。為替マスタに登録してください。`
    }
  }
  return null
})

const canSubmit = computed(() =>
  form.value.productName1.trim() !== '' &&
  expansion.value.colorIds.length > 0 &&
  expansion.value.sizeIds.length > 0 &&
  // 全ての仕入単価行が 仕入先選択済 かつ 単価 > 0 (§2d 複数行対応)
  supplierPrices.value.length > 0 &&
  supplierPrices.value.every((r) => r.supplierId > 0 && Number(r.unitPrice) > 0) &&
  // 重複行・外貨レート未登録が無いこと (review #2/#4)
  priceRowIssue.value == null &&
  // 登録成功後は再送不可 (二重採番防止、原則 2)
  registeredFamilyId.value == null &&
  !submitting.value)

const onSubmit = async () => {
  errorMessage.value = ''
  successMessage.value = ''
  // createComplete 失敗で再送信する経路に備えた前回値クリア (成功後は canSubmit=false で再入しない)
  imageUploadedByCat.value = { 0: 0, 1: 0 }
  if (!canSubmit.value) {
    // 仕入単価行の具体的な問題があればそれを優先表示 (review #2/#4)。
    errorMessage.value = priceRowIssue.value ?? '必須項目を入力してください (商品名 / 色 / サイズ / 単価)'
    return
  }
  submitting.value = true
  try {
    const payload: CompleteFamilyPayload = {
      family: {
        plannedYearCode: form.value.plannedYearCode,
        productTypeId: form.value.productTypeId,
        productSeasonId: form.value.productSeasonId,
        factorySupplierId: form.value.factorySupplierId,
        brandId: form.value.brandId,
        functionId: form.value.functionId,
        productGroupId: form.value.productGroupId,
        upperMaterialId: form.value.upperMaterialId,
        insoleMaterialId: form.value.insoleMaterialId,
        outsoleMaterialId: form.value.outsoleMaterialId,
        productName1: form.value.productName1.trim(),
        // 商品名 2 は新規登録フォームから除外 (§2b)。既存 I/F 互換のため null 固定で送る。
        productName2: null,
        // 旧 品番台帳 項目 (Phase A、未入力は null)
        productYear: form.value.productYear,
        managementSeasonId: form.value.managementSeasonId,
        plannerUserId: form.value.plannerUserId,
        provisionalNumber: form.value.provisionalNumber.trim() || null,
        sampleApprovalDate: form.value.sampleApprovalDate || null,
        retailPrice: form.value.retailPrice,
        deliveryPrice: form.value.deliveryPrice,
        planningCost: form.value.planningCost,
        // ブランド費 自動計算 (§2c): 版権対象価格 × 版権料率。
        brandCost: brandCostComputed.value,
        royaltyTarget: form.value.royaltyTarget,
        royaltyRate: form.value.royaltyRate,
        // 旧 品番台帳 項目 追補 (PR1、未入力は null)
        remark: form.value.remark.trim() || null,
        colorRemark: form.value.colorRemark.trim() || null,
      },
      expansion: {
        colorIds: [...expansion.value.colorIds],
        sizeIds: [...expansion.value.sizeIds],
      },
      // ⑤ 仕入単価 (§2d/e)。複数の仕入先×サイズ行を送る。通貨/為替/仕入原価/仕入利益率/ドレー代は
      // 選択仕入先・為替マスタ・ブランド費・納品価格から算出した値を送る (§2f/g/h/i)。
      supplierPrices: supplierPrices.value.map((r) => {
        const currency = currencyOfRow(r)
        return {
          supplierId: r.supplierId,
          unitPrice: Number(r.unitPrice),
          currencyCode: currency,
          exchangeRate: currency === 'JPY' ? null : rateOfRow(r),
          effectiveFrom: r.effectiveFrom,
          decidedAt: r.decidedAt,
          // 原価明細 (見積系/ロス費/税率) は新規フォームでは入力しない (詳細画面で追加)。
          estimateUnitPrice: null,
          estimateReceivedDate: null,
          estimateCost: null,
          estimateMarginRate: null,
          // 仕入原価 (§2g) / 仕入利益率 (§2h) は自動算出値を送る。DB 桁上限超は保存不可のため null 化 (review #3)。
          purchaseCost: fitOrNull(purchaseCostOfRow(r), NUMERIC_12_2_MAX),
          purchaseMarginRate: fitOrNull(purchaseMarginOfRow(r), NUMERIC_5_2_MAX),
          lossCost: null,
          // ドレー代 (§2i) は選択仕入先の設定値を自動反映。
          drayageCost: drayageOfRow(r),
          taxRate: null,
          // サイズ別仕入単価 (§2e、未選択は null = 全サイズ共通の既定単価)
          sizeId: r.sizeId,
        }
      }),
      // アソート/セット明細 (PR3)。子品番が空の行は除外 (未入力行を送らない)。
      // 子品番は trim、数量は Number 化 (空入力時の NaN は 0 に丸めてサーバの SETC-002 で弾く)。
      // 空配列 = 明細なし (通常商品) として送る。
      setComponents: setComponents.value
        .filter((c) => c.childItemNumber.trim() !== '')
        .map((c) => ({ childItemNumber: c.childItemNumber.trim(), quantity: Number(c.quantity) || 0 })),
    }
    const res = await createComplete(payload)
    // 登録本体は成功。以後は二重採番防止のため canSubmit=false にする (原則 2)。
    registeredFamilyId.value = res.family.id

    // 画像アップロード (2 段階目): 採番済 familyId へ既存 P-06 endpoint で 1 枚ずつ送る。
    // 失敗分は pendingImages に残り、下の導線パネルから選び直しなしで再アップロードできる
    // (原則 4 非ブロッキング)。family は採番済のため例外を投げて再送させると二重採番になる。
    // uploadPendingImages が区分別に imageUploadedByCat を加算する (imageUploadedCount はその合算)。
    const { uploaded, failed } = await uploadPendingImages(res.family.id)
    // 登録本体は成功。緑バナーで明示し、画像が残れば下の導線パネルで再送を促す。
    successMessage.value = `登録成功 (連番 ${res.family.sequenceNo}, SKU ${res.products.length} 件`
      + (uploaded > 0 ? `, 画像 ${uploaded} 枚` : '')
      + ')'
    if (failed === 0) {
      await navigateTo(`/products/${res.family.id}`)
    }
    // failed > 0 のときは遷移せず、導線パネル (registeredFamilyId + pendingImages) で残り画像の再送を促す。
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '登録に失敗しました')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <div v-if="!canEditMaster" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      品番台帳管理権限がないため、商品の新規登録はできません。
      <div class="mt-2">
        <NuxtLink to="/products" class="text-blue-600 underline">商品一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-4">
        <div class="text-xs text-gray-500">
          <NuxtLink to="/products" class="hover:underline">商品一覧</NuxtLink>
          <span class="mx-1">/</span>
          <span>新規登録</span>
        </div>
        <h1 class="text-2xl font-bold">商品新規ウィザード</h1>
        <p class="mt-1 text-sm text-gray-500">
          P-01〜P-03 一括登録。企画情報 + 色×サイズ展開 + 仕入単価を 1 トランザクションで登録します。
        </p>
      </header>

      <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        マスタ情報を読み込み中…
      </div>

      <form v-else class="space-y-3" @submit.prevent="onSubmit">
        <!-- 参照コピー登録 (PR4、旧 spec No.3 参照コード/コピー、任意操作・折りたたみ可)。
             既存商品を品番/商品名/legacy_id で検索選択 → 「この商品をコピー」で詳細を取得し、
             下記フォームの編集可能項目をプリフィルする。明示ボタン操作時のみ上書き。
             保存は通常どおり POST /complete (新 family として新採番)。参照元は読取のみ。 -->
        <details class="rounded-lg border border-blue-200 bg-blue-50/40 p-4 shadow-sm">
          <summary class="cursor-pointer font-semibold text-blue-900">
            参照コピー登録 <span class="ml-2 text-xs font-normal text-blue-700">(任意 — 既存商品の内容をコピーして起票)</span>
          </summary>
          <p class="mt-3 text-xs text-gray-600">
            既存商品を品番 / 商品名 / 旧品番 (legacy_id) で検索して選択し、「この商品をコピー」を押すと
            下のフォームに内容がコピーされます。コピーされるのは編集可能な項目のみで、品番 (7 桁) / SKU /
            旧品番 / 画像 / 状態は引き継がれず、登録時に新しい品番が採番されます。
            コピー後は自由に編集できます。
          </p>
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-[1fr_auto] sm:items-end">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">コピー元の商品</span>
              <AutoComplete
                :model-value="referenceSourceId != null ? String(referenceSourceId) : ''"
                :options="referenceOptions"
                allow-empty
                empty-label="(未選択)"
                placeholder="品番 / 商品名 / 旧品番で検索…"
                @update:model-value="(v) => referenceSourceId = v === '' ? null : Number(v)"
              />
            </label>
            <button
              type="button"
              :disabled="referenceSourceId == null || copying"
              class="h-fit rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
              @click="onCopyFromReference"
            >
              {{ copying ? 'コピー中…' : 'この商品をコピー' }}
            </button>
          </div>
          <p v-if="copyNotice" class="mt-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
            {{ copyNotice }}
          </p>
        </details>

        <!-- Section 1: 企画コード構成 -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">① 企画コード構成 (11 桁品番の上位 9 桁)</h2>
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">年式 <span class="text-red-500">*</span></span>
              <AutoComplete :model-value="form.plannedYearCode" :options="yearCodes.map((y) => ({ value: y, label: y }))" :allow-empty="false" placeholder="年式を検索…" @update:model-value="(v) => form.plannedYearCode = v" />
              <span class="text-xs text-gray-500">11 桁品番の 1 桁目 (A-K, N, Z)</span>
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品タイプ <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.productTypeId" :items="productTypes" placeholder="商品タイプを検索…" @update:model-value="(v) => form.productTypeId = v ?? 0" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品季節 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.productSeasonId" :items="productSeasons" placeholder="商品季節を検索…" @update:model-value="(v) => form.productSeasonId = v ?? 0" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">工場 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.factorySupplierId" :items="suppliers" placeholder="工場を検索…" @update:model-value="(v) => form.factorySupplierId = v ?? 0" />
              <span class="text-xs text-gray-500">11 桁品番の 7 桁目 (工場コード)</span>
            </label>
          </div>
          <p class="mt-3 text-xs text-gray-500">連番 (4-6 桁目) はサーバ側で自動採番</p>
        </section>

        <!-- Section 2: 商品画像 (§2a 企画/本番の 2 区分。「登録」と同時に採番 familyId へ区分付きでアップロード) -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <h2 class="mb-2 border-b border-gray-100 pb-2 font-semibold">
            ② 商品画像 <span class="ml-2 text-xs font-normal text-gray-500">(任意 — JPEG / PNG / WebP、各 5MB、区分ごと最大 5 枚)</span>
          </h2>
          <p class="mb-3 text-xs text-gray-500">
            企画画像・本番画像を区分ごとに登録できます。「登録」と同時に上から順にアップロードされます。
            画像利用シーンの代表画像は、本番画像があれば本番の 1 枚目、無ければ企画の 1 枚目を表示します。
          </p>
          <div v-if="imageError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
            {{ imageError }}
          </div>

          <!-- 区分ごとのサブセクション (企画 / 本番)。§2a、原則8 レスポンシブ -->
          <div class="space-y-3">
            <div v-for="cat in IMAGE_CATEGORIES" :key="cat.key" class="rounded-md border border-gray-200 bg-gray-50 p-3">
              <div class="mb-2 flex items-center justify-between">
                <span class="text-sm font-medium text-gray-700">
                  {{ cat.label }} <span class="ml-1 text-xs font-normal text-gray-400">({{ slotsUsed(cat.key) }} / {{ IMAGE_MAX_COUNT }})</span>
                </span>
                <div v-if="slotsUsed(cat.key) < IMAGE_MAX_COUNT">
                  <input
                    :ref="(el) => setImageInput(cat.key, el as HTMLInputElement | null)"
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    multiple
                    class="hidden"
                    @change="(e) => onImageSelect(e, cat.key)"
                  />
                  <button
                    type="button"
                    :disabled="submitting || retryingImages"
                    class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50 disabled:opacity-50"
                    @click="imageInputs[cat.key]?.click()"
                  >+ {{ cat.label }}を選択</button>
                </div>
              </div>

              <div v-if="pendingByCat(cat.key).length === 0" class="py-4 text-center text-xs text-gray-400">
                {{ cat.label }}が選択されていません (任意)。
              </div>
              <!-- 選択済みプレビュー (レスポンシブ 2→3→5 列、原則 8)。#番号は区分内の並び。 -->
              <div v-else class="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-5">
                <div
                  v-for="(img, idx) in pendingByCat(cat.key)"
                  :key="img.previewUrl"
                  class="group relative overflow-hidden rounded-md border border-gray-200 bg-white"
                >
                  <img :src="img.previewUrl" :alt="img.file.name" class="aspect-square w-full object-cover" />
                  <div class="absolute right-1 top-1 rounded-full bg-black/70 px-2 py-0.5 text-xs text-white">#{{ idx + 1 }}</div>
                  <div class="border-t border-gray-100 bg-gray-50 px-2 py-1 text-xs">
                    <div class="truncate" :title="img.file.name">{{ img.file.name }}</div>
                    <div class="flex justify-between text-gray-500">
                      <span>{{ formatBytes(img.file.size) }}</span>
                      <button type="button" :disabled="submitting || retryingImages" class="text-red-600 hover:underline disabled:opacity-50" @click="removePendingImage(img)">削除</button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        <!-- Section 3: 商品属性 -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">③ 商品属性</h2>
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">ブランド <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.brandId" :items="brands" placeholder="ブランドを検索…" @update:model-value="(v) => form.brandId = v ?? 0" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">機能</span>
              <MasterSelect :model-value="form.functionId" :items="functions_" allow-empty empty-label="(なし)" placeholder="機能を検索…" @update:model-value="(v) => form.functionId = v" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品群 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.productGroupId" :items="productGroups" placeholder="商品群を検索…" @update:model-value="(v) => form.productGroupId = v ?? 0" />
            </label>
          </div>
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">甲皮素材 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.upperMaterialId" :items="materials" placeholder="甲皮素材を検索…" @update:model-value="(v) => form.upperMaterialId = v ?? 0" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">中底素材 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.insoleMaterialId" :items="materials" placeholder="中底素材を検索…" @update:model-value="(v) => form.insoleMaterialId = v ?? 0" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">底素材 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="form.outsoleMaterialId" :items="materials" placeholder="底素材を検索…" @update:model-value="(v) => form.outsoleMaterialId = v ?? 0" />
            </label>
          </div>
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品名 <span class="text-red-500">*</span></span>
              <input v-model="form.productName1" type="text" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
          </div>

          <!-- 旧 品番台帳 項目 (Phase A、全て任意) -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品年度</span>
              <input v-model.number="form.productYear" type="number" min="0" max="9999" step="1" placeholder="例: 2026 (9999=通年)" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">管理季節</span>
              <MasterSelect :model-value="form.managementSeasonId" :items="productSeasons" allow-empty empty-label="(なし)" placeholder="管理季節を検索…" @update:model-value="(v) => form.managementSeasonId = v" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">企画者</span>
              <MasterSelect :model-value="form.plannerUserId" :items="userOptions" allow-empty empty-label="(なし)" placeholder="企画者を検索…" @update:model-value="(v) => form.plannerUserId = v" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">仮番号</span>
              <input v-model="form.provisionalNumber" type="text" maxlength="64" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">サンプル合格日</span>
              <input v-model="form.sampleApprovalDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">版権対象</span>
              <AutoComplete :model-value="form.royaltyTarget != null ? String(form.royaltyTarget) : ''" :options="royaltyTargetOptions" allow-empty empty-label="(なし)" placeholder="版権対象を選択…" @update:model-value="(v) => form.royaltyTarget = v === '' ? null : Number(v)" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">版権料率 (%)</span>
              <input v-model.number="form.royaltyRate" type="number" min="0" step="0.01" placeholder="例: 5.00" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
          </div>

          <!-- 価格 (旧 品番台帳 項目、全て任意) -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">小売価格</span>
              <input v-model.number="form.retailPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">納品価格</span>
              <input v-model.number="form.deliveryPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">企画費</span>
              <input v-model.number="form.planningCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <!-- ブランド費 自動計算 (§2c)。版権対象で選んだ価格 × 版権料率。手入力不可 (読取専用)。 -->
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">ブランド費 <span class="text-xs font-normal text-gray-400">(自動計算)</span></span>
              <div class="rounded-md border border-gray-200 bg-gray-50 px-2.5 py-1.5 text-sm text-gray-700">
                {{ brandCostComputed != null ? `¥${brandCostComputed.toLocaleString()}` : '—' }}
              </div>
              <span class="text-xs text-gray-500">
                {{ form.royaltyTarget == null ? '版権対象を選択すると算出' : (form.royaltyTarget === 1 ? '小売価格' : '納品価格') + ' × 版権料率' }}
              </span>
            </label>
          </div>

          <!-- 備考 (旧 品番台帳 項目 追補 PR1、spec No.39、任意) -->
          <div class="mt-3">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">備考</span>
              <textarea v-model="form.remark" rows="2" maxlength="2000" placeholder="商品本体に関する備考 (任意)" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"></textarea>
            </label>
          </div>
        </section>

        <!-- Section 4: 色×サイズ展開 -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">
            ④ 色 × サイズ展開 <span class="ml-2 text-xs text-gray-500">(SKU {{ skuCount }} 件が生成されます)</span>
          </h2>
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2">
            <div>
              <div class="mb-2 text-sm font-medium">色 (複数選択)</div>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="c in colors"
                  :key="c.id"
                  type="button"
                  :class="expansion.colorIds.includes(c.id)
                    ? 'bg-blue-600 text-white border-blue-600'
                    : 'bg-white text-gray-700 border-gray-300 hover:bg-blue-50'"
                  class="rounded-md border px-3 py-1 text-sm"
                  @click="toggleColor(c.id)"
                >
                  {{ c.code }} {{ c.name }}
                </button>
              </div>
            </div>
            <div>
              <div class="mb-2 text-sm font-medium">サイズ (複数選択)</div>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="s in sizes"
                  :key="s.id"
                  type="button"
                  :class="expansion.sizeIds.includes(s.id)
                    ? 'bg-blue-600 text-white border-blue-600'
                    : 'bg-white text-gray-700 border-gray-300 hover:bg-blue-50'"
                  class="rounded-md border px-3 py-1 text-sm"
                  @click="toggleSize(s.id)"
                >
                  {{ s.code }} {{ s.name }}
                </button>
              </div>
            </div>
          </div>

          <!-- 備考（色）(旧 品番台帳 項目 追補 PR1、spec No.33、商品単位の単一テキスト、任意) -->
          <div class="mt-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">備考（色）</span>
              <textarea v-model="form.colorRemark" rows="2" maxlength="2000" placeholder="色に関する備考 (商品全体で 1 つ、任意)" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"></textarea>
            </label>
          </div>
        </section>

        <!-- Section 5: 仕入単価 (§2d/e 複数の仕入先×サイズ)。通貨は仕入先の適用通貨から自動決定し、
             為替マスタで円換算・仕入原価・仕入利益率・ドレー代を自動計算する (§2f/g/h/i)。 -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <div class="mb-3 flex flex-col gap-2 border-b border-gray-100 pb-2 sm:flex-row sm:items-center sm:justify-between">
            <h2 class="font-semibold">
              ⑤ 仕入単価 <span class="ml-2 text-xs font-normal text-gray-500">(仕入先 × サイズごとに登録。通貨・為替・仕入原価・仕入利益率・ドレー代は自動計算)</span>
            </h2>
            <button type="button" class="self-start rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="addPriceRow">+ 行を追加</button>
          </div>

          <!-- 各行: 入力 (仕入先/サイズ/単価/有効開始日/決定日) + 自動計算サマリ。モバイルは縦積み (原則8)。 -->
          <div class="space-y-3">
            <div
              v-for="(row, idx) in supplierPrices"
              :key="idx"
              class="rounded-md border border-gray-200 bg-gray-50 p-3"
            >
              <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-5 lg:items-end">
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">仕入先 <span class="text-red-500">*</span></span>
                  <MasterSelect :model-value="row.supplierId" :items="suppliers" placeholder="仕入先を検索…" @update:model-value="(v) => row.supplierId = v ?? 0" />
                </label>
                <!-- サイズ別仕入単価 (§2e)。未選択 = 全サイズ共通の既定単価、個別サイズ = そのサイズ専用単価。 -->
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">サイズ</span>
                  <MasterSelect :model-value="row.sizeId" :items="sizes" allow-empty empty-label="全サイズ共通" placeholder="サイズを検索…" @update:model-value="(v) => row.sizeId = v" />
                </label>
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">単価 <span class="text-red-500">*</span> <span class="text-xs font-normal text-gray-400">({{ currencyOfRow(row) }})</span></span>
                  <input v-model.number="row.unitPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
                </label>
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">有効開始日 <span class="text-red-500">*</span></span>
                  <input v-model="row.effectiveFrom" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
                </label>
                <div class="flex items-end gap-2">
                  <label class="flex flex-1 flex-col gap-1">
                    <span class="text-sm font-medium">単価決定日 <span class="text-red-500">*</span></span>
                    <input v-model="row.decidedAt" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
                  </label>
                  <button
                    type="button"
                    :disabled="supplierPrices.length <= 1"
                    class="h-fit rounded-md border border-red-300 bg-white px-2.5 py-1.5 text-sm text-red-600 hover:bg-red-50 disabled:opacity-30"
                    @click="removePriceRow(idx)"
                  >削除</button>
                </div>
              </div>

              <!-- 自動計算 (§2f/g/h/i): 適用通貨 / 適用レート / 円換算 / 仕入原価 / 仕入利益率 / ドレー代 -->
              <div class="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 rounded border border-gray-100 bg-white px-3 py-2 text-xs text-gray-600">
                <span>適用通貨: <span class="font-mono font-semibold text-gray-800">{{ currencyOfRow(row) }}</span></span>
                <span v-if="currencyOfRow(row) !== 'JPY'">
                  適用レート:
                  <span class="font-mono font-semibold" :class="rateOfRow(row) == null ? 'text-red-600' : 'text-gray-800'">{{ rateOfRow(row) != null ? rateOfRow(row) : '未登録' }}</span>
                </span>
                <span>円換算: <span class="font-mono font-semibold text-gray-800">{{ yen(yenUnitPriceOfRow(row)) }}</span></span>
                <span>仕入原価: <span class="font-mono font-semibold text-gray-800">{{ yen(purchaseCostOfRow(row)) }}</span></span>
                <span>仕入利益率: <span class="font-mono font-semibold text-gray-800">{{ purchaseMarginOfRow(row) != null ? `${purchaseMarginOfRow(row)}%` : '—' }}</span></span>
                <span>ドレー代: <span class="font-mono font-semibold text-gray-800">{{ yen(drayageOfRow(row)) }}</span></span>
              </div>
            </div>
          </div>

          <p class="mt-2 text-xs text-gray-500">
            通貨は仕入先マスタの「適用通貨」、ドレー代は仕入先マスタの「ドレー代」から自動反映します。為替レートは
            <NuxtLink to="/masters/exchange-rates" class="text-blue-600 hover:underline">為替マスタ</NuxtLink>
            で登録します。仕入原価 = 円換算仕入単価 + ブランド費、仕入利益率 = 仕入原価 ÷ 納品価格。
            見積単価・ロス費・税率など原価明細は登録後の詳細画面で追加できます。
          </p>
        </section>

        <!-- Section 6: アソート/セット明細 (PR3、旧 spec No.37/38、任意) -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">
              ⑥ アソート/セット明細 <span class="ml-2 text-xs font-normal text-gray-500">(任意 — アソート/セット品のみ)</span>
            </h2>
            <button
              type="button"
              class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50"
              @click="addSetComponent"
            >+ 行を追加</button>
          </div>
          <p class="mb-3 text-xs text-gray-500">
            この商品がアソート/セット品の場合、構成する子品番 (手入力) と数量を 1 行ずつ登録します。通常商品は空のままで構いません。
          </p>

          <div v-if="setComponents.length === 0" class="py-4 text-center text-sm text-gray-400">
            明細なし (通常商品)。アソート/セット品の場合は「+ 行を追加」してください。
          </div>

          <!-- PC: テーブル / モバイル: カード型 (原則8 レスポンシブ) -->
          <div v-else class="space-y-2">
            <div
              v-for="(c, idx) in setComponents"
              :key="idx"
              class="grid grid-cols-1 gap-2 rounded-md border border-gray-200 bg-gray-50 p-3 sm:grid-cols-[1fr_8rem_auto] sm:items-end sm:gap-3"
            >
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">子品番 <span class="text-red-500">*</span></span>
                <input
                  v-model="c.childItemNumber"
                  type="text"
                  maxlength="32"
                  placeholder="例: NA1001A (手入力)"
                  class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
                />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">数量 <span class="text-red-500">*</span></span>
                <input
                  v-model.number="c.quantity"
                  type="number"
                  min="1"
                  step="1"
                  class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
                />
              </label>
              <button
                type="button"
                class="h-fit rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
                @click="removeSetComponent(idx)"
              >削除</button>
            </div>
          </div>
        </section>

        <!-- 実行 -->
        <div v-if="errorMessage" class="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
          {{ errorMessage }}
        </div>
        <div v-if="successMessage" class="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
          {{ successMessage }}
        </div>

        <!-- 登録後の導線パネル。family 採番済のため再登録 (createComplete) はさせず (二重採番防止、原則 2)、
             残った画像のみ再アップロードさせる (原則 4 非ブロッキング)。残数は専用カウントを持たず
             pendingImages.length を直接参照する (別カウントだと「② 商品画像」での削除と乖離するため)。
             詳細画面リンクは画像が無くなっても常に出し、行き止まりを作らない。 -->
        <div
          v-if="registeredFamilyId != null"
          class="rounded border border-amber-300 bg-amber-50 px-3 py-3 text-sm text-amber-800"
        >
          <p>
            商品は登録されました (品番採番済)。画像 <strong>{{ imageUploadedCount }} 枚</strong>をアップロードしました。
            <template v-if="pendingImages.length > 0">
              残り <strong>{{ pendingImages.length }} 枚</strong>は未送信です (上の「② 商品画像」に残っています)。再アップロードするか、商品詳細画面から後で追加できます。
            </template>
          </p>
          <div class="mt-2 flex flex-wrap gap-2">
            <button
              v-if="pendingImages.length > 0"
              type="button"
              :disabled="retryingImages"
              class="rounded-md bg-amber-600 px-4 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-amber-700 disabled:opacity-50"
              @click="retryImageUpload"
            >
              {{ retryingImages ? '再アップロード中…' : `画像を再アップロード (${pendingImages.length} 枚)` }}
            </button>
            <NuxtLink
              :to="`/products/${registeredFamilyId}`"
              class="rounded-md border border-amber-400 bg-white px-4 py-1.5 text-sm font-semibold text-amber-800 no-underline hover:bg-amber-50"
            >
              商品詳細へ移動
            </NuxtLink>
          </div>
        </div>

        <div v-if="registeredFamilyId == null" class="flex justify-end gap-2">
          <NuxtLink
            to="/products"
            class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm hover:bg-gray-50"
          >
            キャンセル
          </NuxtLink>
          <button
            type="submit"
            :disabled="!canSubmit"
            class="rounded-md bg-blue-600 px-6 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {{ submitting ? '登録中…' : `登録 (SKU ${skuCount} 件 + 仕入単価 ${supplierPrices.length} 件${pendingImages.length ? ` + 画像 ${pendingImages.length} 枚` : ''})` }}
          </button>
        </div>
      </form>
    </template>
  </main>
</template>
