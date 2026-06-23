<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CompleteFamilyPayload } from '~/composables/useProducts'

const { canEditMaster } = useAuth()
const { list } = useMasters()
const { createComplete } = useProducts()

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
})

const expansion = ref({
  colorIds: [] as number[],
  sizeIds: [] as number[],
})

const supplierPrice = ref({
  supplierId: 0,
  unitPrice: 0,
  currencyCode: 'JPY',
  exchangeRate: null as number | null,
  effectiveFrom: new Date().toISOString().split('T')[0],
  decidedAt: new Date().toISOString().split('T')[0],
})

onMounted(async () => {
  try {
    const [pt, ps, sup, br, fn, pg, mt, co, sz] = await Promise.all([
      list('product-types'),
      list('product-seasons'),
      list('suppliers'),
      list('brands'),
      list('functions'),
      list('product-groups'),
      list('materials'),
      list('colors'),
      list('sizes'),
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
        },
      ],
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
  <main class="mx-auto max-w-4xl px-4 py-8">
    <div v-if="!canEditMaster" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      品番台帳管理権限がないため、商品の新規登録はできません。
      <div class="mt-2">
        <NuxtLink to="/products" class="text-blue-600 underline">商品一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-6">
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

      <form v-else class="space-y-6" @submit.prevent="onSubmit">
        <!-- Section 1: 企画コード構成 -->
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <h2 class="mb-4 border-b border-gray-100 pb-2 font-semibold">① 企画コード構成 (11 桁品番の上位 9 桁)</h2>
          <div class="grid grid-cols-2 gap-4">
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
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <h2 class="mb-4 border-b border-gray-100 pb-2 font-semibold">② 商品属性</h2>
          <div class="grid grid-cols-2 gap-4">
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
          <div class="mt-4 grid grid-cols-3 gap-4">
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
          <div class="mt-4 grid grid-cols-2 gap-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品名 1 <span class="text-red-500">*</span></span>
              <input v-model="form.productName1" type="text" maxlength="255" class="rounded-md border border-gray-300 px-3 py-2 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">商品名 2</span>
              <input v-model="form.productName2" type="text" maxlength="255" class="rounded-md border border-gray-300 px-3 py-2 text-sm" />
            </label>
          </div>
        </section>

        <!-- Section 3: 色×サイズ展開 -->
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <h2 class="mb-4 border-b border-gray-100 pb-2 font-semibold">
            ③ 色 × サイズ展開 <span class="ml-2 text-xs text-gray-500">(SKU {{ skuCount }} 件が生成されます)</span>
          </h2>
          <div class="grid grid-cols-2 gap-6">
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
        </section>

        <!-- Section 4: 仕入単価 (初回 1 件、追加は P-05 詳細画面で) -->
        <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
          <h2 class="mb-4 border-b border-gray-100 pb-2 font-semibold">④ 仕入単価 (初回)</h2>
          <div class="grid grid-cols-2 gap-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">仕入先 <span class="text-red-500">*</span></span>
              <MasterSelect :model-value="supplierPrice.supplierId" :items="suppliers" placeholder="仕入先を検索…" @update:model-value="(v) => supplierPrice.supplierId = v ?? 0" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">単価 <span class="text-red-500">*</span></span>
              <div class="flex gap-2">
                <input
                  v-model.number="supplierPrice.unitPrice"
                  type="number"
                  min="1"
                  step="0.01"
                  class="flex-1 rounded-md border border-gray-300 px-3 py-2 text-sm"
                />
                <div class="w-24">
                  <AutoComplete :model-value="supplierPrice.currencyCode" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" @update:model-value="(v) => supplierPrice.currencyCode = v" />
                </div>
              </div>
            </label>
            <label v-if="supplierPrice.currencyCode !== 'JPY'" class="flex flex-col gap-1">
              <span class="text-sm font-medium">為替レート <span class="text-xs text-gray-400">(外貨単価 → 円換算)</span></span>
              <input v-model.number="supplierPrice.exchangeRate" type="number" min="0" step="0.0001" placeholder="例: 21.5" class="rounded-md border border-gray-300 px-3 py-2 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">有効開始日 <span class="text-red-500">*</span></span>
              <input v-model="supplierPrice.effectiveFrom" type="date" class="rounded-md border border-gray-300 px-3 py-2 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">単価決定日 <span class="text-red-500">*</span></span>
              <input v-model="supplierPrice.decidedAt" type="date" class="rounded-md border border-gray-300 px-3 py-2 text-sm" />
            </label>
          </div>
          <p class="mt-2 text-xs text-gray-500">追加の仕入先単価は、登録後の詳細画面から追加できます (BR-04 履歴管理)</p>
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
