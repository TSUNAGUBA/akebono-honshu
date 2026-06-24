<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CompleteFamilyPayload, FamilyListItem, FamilyDetail } from '~/composables/useProducts'

const { canEditMaster } = useAuth()
const { list } = useMasters()
// 参照コピー (PR4): 既存商品の検索 (listFamilies) と詳細取得 (getFamily) を再利用する。
// 商品一覧 P-04 (index.vue) と同じくサーバ側検索 API を増やさず、全件取得 + クライアント側
// 部分一致でコピー元を選ぶ。詳細はバルク登録 (createComplete) と同じ既存 endpoint。
const { createComplete, listFamilies, getFamily } = useProducts()
const { apiFetch } = useApi()

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
  productName2: '',
  // 旧 品番台帳 項目 (Phase A、全て任意)
  productYear: null as number | null,
  managementSeasonId: null as number | null,
  plannerUserId: null as number | null,
  provisionalNumber: '',
  sampleApprovalDate: '',
  retailPrice: null as number | null,
  deliveryPrice: null as number | null,
  planningCost: null as number | null,
  brandCost: null as number | null,
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

// アソート/セット明細 (PR3、旧 spec No.37/38)。子品番 (手入力テキスト) + 数量の行コレクション。
// 任意 (空でも可)。空 = 通常商品。色サイズ/単価明細と同じ「行追加/削除」パターン。
const setComponents = ref<{ childItemNumber: string; quantity: number }[]>([])
const addSetComponent = () => {
  setComponents.value.push({ childItemNumber: '', quantity: 1 })
}
const removeSetComponent = (idx: number) => {
  setComponents.value.splice(idx, 1)
}

const supplierPrice = ref({
  supplierId: 0,
  // サイズ別仕入単価 (PR2)。null = 全サイズ共通の既定単価 (未選択)。
  sizeId: null as number | null,
  unitPrice: 0,
  currencyCode: 'JPY',
  exchangeRate: null as number | null,
  effectiveFrom: new Date().toISOString().split('T')[0],
  decidedAt: new Date().toISOString().split('T')[0],
  // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
  estimateUnitPrice: null as number | null,
  estimateReceivedDate: '',
  estimateCost: null as number | null,
  estimateMarginRate: null as number | null,
  purchaseCost: null as number | null,
  purchaseMarginRate: null as number | null,
  lossCost: null as number | null,
  drayageCost: null as number | null,
  taxRate: null as number | null,
})

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

