<script setup lang="ts">
import type { MaterialOrderDetail } from '~/composables/useProduction'
import { moStatusLabel } from '~/composables/useProduction'

const route = useRoute()
// 第二段階契約: 素材発注 id は uuid 文字列。数値変換せず文字列のまま使う。
const id = computed(() => String(route.params.id))
const { moGet, moOrder, moCancel, moDownloadExcel } = useProduction()
const { user } = useAuth()
const canEdit = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) === 1)

const detail = ref<MaterialOrderDetail | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const successMessage = ref('')
const busy = ref(false)

const reload = async () => {
  loading.value = true; errorMessage.value = ''
  try { detail.value = await moGet(id.value) }
  catch { errorMessage.value = '素材発注書の取得に失敗しました' }
  finally { loading.value = false }
}
onMounted(reload)

const run = async (fn: () => Promise<void>, ok: string) => {
  busy.value = true; errorMessage.value = ''; successMessage.value = ''
  try { await fn(); successMessage.value = ok; await reload() }
  catch (e) { errorMessage.value = getApiErrorMessage(e, '操作に失敗しました') }
  finally { busy.value = false }
}
const onOrder = () => run(() => moOrder(id.value), '発注確定しました（素材発注=済）')
const onCancel = () => { const reason = window.prompt('中止理由を入力してください') ?? ''; if (reason) run(() => moCancel(id.value, reason), '中止しました') }
const onExcel = () => run(() => moDownloadExcel(id.value, detail.value?.orderNo ?? 'MO'), 'Excel を出力しました')
</script>

<template>
  <main class="mx-auto max-w-7xl px-3 py-3">
    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>
    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <template v-else-if="detail">
      <header class="mb-4 flex items-start justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold">素材発注書 {{ detail.orderNo }}</h1>
          <p class="mt-1 text-sm text-gray-500">{{ detail.materialSupplierCode }} {{ detail.materialSupplierName }}</p>
        </div>
        <NuxtLink to="/production/material-orders" class="text-sm text-blue-600 hover:underline">← 一覧へ</NuxtLink>
      </header>

      <section class="mb-4 grid grid-cols-2 gap-3 rounded-lg border border-gray-200 bg-white p-4 text-sm shadow-sm sm:grid-cols-3">
        <div><span class="text-gray-500">納入希望日:</span> {{ detail.dueDate }}</div>
        <div><span class="text-gray-500">状態:</span> {{ moStatusLabel(detail.status) }}</div>
        <div><span class="text-gray-500">出力:</span> {{ detail.firstExportedAt ? `出力済 (${formatJstDate(detail.firstExportedAt)})` : '未出力' }}</div>
        <div v-if="detail.productionInstructionNo"><span class="text-gray-500">由来生産指示:</span> {{ detail.productionInstructionNo }}</div>
        <div v-if="detail.cancelReason" class="text-orange-700">中止理由: {{ detail.cancelReason }}</div>
      </section>

      <section class="mb-4 overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
        <table class="w-full">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">No</th>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">素材</th>
              <th class="px-4 py-2 text-right text-xs font-semibold uppercase text-gray-600">数量</th>
              <th class="px-4 py-2 text-left text-xs font-semibold uppercase text-gray-600">単位</th>
              <th class="px-4 py-2 text-right text-xs font-semibold uppercase text-gray-600">単価</th>
              <th class="px-4 py-2 text-right text-xs font-semibold uppercase text-gray-600">金額</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in detail.lines" :key="l.id" class="border-b border-gray-100 last:border-0">
              <td class="px-4 py-2 text-sm">{{ l.lineNo }}</td>
              <td class="px-4 py-2 text-sm">{{ l.materialName }}</td>
              <td class="px-4 py-2 text-right font-mono text-sm">{{ l.requiredQuantity.toLocaleString() }}</td>
              <td class="px-4 py-2 text-sm">{{ l.unit }}</td>
              <td class="px-4 py-2 text-right font-mono text-sm">{{ l.unitPrice === null ? '(未設定)' : `${l.currencyCode} ${l.unitPrice.toLocaleString()}` }}</td>
              <td class="px-4 py-2 text-right font-mono text-sm">{{ l.subtotal.toLocaleString() }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <div class="flex flex-wrap gap-3">
        <button v-if="canEdit && detail.status === 0" type="button" :disabled="busy" class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white hover:bg-blue-700 disabled:opacity-50" @click="onOrder">発注確定</button>
        <button type="button" :disabled="busy" class="rounded-md border border-gray-300 px-4 py-2 text-sm hover:bg-gray-50 disabled:opacity-50" @click="onExcel">📥 Excel 出力</button>
        <button v-if="canEdit && detail.status !== 9" type="button" :disabled="busy" class="rounded-md border border-orange-300 px-4 py-2 text-sm text-orange-700 hover:bg-orange-50 disabled:opacity-50" @click="onCancel">中止</button>
      </div>
    </template>
  </main>
</template>
