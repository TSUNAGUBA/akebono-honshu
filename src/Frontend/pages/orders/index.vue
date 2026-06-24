<script setup lang="ts">
import type { OrderListItem, OrderState } from '~/composables/useOrders'
import { deriveOrderState, orderStateLabel, orderStateBadgeClass } from '~/composables/useOrders'
import type { MasterItem } from '~/composables/useMasters'

const { list } = useOrders()
const { list: listMaster } = useMasters()
const { apiFetch } = useApi()
const { user } = useAuth()

const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)

const items = ref<OrderListItem[]>([])
const loading = ref(true)
const errorMessage = ref('')

// 発注区分タブ (全件 / 国内 / 海外、is_overseas でクライアント側絞込)
type RegionTab = 'all' | 'domestic' | 'overseas'
const regionTab = ref<RegionTab>('all')
const regionTabs: { key: RegionTab; label: string }[] = [
  { key: 'all', label: '全件' },
  { key: 'domestic', label: '国内' },
  { key: 'overseas', label: '海外' },
]

// --- フィルタ用マスタ参照 (発注先=仕入先 / 発注者=ユーザ) ---
const suppliers = ref<MasterItem[]>([])
interface UserOption { id: number; loginId: string; displayName: string }
const users = ref<UserOption[]>([])
// 発注者ドロップダウン用 (orders/new.vue と同じ整形)
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))

// --- 発注状態 5 値 (発注削除のみ既定 OFF、他 4 つ ON) ---
const allStates: OrderState[] = ['requested', 'ordered', 'cancelled', 'delivered', 'deleted']
const defaultStateFilter = (): Record<OrderState, boolean> => ({
  requested: true, ordered: true, cancelled: true, delivered: true, deleted: false,
})

// --- SPLIT フィルタ (AND 合成、状態のみ OR)。products/index.vue のパターンをミラー。 ---
const filters = ref({
  createdFrom: '',                         // date 手入力 (created_at >= From)
  createdTo: '',                           // date 手入力 (created_at <= To)
  supplierId: null as number | null,       // dropdown (発注先=仕入先)
  ordererUserId: null as number | null,    // dropdown (発注者=ユーザ)
  undecidedPrice: false,                   // checkbox (単価未決定を含む発注のみ)
  hideLines: false,                        // checkbox (明細列を隠す表示トグル、後述)
  customer: '',                            // text 手入力 (得意先 部分一致)
  states: defaultStateFilter(),            // checkbox×5 (発注状態、OR 合成)
})

const clearFilters = () => {
  filters.value = {
    createdFrom: '',
    createdTo: '',
    supplierId: null,
    ordererUserId: null,
    undecidedPrice: false,
    hideLines: false,
    customer: '',
    states: defaultStateFilter(),
  }
}

// 状態フィルタが既定 (発注削除のみ OFF) と異なるか。クリアボタンの活性判定に使う。
const isStateFilterDefault = computed(() => {
  const d = defaultStateFilter()
  return allStates.every((s) => filters.value.states[s] === d[s])
})

const hasActiveFilter = computed(() =>
  filters.value.createdFrom !== '' ||
  filters.value.createdTo !== '' ||
  filters.value.supplierId != null ||
  filters.value.ordererUserId != null ||
  filters.value.undecidedPrice ||
  filters.value.hideLines ||
  filters.value.customer.trim() !== '' ||
  !isStateFilterDefault.value,
)

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    // 発注状態 5 値フィルタ (#3a) のため全状態 (中止/納品完了/削除含む) を取得し client-side で絞り込む。
    items.value = await list(true)
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '発注書一覧の取得に失敗しました'
  } finally {
    loading.value = false
  }
}

// フィルタ用マスタ・ユーザの読込はドロップダウンの選択肢にのみ使う補助処理。
// 失敗しても一覧本体 (reload) は表示する (原則 4 非ブロッキング)。
const loadFilterSources = async () => {
  try {
    const [sup, usr] = await Promise.all([
      listMaster('suppliers'),
      apiFetch<{ data: UserOption[] }>('/users'),
    ])
    suppliers.value = sup
    users.value = usr.data
  } catch (e) {
    console.error('フィルタ選択肢 (仕入先/ユーザ) の取得に失敗しました', e)
  }
}

onMounted(() => {
  reload()
  loadFilterSources()
})

// 部分一致用に正規化 (大小文字を無視)。
const inc = (haystack: string | null | undefined, needle: string): boolean =>
  (haystack ?? '').toLowerCase().includes(needle.trim().toLowerCase())

const stateOf = (i: OrderListItem): OrderState =>
  deriveOrderState({ status: i.status, firstExportedAt: i.firstExportedAt, deliveredAt: i.deliveredAt, isDeleted: i.isDeleted })

// 作成日は ISO 文字列 (createdAt) の日付部分を YYYY-MM-DD で取り出して date 入力と比較。
const createdDate = (i: OrderListItem): string => (i.createdAt ?? '').slice(0, 10)

const filtered = computed(() => {
  const f = filters.value
  const custQ = f.customer.trim()

  // まず発注区分タブ (国内/海外) で絞込
  let base = items.value
  if (regionTab.value === 'domestic') base = base.filter((i) => !i.isOverseas)
  else if (regionTab.value === 'overseas') base = base.filter((i) => i.isOverseas)

  return base.filter((i) => {
    // 作成日 From/To (date 文字列の辞書順比較で日付比較可能)
    if (f.createdFrom !== '' && createdDate(i) < f.createdFrom) return false
    if (f.createdTo !== '' && createdDate(i) > f.createdTo) return false
    // 発注先 / 発注者 (ID 一致)
    if (f.supplierId != null && i.supplierId !== f.supplierId) return false
    if (f.ordererUserId != null && i.ordererUserId !== f.ordererUserId) return false
    // 仕入単価未決定を含む発注のみ
    if (f.undecidedPrice && !i.hasUndecidedPrice) return false
    // 得意先 部分一致
    if (custQ !== '' && !inc(i.customerName, custQ)) return false
    // 発注状態 (選択された状態のいずれかに該当 = OR)
    if (!f.states[stateOf(i)]) return false
    return true
  })
})