// サイズ別/複数の仕入単価コピー用 (PR2)。新規フォームの単価 UI は 1 行 (supplierPrice) のみだが、
// コピー元が複数の有効単価を持つ場合、先頭を編集可能な supplierPrice に載せ、残りは
// ここに保持して保存時に payload.supplierPrices へ追加する (読取専用で表示・行削除可)。
type CopiedExtraPrice = CompleteFamilyPayload['supplierPrices'][number] & { supplierName: string; sizeName: string | null }
const copiedExtraSupplierPrices = ref<CopiedExtraPrice[]>([])
const removeCopiedExtraPrice = (idx: number) => {
  copiedExtraSupplierPrices.value.splice(idx, 1)
}

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
    form.value.productName2 = f.productName2 ?? ''
    form.value.productYear = f.productYear
    form.value.managementSeasonId = f.managementSeasonId
    form.value.plannerUserId = f.plannerUserId
    form.value.provisionalNumber = f.provisionalNumber ?? ''
    form.value.sampleApprovalDate = f.sampleApprovalDate ?? ''
    form.value.retailPrice = f.retailPrice
    form.value.deliveryPrice = f.deliveryPrice
    form.value.planningCost = f.planningCost
    form.value.brandCost = f.brandCost
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

    // (3) 仕入単価明細をコピー (PR2 サイズ別含む)。先頭を編集可能な supplierPrice に載せ、
    //     残り (サイズ別/複数) は copiedExtraSupplierPrices に保持して保存時に追加する。
    const prices = src.currentSupplierPrices
    copiedExtraSupplierPrices.value = []
    if (prices.length > 0) {
      const head = prices[0]
      supplierPrice.value.supplierId = head.supplierId
      supplierPrice.value.sizeId = head.sizeId
      supplierPrice.value.unitPrice = head.unitPrice
      supplierPrice.value.currencyCode = head.currencyCode
      supplierPrice.value.exchangeRate = head.exchangeRate
      supplierPrice.value.effectiveFrom = head.effectiveFrom
      supplierPrice.value.decidedAt = head.decidedAt
      supplierPrice.value.estimateUnitPrice = head.estimateUnitPrice
      supplierPrice.value.estimateReceivedDate = head.estimateReceivedDate ?? ''
      supplierPrice.value.estimateCost = head.estimateCost
      supplierPrice.value.estimateMarginRate = head.estimateMarginRate
      supplierPrice.value.purchaseCost = head.purchaseCost
      supplierPrice.value.purchaseMarginRate = head.purchaseMarginRate
      supplierPrice.value.lossCost = head.lossCost
      supplierPrice.value.drayageCost = head.drayageCost
      supplierPrice.value.taxRate = head.taxRate
      // 2 件目以降は payload 追加用に保持 (UI は読取専用で件数・行削除のみ)。
      copiedExtraSupplierPrices.value = prices.slice(1).map((p) => ({
        supplierId: p.supplierId,
        unitPrice: p.unitPrice,
        currencyCode: p.currencyCode,
        exchangeRate: p.exchangeRate,
        effectiveFrom: p.effectiveFrom,
        decidedAt: p.decidedAt,
        estimateUnitPrice: p.estimateUnitPrice,
        estimateReceivedDate: p.estimateReceivedDate,
        estimateCost: p.estimateCost,
        estimateMarginRate: p.estimateMarginRate,
        purchaseCost: p.purchaseCost,
        purchaseMarginRate: p.purchaseMarginRate,
        lossCost: p.lossCost,
        drayageCost: p.drayageCost,
        taxRate: p.taxRate,
        sizeId: p.sizeId,
        supplierName: `${p.supplierCode} ${p.supplierName}`,
        sizeName: p.sizeName,
      }))
    }

    // (4) アソート/セット明細をコピー (PR3)。子品番 + 数量。lineNo 昇順は API 側で保証済。
    setComponents.value = (src.setComponents ?? []).map((c) => ({
      childItemNumber: c.childItemNumber,
      quantity: c.quantity,
    }))

    const extra = copiedExtraSupplierPrices.value.length
    copyNotice.value = `品番 ${f.itemNumber}「${f.productName1}」の内容をコピーしました`
      + ` (色 ${expansion.value.colorIds.length} / サイズ ${expansion.value.sizeIds.length}`
      + ` / 単価 ${prices.length} 件${extra > 0 ? ` (うち追加 ${extra} 件)` : ''}`
      + ` / アソート ${setComponents.value.length} 行)。`
      + ' このまま編集して登録すると新しい品番が採番されます。'
  } catch (e) {
    const err = e as { data?: { detail?: string }; statusMessage?: string }
    errorMessage.value = err.data?.detail ?? err.statusMessage ?? 'コピー元の取得に失敗しました'
  } finally {
    copying.value = false
  }
}

