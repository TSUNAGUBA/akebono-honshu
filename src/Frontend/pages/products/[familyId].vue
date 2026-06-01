<script setup lang="ts">
import type { FamilyDetail, ImageSummary } from '~/composables/useProducts'
import { productStatusLabel } from '~/composables/useProducts'
import type { MasterItem } from '~/composables/useMasters'

const route = useRoute()
const familyId = computed(() => Number(route.params.familyId))
const { canEditMaster } = useAuth()
const {
  getFamily, updateFamily, deleteFamily,
  addSupplierPrice, uploadImage, deleteImage,
} = useProducts()
const { list } = useMasters()

const detail = ref<FamilyDetail | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const successMessage = ref('')

// 編集用フォーム
const editing = ref(false)
const editForm = ref({
  brandId: 0,
  functionId: null as number | null,
  productGroupId: 0,
  upperMaterialId: 0,
  insoleMaterialId: 0,
  outsoleMaterialId: 0,
  productName1: '',
  productName2: '',
  status: 1,
})

// 単価追加フォーム
const showPriceForm = ref(false)
const priceForm = ref({
  supplierId: 0,
  unitPrice: 0,
  currencyCode: 'JPY',
  effectiveFrom: new Date().toISOString().split('T')[0],
  decidedAt: new Date().toISOString().split('T')[0],
})

// マスタ参照
const brands = ref<MasterItem[]>([])
const functions_ = ref<MasterItem[]>([])
const productGroups = ref<MasterItem[]>([])
const materials = ref<MasterItem[]>([])
const suppliers = ref<MasterItem[]>([])

// 画像アップロード
const fileInput = ref<HTMLInputElement | null>(null)
const uploading = ref(false)

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    detail.value = await getFamily(familyId.value)
    if (detail.value) {
      editForm.value = {
        brandId: detail.value.family.brandId,
        functionId: detail.value.family.functionId,
        productGroupId: detail.value.family.productGroupId,
        upperMaterialId: detail.value.family.upperMaterialId,
        insoleMaterialId: detail.value.family.insoleMaterialId,
        outsoleMaterialId: detail.value.family.outsoleMaterialId,
        productName1: detail.value.family.productName1,
        productName2: detail.value.family.productName2 ?? '',
        status: detail.value.family.status,
      }
    }
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 404
      ? '商品企画が見つかりません'
      : '商品詳細の取得に失敗しました'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await Promise.all([
    reload(),
    (async () => {
      const [br, fn, pg, mt, sup] = await Promise.all([
        list('brands'), list('functions'), list('product-groups'),
        list('materials'), list('suppliers'),
      ])
      brands.value = br
      functions_.value = fn
      productGroups.value = pg
      materials.value = mt
      suppliers.value = sup
      if (sup.length) priceForm.value.supplierId = sup[0].id
    })(),
  ])
})

const onStartEdit = () => {
  editing.value = true
  successMessage.value = ''
  errorMessage.value = ''
}

const onCancelEdit = () => {
  editing.value = false
  reload()
}

const onSaveEdit = async () => {
  if (!detail.value) return
  errorMessage.value = ''
  try {
    await updateFamily(familyId.value, {
      brandId: editForm.value.brandId,
      functionId: editForm.value.functionId,
      productGroupId: editForm.value.productGroupId,
      upperMaterialId: editForm.value.upperMaterialId,
      insoleMaterialId: editForm.value.insoleMaterialId,
      outsoleMaterialId: editForm.value.outsoleMaterialId,
      productName1: editForm.value.productName1.trim(),
      productName2: editForm.value.productName2.trim() || null,
      status: Number(editForm.value.status),
    })
    successMessage.value = '更新しました'
    editing.value = false
    await reload()
  } catch (e) {
    errorMessage.value = '更新に失敗しました'
  }
}

const onDelete = async () => {
  if (!detail.value) return
  if (!confirm(`${detail.value.family.productName1} を論理削除しますか？\n配下の SKU も連動で削除されます (復元は SQL で別途対応)`)) return
  try {
    await deleteFamily(familyId.value)
    await navigateTo('/products')
  } catch {
    errorMessage.value = '削除に失敗しました'
  }
}

