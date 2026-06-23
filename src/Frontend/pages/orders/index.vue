<script setup lang="ts">
import type { OrderListItem } from '~/composables/useOrders'

const { list } = useOrders()
const { user } = useAuth()

const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)

const items = ref<OrderListItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const includeCancelled = ref(false)
const search = ref('')
// 発注区分タブ (全件 / 国内 / 海外、is_overseas でクライアント側絞込)
type RegionTab = 'all' | 'domestic' | 'overseas'
const regionTab = ref<RegionTab>('all')
const regionTabs: { key: RegionTab; label: string }[] = [
  { key: 'all', label: '全件' },
  { key: 'domestic', label: '国内' },
  { key: 'overseas', label: '海外' },
]

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    items.value = await list(includeCancelled.value)
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '発注書一覧の取得に失敗しました'
  } finally {
    loading.value = false
  }
}

watch(includeCancelled, reload)
onMounted(reload)

const filtered = computed(() => {
  // 発注区分タブ (国内/海外) でまず絞込
  let base = items.value
  if (regionTab.value === 'domestic') base = base.filter((i) => !i.isOverseas)
  else if (regionTab.value === 'overseas') base = base.filter((i) => i.isOverseas)

  const q = search.value.trim().toLowerCase()
  if (!q) return base
  return base.filter(
    (i) =>
      i.mgmtNo.toLowerCase().includes(q) ||
      (i.orderNo ?? '').toLowerCase().includes(q) ||
      i.supplierName.toLowerCase().includes(q) ||
      i.deliveryDestinationName.toLowerCase().includes(q),
  )
})

// 各タブの件数 (区分絞込のみ、検索語は無視してタブ自体のラベルに件数表示)
const regionCount = (key: RegionTab): number => {
  if (key === 'domestic') return items.value.filter((i) => !i.isOverseas).length
  if (key === 'overseas') return items.value.filter((i) => i.isOverseas).length
  return items.value.length
}

// 発注区分バッジ (国内: グレー / 海外: インディゴ。詳細画面 [id].vue と配色を統一)
const regionBadge = (i: OrderListItem): { label: string; cls: string } =>
  i.isOverseas
    ? { label: '海外', cls: 'bg-indigo-100 text-indigo-700' }
    : { label: '国内', cls: 'bg-gray-100 text-gray-600' }

const exportBadge = (i: OrderListItem): { label: string; cls: string } => {
  if (!i.firstExportedAt) return { label: '未出力', cls: 'bg-gray-100 text-gray-600' }
  const dt = new Date(i.firstExportedAt).toLocaleDateString('ja-JP')
  return { label: `初回出力済 (${dt})`, cls: 'bg-blue-100 text-blue-700' }
}

const statusBadge = (s: number): { label: string; cls: string } =>
  s === 1
    ? { label: 'Cancelled', cls: 'bg-orange-100 text-orange-700' }
    : { label: 'Active', cls: 'bg-green-100 text-green-700' }
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <header class="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold">発注書</h1>
        <p class="mt-1 text-sm text-gray-500">
          O-03 一覧 / O-06 Excel 出力 (F-22 帳票宛名「&lt;official_name&gt; 御中 &lt;supplier_code&gt;」)
        </p>
      </div>
      <NuxtLink
        v-if="canCreateOrder"
        to="/orders/new"
        class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white shadow-sm hover:bg-blue-700"
      >
        + 新規発注書
      </NuxtLink>
      <span v-else class="text-xs text-gray-400">参照のみ (発注書作成権限なし)</span>
    </header>

    <!-- 発注区分タブ (全件 / 国内 / 海外、is_overseas でクライアント側絞込) -->
    <div class="mb-3 inline-flex overflow-hidden rounded-md border border-gray-300 text-sm">
      <button
        v-for="(t, ti) in regionTabs"
        :key="t.key"
        type="button"
        class="px-4 py-1.5 font-medium transition-colors"
        :class="[
          ti > 0 ? 'border-l border-gray-300' : '',
          regionTab === t.key ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50',
        ]"
        @click="regionTab = t.key"
      >
        {{ t.label }}
        <span
          class="ml-1 text-xs"
          :class="regionTab === t.key ? 'text-blue-100' : 'text-gray-400'"
        >({{ regionCount(t.key) }})</span>
      </button>
    </div>

    <div class="mb-3 flex items-center gap-4">
      <input
        v-model="search"
        type="search"
        placeholder="管理番号 / 発注番号 / 仕入先 / 納品先で検索"
        class="w-80 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      />
      <label class="inline-flex items-center gap-2 text-sm text-gray-600">
        <input v-model="includeCancelled" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
        中止済みを含む
      </label>
      <span class="ml-auto text-xs text-gray-500">{{ filtered.length }} 件</span>
    </div>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
      {{ errorMessage }}
    </div>

    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">
      読み込み中…
    </section>

    <section v-else class="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">管理番号</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">区分</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">発注番号</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">仕入先</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">納品先</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">納入日</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">明細</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">合計</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">出力</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0">
            <td colspan="10" class="px-4 py-8 text-center text-sm text-gray-500">
              {{ search ? '検索条件に一致するデータがありません' : 'データがありません' }}
            </td>
          </tr>
          <tr
            v-for="i in filtered"
            :key="i.id"
            class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50"
            @click="navigateTo(`/orders/${i.id}`)"
          >
            <td class="px-4 py-3 font-mono text-sm">{{ i.mgmtNo }}</td>
            <td class="px-4 py-3 text-sm">
              <span :class="regionBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs font-medium">
                {{ regionBadge(i).label }}
              </span>
            </td>
            <td class="px-4 py-3 font-mono text-sm">{{ i.orderNo ?? '—' }}</td>
            <td class="px-4 py-3 text-sm">
              <div class="font-medium">{{ i.supplierName }}</div>
              <div class="font-mono text-xs text-gray-500">{{ i.supplierCode }}</div>
            </td>
            <td class="px-4 py-3 text-sm">{{ i.deliveryDestinationName }}</td>
            <td class="px-4 py-3 text-sm">{{ i.dueDate }}</td>
            <td class="px-4 py-3 text-right font-mono text-sm">{{ i.lineCount }}</td>
            <td class="px-4 py-3 text-right font-mono text-sm">
              {{ i.currencyCode }} {{ i.totalAmount.toLocaleString() }}
            </td>
            <td class="px-4 py-3 text-sm">
              <span :class="exportBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
                {{ exportBadge(i).label }}
              </span>
            </td>
            <td class="px-4 py-3 text-sm">
              <span :class="statusBadge(i.status).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
                {{ statusBadge(i.status).label }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <p class="mt-3 text-xs text-gray-400">API: GET /api/v1/orders</p>
  </main>
</template>
