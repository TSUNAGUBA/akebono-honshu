<script setup lang="ts">
import type { OrderListItem, OrderState } from '~/composables/useOrders'
import { deriveOrderState, orderStateLabel, orderStateBadgeClass } from '~/composables/useOrders'
import type { MasterItem } from '~/composables/useMasters'

const { list, bulkExport, bulkStatus } = useOrders()
const { list: listMaster } = useMasters()
const { apiData } = useApi()
const { user } = useAuth()

const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) === 1)

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
interface UserOption { id: string; loginId: string; displayName: string }
const users = ref<UserOption[]>([])
// 発注者ドロップダウン用 (orders/new.vue と同じ整形)
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))

// --- 発注状態 4 値 (§3b)。発注削除のみ既定 OFF、他 3 つ ON ---
const allStates: OrderState[] = ['notOrdered', 'ordered', 'cancelled', 'deleted']
const defaultStateFilter = (): Record<OrderState, boolean> => ({
  notOrdered: true, ordered: true, cancelled: true, deleted: false,
})

// --- SPLIT フィルタ (AND 合成、状態のみ OR)。products/index.vue のパターンをミラー。 ---
const filters = ref({
  createdFrom: '',                         // date 手入力 (created_at >= From)
  createdTo: '',                           // date 手入力 (created_at <= To)
  supplierId: null as string | null,       // dropdown (発注先=仕入先)
  ordererUserId: null as string | null,    // dropdown (発注者=ユーザ)
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

// 有効なフィルタ項目の件数 (FilterPanel の「絞込中 N」チップ用)。
// 発注状態は既定 (削除のみ OFF) と異なるときのみ 1 件としてカウントする。
const activeFilterCount = computed(() => {
  const f = filters.value
  let n = 0
  if (f.createdFrom !== '') n++
  if (f.createdTo !== '') n++
  if (f.supplierId != null) n++
  if (f.ordererUserId != null) n++
  if (f.undecidedPrice) n++
  if (f.hideLines) n++
  if (f.customer.trim() !== '') n++
  if (!isStateFilterDefault.value) n++
  return n
})

// キーセットページング (AKB-DOC-12 §7.1)。limit=200 で取得し、続きは「さらに読み込む」で辿る。
const nextCursor = ref<string | null>(null)
const loadingMore = ref(false)
// リロード世代。reload 開始でインクリメントし、in-flight の応答が旧世代なら破棄する
// (一括操作後 reload と「さらに読み込む」の競合で、混在リスト・旧カーソルが残るのを防ぐ)。
const requestSeq = ref(0)

const reload = async () => {
  const seq = ++requestSeq.value
  loading.value = true
  errorMessage.value = ''
  try {
    // 発注状態 4 値フィルタ (§3b) のため全状態 (中止/削除含む) を取得し client-side で絞り込む。
    const page = await list(true)
    if (seq !== requestSeq.value) return
    items.value = page.items
    nextCursor.value = page.page.hasMore ? page.page.nextCursor : null
  } catch (e) {
    // 旧世代 (reload で置き換え済み) の失敗は表示を汚染しない
    if (seq !== requestSeq.value) return
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '発注書一覧の取得に失敗しました'
  } finally {
    // 新しい reload が in-flight の場合、旧世代がローディング表示を先に消さない
    if (seq === requestSeq.value) loading.value = false
  }
}

const loadMore = async () => {
  if (!nextCursor.value || loadingMore.value) return
  const seq = requestSeq.value
  loadingMore.value = true
  try {
    const page = await list(true, nextCursor.value)
    if (seq !== requestSeq.value) return
    items.value = [...items.value, ...page.items]
    nextCursor.value = page.page.hasMore ? page.page.nextCursor : null
  } catch (e) {
    // 旧世代 (reload で置き換え済み) の失敗は表示を汚染しない
    if (seq !== requestSeq.value) return
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '発注書一覧の取得に失敗しました'
  } finally {
    // loadingMore は同時実行ガードのため無条件で解除する
    loadingMore.value = false
  }
}

// フィルタ用マスタ・ユーザの読込はドロップダウンの選択肢にのみ使う補助処理。
// 失敗しても一覧本体 (reload) は表示する (原則 4 非ブロッキング)。
const loadFilterSources = async () => {
  try {
    const [sup, usr] = await Promise.all([
      listMaster('suppliers'),
      apiData<UserOption[]>('/users'),
    ])
    suppliers.value = sup
    users.value = usr
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
  deriveOrderState({ status: i.status, orderedAt: i.orderedAt, isDeleted: i.isDeleted })

// 作成日は UTC タイムスタンプ (createdAt、末尾 Z) を JST の YYYY-MM-DD へ変換して date 入力と比較。
// 文字列 slice では UTC 日付になり JST と最大 9 時間ずれるため必ず変換する (AKB-DOC-12)。
const createdDate = (i: OrderListItem): string => (i.createdAt ? toJstDateString(i.createdAt) : '')

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
  const dt = formatJstDate(i.firstExportedAt)
  return { label: `初回出力済 (${dt})`, cls: 'bg-blue-100 text-blue-700' }
}

// ─── 一括ダウンロード (#3b) ───────────────────────────
// チェックした発注を 発注書 / 管理表 / 発注書+管理表 でまとめてダウンロードする。
// 選択は order id の Set。フィルタで非表示になった id は watch で間引く
// (見えていない発注を黙ってダウンロードしないため)。
const selectedIds = ref<Set<string>>(new Set())

// フィルタ変更で filtered から外れた選択を除去する (表示中の発注のみ選択状態を保持)。
watch(filtered, (rows) => {
  const visible = new Set(rows.map((r) => r.id))
  const next = new Set<string>()
  for (const id of selectedIds.value) if (visible.has(id)) next.add(id)
  if (next.size !== selectedIds.value.size) selectedIds.value = next
})

const isSelected = (id: string): boolean => selectedIds.value.has(id)
const toggleRow = (id: string): void => {
  const next = new Set(selectedIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  selectedIds.value = next
}

const selectedCount = computed(() => selectedIds.value.size)

// 全選択チェックボックスの状態 (表示中 filtered 行が基準)。
const allSelected = computed(() =>
  filtered.value.length > 0 && filtered.value.every((i) => selectedIds.value.has(i.id)),
)
const someSelected = computed(() => selectedCount.value > 0 && !allSelected.value)

// 全選択チェックボックスの indeterminate は DOM プロパティのため ref 経由で反映する
// (v-bind の属性では設定できない)。
const selectAllEl = ref<HTMLInputElement | null>(null)
watchEffect(() => {
  if (selectAllEl.value) selectAllEl.value.indeterminate = someSelected.value
})

const toggleSelectAll = (): void => {
  if (allSelected.value) {
    selectedIds.value = new Set()
  } else {
    selectedIds.value = new Set(filtered.value.map((i) => i.id))
  }
}

const clearSelection = (): void => {
  selectedIds.value = new Set()
}

type BulkFormat = 'order' | 'management' | 'both'
const downloading = ref<BulkFormat | null>(null)
const bulkError = ref('')

const runBulkExport = async (format: BulkFormat): Promise<void> => {
  if (downloading.value || selectedCount.value === 0) return
  bulkError.value = ''
  downloading.value = format
  try {
    // 表示順 (filtered) で id を渡す。ダウンロード成功後に選択をクリアする。
    const ids = filtered.value.filter((i) => selectedIds.value.has(i.id)).map((i) => i.id)
    await bulkExport(ids, format)
    clearSelection()
  } catch (e) {
    console.error('一括ダウンロードに失敗しました', e)
    const err = e as { statusCode?: number }
    bulkError.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '一括ダウンロードに失敗しました'
  } finally {
    downloading.value = null
  }
}

// ─── 発注状態の一括変更 (§3c) ───────────────────────────
// チェックした発注を 未発注/発注済/発注中止/発注削除 へ一括変更する。ダウンロードとは独立したユーザー操作。
// 終端状態で変更できない発注はサーバ側でスキップされ、updated/skipped を通知する (原則4 非ブロッキング)。
const changingStatus = ref<OrderState | null>(null)
const statusNotice = ref('')

const runBulkStatus = async (target: OrderState): Promise<void> => {
  if (changingStatus.value || selectedCount.value === 0) return
  let cancelReason: string | undefined
  // 発注中止は理由を任意入力、発注削除は確認ダイアログを挟む (誤操作防止、原則2 状態保護)。
  if (target === 'cancelled') {
    const r = window.prompt(`${selectedCount.value} 件を発注中止にします。中止理由 (任意):`, '一括変更による中止')
    if (r === null) return
    cancelReason = r.trim() || undefined
  } else if (target === 'deleted') {
    if (!window.confirm(`${selectedCount.value} 件を発注削除 (論理削除) しますか？`)) return
  }
  statusNotice.value = ''
  bulkError.value = ''
  changingStatus.value = target
  try {
    // 表示順 (filtered) で id を渡す。
    const ids = filtered.value.filter((i) => selectedIds.value.has(i.id)).map((i) => i.id)
    const res = await bulkStatus(ids, target, cancelReason)
    await reload()
    clearSelection()
    statusNotice.value = res.skipped > 0
      ? `${res.updated} 件を「${orderStateLabel(target)}」に変更しました (${res.skipped} 件は終端状態のためスキップ)`
      : `${res.updated} 件を「${orderStateLabel(target)}」に変更しました`
  } catch (e) {
    const err = e as { statusCode?: number }
    bulkError.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : '発注状態の一括変更に失敗しました'
  } finally {
    changingStatus.value = null
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-3 py-3">
    <header class="mb-4 flex items-start justify-between gap-4">
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
         開閉可能 (FilterPanel)。dropdown は MasterSelect (uuid 文字列 ID・allow-empty)、date/text は手入力。
         products/index.vue のパネルをミラー。レスポンシブグリッド: モバイル 1 列 → sm 2 列 → lg 4 列。 -->
    <FilterPanel
      title="絞込"
      storage-key="filters:orders"
      :active-count="activeFilterCount"
    >
      <template #actions>
        <button
          type="button"
          class="rounded-md border border-gray-300 bg-white px-3 py-1 text-xs text-gray-600 hover:bg-gray-50 disabled:opacity-40"
          :disabled="!hasActiveFilter"
          @click="clearFilters"
        >
          クリア
        </button>
      </template>
      <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
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
    </FilterPanel>

    <div class="mb-3 flex items-center gap-4">
      <span class="ml-auto text-xs text-gray-500">{{ filtered.length }} 件{{ nextCursor ? ' (続きあり)' : '' }}</span>
    </div>

    <!-- 一括操作バー (§3a)。常時表示し、選択の有無でボタンの活性/非活性を制御する。
         左: 選択状態 + 発注状態の一括変更 (§3c) / 右: 一括ダウンロード (§3d 1ファイルは単一DL)。モバイルは縦積み。 -->
    <div class="mb-3 rounded-lg border border-gray-200 bg-gray-50 p-3 shadow-sm">
      <div class="mb-2 flex items-center gap-3 text-sm">
        <span class="font-semibold" :class="selectedCount > 0 ? 'text-blue-800' : 'text-gray-500'">
          {{ selectedCount > 0 ? `${selectedCount} 件選択中` : 'チェックボックスで発注を選択してください' }}
        </span>
        <button
          type="button"
          class="rounded-md border border-gray-300 bg-white px-2.5 py-1 text-xs text-gray-600 hover:bg-gray-100 disabled:opacity-40"
          :disabled="selectedCount === 0"
          @click="clearSelection"
        >
          選択解除
        </button>
      </div>
      <div class="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <!-- 発注状態の一括変更 (§3c) -->
        <div class="flex flex-col gap-1.5 sm:flex-row sm:items-center">
          <span class="text-xs font-medium text-gray-600 sm:mr-1">状態を一括変更</span>
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-100 disabled:opacity-40"
              :disabled="selectedCount === 0 || changingStatus !== null"
              @click="runBulkStatus('notOrdered')"
            >{{ changingStatus === 'notOrdered' ? '変更中…' : '未発注に戻す' }}</button>
            <button
              type="button"
              class="rounded-md border border-green-300 bg-white px-3 py-1.5 text-sm font-medium text-green-700 hover:bg-green-50 disabled:opacity-40"
              :disabled="selectedCount === 0 || changingStatus !== null"
              @click="runBulkStatus('ordered')"
            >{{ changingStatus === 'ordered' ? '変更中…' : '発注済にする' }}</button>
            <button
              type="button"
              class="rounded-md border border-orange-300 bg-white px-3 py-1.5 text-sm font-medium text-orange-700 hover:bg-orange-50 disabled:opacity-40"
              :disabled="selectedCount === 0 || changingStatus !== null"
              @click="runBulkStatus('cancelled')"
            >{{ changingStatus === 'cancelled' ? '変更中…' : '発注中止' }}</button>
            <button
              type="button"
              class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-40"
              :disabled="selectedCount === 0 || changingStatus !== null"
              @click="runBulkStatus('deleted')"
            >{{ changingStatus === 'deleted' ? '変更中…' : '発注削除' }}</button>
          </div>
        </div>
        <!-- 一括ダウンロード (§3d) -->
        <div class="flex flex-col gap-1.5 sm:flex-row sm:items-center">
          <span class="text-xs font-medium text-gray-600 sm:mr-1">一括ダウンロード</span>
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-blue-700 disabled:opacity-40"
              :disabled="selectedCount === 0 || downloading !== null"
              @click="runBulkExport('order')"
            >
              {{ downloading === 'order' ? '生成中…' : '発注書' }}
            </button>
            <button
              type="button"
              class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-blue-700 disabled:opacity-40"
              :disabled="selectedCount === 0 || downloading !== null"
              @click="runBulkExport('management')"
            >
              {{ downloading === 'management' ? '生成中…' : '管理表' }}
            </button>
            <button
              type="button"
              class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-blue-700 disabled:opacity-40"
              :disabled="selectedCount === 0 || downloading !== null"
              @click="runBulkExport('both')"
            >
              {{ downloading === 'both' ? '生成中…' : '発注書+管理表' }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="statusNotice" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
      {{ statusNotice }}
    </div>

    <div v-if="bulkError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
      {{ bulkError }}
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
            <!-- 一括ダウンロード選択 (#3b)。全選択は表示中 (filtered) の行が対象。 -->
            <th class="w-10 px-3 py-3 text-center">
              <input
                ref="selectAllEl"
                type="checkbox"
                class="h-4 w-4 rounded border-gray-300"
                :checked="allSelected"
                :disabled="filtered.length === 0"
                aria-label="表示中の発注をすべて選択"
                @change="toggleSelectAll"
              />
            </th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">管理番号</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">区分</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">発注番号</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">仕入先</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">納品先</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">納入日</th>
            <!-- 明細列: 明細非表示フィルタ ON で列ごと隠す (後述) -->
            <th v-if="!filters.hideLines" class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">明細</th>
            <th class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">合計</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">出力</th>
            <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="filtered.length === 0">
            <td :colspan="filters.hideLines ? 10 : 11" class="px-4 py-8 text-center text-sm text-gray-500">
              {{ hasActiveFilter ? '検索条件に一致するデータがありません' : 'データがありません' }}
            </td>
          </tr>
          <tr
            v-for="i in filtered"
            :key="i.id"
            class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50"
            :class="isSelected(i.id) ? 'bg-blue-50' : ''"
            @click="navigateTo(`/orders/${i.id}`)"
          >
            <!-- チェックボックスセル: 行ナビゲーションを止めて選択のみ行う。 -->
            <td class="w-10 px-3 py-3 text-center" @click.stop>
              <input
                type="checkbox"
                class="h-4 w-4 rounded border-gray-300"
                :checked="isSelected(i.id)"
                :aria-label="`発注 ${i.mgmtNo} を選択`"
                @change="toggleRow(i.id)"
              />
            </td>
            <td class="px-3 py-2 font-mono text-sm">{{ i.mgmtNo }}</td>
            <td class="px-3 py-2 text-sm">
              <span :class="regionBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs font-medium">
                {{ regionBadge(i).label }}
              </span>
            </td>
            <td class="px-3 py-2 font-mono text-sm">{{ i.orderNo ?? '—' }}</td>
            <td class="px-3 py-2 text-sm">
              <div class="font-medium">{{ i.supplierName }}</div>
              <div class="font-mono text-xs text-gray-500">{{ i.supplierCode }}</div>
            </td>
            <td class="px-3 py-2 text-sm">{{ i.deliveryDestinationName }}</td>
            <td class="px-3 py-2 text-sm">{{ i.dueDate }}</td>
            <td v-if="!filters.hideLines" class="px-3 py-2 text-right font-mono text-sm">{{ i.lineCount }}</td>
            <td class="px-3 py-2 text-right font-mono text-sm">
              {{ i.currencyCode }} {{ i.totalAmount.toLocaleString() }}
            </td>
            <td class="px-3 py-2 text-sm">
              <span :class="exportBadge(i).cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
                {{ exportBadge(i).label }}
              </span>
            </td>
            <td class="px-3 py-2 text-sm">
              <span :class="orderStateBadgeClass(stateOf(i))" class="inline-block rounded-full px-2 py-0.5 text-xs font-medium">
                {{ stateLabel(stateOf(i)) }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <div v-if="nextCursor && !loading" class="mt-3 text-center">
      <button type="button" :disabled="loadingMore" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm text-gray-700 shadow-sm hover:bg-gray-50 disabled:opacity-50" @click="loadMore">
        {{ loadingMore ? '読み込み中…' : 'さらに読み込む' }}
      </button>
    </div>

    <p class="mt-3 text-xs text-gray-400">API: GET /api/maker/v1/orders</p>
  </main>
</template>
