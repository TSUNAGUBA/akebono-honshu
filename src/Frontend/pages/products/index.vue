<script setup lang="ts">
import { productStatusLabel } from '~/composables/useProducts'
import type { FamilyListItem } from '~/composables/useProducts'
import type { MasterItem } from '~/composables/useMasters'

const { listFamilies } = useProducts()
const { list: listMaster } = useMasters()
const { apiData } = useApi()
const { canEditMaster } = useAuth()

const items = ref<FamilyListItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const includeDeleted = ref(false)
const view = ref<'table' | 'card'>('table')

// --- フィルタ用マスタ参照 (商品タイプ / 商品季節 / 仕入先=工場 / ブランド / 企画者) ---
const productTypes = ref<MasterItem[]>([])
const productSeasons = ref<MasterItem[]>([])
// 工場 (Part2)。一覧の「工場」絞込は工場マスタを参照する (旧: 仕入先兼用)。
const factories = ref<MasterItem[]>([])
const brands = ref<MasterItem[]>([])
interface UserOption { id: string; loginId: string; displayName: string }
const users = ref<UserOption[]>([])
// 企画者ドロップダウン用 (orders/new.vue・products/new.vue と同じ整形)
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))

// --- 9 つの SPLIT フィルタ (AND 合成、未指定 = 全件) ---
// dropdown は uuid 文字列 ID (null = 未指定)、text は手入力 (部分一致)。
const filters = ref({
  itemNumber: '',           // text 部分一致 (品番=itemNumber / 他品番=itemFamilyNumber / 旧品番=legacyId)
  productYear: '',          // text 前方/部分一致 (product_year)
  productTypeId: null as string | null,   // dropdown
  productSeasonId: null as string | null, // dropdown
  productName: '',          // text 部分一致 (product_name_1/2)
  provisionalNumber: '',    // text 部分一致 (provisional_number)
  factorySupplierId: null as string | null, // dropdown (= 工場)
  plannerUserId: null as string | null,     // dropdown (users)
  brandId: null as string | null,           // dropdown
})

const clearFilters = () => {
  filters.value = {
    itemNumber: '',
    productYear: '',
    productTypeId: null,
    productSeasonId: null,
    productName: '',
    provisionalNumber: '',
    factorySupplierId: null,
    plannerUserId: null,
    brandId: null,
  }
}

const hasActiveFilter = computed(() =>
  filters.value.itemNumber.trim() !== '' ||
  filters.value.productYear.trim() !== '' ||
  filters.value.productTypeId != null ||
  filters.value.productSeasonId != null ||
  filters.value.productName.trim() !== '' ||
  filters.value.provisionalNumber.trim() !== '' ||
  filters.value.factorySupplierId != null ||
  filters.value.plannerUserId != null ||
  filters.value.brandId != null,
)

// 有効なフィルタ項目の件数 (FilterPanel の「絞込中 N」チップ用)。
const activeFilterCount = computed(() => {
  const f = filters.value
  let n = 0
  if (f.itemNumber.trim() !== '') n++
  if (f.productYear.trim() !== '') n++
  if (f.productTypeId != null) n++
  if (f.productSeasonId != null) n++
  if (f.productName.trim() !== '') n++
  if (f.provisionalNumber.trim() !== '') n++
  if (f.factorySupplierId != null) n++
  if (f.plannerUserId != null) n++
  if (f.brandId != null) n++
  return n
})

// キーセットページング (AKB-DOC-12 §7.1)。limit=200 で取得し、続きは「さらに読み込む」で辿る。
const nextCursor = ref<string | null>(null)
const loadingMore = ref(false)
// リロード世代。reload 開始でインクリメントし、in-flight の応答が旧世代なら破棄する
// (フィルタ切替 reload と「さらに読み込む」の競合で、混在リスト・旧条件カーソルが残るのを防ぐ)。
const requestSeq = ref(0)

const reload = async () => {
  const seq = ++requestSeq.value
  loading.value = true
  errorMessage.value = ''
  try {
    const page = await listFamilies(includeDeleted.value)
    if (seq !== requestSeq.value) return
    items.value = page.items
    nextCursor.value = page.page.hasMore ? page.page.nextCursor : null
  } catch (e) {
    // 旧世代 (reload で置き換え済み) の失敗は表示を汚染しない
    if (seq !== requestSeq.value) return
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '商品企画一覧の取得に失敗しました'
  } finally {
    // 新しい reload が in-flight の場合、旧世代がローディング表示を先に消さない
    if (seq === requestSeq.value) loading.value = false
  }
}

