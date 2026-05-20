<script setup lang="ts">
import { productStatusLabel } from '~/composables/useProducts'
import type { FamilyListItem } from '~/composables/useProducts'

const { listFamilies, imageUrl } = useProducts()
const { canEditMaster } = useAuth()

const items = ref<FamilyListItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const includeDeleted = ref(false)
const search = ref('')
const view = ref<'table' | 'card'>('table')

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    items.value = await listFamilies(includeDeleted.value)
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '商品企画一覧の取得に失敗しました'
  } finally {
    loading.value = false
  }
}

watch(includeDeleted, reload)
onMounted(reload)

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return items.value
  return items.value.filter(
    (i) =>
      i.productName1.toLowerCase().includes(q) ||
      (i.productName2 ?? '').toLowerCase().includes(q) ||
      i.sku9Digit.toLowerCase().includes(q) ||
      i.brandName.toLowerCase().includes(q),
  )
})

const statusBadge = (status: number): { label: string; cls: string } => {
  switch (status) {
    case 0: return { label: 'Draft', cls: 'bg-gray-100 text-gray-600' }
    case 1: return { label: 'Active', cls: 'bg-green-100 text-green-700' }
    case 2: return { label: '販売終了', cls: 'bg-orange-100 text-orange-700' }
    default: return { label: '?', cls: 'bg-gray-100 text-gray-500' }
  }
}

