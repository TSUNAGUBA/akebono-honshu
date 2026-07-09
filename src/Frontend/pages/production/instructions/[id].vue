<script setup lang="ts">
import type { PiDetail } from '~/composables/useProduction'
import { piStatusLabel } from '~/composables/useProduction'

const route = useRoute()
const id = computed(() => Number(route.params.id))
const { piGet, piIssue, piComplete, piCancel, piDownloadExcel } = useProduction()
const { user } = useAuth()
const canEdit = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)

const detail = ref<PiDetail | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const successMessage = ref('')
const busy = ref(false)

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try { detail.value = await piGet(id.value) }
  catch { errorMessage.value = '生産指示書の取得に失敗しました' }
  finally { loading.value = false }
}
onMounted(reload)

const run = async (fn: () => Promise<void>, ok: string) => {
  busy.value = true; errorMessage.value = ''; successMessage.value = ''
  try { await fn(); successMessage.value = ok; await reload() }
  catch (e) { errorMessage.value = getApiErrorMessage(e, '操作に失敗しました') }
  finally { busy.value = false }
}

const onIssue = () => run(() => piIssue(id.value), '発行しました（生産指示=済）')
const onComplete = () => run(() => piComplete(id.value), '生産完了にしました')
const onCancel = () => { const reason = window.prompt('中止理由を入力してください') ?? ''; if (reason) run(() => piCancel(id.value, reason), '中止しました') }
const onExcel = () => run(() => piDownloadExcel(id.value, detail.value?.instructionNo ?? 'PI'), 'Excel を出力しました')
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-4">
    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>
    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <template v-else-if="detail">
      <header class="mb-4 flex items-start justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold">生産指示書 {{ detail.instructionNo }}</h1>
          <p class="mt-1 text-sm text-gray-500">{{ detail.productSku9 }} / {{ detail.productName }}</p>
        </div>
        <NuxtLink to="/production/instructions" class="text-sm text-blue-600 hover:underline">← 一覧へ</NuxtLink>
      </header>

      <section class="mb-4 grid grid-cols-2 gap-3 rounded-lg border border-gray-200 bg-white p-4 text-sm shadow-sm sm:grid-cols-3">
        <div><span class="text-gray-500">加工先:</span> {{ detail.factoryCode }} {{ detail.factoryName }}</div>
        <div><span class="text-gray-500">生産数量:</span> {{ detail.plannedQuantity.toLocaleString() }}</div>
        <div><span class="text-gray-500">希望納期:</span> {{ detail.dueDate }}</div>
        <div><span class="text-gray-500">状態:</span> {{ piStatusLabel(detail.status) }}</div>
        <div><span class="text-gray-500">出力:</span> {{ detail.firstExportedAt ? `出力済 (${formatJstDate(detail.firstExportedAt)})` : '未出力' }}</div>
        <div v-if="detail.cancelReason" class="text-orange-700">中止理由: {{ detail.cancelReason }}</div>
      </section>

      <section v-if="detail.communicationText" class="mb-4 rounded-lg border border-gray-200 bg-white p-4 text-sm shadow-sm">
        <div class="mb-1 text-xs font-semibold text-gray-500">連絡事項</div>
        <div class="whitespace-pre-wrap">{{ detail.communicationText }}</div>
      </section>

      <section class="mb-4 overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
        <table class="w-full">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">No</th>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">品番(SKU)</th>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">色</th>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">サイズ</th>
              <th class="px-4 py-2 text-right text-xs font-semibold uppercase text-gray-600">生産数量</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in detail.lines" :key="l.id" class="border-b border-gray-100 last:border-0">
              <td class="px-4 py-2 text-sm">{{ l.lineNo }}</td>
              <td class="px-4 py-2 font-mono text-sm">{{ l.sku }}</td>
              <td class="px-4 py-2 text-sm">{{ l.colorName }}</td>
              <td class="px-4 py-2 text-sm">{{ l.sizeName }}</td>
              <td class="px-4 py-2 text-right font-mono text-sm">{{ l.quantity.toLocaleString() }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <div class="flex flex-wrap gap-3">
        <button v-if="canEdit && detail.status === 0" type="button" :disabled="busy" class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white hover:bg-blue-700 disabled:opacity-50" @click="onIssue">発行 (指示確定)</button>
        <button v-if="canEdit && detail.status === 1" type="button" :disabled="busy" class="rounded-md bg-gray-700 px-4 py-2 text-sm text-white hover:bg-gray-800 disabled:opacity-50" @click="onComplete">生産完了</button>
        <button type="button" :disabled="busy" class="rounded-md border border-gray-300 px-4 py-2 text-sm hover:bg-gray-50 disabled:opacity-50" @click="onExcel">📥 Excel 出力</button>
        <NuxtLink v-if="canEdit && detail.status !== 9" :to="`/production/material-orders/new?piId=${detail.id}`" class="rounded-md bg-amber-500 px-4 py-2 text-sm text-white hover:bg-amber-600">この指示から素材発注を作成</NuxtLink>
        <button v-if="canEdit && detail.status !== 9 && detail.status !== 2" type="button" :disabled="busy" class="rounded-md border border-orange-300 px-4 py-2 text-sm text-orange-700 hover:bg-orange-50 disabled:opacity-50" @click="onCancel">中止</button>
      </div>
    </template>
  </main>
</template>
