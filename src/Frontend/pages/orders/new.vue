<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CreateOrderPayload, CommunicationSuggestion } from '~/composables/useOrders'

const { user } = useAuth()
const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)
const { list } = useMasters()
const { create, communicationSuggestions, priceSuggestion } = useOrders()
const { apiFetch } = useApi()

// マスタ参照
const suppliers = ref<MasterItem[]>([])
const destinations = ref<MasterItem[]>([])
const departments = ref<MasterItem[]>([])
const warehouses = ref<MasterItem[]>([])

// SKU 候補 (products テーブル全件、簡素実装)
interface SkuOption {
  id: number
  sku: string
  productName: string
  colorName: string
  sizeName: string
}
const skus = ref<SkuOption[]>([])

// ユーザ候補 (発注担当者・管理者・副担当者用)
interface UserOption { id: number; loginId: string; displayName: string }
const users = ref<UserOption[]>([])

// 連絡文章テンプレ
const commTemplates = ref<CommunicationSuggestion[]>([])

const loading = ref(true)
const submitting = ref(false)
const errorMessage = ref('')

// フォーム
const today = new Date().toISOString().split('T')[0]
const form = ref({
  supplierId: 0,
  deliveryDestinationId: 0,
  departmentId: 0,
  warehouseId: 0,
  dueDate: today,
  ordererUserId: 0,
  managerUserId: 0,
  subOrderer1UserId: null as number | null,
  subOrderer2UserId: null as number | null,
  subOrderer3UserId: null as number | null,
  subOrderer4UserId: null as number | null,
  subOrderer5UserId: null as number | null,
  subOrderer6UserId: null as number | null,
  // 連絡文書 6 行 (構造化、PR6)。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」。各行はテンプレ
  // 選択式 (連絡文書サジェスト再利用) + 自由編集可。新フローは本 6 列を SoT として送る (communicationText
  // は新フロントから書かない)。固定長 6 の配列で各スロットを保持する。
  communicationLines: ['', '', '', '', '', ''] as string[],
  // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
  isOverseas: false,
  landingPlace: '',
  customerRef: '',
  factoryShippingDate: '',
  deliveryPlaceShippingDate: '',
  overseasDepartureDate: '',
  warehouse2Id: null as number | null,
  warehouse3Id: null as number | null,
})

// 分納×倉庫の多次元明細 (PR5b)。1 明細を「(倉庫 × 納期) の分納行」の集合で多次元化する。
// 倉庫 / 納期 は任意 (null 許容)。数量は正の整数、入数は任意。
interface DeliveryRow {
  warehouseId: number | null
  deliveryDate: string
  quantity: number
  packQuantity: number | null
}
interface LineRow {
  productId: number
  quantity: number
  unitPriceSnapshot: number
  currencyCodeSnapshot: string
  // 旧 発注明細 項目 (Phase B、任意)
  packQuantity: number | null
  estimateUnitPrice: number | null
  // 発注明細 備考 (spec 明細 No.26、任意)
  remark: string | null
  // 分納×倉庫の多次元明細 (PR5b、任意)。空 = 分納なし (単一明細、従来挙動)。
  deliveries: DeliveryRow[]
  // 分納サブセクションの開閉 (UI 状態のみ、payload には含めない)。
  showDeliveries: boolean
}
const lines = ref<LineRow[]>([
  { productId: 0, quantity: 1, unitPriceSnapshot: 0, currencyCodeSnapshot: 'JPY', packQuantity: null, estimateUnitPrice: null, remark: null, deliveries: [], showDeliveries: false },
])

// --- オートコンプリート選択肢（マスタ参照を部分一致検索可能に） ---
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))
const skuOptions = computed(() => skus.value.map((s) => ({ id: s.id, label: `${s.sku} - ${s.productName} (${s.colorName}/${s.sizeName})`, code: s.sku })))
// 副担当者 1〜6（DTO に存在するが従来 UI が無く未入力だった項目を補完）
const subKeys = ['subOrderer1UserId', 'subOrderer2UserId', 'subOrderer3UserId', 'subOrderer4UserId', 'subOrderer5UserId', 'subOrderer6UserId'] as const
const subOrdererValue = (n: number): number | null => form.value[subKeys[n - 1]]
const setSubOrderer = (n: number, v: number | null) => { form.value[subKeys[n - 1]] = v }