const onAddPrice = async () => {
  if (priceForm.value.unitPrice <= 0) {
    errorMessage.value = '単価は 0 より大きい値を指定してください'
    return
  }
  try {
    await addSupplierPrice(familyId.value, {
      supplierId: priceForm.value.supplierId,
      unitPrice: Number(priceForm.value.unitPrice),
      currencyCode: priceForm.value.currencyCode,
      exchangeRate: null,
      effectiveFrom: priceForm.value.effectiveFrom,
      decidedAt: priceForm.value.decidedAt,
    })
    successMessage.value = '単価を追加しました (旧単価の有効終了日も自動更新済)'
    showPriceForm.value = false
    await reload()
  } catch (e) {
    const err = e as { data?: { detail?: string } }
    errorMessage.value = err.data?.detail ?? '単価追加に失敗しました'
  }
}

const onFileSelect = async (event: Event) => {
  const target = event.target as HTMLInputElement
  if (!target.files?.length) return
  uploading.value = true
  errorMessage.value = ''
  try {
    for (const file of Array.from(target.files)) {
      await uploadImage(familyId.value, file)
    }
    successMessage.value = '画像をアップロードしました'
    await reload()
  } catch (e) {
    const err = e as { data?: { detail?: string } }
    errorMessage.value = err.data?.detail ?? '画像アップロードに失敗しました'
  } finally {
    uploading.value = false
    if (target) target.value = ''
  }
}

const onDeleteImage = async (img: ImageSummary) => {
  if (!confirm(`画像 (orderNo=${img.orderNo}) を削除しますか？`)) return
  try {
    await deleteImage(familyId.value, img.id)
    await reload()
  } catch {
    errorMessage.value = '画像削除に失敗しました'
  }
}

const formatBytes = (b: number) => `${(b / 1024).toFixed(1)} KB`
</script>

