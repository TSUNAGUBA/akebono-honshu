<script setup lang="ts">
// ドレー代マスタ (Part5)。ドレー代は仕入先ごとの属性 (suppliers.drayage_cost) のため、
// 仕入先一覧をドレー代のみ編集できる専用画面として提供する (既存の仕入先エンドポイントを再利用)。
import type { MasterItem } from '~/composables/useMasters'

const { canEditMaster } = useAuth()
const { list, update } = useMasters()

// 仕入先の拡張列 (SupplierListItem 由来)。MasterItem の index signature 経由で保持される値を明示する。
interface SupplierRow extends MasterItem {
  officialName?: string | null
  countryId?: string
  supplierType?: number
  alertTarget?: number
  currencyCode?: string
  drayageCost?: number | null
}

const suppliers = ref<SupplierRow[]>([])
const edited = ref<Record<string, number | null>>({})
const loading = ref(true)
const savingId = ref<string | null>(null)
const errorMessage = ref('')
const successMessage = ref('')

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    suppliers.value = (await list('suppliers')) as SupplierRow[]
    edited.value = {}
    for (const s of suppliers.value) edited.value[s.id] = s.drayageCost ?? null
  } catch {
    errorMessage.value = '仕入先の取得に失敗しました'
  } finally {
    loading.value = false
  }
}
onMounted(reload)

// 変更があるか (保存ボタンの活性判定)。
const isDirty = (s: SupplierRow): boolean => (edited.value[s.id] ?? null) !== (s.drayageCost ?? null)

const save = async (s: SupplierRow) => {
  if (!canEditMaster.value) return
  savingId.value = s.id
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const raw = edited.value[s.id]
    const drayageCost = raw === null || raw === undefined || Number.isNaN(Number(raw)) ? null : Number(raw)
    // ドレー代のみ更新。他の仕入先属性は現在値をそのまま送る (工場コードは仕入先フォーム同様に既存値を保持)。
    await update('suppliers', s.id, {
      code: s.code,
      name: s.name,
      officialName: s.officialName ?? null,
      countryId: s.countryId,
      supplierType: s.supplierType ?? 0,
      alertTarget: s.alertTarget ?? 0,
      currencyCode: s.currencyCode ?? 'JPY',
      drayageCost,
    })
    s.drayageCost = drayageCost
    edited.value[s.id] = drayageCost
    successMessage.value = `「${s.name}」のドレー代を更新しました`
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, 'ドレー代の更新に失敗しました')
  } finally {
    savingId.value = null
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-lg px-3 py-3">
    <header class="mb-3">
      <div class="text-xs text-gray-500">
        <NuxtLink to="/masters" class="hover:underline">マスタ一覧</NuxtLink>
        <span class="mx-1">/</span>
        <span>ドレー代</span>
      </div>
      <h1 class="text-2xl font-bold">ドレー代マスタ</h1>
      <p class="mt-1 text-sm text-gray-500">
        仕入先ごとのドレー代を管理します。ここで設定した値は商品⑤仕入単価に自動反映されます。
      </p>
    </header>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>

    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

    <div v-else class="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-sm">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-3 py-2 text-left">コード</th>
            <th class="px-3 py-2 text-left">仕入先</th>
            <th class="px-3 py-2 text-left">通貨</th>
            <th class="px-3 py-2 text-right">ドレー代</th>
            <th class="px-3 py-2 text-right"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in suppliers" :key="s.id" class="border-b border-gray-100 last:border-0">
            <td class="whitespace-nowrap px-3 py-2 font-mono text-gray-600">{{ s.code }}</td>
            <td class="px-3 py-2">{{ s.name }}</td>
            <td class="px-3 py-2 font-mono text-gray-600">{{ s.currencyCode ?? 'JPY' }}</td>
            <td class="px-3 py-2 text-right">
              <input
                v-model.number="edited[s.id]"
                type="number"
                min="0"
                step="0.01"
                placeholder="未設定"
                :disabled="!canEditMaster"
                class="w-32 rounded-md border border-gray-300 px-2 py-1 text-right text-sm disabled:bg-gray-50"
              />
            </td>
            <td class="px-3 py-2 text-right">
              <button
                v-if="canEditMaster"
                type="button"
                :disabled="!isDirty(s) || savingId === s.id"
                class="rounded-md bg-blue-600 px-3 py-1 text-xs font-semibold text-white hover:bg-blue-700 disabled:opacity-40"
                @click="save(s)"
              >{{ savingId === s.id ? '保存中…' : '保存' }}</button>
            </td>
          </tr>
          <tr v-if="suppliers.length === 0">
            <td colspan="5" class="px-3 py-6 text-center text-gray-500">仕入先が登録されていません</td>
          </tr>
        </tbody>
      </table>
    </div>

    <p v-if="!canEditMaster" class="mt-3 text-xs text-gray-500">閲覧のみ (編集権限がありません)。</p>
  </main>
</template>
