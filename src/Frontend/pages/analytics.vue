<script setup lang="ts">
// 分析ダッシュボード: 既存 API（商品・発注・生産）の DB 実データを集計して KPI 表示する。
// 各取得は独立に try/catch し、失敗しても他の指標表示を止めない（原則4 グレースフルデグラデーション）。
const { apiFetch } = useApi()

interface Stat { label: string; value: number; to: string; alert?: boolean }
const stats = ref<Stat[]>([])
const loading = ref(true)
const partialError = ref(false)

const fetchData = async (path: string): Promise<unknown[]> => {
  try {
    const res = await apiFetch<{ data: unknown[] }>(path)
    return res.data ?? []
  } catch (e) {
    console.error('[analytics] 取得に失敗しました', path, e)
    partialError.value = true
    return []
  }
}

onMounted(async () => {
  loading.value = true
  try {
    const [families, orders, instructions, materialOrders, status] = await Promise.all([
      fetchData('/products/families'),
      fetchData('/orders'),
      fetchData('/production-instructions'),
      fetchData('/material-orders'),
      fetchData('/production/status'),
    ])
    const unarranged = (status as Array<{ materialOrder?: string; productionInstruction?: string }>)
      .filter((r) => r.materialOrder === 'undone' || r.productionInstruction === 'undone').length

    stats.value = [
      { label: '商品企画', value: families.length, to: '/products' },
      { label: '発注書', value: orders.length, to: '/orders' },
      { label: '生産指示書', value: instructions.length, to: '/production/instructions' },
      { label: '素材発注書', value: materialOrders.length, to: '/production/material-orders' },
      { label: '手配未完了の品番', value: unarranged, to: '/production/status', alert: true },
    ]
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <main class="mx-auto w-full max-w-screen-2xl px-4 py-4">
    <div class="mb-4">
      <h1 class="text-xl font-bold text-gray-800">分析ダッシュボード</h1>
      <p class="mt-1 text-sm text-gray-500">商品・発注・生産の状況（DB 実データの集計）</p>
    </div>

    <div v-if="partialError" class="mb-4 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800">
      一部の指標を取得できませんでした。表示は取得できた範囲です。
    </div>

    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
      読み込み中…
    </div>
    <div v-else class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
      <NuxtLink
        v-for="s in stats"
        :key="s.label"
        :to="s.to"
        class="rounded-xl border border-gray-200 bg-white p-5 no-underline shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"
      >
        <div class="text-sm text-gray-500">{{ s.label }}</div>
        <div
          class="mt-1 text-3xl font-bold"
          :class="s.alert && s.value > 0 ? 'text-red-600' : 'text-gray-800'"
        >
          {{ s.value }}
        </div>
      </NuxtLink>
    </div>

    <p class="mt-4 text-xs text-gray-400">
      集計元 API: /products/families, /orders, /production-instructions, /material-orders, /production/status
    </p>
  </main>
</template>