<template>
  <main class="mx-auto max-w-6xl px-4 py-8">
    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
      読み込み中…
    </div>

    <div v-else-if="!detail" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      {{ errorMessage || '商品が見つかりません' }}
      <div class="mt-2">
        <NuxtLink to="/products" class="text-blue-600 underline">商品一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-6 flex items-start justify-between gap-4">
        <div>
          <div class="text-xs text-gray-500">
            <NuxtLink to="/products" class="hover:underline">商品管理</NuxtLink>
            <span class="mx-1">/</span>
            <span class="font-mono">品番 {{ detail.family.itemNumber }}</span>
            <span class="mx-1 text-gray-400">·</span>
            <span class="font-mono">他品番 {{ detail.family.itemFamilyNumber }}</span>
          </div>
          <h1 class="text-2xl font-bold">{{ detail.family.productName1 }}</h1>
          <p v-if="detail.family.productName2" class="text-sm text-gray-500">{{ detail.family.productName2 }}</p>
        </div>
        <div class="flex gap-2">
          <button
            v-if="canEditMaster && !editing"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50"
            @click="onStartEdit"
          >編集</button>
          <button
            v-if="canEditMaster && !editing"
            type="button"
            class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
            @click="onDelete"
          >削除</button>
        </div>
      </header>

      <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
        {{ errorMessage }}
      </div>
      <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
        {{ successMessage }}
      </div>

      <!-- 企画情報 -->
      <section class="mb-6 rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">企画情報</h2>

        <div v-if="!editing" class="grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
          <div><span class="text-gray-500">年式:</span> <strong>{{ detail.family.plannedYearCode }}</strong></div>
          <div><span class="text-gray-500">連番:</span> <strong class="font-mono">{{ detail.family.sequenceNo }}</strong></div>
          <div><span class="text-gray-500">商品タイプ:</span> {{ detail.family.productTypeName }}</div>
          <div><span class="text-gray-500">商品季節:</span> {{ detail.family.productSeasonName }}</div>
          <div><span class="text-gray-500">工場:</span> {{ detail.family.factorySupplierName }}</div>
          <div><span class="text-gray-500">状態:</span> {{ productStatusLabel(detail.family.status) }}</div>
          <div><span class="text-gray-500">ブランド:</span> {{ detail.family.brandName }}</div>
          <div><span class="text-gray-500">機能:</span> {{ detail.family.functionName ?? '—' }}</div>
          <div><span class="text-gray-500">商品群:</span> {{ detail.family.productGroupName }}</div>
          <div><span class="text-gray-500">甲皮素材:</span> {{ detail.family.upperMaterialName }}</div>
          <div><span class="text-gray-500">中底素材:</span> {{ detail.family.insoleMaterialName }}</div>
          <div><span class="text-gray-500">底素材:</span> {{ detail.family.outsoleMaterialName }}</div>
        </div>

        <form v-else class="grid grid-cols-2 gap-4 text-sm" @submit.prevent="onSaveEdit">
          <label class="flex flex-col gap-1">
            <span class="font-medium">ブランド</span>
            <select v-model.number="editForm.brandId" class="rounded-md border border-gray-300 px-3 py-2">
              <option v-for="b in brands" :key="b.id" :value="b.id">{{ b.code }} - {{ b.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">機能</span>
            <select v-model.number="editForm.functionId" class="rounded-md border border-gray-300 px-3 py-2">
              <option :value="null">(なし)</option>
              <option v-for="f in functions_" :key="f.id" :value="f.id">{{ f.code }} - {{ f.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">商品群</span>
            <select v-model.number="editForm.productGroupId" class="rounded-md border border-gray-300 px-3 py-2">
              <option v-for="g in productGroups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">状態</span>
            <select v-model.number="editForm.status" class="rounded-md border border-gray-300 px-3 py-2">
              <option :value="0">Draft</option>
              <option :value="1">Active</option>
              <option :value="2">Discontinued</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">甲皮素材</span>
            <select v-model.number="editForm.upperMaterialId" class="rounded-md border border-gray-300 px-3 py-2">
              <option v-for="m in materials" :key="m.id" :value="m.id">{{ m.code }} - {{ m.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">中底素材</span>
            <select v-model.number="editForm.insoleMaterialId" class="rounded-md border border-gray-300 px-3 py-2">
              <option v-for="m in materials" :key="m.id" :value="m.id">{{ m.code }} - {{ m.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">底素材</span>
            <select v-model.number="editForm.outsoleMaterialId" class="rounded-md border border-gray-300 px-3 py-2">
              <option v-for="m in materials" :key="m.id" :value="m.id">{{ m.code }} - {{ m.name }}</option>
            </select>
          </label>
          <label class="col-span-2 flex flex-col gap-1">
            <span class="font-medium">商品名 1</span>
            <input v-model="editForm.productName1" type="text" maxlength="255" class="rounded-md border border-gray-300 px-3 py-2" />
          </label>
          <label class="col-span-2 flex flex-col gap-1">
            <span class="font-medium">商品名 2</span>
            <input v-model="editForm.productName2" type="text" maxlength="255" class="rounded-md border border-gray-300 px-3 py-2" />
          </label>
          <div class="col-span-2 flex justify-end gap-2 pt-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 hover:bg-gray-50" @click="onCancelEdit">キャンセル</button>
            <button type="submit" class="rounded-md bg-blue-600 px-4 py-1.5 text-white hover:bg-blue-700">保存</button>
          </div>
        </form>
      </section>

      <!-- SKU 一覧 -->
      <section class="mb-6 rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">SKU 一覧 ({{ detail.products.length }} 件)</h2>
        <table class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-3 py-2 text-left">SKU (11 桁)</th>
              <th class="px-3 py-2 text-left">色</th>
              <th class="px-3 py-2 text-left">サイズ</th>
              <th class="px-3 py-2 text-left">状態</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in detail.products" :key="p.id" class="border-b border-gray-100 last:border-0"
                :class="{ 'text-gray-400 bg-gray-50': p.isDeleted }">
              <td class="px-3 py-2 font-mono">{{ p.sku }}</td>
              <td class="px-3 py-2">{{ p.colorCode }} {{ p.colorName }}</td>
              <td class="px-3 py-2">{{ p.sizeCode }} {{ p.sizeName }}</td>
              <td class="px-3 py-2">{{ p.isDeleted ? '削除済' : '有効' }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- 仕入単価 -->
      <section class="mb-6 rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
          <h2 class="font-semibold">現在有効な仕入単価 ({{ detail.currentSupplierPrices.length }} 件)</h2>
          <button
            v-if="canEditMaster"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50"
            @click="showPriceForm = !showPriceForm"
          >
            {{ showPriceForm ? '閉じる' : '+ 新単価追加' }}
          </button>
        </div>

        <form v-if="showPriceForm" class="mb-4 grid grid-cols-4 gap-3 rounded-md bg-gray-50 p-4 text-sm" @submit.prevent="onAddPrice">
          <label class="flex flex-col gap-1">
            <span class="font-medium">仕入先</span>
            <select v-model.number="priceForm.supplierId" class="rounded-md border border-gray-300 px-2 py-1.5">
              <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.code }} - {{ s.name }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">単価</span>
            <div class="flex gap-1">
              <input v-model.number="priceForm.unitPrice" type="number" step="0.01" min="0.01"
                     class="flex-1 rounded-md border border-gray-300 px-2 py-1.5" />
              <select v-model="priceForm.currencyCode" class="w-20 rounded-md border border-gray-300 px-1 py-1.5">
                <option value="JPY">JPY</option>
                <option value="USD">USD</option>
                <option value="CNY">CNY</option>
              </select>
            </div>
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">有効開始</span>
            <input v-model="priceForm.effectiveFrom" type="date" class="rounded-md border border-gray-300 px-2 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">決定日</span>
            <input v-model="priceForm.decidedAt" type="date" class="rounded-md border border-gray-300 px-2 py-1.5" />
          </label>
          <div class="col-span-4 flex justify-end">
            <button type="submit" class="rounded-md bg-blue-600 px-4 py-1.5 text-white hover:bg-blue-700">追加</button>
          </div>
        </form>

        <table class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-3 py-2 text-left">仕入先</th>
              <th class="px-3 py-2 text-right">単価</th>
              <th class="px-3 py-2 text-left">有効開始</th>
              <th class="px-3 py-2 text-left">決定日</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in detail.currentSupplierPrices" :key="p.id" class="border-b border-gray-100 last:border-0">
              <td class="px-3 py-2">{{ p.supplierCode }} {{ p.supplierName }}</td>
              <td class="px-3 py-2 text-right font-mono">{{ p.currencyCode }} {{ p.unitPrice.toLocaleString() }}</td>
              <td class="px-3 py-2">{{ p.effectiveFrom }}</td>
              <td class="px-3 py-2">{{ p.decidedAt }}</td>
            </tr>
            <tr v-if="detail.currentSupplierPrices.length === 0">
              <td colspan="4" class="px-3 py-4 text-center text-gray-500">単価が登録されていません</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- 画像管理 -->
      <section class="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
          <h2 class="font-semibold">画像 ({{ detail.images.length }} / 5 枚)</h2>
          <div v-if="canEditMaster && detail.images.length < 5">
            <input
              ref="fileInput"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              multiple
              class="hidden"
              @change="onFileSelect"
            />
            <button
              type="button"
              :disabled="uploading"
              class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50 disabled:opacity-50"
              @click="fileInput?.click()"
            >
              {{ uploading ? 'アップロード中…' : '+ 画像追加' }}
            </button>
          </div>
        </div>

        <div v-if="detail.images.length === 0" class="py-8 text-center text-sm text-gray-500">
          画像が登録されていません (JPEG / PNG / WebP、最大 5MB、5 枚まで)
        </div>

        <div v-else class="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-5">
          <div
            v-for="img in detail.images"
            :key="img.id"
            class="group relative overflow-hidden rounded-md border border-gray-200"
          >
            <img v-if="img.url" :src="img.url" :alt="img.originalFilename ?? `image-${img.orderNo}`"
                 class="aspect-square w-full object-cover" />
            <div v-else
                 class="flex aspect-square w-full items-center justify-center bg-gray-100 text-xs text-gray-400">
              画像読込失敗
            </div>
            <div class="absolute right-1 top-1 rounded-full bg-black/70 px-2 py-0.5 text-xs text-white">
              #{{ img.orderNo }}
            </div>
            <div class="border-t border-gray-100 bg-gray-50 px-2 py-1 text-xs">
              <div class="truncate" :title="img.originalFilename ?? ''">
                {{ img.originalFilename ?? '—' }}
              </div>
              <div class="flex justify-between text-gray-500">
                <span>{{ formatBytes(img.fileSizeBytes) }}</span>
                <button
                  v-if="canEditMaster"
                  type="button"
                  class="text-red-600 hover:underline"
                  @click="onDeleteImage(img)"
                >削除</button>
              </div>
            </div>
          </div>
        </div>
      </section>

      <p class="mt-3 text-xs text-gray-400">
        API: GET/PATCH/DELETE /api/v1/products/families/{{ familyId }} + 画像 + 単価
      </p>
    </template>
  </main>
</template>