const formatPriceRange = (min: number | null, max: number | null, currency: string): string => {
  if (min === null || max === null) return '—'
  if (min === max) return `${currency} ${min.toLocaleString()}`
  return `${currency} ${min.toLocaleString()} 〜 ${max.toLocaleString()}`
}
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <header class="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold">商品管理</h1>
        <p class="mt-1 text-sm text-gray-500">P-04 商品企画一覧 / P-01〜P-03 新規ウィザード起動</p>
      </div>
      <NuxtLink
        v-if="canEditMaster"
        to="/products/new"
        class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white shadow-sm hover:bg-blue-700"
      >
        + 新規商品ウィザード
      </NuxtLink>
      <span v-else class="text-xs text-gray-400">参照のみ (品番台帳管理権限なし)</span>
    </header>

    <div class="mb-3 flex items-center gap-4">
      <input
        v-model="search"
        type="search"
        placeholder="商品名 / 上位コード / ブランドで検索"
        class="w-72 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      />
      <label class="inline-flex items-center gap-2 text-sm text-gray-600">
        <input v-model="includeDeleted" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
        削除済みを含む
      </label>
      <div class="ml-auto flex items-center gap-1 text-sm">
        <button
          type="button"
          :class="view === 'table' ? 'bg-blue-100 text-blue-700 font-semibold' : 'text-gray-600'"
          class="rounded-md border border-gray-200 px-3 py-1 hover:bg-gray-50"
          @click="view = 'table'"
        >テーブル</button>
        <button
          type="button"
          :class="view === 'card' ? 'bg-blue-100 text-blue-700 font-semibold' : 'text-gray-600'"
          class="rounded-md border border-gray-200 px-3 py-1 hover:bg-gray-50"
          @click="view = 'card'"
        >カード</button>
      </div>
      <span class="text-xs text-gray-500">{{ filtered.length }} 件</span>
    </div>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
      {{ errorMessage }}
    </div>

    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">
      読み込み中…
    </section>

    <!-- テーブル表示 -->
    <section v-else-if="view === 'table'" class="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">上位コード</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">商品名</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">ブランド</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">タイプ / 季節</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">工場</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">SKU</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">単価レンジ</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">更新日</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0">
            <td colspan="9" class="px-4 py-8 text-center text-sm text-gray-500">
              {{ search ? '検索条件に一致するデータがありません' : 'データがありません' }}
            </td>
          </tr>
          <tr
            v-for="i in filtered"
            :key="i.id"
            class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50"
            @click="navigateTo(`/products/${i.id}`)"
          >
            <td class="px-4 py-3 font-mono text-sm">{{ i.sku9Digit }}</td>
            <td class="px-4 py-3 text-sm">
              <div class="font-medium">{{ i.productName1 }}</div>
              <div v-if="i.productName2" class="text-xs text-gray-500">{{ i.productName2 }}</div>
            </td>
            <td class="px-4 py-3 text-sm">{{ i.brandName }}</td>
            <td class="px-4 py-3 text-sm text-gray-600">
              {{ i.productTypeName }} / {{ i.productSeasonName }}
            </td>
            <td class="px-4 py-3 text-sm">{{ i.factorySupplierName }}</td>
            <td class="px-4 py-3 text-right text-sm font-mono">{{ i.skuVariationCount }}</td>
            <td class="px-4 py-3 text-sm font-mono">
              {{ formatPriceRange(i.currentMinPrice, i.currentMaxPrice, i.currencyCode) }}
            </td>
            <td class="px-4 py-3 text-sm">
              <span :class="statusBadge(i.status).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
                {{ statusBadge(i.status).label }}
              </span>
            </td>
            <td class="px-4 py-3 text-xs text-gray-500">
              {{ new Date(i.updatedAt).toLocaleDateString('ja-JP') }}
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <!-- カード表示 (画像メイン、PC 5 列固定) -->
    <section v-else class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-5">
      <NuxtLink
        v-for="i in filtered"
        :key="i.id"
        :to="`/products/${i.id}`"
        class="group flex flex-col overflow-hidden rounded-md border border-gray-200 bg-white shadow-sm transition hover:border-blue-500 hover:shadow-md"
      >
        <!-- 画像エリア (アスペクト 1:1 正方形) -->
        <div class="relative aspect-square w-full overflow-hidden bg-gray-100">
          <img
            v-if="i.primaryImageS3Key"
            :src="imageUrl(i.primaryImageS3Key)"
            :alt="i.productName1"
            class="h-full w-full object-cover transition group-hover:scale-105"
          />
          <div v-else class="flex h-full w-full items-center justify-center text-gray-300">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-10 w-10" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
            </svg>
          </div>
          <span :class="statusBadge(i.status).cls"
                class="absolute right-1.5 top-1.5 inline-block rounded-full px-1.5 py-0.5 text-[10px] shadow-sm">
            {{ statusBadge(i.status).label }}
          </span>
          <span v-if="i.imageCount > 0"
                class="absolute bottom-1.5 left-1.5 inline-flex items-center gap-0.5 rounded-full bg-black/60 px-1.5 py-0.5 text-[10px] text-white">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-2.5 w-2.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
            </svg>
            {{ i.imageCount }}
          </span>
        </div>
        <!-- カード下部 (コンパクト) -->
        <div class="flex flex-1 flex-col p-2">
          <div class="font-mono text-[10px] text-gray-500">{{ i.sku9Digit }}</div>
          <div class="mt-0.5 line-clamp-2 text-sm font-semibold text-gray-900" :title="i.productName1">
            {{ i.productName1 }}
          </div>
          <div v-if="i.productName2" class="line-clamp-1 text-xs text-gray-500" :title="i.productName2">
            {{ i.productName2 }}
          </div>
          <div class="mt-1 flex items-center justify-between text-[11px] text-gray-600">
            <span class="truncate">{{ i.brandName }}</span>
            <span class="font-mono whitespace-nowrap">SKU {{ i.skuVariationCount }}</span>
          </div>
          <div class="mt-1 border-t border-gray-100 pt-1 font-mono text-xs font-semibold text-gray-900">
            {{ formatPriceRange(i.currentMinPrice, i.currentMaxPrice, i.currencyCode) }}
          </div>
        </div>
      </NuxtLink>
      <div v-if="filtered.length === 0" class="col-span-full rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        {{ search ? '検索条件に一致するデータがありません' : 'データがありません' }}
      </div>
    </section>

    <p class="mt-3 text-xs text-gray-400">
      API: GET /api/v1/products/families
    </p>
  </main>
</template>