// 各区分タブの件数 (状態フィルタ適用後、区分のみで再集計しタブラベルに表示)
const stateFilteredItems = computed(() => {
  const f = filters.value
  const custQ = f.customer.trim()
  return items.value.filter((i) => {
    if (f.createdFrom !== '' && createdDate(i) < f.createdFrom) return false
    if (f.createdTo !== '' && createdDate(i) > f.createdTo) return false
    if (f.supplierId != null && i.supplierId !== f.supplierId) return false
    if (f.ordererUserId != null && i.ordererUserId !== f.ordererUserId) return false
    if (f.undecidedPrice && !i.hasUndecidedPrice) return false
    if (custQ !== '' && !inc(i.customerName, custQ)) return false
    if (!f.states[stateOf(i)]) return false
    return true
  })
})
const regionCount = (key: RegionTab): number => {
  if (key === 'domestic') return stateFilteredItems.value.filter((i) => !i.isOverseas).length
  if (key === 'overseas') return stateFilteredItems.value.filter((i) => i.isOverseas).length
  return stateFilteredItems.value.length
}

const stateLabel = (s: OrderState) => orderStateLabel(s)

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
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-8">
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

    <!-- 絞込フィルタパネル (SPLIT フィルタ、AND 合成・状態のみ OR、未指定 = 全件)。
         dropdown は MasterSelect (数値 ID・allow-empty)、date/text は手入力。
         products/index.vue のパネルをミラー。レスポンシブグリッド: モバイル 1 列 → sm 2 列 → lg 4 列。 -->
    <section class="mb-4 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
        <h2 class="text-sm font-semibold text-gray-700">絞込</h2>
        <button
          type="button"
          class="rounded-md border border-gray-300 bg-white px-3 py-1 text-xs text-gray-600 hover:bg-gray-50 disabled:opacity-40"
          :disabled="!hasActiveFilter"
          @click="clearFilters"
        >
          クリア
        </button>
      </div>
      <div class="grid grid-cols-1 gap-x-4 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <label class="flex flex-col gap-1">
          <span class="font-medium">作成日 From</span>
          <input
            v-model="filters.createdFrom"
            type="date"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">作成日 To</span>
          <input
            v-model="filters.createdTo"
            type="date"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">発注先</span>
          <MasterSelect
            v-model="filters.supplierId"
            :items="suppliers"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">発注者</span>
          <MasterSelect
            v-model="filters.ordererUserId"
            :items="userOptions"
            allow-empty
            empty-label="（すべて）"
            placeholder="（すべて）"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">得意先</span>
          <input
            v-model="filters.customer"
            type="text"
            placeholder="得意先で部分一致"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <label class="flex items-center gap-2 sm:mt-6">
          <input v-model="filters.undecidedPrice" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
          <span class="font-medium">仕入単価未決定を含む</span>
        </label>
        <label class="flex items-center gap-2 sm:mt-6">
          <input v-model="filters.hideLines" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
          <span class="font-medium">明細非表示</span>
        </label>
      </div>

      <!-- 発注状態 (checkbox×5、OR 合成。既定: 発注削除のみ OFF) -->
      <div class="mt-3 border-t border-gray-100 pt-3">
        <span class="mb-2 block text-sm font-medium text-gray-700">発注状態</span>
        <div class="flex flex-wrap gap-x-4 gap-y-2 text-sm">
          <label v-for="s in allStates" :key="s" class="inline-flex items-center gap-1.5">
            <input v-model="filters.states[s]" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
            <span
              :class="orderStateBadgeClass(s)"
              class="inline-block rounded-full px-2 py-0.5 text-xs font-medium"
            >{{ stateLabel(s) }}</span>
          </label>
        </div>
      </div>
    </section>

    <div class="mb-3 flex items-center gap-4">
      <span class="ml-auto text-xs text-gray-500">{{ filtered.length }} 件</span>
    </div>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
      {{ errorMessage }}
    </div>

    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">
      読み込み中…
    </section>

    <section v-else class="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">管理番号</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">区分</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">発注番号</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">仕入先</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">納品先</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">納入日</th>
            <!-- 明細列: 明細非表示フィルタ ON で列ごと隠す (後述) -->
            <th v-if="!filters.hideLines" class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">明細</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">合計</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">出力</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0">
            <td :colspan="filters.hideLines ? 9 : 10" class="px-4 py-8 text-center text-sm text-gray-500">
              {{ hasActiveFilter ? '検索条件に一致するデータがありません' : 'データがありません' }}
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
            <td v-if="!filters.hideLines" class="px-4 py-3 text-right font-mono text-sm">{{ i.lineCount }}</td>
            <td class="px-4 py-3 text-right font-mono text-sm">
              {{ i.currencyCode }} {{ i.totalAmount.toLocaleString() }}
            </td>
            <td class="px-4 py-3 text-sm">
              <span :class="exportBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
                {{ exportBadge(i).label }}
              </span>
            </td>
            <td class="px-4 py-3 text-sm">
              <span :class="orderStateBadgeClass(stateOf(i))" class="inline-block rounded-full px-2 py-0.5 text-xs font-medium">
                {{ stateLabel(stateOf(i)) }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <p class="mt-3 text-xs text-gray-400">API: GET /api/v1/orders</p>
  </main>
</template>
