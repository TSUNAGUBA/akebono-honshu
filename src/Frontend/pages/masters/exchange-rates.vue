<script setup lang="ts">
// 為替マスタ (§2f)。年月 (YYYY-MM) × 通貨ごとの対円レートを CRUD する専用ページ。
// code/name を持たない bespoke master のため、汎用 [master].vue ではなく専用実装 (仕入先とは別ページ)。
const { canEditMaster } = useAuth()
const { apiFetch, apiData } = useApi()

interface ExchangeRateItem {
  // 第二段階契約: エンティティ ID は uuid 文字列
  id: string
  yearMonth: string
  currencyCode: string
  rate: number
  deletedAt: string | null
  createdAt: string
  updatedAt: string
}
interface CurrencyItem { id: string; code: string; name: string }

const items = ref<ExchangeRateItem[]>([])
// 通貨マスタ (対象通貨のドロップダウン用。JPY を除いた外貨のみ選択肢に出す)。
const currencies = ref<CurrencyItem[]>([])
const foreignCurrencies = computed(() => currencies.value.filter((c) => c.code !== 'JPY'))
const currencyName = (code: string) => currencies.value.find((c) => c.code === code)?.name ?? ''
const loading = ref(true)
const includeDeleted = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const submitting = ref(false)

// 追加/編集フォーム。editingId=null は新規、非 null は該当 id の編集。
const nowMonth = currentMonthJst() // 'YYYY-MM' (JST 基準。UTC 由来だと月初 09:00 まで前月になる)
const editingId = ref<string | null>(null)
const form = ref({ yearMonth: nowMonth, currencyCode: '', rate: null as number | null })

const resetForm = () => {
  editingId.value = null
  form.value = { yearMonth: nowMonth, currencyCode: foreignCurrencies.value[0]?.code ?? '', rate: null }
}

// 通貨マスタは対象通貨ドロップダウン用の補助データ。失敗しても本体は使える (原則4 非ブロッキング)。
const loadCurrencies = async () => {
  try {
    currencies.value = await apiData<CurrencyItem[]>('/masters/currencies?includeDeleted=false')
    if (form.value.currencyCode === '' && foreignCurrencies.value.length) {
      form.value.currencyCode = foreignCurrencies.value[0].code
    }
  } catch {
    currencies.value = []
  }
}

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    items.value = await apiData<ExchangeRateItem[]>(
      `/masters/exchange-rates?includeDeleted=${includeDeleted.value}`,
    )
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '為替マスタの取得に失敗しました'
  } finally {
    loading.value = false
  }
}

watch(includeDeleted, reload)
onMounted(() => { reload(); loadCurrencies() })

const startEdit = (item: ExchangeRateItem) => {
  editingId.value = item.id
  form.value = { yearMonth: item.yearMonth, currencyCode: item.currencyCode, rate: item.rate }
  successMessage.value = ''
  errorMessage.value = ''
}

const onSubmit = async () => {
  errorMessage.value = ''
  successMessage.value = ''
  if (!/^\d{4}-\d{2}$/.test(form.value.yearMonth.trim())) {
    errorMessage.value = '年月は YYYY-MM 形式で入力してください'
    return
  }
  if (form.value.currencyCode.trim().length !== 3) {
    errorMessage.value = '通貨を選択してください'
    return
  }
  if (form.value.rate == null || form.value.rate <= 0) {
    errorMessage.value = 'レートは正の数で入力してください'
    return
  }
  submitting.value = true
  try {
    const payload = {
      yearMonth: form.value.yearMonth.trim(),
      currencyCode: form.value.currencyCode.trim().toUpperCase(),
      rate: Number(form.value.rate),
    }
    if (editingId.value == null) {
      await apiFetch(`/masters/exchange-rates`, { method: 'POST', body: payload })
      successMessage.value = '為替レートを登録しました'
    } else {
      await apiFetch(`/masters/exchange-rates/${editingId.value}`, { method: 'PATCH', body: payload })
      successMessage.value = '為替レートを更新しました'
    }
    resetForm()
    await reload()
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '保存に失敗しました')
  } finally {
    submitting.value = false
  }
}

const onDelete = async (item: ExchangeRateItem) => {
  if (!window.confirm(`${item.yearMonth} ${item.currencyCode} を削除しますか？`)) return
  errorMessage.value = ''
  try {
    await apiFetch(`/masters/exchange-rates/${item.id}`, { method: 'DELETE' })
    await reload()
  } catch {
    errorMessage.value = '削除に失敗しました'
  }
}

