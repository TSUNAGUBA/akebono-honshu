<script setup lang="ts">
import type { PiListItem } from '~/composables/useProduction'
import { piStatusLabel } from '~/composables/useProduction'

const { piList } = useProduction()
const { user } = useAuth()
const canCreate = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)

const items = ref<PiListItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const includeCancelled = ref(false)
const search = ref('')

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try { items.value = await piList(includeCancelled.value) }
  catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401 ? 'セッションが切れました。再ログインしてください。' : '生産指示書一覧の取得に失敗しました'
  } finally { loading.value = false }
}
watch(includeCancelled, reload)
onMounted(reload)

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return items.value
  return items.value.filter(i =>
    i.instructionNo.toLowerCase().includes(q) || i.productSku9.toLowerCase().includes(q) ||
    i.productName.toLowerCase().includes(q) || i.factoryName.toLowerCase().includes(q))
})

const statusBadge = (s: number) => {
  if (s === 9) return { cls: 'bg-orange-100 text-orange-700' }
  if (s === 2) return { cls: 'bg-gray-200 text-gray-700' }
  if (s === 1) return { cls: 'bg-green-100 text-green-700' }
  return { cls: 'bg-yellow-100 text-yellow-700' }
}
const exportBadge = (i: PiListItem) => i.firstExportedAt
  ? { label: `出力済 (${new Date(i.firstExportedAt).toLocaleDateString('ja-JP')})`, cls: 'bg-blue-100 text-blue-700' }
  : { label: '未出力', cls: 'bg-gray-100 text-gray-600' }
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <header class="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold">生産指示書</h1>
        <p class="mt-1 text-sm text-gray-500">PI-02 一覧 / PI-04 Excel 出力（加工指図書）</p>
      </div>
      <NuxtLink v-if="canCreate" to="/production/instructions/new" class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white shadow-sm hover:bg-blue-700">+ 新規生産指示</NuxtLink>
      <span v-else class="text-xs text-gray-400">参照のみ (発注書作成権限なし)</span>
    </header>

    <!-- 検索/絞込パネル (開閉可能)。検索ボックス + 中止済みトグル。 -->
    <FilterPanel title="検索" storage-key="filters:production-instructions" :active-count="search.trim() ? 1 : 0">
      <div class="flex flex-wrap items-center gap-4">
        <input v-model="search" type="search" placeholder="指示番号 / 品番 / 品名 / 加工先で検索" class="w-80 max-w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none" />
        <label class="inline-flex items-center gap-2 text-sm text-gray-600">
          <input v-model="includeCancelled" type="checkbox" class="h-4 w-4 rounded border-gray-300" /> 中止済みを含む
        </label>
      </div>
    </FilterPanel>

    <div class="mb-3 flex items-center gap-4">
      <span class="ml-auto text-xs text-gray-500">{{ filtered.length }} 件</span>
    </div>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <section v-else class="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">指示番号</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">品番</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">品名</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">加工先</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">生産数量</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">納期</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">出力</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0"><td colspan="8" class="px-4 py-8 text-center text-sm text-gray-500">データがありません</td></tr>
          <tr v-for="i in filtered" :key="i.id" class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50" @click="navigateTo(`/production/instructions/${i.id}`)">
            <td class="px-4 py-3 font-mono text-sm">{{ i.instructionNo }}</td>
            <td class="px-4 py-3 font-mono text-sm">{{ i.productSku9 }}</td>
            <td class="px-4 py-3 text-sm">{{ i.productName }}</td>
            <td class="px-4 py-3 text-sm"><div class="font-medium">{{ i.factoryName }}</div><div class="font-mono text-xs text-gray-500">{{ i.factoryCode }}</div></td>
            <td class="px-4 py-3 text-right font-mono text-sm">{{ i.plannedQuantity.toLocaleString() }}</td>
            <td class="px-4 py-3 text-sm">{{ i.dueDate }}</td>
            <td class="px-4 py-3 text-sm"><span :class="exportBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">{{ exportBadge(i).label }}</span></td>
            <td class="px-4 py-3 text-sm"><span :class="statusBadge(i.status).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">{{ piStatusLabel(i.status) }}</span></td>
          </tr>
        </tbody>
      </table>
    </section>
    <p class="mt-3 text-xs text-gray-400">API: GET /api/v1/production-instructions</p>
  </main>
</template>