const loadMore = async () => {
  if (!nextCursor.value || loadingMore.value) return
  const seq = requestSeq.value
  loadingMore.value = true
  try {
    const page = await listFamilies(includeDeleted.value, nextCursor.value)
    if (seq !== requestSeq.value) return
    items.value = [...items.value, ...page.items]
    nextCursor.value = page.page.hasMore ? page.page.nextCursor : null
  } catch (e) {
    // 旧世代 (reload で置き換え済み) の失敗は表示を汚染しない
    if (seq !== requestSeq.value) return
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '商品企画一覧の取得に失敗しました'
  } finally {
    // loadingMore は同時実行ガードのため無条件で解除する
    loadingMore.value = false
  }
}

// フィルタ用マスタ・ユーザの読込はドロップダウンの選択肢にのみ使う補助処理。
// 失敗しても一覧本体 (reload) は表示する (原則 4 非ブロッキング)。
const loadFilterSources = async () => {
  try {
    const [pt, ps, fac, br, usr] = await Promise.all([
      listMaster('product-types'),
      listMaster('product-seasons'),
      listMaster('factories'),
      listMaster('brands'),
      apiData<UserOption[]>('/users'),
    ])
    productTypes.value = pt
    productSeasons.value = ps
    factories.value = fac
    brands.value = br
    users.value = usr
  } catch (e) {
    // フィルタ選択肢の取得失敗は致命的でない。ドロップダウンが空になるだけで一覧は使える。
    console.error('フィルタ選択肢 (マスタ/ユーザ) の取得に失敗しました', e)
  }
}

watch(includeDeleted, reload)
onMounted(() => {
  reload()
  loadFilterSources()
})

// 部分一致用に正規化 (大小文字を無視)。
const inc = (haystack: string | null | undefined, needle: string): boolean =>
  (haystack ?? '').toLowerCase().includes(needle.trim().toLowerCase())