const onRestore = async (item: ExchangeRateItem) => {
  errorMessage.value = ''
  try {
    await apiFetch(`/masters/exchange-rates/${item.id}/restore`, { method: 'POST' })
    await reload()
  } catch {
    errorMessage.value = '復元に失敗しました'
  }
}
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-4">
    <header class="mb-4">
      <div class="text-xs text-gray-500">
        <NuxtLink to="/masters" class="hover:underline">マスタ一覧</NuxtLink>
        <span class="mx-1">/</span>
        <span>為替マスタ</span>
      </div>
      <h1 class="text-2xl font-bold">為替マスタ</h1>
      <p class="mt-1 text-sm text-gray-500">
        年月 (YYYY-MM) × 通貨ごとの対円レートを登録します。商品⑤仕入単価では、選択した仕入先の適用通貨で
        この為替レートを解決し、円換算価格を表示します (§2f)。
      </p>
    </header>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>

    <!-- 追加/編集フォーム (編集権限がある場合のみ) -->
    <section v-if="canEditMaster" class="mb-5 rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
      <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">{{ editingId == null ? '為替レートを追加' : '為替レートを編集' }}</h2>
      <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-4 lg:items-end">
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">年月 <span class="text-red-500">*</span></span>
          <input v-model="form.yearMonth" type="month" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">通貨 <span class="text-red-500">*</span></span>
          <!-- 通貨マスタから選択 (自由入力を廃止し文字列一致の整合性を担保)。JPY は円のため対象外。 -->
          <select v-model="form.currencyCode" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm">
            <option value="">（選択）</option>
            <option v-for="c in foreignCurrencies" :key="c.id" :value="c.code">{{ c.code }} — {{ c.name }}</option>
          </select>
          <span v-if="foreignCurrencies.length === 0" class="text-xs text-amber-600">
            通貨マスタに外貨が未登録です。<NuxtLink to="/masters/currencies" class="text-blue-600 hover:underline">通貨マスタ</NuxtLink>で登録してください。
          </span>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">対円レート <span class="text-red-500">*</span></span>
          <input v-model.number="form.rate" type="number" min="0" step="0.0001" placeholder="例: 150.0000" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
        </label>
        <div class="flex gap-2">
          <button
            type="button"
            :disabled="submitting"
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
            @click="onSubmit"
          >{{ submitting ? '保存中…' : (editingId == null ? '追加' : '更新') }}</button>
          <button
            v-if="editingId != null"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm hover:bg-gray-50"
            @click="resetForm"
          >キャンセル</button>
        </div>
      </div>
    </section>

    <div class="mb-3 flex items-center justify-between">
      <label class="inline-flex items-center gap-2 text-sm">
        <input v-model="includeDeleted" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
        <span>削除済みも表示</span>
      </label>
      <span class="text-xs text-gray-500">{{ items.length }} 件</span>
    </div>

    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>
    <section v-else class="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-sm">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">年月</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">通貨</th>
            <th class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">対円レート</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
            <th v-if="canEditMaster" class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="items.length === 0">
            <td :colspan="canEditMaster ? 5 : 4" class="px-4 py-8 text-center text-sm text-gray-500">データがありません</td>
          </tr>
          <tr v-for="i in items" :key="i.id" class="border-b border-gray-100 last:border-0" :class="i.deletedAt ? 'opacity-50' : ''">
            <td class="px-3 py-2 font-mono">{{ i.yearMonth }}</td>
            <td class="px-3 py-2"><span class="font-mono">{{ i.currencyCode }}</span> <span class="text-xs text-gray-500">{{ currencyName(i.currencyCode) }}</span></td>
            <td class="px-3 py-2 text-right font-mono">{{ i.rate.toLocaleString(undefined, { minimumFractionDigits: 2 }) }}</td>
            <td class="px-3 py-2">
              <span v-if="i.deletedAt" class="rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-700">削除済</span>
              <span v-else class="rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700">有効</span>
            </td>
            <td v-if="canEditMaster" class="px-3 py-2 text-right">
              <div class="flex justify-end gap-2">
                <button v-if="!i.deletedAt" type="button" class="text-xs text-blue-600 hover:underline" @click="startEdit(i)">編集</button>
                <button v-if="!i.deletedAt" type="button" class="text-xs text-red-600 hover:underline" @click="onDelete(i)">削除</button>
                <button v-else type="button" class="text-xs text-blue-600 hover:underline" @click="onRestore(i)">復元</button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
  </main>
</template>