onMounted(async () => {
  // 参照コピー元一覧は補助処理。本体マスタ読込 (フォーム表示の前提) とは独立に発火させ、
  // 失敗してもフォームは使えるようにする (原則 4 非ブロッキング、await しない)。
  loadReferenceSources()
  try {
    const [pt, ps, sup, br, fn, pg, mt, co, sz, usrRes] = await Promise.all([
      list('product-types'),
      list('product-seasons'),
      list('suppliers'),
      list('brands'),
      list('functions'),
      list('product-groups'),
      list('materials'),
      list('colors'),
      list('sizes'),
      apiFetch<{ data: UserOption[] }>('/users'),
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
    users.value = usrRes.data

    // 初期値設定
    if (pt.length) form.value.productTypeId = pt[0].id
    if (ps.length) form.value.productSeasonId = ps[0].id
    if (sup.length) {
      form.value.factorySupplierId = sup[0].id
      supplierPrice.value.supplierId = sup[0].id
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

const canSubmit = computed(() =>
  form.value.productName1.trim() !== '' &&
  expansion.value.colorIds.length > 0 &&
  expansion.value.sizeIds.length > 0 &&
  supplierPrice.value.unitPrice > 0 &&
  !submitting.value)

const onSubmit = async () => {
  errorMessage.value = ''
  successMessage.value = ''
  if (!canSubmit.value) {
    errorMessage.value = '必須項目を入力してください (商品名 / 色 / サイズ / 単価)'
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
        productName2: form.value.productName2.trim() || null,
        // 旧 品番台帳 項目 (Phase A、未入力は null)
        productYear: form.value.productYear,
        managementSeasonId: form.value.managementSeasonId,
        plannerUserId: form.value.plannerUserId,
        provisionalNumber: form.value.provisionalNumber.trim() || null,
        sampleApprovalDate: form.value.sampleApprovalDate || null,
        retailPrice: form.value.retailPrice,
        deliveryPrice: form.value.deliveryPrice,
        planningCost: form.value.planningCost,
        brandCost: form.value.brandCost,
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
      supplierPrices: [
        {
          supplierId: supplierPrice.value.supplierId,
          unitPrice: Number(supplierPrice.value.unitPrice),
          currencyCode: supplierPrice.value.currencyCode,
          exchangeRate: supplierPrice.value.currencyCode === 'JPY' ? null : supplierPrice.value.exchangeRate,
          effectiveFrom: supplierPrice.value.effectiveFrom,
          decidedAt: supplierPrice.value.decidedAt,
          // 旧 仕入コスト計算明細 項目 (Phase C、未入力は null)
          estimateUnitPrice: supplierPrice.value.estimateUnitPrice,
          estimateReceivedDate: supplierPrice.value.estimateReceivedDate || null,
          estimateCost: supplierPrice.value.estimateCost,
          estimateMarginRate: supplierPrice.value.estimateMarginRate,
          purchaseCost: supplierPrice.value.purchaseCost,
          purchaseMarginRate: supplierPrice.value.purchaseMarginRate,
          lossCost: supplierPrice.value.lossCost,
          drayageCost: supplierPrice.value.drayageCost,
          taxRate: supplierPrice.value.taxRate,
          // サイズ別仕入単価 (PR2、未選択は null = 全サイズ共通の既定単価)
          sizeId: supplierPrice.value.sizeId,
        },
        // 参照コピー (PR4) で 2 件目以降の有効単価をコピーした場合に追加する。
        // 表示専用フィールド (supplierName/sizeName) は payload に送らないため除外する。
        ...copiedExtraSupplierPrices.value.map(({ supplierName: _s, sizeName: _z, ...p }) => p),
      ],
      // アソート/セット明細 (PR3)。子品番が空の行は除外 (未入力行を送らない)。
      // 子品番は trim、数量は Number 化 (空入力時の NaN は 0 に丸めてサーバの SETC-002 で弾く)。
      // 空配列 = 明細なし (通常商品) として送る。
      setComponents: setComponents.value
        .filter((c) => c.childItemNumber.trim() !== '')
        .map((c) => ({ childItemNumber: c.childItemNumber.trim(), quantity: Number(c.quantity) || 0 })),
    }
    const res = await createComplete(payload)
    successMessage.value = `登録成功 (連番 ${res.family.sequenceNo}, SKU ${res.products.length} 件)`
    await navigateTo(`/products/${res.family.id}`)
  } catch (e) {
    const err = e as { data?: { detail?: string }; statusMessage?: string }
    errorMessage.value = err.data?.detail ?? err.statusMessage ?? '登録に失敗しました'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-5">
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

      <form v-else class="space-y-4" @submit.prevent="onSubmit">
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
          <div class="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-[1fr_auto] sm:items-end">
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

          <!-- コピー元が複数の有効単価を持つ場合の 2 件目以降 (サイズ別含む、PR2)。
               先頭は下の「④ 仕入単価 (初回)」に載り編集可能。ここは追加登録される分の確認用 (読取専用)。 -->
          <div v-if="copiedExtraSupplierPrices.length > 0" class="mt-3 rounded-md border border-blue-200 bg-white p-3">
            <div class="mb-2 text-sm font-medium text-blue-900">
              追加コピーされる仕入単価 ({{ copiedExtraSupplierPrices.length }} 件)
            </div>
            <p class="mb-2 text-xs text-gray-500">
              先頭の 1 件は「④ 仕入単価 (初回)」で編集できます。以下は登録時にそのまま追加されます (不要なら削除)。
            </p>
            <ul class="space-y-1">
              <li
                v-for="(p, idx) in copiedExtraSupplierPrices"
                :key="idx"
                class="flex items-center justify-between gap-2 rounded border border-gray-100 bg-gray-50 px-2 py-1 text-sm"
              >
                <span class="font-mono">
                  {{ p.supplierName }} ·
                  {{ p.sizeName ?? '全サイズ共通' }} ·
                  {{ p.currencyCode }} {{ p.unitPrice.toLocaleString() }}
                  <span class="text-gray-500">({{ p.effectiveFrom }}〜)</span>
                </span>
                <button
                  type="button"
                  class="rounded border border-red-300 bg-white px-2 py-0.5 text-xs text-red-600 hover:bg-red-50"
                  @click="removeCopiedExtraPrice(idx)"
                >削除</button>
              </li>
            </ul>
          </div>
        </details>

        <!-- Section 1: 企画コード構成 -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">① 企画コード構成 (11 桁品番の上位 9 桁)</h2>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-4">
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

        <!-- Section 2: 商品属性 -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">② 商品属性</h2>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
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
          <div class="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
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
          <div class="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品名 1 <span class="text-red-500">*</span></span>
              <input v-model="form.productName1" type="text" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品名 2</span>
              <input v-model="form.productName2" type="text" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
          </div>

          <!-- 旧 品番台帳 項目 (Phase A、全て任意) -->
          <div class="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-4">
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
          <div class="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-4">
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
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">ブランド費</span>
              <input v-model.number="form.brandCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
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

        <!-- Section 3: 色×サイズ展開 -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">
            ③ 色 × サイズ展開 <span class="ml-2 text-xs text-gray-500">(SKU {{ skuCount }} 件が生成されます)</span>
          </h2>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2">
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

        <!-- Section 4: 仕入単価 (初回 1 件、追加は P-05 詳細画面で) -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">④ 仕入単価 (初回)</h2>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">仕入先 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="supplierPrice.supplierId" :items="suppliers" placeholder="仕入先を検索…" @update:model-value="(v) => supplierPrice.supplierId = v ?? 0" />
            </label>
            <!-- サイズ別仕入単価 (PR2)。未選択 = 全サイズ共通の既定単価、個別サイズ = そのサイズ専用単価。 -->
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">サイズ</span>
              <MasterSelect :model-value="supplierPrice.sizeId" :items="sizes" allow-empty empty-label="全サイズ共通" placeholder="サイズを検索…" @update:model-value="(v) => supplierPrice.sizeId = v" />
              <span class="text-xs text-gray-500">未選択 = 全サイズ共通の既定単価</span>
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">単価 <span class="text-red-500">*</span></span>
              <div class="flex gap-2">
                <input
                  v-model.number="supplierPrice.unitPrice"
                  type="number"
                  min="1"
                  step="0.01"
                  class="flex-1 rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
                />
                <div class="w-24">
                  <AutoComplete :model-value="supplierPrice.currencyCode" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" @update:model-value="(v) => supplierPrice.currencyCode = v" />
                </div>
              </div>
            </label>
            <label v-if="supplierPrice.currencyCode !== 'JPY'" class="flex flex-col gap-1">
              <span class="text-sm font-medium">為替レート <span class="text-xs text-gray-400">(外貨単価 → 円換算)</span></span>
              <input v-model.number="supplierPrice.exchangeRate" type="number" min="0" step="0.0001" placeholder="例: 21.5" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">有効開始日 <span class="text-red-500">*</span></span>
              <input v-model="supplierPrice.effectiveFrom" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">単価決定日 <span class="text-red-500">*</span></span>
              <input v-model="supplierPrice.decidedAt" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
          </div>

          <!-- 旧 仕入コスト計算明細 項目 (Phase C、全て任意)。clutter 回避のため折りたたみ。 -->
          <details class="mt-3 rounded-md border border-gray-200 bg-gray-50 p-3">
            <summary class="cursor-pointer text-sm font-medium text-gray-700">原価明細 (任意)</summary>
            <div class="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">見積単価</span>
                <input v-model.number="supplierPrice.estimateUnitPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">見積単価受領日</span>
                <input v-model="supplierPrice.estimateReceivedDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">見積原価</span>
                <input v-model.number="supplierPrice.estimateCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">見積利益率 (%)</span>
                <input v-model.number="supplierPrice.estimateMarginRate" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">仕入原価</span>
                <input v-model.number="supplierPrice.purchaseCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">仕入利益率 (%)</span>
                <input v-model.number="supplierPrice.purchaseMarginRate" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">ロス費</span>
                <input v-model.number="supplierPrice.lossCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">ドレー代</span>
                <input v-model.number="supplierPrice.drayageCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">税率 (%)</span>
                <input v-model.number="supplierPrice.taxRate" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              </label>
            </div>
          </details>

          <p class="mt-2 text-xs text-gray-500">追加の仕入先単価は、登録後の詳細画面から追加できます (BR-04 履歴管理)</p>
        </section>

        <!-- Section 5: アソート/セット明細 (PR3、旧 spec No.37/38、任意) -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">
              ⑤ アソート/セット明細 <span class="ml-2 text-xs font-normal text-gray-500">(任意 — アソート/セット品のみ)</span>
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
        <div class="flex justify-end gap-2">
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
            {{ submitting ? '登録中…' : `登録 (SKU ${skuCount} 件 + 単価 1 件)` }}
          </button>
        </div>
      </form>
    </template>
  </main>
</template>