// 9 フィルタを AND 合成。各フィルタは未指定なら true (= 絞り込まない)。
const filtered = computed(() => {
  const f = filters.value
  const itemQ = f.itemNumber.trim()
  const yearQ = f.productYear.trim()
  const nameQ = f.productName.trim()
  const provQ = f.provisionalNumber.trim()
  return items.value.filter((i) => {
    // 品番 (企画コード) は品番 / 他品番 / 旧品番 のいずれかに部分一致 (商品名と同じ複数フィールド OR)。
    if (itemQ !== '' && !(inc(i.itemNumber, itemQ) || inc(i.itemFamilyNumber, itemQ) || inc(i.legacyId, itemQ))) return false
    if (yearQ !== '' && !inc(i.productYear != null ? String(i.productYear) : '', yearQ)) return false
    if (f.productTypeId != null && i.productTypeId !== f.productTypeId) return false
    if (f.productSeasonId != null && i.productSeasonId !== f.productSeasonId) return false
    if (nameQ !== '' && !(inc(i.productName1, nameQ) || inc(i.productName2, nameQ))) return false
    if (provQ !== '' && !inc(i.provisionalNumber, provQ)) return false
    if (f.factorySupplierId != null && i.factorySupplierId !== f.factorySupplierId) return false
    if (f.plannerUserId != null && i.plannerUserId !== f.plannerUserId) return false
    if (f.brandId != null && i.brandId !== f.brandId) return false
    return true
  })
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
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <header class="mb-4 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold">商品一覧</h1>
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

    <!-- 絞込フィルタパネル (9 つの SPLIT フィルタ、AND 合成、未指定 = 全件)。
         開閉可能 (FilterPanel)。dropdown は MasterSelect (uuid 文字列 ID・allow-empty)、text は手入力で部分一致。
         レスポンシブグリッド: モバイル 1 列 → sm 2 列 → lg 4 列。 -->
    <FilterPanel
      title="絞込"
      storage-key="filters:products"
      :active-count="activeFilterCount"
    >
      <template #actions>
        <button
          type="button"
          class="rounded-md border border-gray-300 bg-white px-3 py-1 text-xs text-gray-600 hover:bg-gray-50 disabled:opacity-40"
          :disabled="!hasActiveFilter"
          @click="clearFilters"
        >
          クリア
        </button>
      </template>
      <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <label class="flex flex-col gap-1">
          <span class="font-medium">品番</span>
          <input
            v-model="filters.itemNumber"
            type="text"
            placeholder="品番 / 他品番で部分一致"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">商品年度</span>
          <input
            v-model="filters.productYear"
            type="text"
            inputmode="numeric"
            placeholder="例: 2025 / 9999"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">商品タイプ</span>
          <MasterSelect
            v-model="filters.productTypeId"
            :items="productTypes"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">商品季節</span>
          <MasterSelect
            v-model="filters.productSeasonId"
            :items="productSeasons"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">商品名</span>
          <input
            v-model="filters.productName"
            type="text"
            placeholder="商品名で部分一致"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">仮番号</span>
          <input
            v-model="filters.provisionalNumber"
            type="text"
            placeholder="仮番号で部分一致"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">工場</span>
          <MasterSelect
            v-model="filters.factorySupplierId"
            :items="factories"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">企画者</span>
          <MasterSelect
            v-model="filters.plannerUserId"
            :items="userOptions"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">ブランド</span>
          <MasterSelect
            v-model="filters.brandId"
            :items="brands"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
      </div>
    </FilterPanel>

    <div class="mb-3 flex flex-wrap items-center gap-x-4 gap-y-2">
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
      <span class="text-xs text-gray-500">{{ filtered.length }} 件{{ nextCursor ? ' (続きあり)' : '' }}</span>
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
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">品番 / 他品番</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">商品名</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">年度</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">ブランド</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">タイプ / 季節</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">工場</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">企画者</th>
            <th class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">SKU</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">単価レンジ</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">更新日</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0">
            <td colspan="11" class="px-4 py-8 text-center text-sm text-gray-500">
              {{ hasActiveFilter ? '検索条件に一致するデータがありません' : 'データがありません' }}
            </td>
          </tr>
          <tr
            v-for="i in filtered"
            :key="i.id"
            class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50"
            @click="navigateTo(`/products/${i.id}`)"
          >
            <td class="px-3 py-2 font-mono text-sm leading-tight">
              <div>{{ i.itemNumber }}</div>
              <div class="text-xs text-gray-500">{{ i.itemFamilyNumber }}</div>
            </td>
            <td class="px-3 py-2 text-sm">
              <div class="font-medium">{{ i.productName1 }}</div>
              <div v-if="i.productName2" class="text-xs text-gray-500">{{ i.productName2 }}</div>
            </td>
            <td class="px-3 py-2 text-sm font-mono text-gray-600">{{ i.productYear ?? '—' }}</td>
            <td class="px-3 py-2 text-sm">{{ i.brandName }}</td>
            <td class="px-3 py-2 text-sm text-gray-600">
              {{ i.productTypeName }} / {{ i.productSeasonName }}
            </td>
            <td class="px-3 py-2 text-sm">{{ i.factorySupplierName }}</td>
            <td class="px-3 py-2 text-sm text-gray-600">{{ i.plannerName ?? '—' }}</td>
            <td class="px-3 py-2 text-right text-sm font-mono">{{ i.skuVariationCount }}</td>
            <td class="px-3 py-2 text-sm font-mono">
              {{ formatPriceRange(i.currentMinPrice, i.currentMaxPrice, i.currencyCode) }}
            </td>
            <td class="px-3 py-2 text-sm">
              <span :class="statusBadge(i.status).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
                {{ statusBadge(i.status).label }}
              </span>
            </td>
            <td class="px-3 py-2 text-xs text-gray-500">
              {{ formatJstDate(i.updatedAt) }}
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
            v-if="i.primaryImageUrl"
            :src="i.primaryImageUrl"
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
          <div class="font-mono text-[10px] text-gray-500">{{ i.itemNumber }}</div>
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
          <div class="mt-0.5 flex items-center justify-between text-[11px] text-gray-500">
            <span class="font-mono">{{ i.productYear ?? '—' }}</span>
            <span class="truncate" :title="i.plannerName ?? ''">{{ i.plannerName ?? '—' }}</span>
          </div>
          <div class="mt-1 border-t border-gray-100 pt-1 font-mono text-xs font-semibold text-gray-900">
            {{ formatPriceRange(i.currentMinPrice, i.currentMaxPrice, i.currencyCode) }}
          </div>
        </div>
      </NuxtLink>
      <div v-if="filtered.length === 0" class="col-span-full rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        {{ hasActiveFilter ? '検索条件に一致するデータがありません' : 'データがありません' }}
      </div>
    </section>

    <div v-if="nextCursor && !loading" class="mt-3 text-center">
      <button type="button" :disabled="loadingMore" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm text-gray-700 shadow-sm hover:bg-gray-50 disabled:opacity-50" @click="loadMore">
        {{ loadingMore ? '読み込み中…' : 'さらに読み込む' }}
      </button>
    </div>

    <p class="mt-3 text-xs text-gray-400">
      API: GET /api/maker/v1/products/families
    </p>
  </main>
</template>