onMounted(async () => {
  try {
    const [sup, dest, dept, wh, comm, family, usrRes] = await Promise.all([
      list('suppliers'),
      list('delivery-destinations'),
      list('departments'),
      list('warehouses'),
      communicationSuggestions(),
      // 商品企画から全 SKU を引いてくる (簡素実装、Iter 4 で検索 + ページング)
      apiFetch<{ data: { id: number; productName1: string; skuVariationCount: number }[] }>('/products/families'),
      apiFetch<{ data: UserOption[] }>('/users'),
    ])
    suppliers.value = sup
    destinations.value = dest
    departments.value = dept
    warehouses.value = wh
    commTemplates.value = comm
    users.value = usrRes.data

    // 各 family の詳細を並列取得して SKU リスト構築
    const skuLists = await Promise.all(
      family.data.map((f) =>
        apiFetch<{ products: { id: number; sku: string; colorName: string; sizeName: string }[] }>(`/products/families/${f.id}`)
          .then((d) =>
            d.products.map((p) => ({
              id: p.id,
              sku: p.sku,
              productName: f.productName1,
              colorName: p.colorName,
              sizeName: p.sizeName,
            })),
          ),
      ),
    )
    skus.value = skuLists.flat()

    // 初期値
    if (sup.length) form.value.supplierId = sup[0].id
    if (dest.length) form.value.deliveryDestinationId = dest[0].id
    if (dept.length) form.value.departmentId = dept[0].id
    if (wh.length) form.value.warehouseId = wh[0].id
    if (usrRes.data.length) {
      form.value.ordererUserId = usrRes.data[0].id
      form.value.managerUserId = usrRes.data[0].id
    }
    if (skus.value.length) lines.value[0].productId = skus.value[0].id
  } catch (e) {
    errorMessage.value = 'マスタ情報の取得に失敗しました'
  } finally {
    loading.value = false
  }
})

const addLine = () => {
  lines.value.push({
    productId: skus.value[0]?.id ?? 0,
    quantity: 1,
    unitPriceSnapshot: 0,
    currencyCodeSnapshot: 'JPY',
    packQuantity: null,
    estimateUnitPrice: null,
    remark: null,
    deliveries: [],
    showDeliveries: false,
  })
  // 追加直後の明細にも現単価を補完する (reviewer M-1)。既定選択された SKU に対し size-aware に
  // サジェスト (force=true)。supplier 未選択や現単価なしなら applyPriceSuggestion 内で no-op。
  applyPriceSuggestion(lines.value.length - 1, true)
}

const removeLine = (idx: number) => {
  if (lines.value.length <= 1) return
  lines.value.splice(idx, 1)
}

// 分納×倉庫の多次元明細 (PR5b)。行追加/削除は SETC (アソート明細) と同じパターン。
const addDelivery = (lineIdx: number) => {
  const l = lines.value[lineIdx]
  l.showDeliveries = true
  l.deliveries.push({ warehouseId: form.value.warehouseId || null, deliveryDate: '', quantity: 1, packQuantity: null })
}
const removeDelivery = (lineIdx: number, dIdx: number) => {
  lines.value[lineIdx].deliveries.splice(dIdx, 1)
}
// 分納が 1 件以上ある明細の数量は分納数量の合計 (SUM) で表示する (単一数量入力は分納なし時のみ)。
// サーバ側も line.Quantity = SUM に再計算するため、UI 表示とサーバ保存が一致する。
const lineQuantity = (l: LineRow): number =>
  l.deliveries.length > 0 ? l.deliveries.reduce((s, d) => s + (Number(d.quantity) || 0), 0) : Number(l.quantity) || 0

