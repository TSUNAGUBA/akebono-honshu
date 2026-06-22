<script setup lang="ts">
import type { ProductionStatusRow } from '~/composables/useProduction'

const { statusList } = useProduction()

const rows = ref<ProductionStatusRow[]>([])
const loading = ref(true)
const errorMessage = ref('')
const onlyMaterialUndone = ref(false)
const onlyInstructionUndone = ref(false)
const onlyBomUnregistered = ref(false)

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    rows.value = await statusList({
      materialOrderState: onlyMaterialUndone.value ? 'undone' : undefined,
      productionInstructionState: onlyInstructionUndone.value ? 'undone' : undefined,
      bomRegistered: onlyBomUnregistered.value ? false : undefined,
    })
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401 ? 'セッションが切れました。再ログインしてください。' : '生産手配状況の取得に失敗しました'
  } finally {
    loading.value = false
  }
}

watch([onlyMaterialUndone, onlyInstructionUndone, onlyBomUnregistered], reload)
onMounted(reload)

const doneBadge = (s: string) => s === 'done'
  ? { label: '済', cls: 'bg-green-100 text-green-700' }
  : { label: '未', cls: 'bg-gray-100 text-gray-500' }
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <header class="mb-6">
      <h1 class="text-2xl font-bold">生産手配状況</h1>
      <p class="mt-1 text-sm text-gray-500">PS-01 品番ごとの「素材発注」「生産指示」未/済バッジ。未手配の品番を絞り込めます。</p>
    </header>

    <div class="mb-3 flex flex-wrap items-center gap-4 text-sm">
      <label class="inline-flex items-center gap-2 text-gray-600">
        <input v-model="onlyMaterialUndone" type="checkbox" class="h-4 w-4 rounded border-gray-300" /> 素材発注=未のみ
      </label>
      <label class="inline-flex items-center gap-2 text-gray-600">
        <input v-model="onlyInstructionUndone" type="checkbox" class="h-4 w-4 rounded border-gray-300" /> 生産指示=未のみ
      </label>
      <label class="inline-flex items-center gap-2 text-gray-600">
        <input v-model="onlyBomUnregistered" type="checkbox" class="h-4 w-4 rounded border-gray-300" /> BOM未登録のみ
      </label>
      <span class="ml-auto text-xs text-gray-500">{{ rows.length }} 件</span>
    </div>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>

    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <section v-else class="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">品番</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">商品名</th>
            <th class="px-4 py-3 text-center text-xs font-semibold uppercase text-gray-600">素材発注</th>
            <th class="px-4 py-3 text-center text-xs font-semibold uppercase text-gray-600">生産指示</th>
            <th class="px-4 py-3 text-center text-xs font-semibold uppercase text-gray-600">BOM</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">手配</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="rows.length === 0"><td colspan="6" class="px-4 py-8 text-center text-sm text-gray-500">該当する品番がありません</td></tr>
          <tr v-for="r in rows" :key="r.familyId" class="border-b border-gray-100 last:border-0 hover:bg-blue-50">
            <td class="px-4 py-3 font-mono text-sm">
              <NuxtLink :to="`/products/${r.familyId}`" class="text-blue-700 hover:underline">{{ r.sku9 }}</NuxtLink>
            </td>
            <td class="px-4 py-3 text-sm">{{ r.productName }}</td>
            <td class="px-4 py-3 text-center">
              <span :class="doneBadge(r.materialOrder).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">{{ doneBadge(r.materialOrder).label }}</span>
            </td>
            <td class="px-4 py-3 text-center">
              <span :class="doneBadge(r.productionInstruction).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">{{ doneBadge(r.productionInstruction).label }}</span>
            </td>
            <td class="px-4 py-3 text-center">
              <span v-if="r.bomRegistered" class="inline-block rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700">登録済</span>
              <span v-else class="inline-block rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-700">未登録</span>
            </td>
            <td class="px-4 py-3 text-sm">
              <div class="flex flex-wrap gap-2">
                <NuxtLink :to="`/production/bom/${r.familyId}`" class="rounded bg-amber-50 px-2 py-1 text-xs text-amber-700 hover:bg-amber-100">BOM編集</NuxtLink>
                <NuxtLink :to="`/production/instructions/new?familyId=${r.familyId}`" class="rounded bg-blue-50 px-2 py-1 text-xs text-blue-700 hover:bg-blue-100">生産指示作成</NuxtLink>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
    <p class="mt-3 text-xs text-gray-400">API: GET /api/v1/production/status</p>
  </main>
</template>
