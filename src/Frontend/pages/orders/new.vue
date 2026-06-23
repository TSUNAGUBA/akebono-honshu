<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CreateOrderPayload, CommunicationSuggestion } from '~/composables/useOrders'

const { user } = useAuth()
const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)
const { list } = useMasters()
const { create, communicationSuggestions } = useOrders()
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
  communicationText: '',
})

interface LineRow {
  productId: number
  quantity: number
  unitPriceSnapshot: number
  currencyCodeSnapshot: string
}
const lines = ref<LineRow[]>([
  { productId: 0, quantity: 1, unitPriceSnapshot: 0, currencyCodeSnapshot: 'JPY' },
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
  })
}

const removeLine = (idx: number) => {
  if (lines.value.length <= 1) return
  lines.value.splice(idx, 1)
}

const lineSubtotal = (l: LineRow) => l.quantity * l.unitPriceSnapshot
const totalAmount = computed(() => lines.value.reduce((sum, l) => sum + lineSubtotal(l), 0))

const applyTemplate = (tpl: CommunicationSuggestion) => {
  form.value.communicationText = (form.value.communicationText ? form.value.communicationText + '\n' : '') + tpl.body
}

const canSubmit = computed(() =>
  form.value.supplierId > 0 &&
  form.value.deliveryDestinationId > 0 &&
  form.value.departmentId > 0 &&
  form.value.warehouseId > 0 &&
  form.value.ordererUserId > 0 &&
  form.value.managerUserId > 0 &&
  lines.value.every((l) => l.productId > 0 && l.quantity > 0 && l.unitPriceSnapshot >= 0) &&
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
      communicationText: form.value.communicationText.trim() || null,
      lines: lines.value.map((l) => ({
        productId: l.productId,
        quantity: Number(l.quantity),
        unitPriceSnapshot: Number(l.unitPriceSnapshot),
        currencyCodeSnapshot: l.currencyCodeSnapshot,
      })),
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
  <main class="mx-auto max-w-5xl px-4 py-8">
    <div v-if="!canCreateOrder" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      発注書作成権限がありません。
      <div class="mt-2">
        <NuxtLink to="/orders" class="text-blue-600 underline">発注書一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-6">
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

      <form v-else class="space-y-6" @submit.prevent="onSubmit">
        <!-- 発注書ヘッダ -->
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <h2 class="mb-4 border-b border-gray-100 pb-2 font-semibold">発注書ヘッダ</h2>
          <div class="grid grid-cols-2 gap-4 text-sm">
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
              <input v-model="form.dueDate" type="date" class="rounded-md border border-gray-300 px-3 py-2" />
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

        <!-- 明細 -->
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <div class="mb-4 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">明細 ({{ lines.length }} 件、合計 {{ totalAmount.toLocaleString() }} 円)</h2>
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="addLine">+ 明細追加</button>
          </div>
          <table class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50">
              <tr>
                <th class="px-2 py-2 text-left">SKU</th>
                <th class="px-2 py-2 text-right">数量</th>
                <th class="px-2 py-2 text-right">単価</th>
                <th class="px-2 py-2 text-left">通貨</th>
                <th class="px-2 py-2 text-right">小計</th>
                <th class="px-2 py-2 text-right"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, idx) in lines" :key="idx" class="border-b border-gray-100 last:border-0">
                <td class="px-2 py-2">
                  <MasterSelect v-model="l.productId" :items="skuOptions" placeholder="SKU・品名で検索…" />
                </td>
                <td class="px-2 py-2 text-right">
                  <input v-model.number="l.quantity" type="number" min="1" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-2 text-right">
                  <input v-model.number="l.unitPriceSnapshot" type="number" min="0" step="0.01" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-2">
                  <div class="w-20">
                    <AutoComplete :model-value="l.currencyCodeSnapshot" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" @update:model-value="(v) => l.currencyCodeSnapshot = v" />
                  </div>
                </td>
                <td class="px-2 py-2 text-right font-mono">{{ lineSubtotal(l).toLocaleString() }}</td>
                <td class="px-2 py-2 text-right">
                  <button type="button" :disabled="lines.length <= 1" class="text-xs text-red-600 hover:underline disabled:opacity-30" @click="removeLine(idx)">削除</button>
                </td>
              </tr>
            </tbody>
          </table>
        </section>

        <!-- 連絡文章 (O-07) -->
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">連絡文章 (O-07)</h2>
            <div v-if="commTemplates.length > 0" class="text-xs text-gray-500">
              テンプレ:
              <button
                v-for="(t, i) in commTemplates"
                :key="i"
                type="button"
                class="ml-1 rounded border border-gray-300 px-2 py-0.5 hover:bg-blue-50"
                @click="applyTemplate(t)"
              >
                {{ t.sourceLabel }}
              </button>
            </div>
          </div>
          <textarea v-model="form.communicationText" rows="4" placeholder="発注先への連絡事項..." class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
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