// サイズ別仕入単価 (PR2)。SKU の size に対応する現単価を発注先からサジェストし、明細の単価/通貨を
// 自動補完する (入力補助)。フォールバック: (family, supplier, SKUのsize) → 無ければ (…, 全サイズ既定)。
// 非ブロッキング (CLAUDE.md 原則4): 取得失敗・現単価なしの場合は既存値を保持し、ユーザは手入力できる。
// サーバ側で snapshot を上書きしないため、ここで補完した値も保存前にユーザが自由に編集可能。
//
// 非破壊サジェスト (reviewer 指摘 Minor 対応): 単価が未入力相当 (<=0、「単価未決定」の既定値) の明細
// にのみ補完する。ユーザが手入力済の単価は SKU/発注先を変えても上書きしない。force=true (SKU 明示選択時)
// は新しい SKU の現単価を反映するため上書きを許す。
const applyPriceSuggestion = async (idx: number, force = false) => {
  const line = lines.value[idx]
  if (!line || line.productId <= 0 || form.value.supplierId <= 0) return
  // 発注先変更時 (force=false) は手入力済 (>0) の単価を保護する。
  if (!force && line.unitPriceSnapshot > 0) return
  try {
    const sug = await priceSuggestion(line.productId, form.value.supplierId)
    if (sug.found && sug.unitPrice != null) {
      line.unitPriceSnapshot = sug.unitPrice
      if (sug.currencyCode) line.currencyCodeSnapshot = sug.currencyCode
    }
  } catch (e) {
    // 単価サジェストの失敗は主要フロー (発注作成) を止めない。手入力で続行可能。
    console.error('単価サジェスト取得に失敗しました (手入力で続行可能)', e)
  }
}

// SKU 選択変更時に単価を再サジェスト (明示選択なので force=true で新 SKU の現単価を反映)。
const onLineProductChange = (idx: number, v: number | null) => {
  lines.value[idx].productId = v ?? 0
  applyPriceSuggestion(idx, true)
}

// 発注先 (ヘッダ) 変更時は全明細の単価を再サジェスト (supplier 由来の現単価が変わるため)。
// 手入力済 (>0) の明細は保護する (force=false)。
watch(() => form.value.supplierId, () => {
  lines.value.forEach((_, idx) => applyPriceSuggestion(idx))
})

// 小計 = 数量 × 単価。数量は分納合計 (lineQuantity) を使うため、分納展開しても合計は整合する。
const lineSubtotal = (l: LineRow) => lineQuantity(l) * l.unitPriceSnapshot
const totalAmount = computed(() => lines.value.reduce((sum, l) => sum + lineSubtotal(l), 0))

// 連絡文書 6 行 (PR6): テンプレ選択肢を AutoComplete 用に変換 (value=本文、label=出典ラベル)。
// 同一本文の重複は value 重複になるため出典ラベルを付して一意化する。
const commTemplateOptions = computed(() =>
  commTemplates.value.map((t, i) => ({ value: `${i}`, label: t.sourceLabel, searchText: t.body })),
)
// テンプレを指定スロットに適用する。AutoComplete は index 文字列を value にするため body を引き直す。
const applyTemplateToLine = (slot: number, optionValue: string) => {
  const idx = Number(optionValue)
  const tpl = commTemplates.value[idx]
  if (tpl && slot >= 0 && slot < form.value.communicationLines.length) {
    form.value.communicationLines[slot] = tpl.body
  }
}

// 明細の数量妥当性: 分納なしは単一 quantity > 0、分納ありは全分納 quantity > 0 かつ合計 > 0。
const lineQuantityValid = (l: LineRow): boolean =>
  l.deliveries.length > 0
    ? l.deliveries.every((d) => (Number(d.quantity) || 0) > 0) && lineQuantity(l) > 0
    : (Number(l.quantity) || 0) > 0

const canSubmit = computed(() =>
  form.value.supplierId > 0 &&
  form.value.deliveryDestinationId > 0 &&
  form.value.departmentId > 0 &&
  form.value.warehouseId > 0 &&
  form.value.ordererUserId > 0 &&
  form.value.managerUserId > 0 &&
  lines.value.every((l) => l.productId > 0 && lineQuantityValid(l) && l.unitPriceSnapshot >= 0) &&
  !submitting.value)

