<script setup lang="ts">
import type { MaterialOrderListItem } from '~/composables/useProduction'
import { moStatusLabel } from '~/composables/useProduction'

const { moList } = useProduction()
const items = ref<MaterialOrderListItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const includeCancelled = ref(false)
const search = ref('')

const reload = async () => {
  loading.value = true; errorMessage.value = ''
  try { items.value = await moList(includeCancelled.value) }
  catch (e) { const err = e as { statusCode?: number }; errorMessage.value = err.statusCode === 401 ? 'セッションが切れました。再ログインしてください。' : '素材発注書一覧の取得に失敗しました' }
  finally { loading.value = false }
}
watch(includeCancelled, reload)
onMounted(reload)

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return items.value
  return items.value.filter(i => i.orderNo.toLowerCase().includes(q) || i.materialSupplierName.toLowerCase().includes(q))
})
const statusBadge = (s: number) => s === 9 ? { cls: 'bg-orange-100 text-orange-700' } : s === 1 ? { cls: 'bg-green-100 text-green-700' } : { cls: 'bg-yellow-100 text-yellow-700' }
const exportBadge = (i: MaterialOrderListItem) => i.firstExportedAt ? { label: `出力済 (${new Date(i.firstExportedAt).toLocaleDateString('ja-JP')})`, cls: 'bg-blue-100 text-blue-700' } : { label: '未出力', cls: 'bg-gray-100 text-gray-600' }
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <header class="mb-6">
      <h1 class="text-2xl font-bold">素材発注書</h1>
      <p class="mt-1 text-sm text-gray-500">
        MO-02 一覧 / MO-04 Excel 出力（生地材料発注）。新規作成は
        <NuxtLink to="/production/instructions" class="text-blue-700 hover:underline">生産指示書</NuxtLink>
        の詳細から行います。
      </p>
    </header>

    <div class="mb-3 flex items-center gap-4">
      <input v-model="search" type="search" placeholder="発注番号 / 素材仕入先で検索" class="w-80 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none" />
      <label class="inline-flex items-center gap-2 text-sm text-gray-600"><input v-model="includeCancelled" type="checkbox" class="h-4 w-4 rounded border-gray-300" /> 中止済みを含む</label>
      <span class="ml-auto text-xs text-gray-500">{{ filtered.length }} 件</span>
    </div>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <section v-else class="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">発注番号</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">素材仕入先</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">明細</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">合計</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">納期</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">出力</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0"><td colspan="7" class="px-4 py-8 text-center text-sm text-gray-500">データがありません</td></tr>
          <tr v-for="i in filtered" :key="i.id" class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50" @click="navigateTo(`/production/material-orders/${i.id}`)">
            <td class="px-4 py-3 font-mono text-sm">{{ i.orderNo }}</td>
            <td class="px-4 py-3 text-sm"><div class="font-medium">{{ i.materialSupplierName }}</div><div class="font-mono text-xs text-gray-500">{{ i.materialSupplierCode }}</div></td>
            <td class="px-4 py-3 text-right font-mono text-sm">{{ i.lineCount }}</td>
            <td class="px-4 py-3 text-right font-mono text-sm">{{ i.currencyCode }} {{ i.totalAmount.toLocaleString() }}</td>
            <td class="px-4 py-3 text-sm">{{ i.dueDate }}</td>
            <td class="px-4 py-3 text-sm"><span :class="exportBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">{{ exportBadge(i).label }}</span></td>
            <td class="px-4 py-3 text-sm"><span :class="statusBadge(i.status).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">{{ moStatusLabel(i.status) }}</span></td>
          </tr>
        </tbody>
      </table>
    </section>
    <p class="mt-3 text-xs text-gray-400">API: GET /api/v1/material-orders</p>
  </main>
</template>