const onSubmit = async () => {
  errorMessage.value = ''
  if (!canSubmit.value) {
    errorMessage.value = '必須項目を入力してください (仕入先 / 納品先 / 担当者 / 明細)'
    return
  }
  submitting.value = true
  try {
    const payload: CreateOrderPayload = {
      supplierId: form.value.supplierId,
      deliveryDestinationId: form.value.deliveryDestinationId,
      departmentId: form.value.departmentId,
      warehouseId: form.value.warehouseId,
      dueDate: form.value.dueDate,
      ordererUserId: form.value.ordererUserId,
      managerUserId: form.value.managerUserId,
      subOrderer1UserId: form.value.subOrderer1UserId,
      subOrderer2UserId: form.value.subOrderer2UserId,
      subOrderer3UserId: form.value.subOrderer3UserId,
      subOrderer4UserId: form.value.subOrderer4UserId,
      subOrderer5UserId: form.value.subOrderer5UserId,
      subOrderer6UserId: form.value.subOrderer6UserId,
      // 連絡文書 6 行 (構造化、PR6)。新フローは本 6 列を SoT として送る (空欄は null)。
      // communicationText は新規作成では書かない (旧データ専用フォールバック列のため null)。
      communicationText: null,
      communicationLine1: form.value.communicationLines[0].trim() || null,
      communicationLine2: form.value.communicationLines[1].trim() || null,
      communicationLine3: form.value.communicationLines[2].trim() || null,
      communicationLine4: form.value.communicationLines[3].trim() || null,
      communicationLine5: form.value.communicationLines[4].trim() || null,
      communicationLine6: form.value.communicationLines[5].trim() || null,
      lines: lines.value.map((l) => ({
        productId: l.productId,
        // 分納あり明細は quantity = 分納合計 (lineQuantity)。サーバも SUM に再計算するが、整合のため
        // 合計値を送る。分納なしは単一 quantity をそのまま送る (従来挙動)。
        quantity: lineQuantity(l),
        unitPriceSnapshot: Number(l.unitPriceSnapshot),
        currencyCodeSnapshot: l.currencyCodeSnapshot,
        packQuantity: l.packQuantity != null ? Number(l.packQuantity) : null,
        estimateUnitPrice: l.estimateUnitPrice != null ? Number(l.estimateUnitPrice) : null,
        // 発注明細 備考 (spec 明細 No.26)。空欄は null で送る。
        remark: l.remark?.trim() || null,
        // 分納×倉庫の多次元明細 (PR5b)。空 = null (分納なし、従来挙動)。1 件以上あれば配列で送る。
        deliveries: l.deliveries.length > 0
          ? l.deliveries.map((d) => ({
              warehouseId: d.warehouseId,
              deliveryDate: d.deliveryDate || null,
              quantity: Number(d.quantity) || 0,
              packQuantity: d.packQuantity != null ? Number(d.packQuantity) : null,
            }))
          : null,
      })),
      // 旧 発注書 国内/海外 項目 (Phase B)。海外区分が false のときは海外専用項目は送らない (null/空)。
      isOverseas: form.value.isOverseas,
      landingPlace: form.value.isOverseas ? (form.value.landingPlace.trim() || null) : null,
      customerRef: form.value.isOverseas ? (form.value.customerRef.trim() || null) : null,
      factoryShippingDate: form.value.isOverseas ? (form.value.factoryShippingDate || null) : null,
      deliveryPlaceShippingDate: form.value.isOverseas ? (form.value.deliveryPlaceShippingDate || null) : null,
      overseasDepartureDate: form.value.isOverseas ? (form.value.overseasDepartureDate || null) : null,
      warehouse2Id: form.value.isOverseas ? form.value.warehouse2Id : null,
      warehouse3Id: form.value.isOverseas ? form.value.warehouse3Id : null,
    }
    const res = await create(payload)
    await navigateTo(`/orders/${res.id}`)
  } catch (e) {
    const err = e as { data?: { detail?: string } }
    errorMessage.value = err.data?.detail ?? '発注書作成に失敗しました'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-5">
    <div v-if="!canCreateOrder" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      発注書作成権限がありません。
      <div class="mt-2">
        <NuxtLink to="/orders" class="text-blue-600 underline">発注書一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-4">
        <div class="text-xs text-gray-500">
          <NuxtLink to="/orders" class="hover:underline">発注書</NuxtLink>
          <span class="mx-1">/</span>
          <span>新規作成</span>
        </div>
        <h1 class="text-2xl font-bold">新規発注書 (O-01)</h1>
        <p class="mt-1 text-sm text-gray-500">
          管理番号は保存時に自動採番、発注番号は初回 Excel 出力時に採番されます (BR-03)。
        </p>
      </header>

      <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        マスタ情報を読み込み中…
      </div>

      <form v-else class="space-y-4" @submit.prevent="onSubmit">
        <!-- ① 発注区分: 先頭の必須選択 (is_overseas、国内/海外 で以降のフォームが変化) -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 class="font-semibold">① 発注区分 <span class="text-red-500">*</span></h2>
              <p class="mt-0.5 text-xs text-gray-500">
                国内 / 海外 を選択してください。海外を選ぶと海外発注情報の入力欄が表示されます。
              </p>
            </div>
            <!-- 発注区分 国内/海外 セグメントトグル (is_overseas、二択モードスイッチ) -->
            <div class="inline-flex self-start overflow-hidden rounded-md border border-gray-300 text-sm">
              <button
                type="button"
                class="px-6 py-1.5 font-medium transition-colors"
                :class="!form.isOverseas ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'"
                @click="form.isOverseas = false"
              >国内</button>
              <button
                type="button"
                class="border-l border-gray-300 px-6 py-1.5 font-medium transition-colors"
                :class="form.isOverseas ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'"
                @click="form.isOverseas = true"
              >海外</button>
            </div>
          </div>
        </section>

        <!-- ② 発注書ヘッダ -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div class="mb-3 flex flex-col gap-3 border-b border-gray-100 pb-3 sm:flex-row sm:items-center sm:justify-between">
            <h2 class="font-semibold">② 発注書ヘッダ</h2>
          </div>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="font-medium">仕入先 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.supplierId" :items="suppliers" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納品先 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.deliveryDestinationId" :items="destinations" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">事業部 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.departmentId" :items="departments" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.warehouseId" :items="warehouses" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入日 <span class="text-red-500">*</span></span>
              <input v-model="form.dueDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注担当 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.ordererUserId" :items="userOptions" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注管理者 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.managerUserId" :items="userOptions" />
            </label>
            <label v-for="n in 6" :key="n" class="flex flex-col gap-1">
              <span class="font-medium">副担当者{{ n }}</span>
              <MasterSelect
                :model-value="subOrdererValue(n)"
                :items="userOptions"
                allow-empty
                empty-label="（なし）"
                placeholder="（任意）"
                @update:model-value="(v) => setSubOrderer(n, v)"
              />
            </label>
          </div>
        </section>

        <!-- ③ 海外発注情報 (is_overseas=true のときのみ表示、Phase B) -->
        <section v-if="form.isOverseas" class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">③ 海外発注情報</h2>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="font-medium">荷揚地</span>
              <input v-model="form.landingPlace" type="text" maxlength="128" placeholder="Port of entry" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">得意先</span>
              <input v-model="form.customerRef" type="text" maxlength="128" placeholder="得意先 / 受注先" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">工場出荷日</span>
              <input v-model="form.factoryShippingDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納品所出荷日</span>
              <input v-model="form.deliveryPlaceShippingDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">海外出港日</span>
              <input v-model="form.overseasDepartureDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫2</span>
              <MasterSelect
                v-model="form.warehouse2Id"
                :items="warehouses"
                allow-empty
                empty-label="（なし）"
                placeholder="（任意）"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫3</span>
              <MasterSelect
                v-model="form.warehouse3Id"
                :items="warehouses"
                allow-empty
                empty-label="（なし）"
                placeholder="（任意）"
              />
            </label>
          </div>
        </section>

        <!-- 明細 -->
        <section class="overflow-x-auto rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">④ 明細 ({{ lines.length }} 件、合計 {{ totalAmount.toLocaleString() }} 円)</h2>
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="addLine">+ 明細追加</button>
          </div>
          <table class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50">
              <tr>
                <th class="px-2 py-1.5 text-left">SKU</th>
                <th class="px-2 py-1.5 text-right">数量</th>
                <th class="px-2 py-1.5 text-right">入数</th>
                <th class="px-2 py-1.5 text-right">単価</th>
                <th class="px-2 py-1.5 text-right">見積単価</th>
                <th class="px-2 py-1.5 text-left">通貨</th>
                <th class="px-2 py-1.5 text-left">備考</th>
                <th class="px-2 py-1.5 text-right">小計</th>
                <th class="px-2 py-1.5 text-right"></th>
              </tr>
            </thead>
            <tbody>
              <template v-for="(l, idx) in lines" :key="idx">
              <tr class="border-b border-gray-100" :class="l.showDeliveries || l.deliveries.length > 0 ? '' : 'last:border-0'">
                <td class="px-2 py-1.5">
                  <!-- サイズ別仕入単価 (PR2): SKU 選択時に単価をサジェスト補完 (onLineProductChange)。 -->
                  <MasterSelect :model-value="l.productId" :items="skuOptions" placeholder="SKU・品名で検索…" @update:model-value="(v) => onLineProductChange(idx, v)" />
                </td>
                <td class="px-2 py-1.5 text-right">
                  <!-- 分納あり時は数量を合計の読取表示 (単一数量入力は分納なし時のみ、PR5b)。 -->
                  <input v-if="l.deliveries.length === 0" v-model.number="l.quantity" type="number" min="1" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                  <span v-else class="inline-block w-20 px-2 py-1 text-right font-mono text-gray-700" title="分納数量の合計">{{ lineQuantity(l).toLocaleString() }}</span>
                </td>
                <td class="px-2 py-1.5 text-right">
                  <input v-model.number="l.packQuantity" type="number" min="0" placeholder="—" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-1.5 text-right">
                  <input v-model.number="l.unitPriceSnapshot" type="number" min="0" step="0.01" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-1.5 text-right">
                  <input v-model.number="l.estimateUnitPrice" type="number" min="0" step="0.01" placeholder="—" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-1.5">
                  <div class="w-20">
                    <AutoComplete :model-value="l.currencyCodeSnapshot" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" @update:model-value="(v) => l.currencyCodeSnapshot = v" />
                  </div>
                </td>
                <td class="px-2 py-1.5">
                  <input v-model="l.remark" type="text" maxlength="255" placeholder="—" class="w-32 rounded-md border border-gray-300 px-2 py-1" />
                </td>
                <td class="px-2 py-1.5 text-right font-mono">{{ lineSubtotal(l).toLocaleString() }}</td>
                <td class="px-2 py-1.5 text-right">
                  <div class="flex items-center justify-end gap-2">
                    <!-- 分納 / 倉庫別 サブセクションの開閉トグル (PR5b)。分納あり件数をバッジ表示。 -->
                    <button type="button" class="text-xs text-blue-600 hover:underline" @click="l.showDeliveries = !l.showDeliveries">
                      分納{{ l.deliveries.length > 0 ? ` (${l.deliveries.length})` : '' }}
                    </button>
                    <button type="button" :disabled="lines.length <= 1" class="text-xs text-red-600 hover:underline disabled:opacity-30" @click="removeLine(idx)">削除</button>
                  </div>
                </td>
              </tr>
              <!-- 分納 / 倉庫別 サブセクション (PR5b、任意・折りたたみ可)。倉庫 + 納期 + 数量 + 入数 を行追加/削除。 -->
              <tr v-if="l.showDeliveries" class="border-b border-gray-100 last:border-0 bg-gray-50">
                <td colspan="9" class="px-3 py-2">
                  <div class="mb-2 flex items-center justify-between">
                    <span class="text-xs font-semibold text-gray-600">分納 / 倉庫別 (任意 — 倉庫×納期で多次元化。空なら単一明細)</span>
                    <button type="button" class="rounded-md border border-gray-300 bg-white px-2 py-0.5 text-xs hover:bg-gray-100" @click="addDelivery(idx)">+ 分納行を追加</button>
                  </div>
                  <div v-if="l.deliveries.length === 0" class="py-2 text-center text-xs text-gray-400">
                    分納なし (単一明細)。倉庫別・複数納期で分けたい場合は「+ 分納行を追加」してください。
                  </div>
                  <div v-else class="space-y-2">
                    <div v-for="(d, dIdx) in l.deliveries" :key="dIdx" class="grid grid-cols-1 gap-2 rounded-md border border-gray-200 bg-white p-2 sm:grid-cols-[1fr_10rem_7rem_7rem_auto] sm:items-end">
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">倉庫</span>
                        <MasterSelect v-model="d.warehouseId" :items="warehouses" allow-empty empty-label="（未指定）" placeholder="（任意）" />
                      </label>
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">納期</span>
                        <input v-model="d.deliveryDate" type="date" class="rounded-md border border-gray-300 px-2 py-1 text-sm" />
                      </label>
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">数量 <span class="text-red-500">*</span></span>
                        <input v-model.number="d.quantity" type="number" min="1" class="rounded-md border border-gray-300 px-2 py-1 text-right text-sm" />
                      </label>
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">入数</span>
                        <input v-model.number="d.packQuantity" type="number" min="0" placeholder="—" class="rounded-md border border-gray-300 px-2 py-1 text-right text-sm" />
                      </label>
                      <button type="button" class="h-fit rounded-md border border-red-300 bg-white px-2 py-1 text-xs text-red-600 hover:bg-red-50" @click="removeDelivery(idx, dIdx)">削除</button>
                    </div>
                    <div class="text-right text-xs text-gray-500">分納合計数量: <span class="font-mono font-semibold">{{ lineQuantity(l).toLocaleString() }}</span></div>
                  </div>
                </td>
              </tr>
              </template>
            </tbody>
          </table>
        </section>

        <!-- 連絡文書 6 行 (構造化、PR6。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」) -->
        <section class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div class="mb-3 flex flex-col gap-1 border-b border-gray-100 pb-2 sm:flex-row sm:items-center sm:justify-between">
            <h2 class="font-semibold">⑤ 連絡文書 (O-07、6 行)</h2>
            <p class="text-xs text-gray-500">各行はテンプレ選択 + 自由編集できます (空行は出力されません)。</p>
          </div>
          <!-- 6 行スロット。各行: テンプレ選択 (AutoComplete) + テキスト入力。モバイルは縦積み (原則8)。 -->
          <div class="space-y-2">
            <div
              v-for="(_, i) in form.communicationLines"
              :key="i"
              class="flex flex-col gap-2 sm:flex-row sm:items-center"
            >
              <span class="w-12 shrink-0 text-xs font-medium text-gray-500">{{ (i + 1).toString().padStart(2, '0') }} 行</span>
              <input
                v-model="form.communicationLines[i]"
                type="text"
                :placeholder="`連絡文書 ${(i + 1).toString().padStart(2, '0')} 行目...`"
                class="flex-1 rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
              >
              <div v-if="commTemplates.length > 0" class="sm:w-64">
                <AutoComplete
                  :model-value="''"
                  :options="commTemplateOptions"
                  placeholder="テンプレから選択…"
                  empty-label="（選択しない）"
                  @update:model-value="(v) => applyTemplateToLine(i, v)"
                />
              </div>
            </div>
          </div>
        </section>

        <div v-if="errorMessage" class="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
          {{ errorMessage }}
        </div>

        <div class="flex justify-end gap-2">
          <NuxtLink to="/orders" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm hover:bg-gray-50">キャンセル</NuxtLink>
          <button type="submit" :disabled="!canSubmit" class="rounded-md bg-blue-600 px-6 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50">
            {{ submitting ? '保存中…' : `登録 (合計 ${totalAmount.toLocaleString()} 円)` }}
          </button>
        </div>
      </form>
    </template>
  </main>
</template>
