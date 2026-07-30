<script setup lang="ts">
/**
 * 直行/直帰申請パネル（申請タブ）。Iteration 33。
 * - 自分の申請（本人）: 申請モーダル + 自分の申請一覧（多段承認の進捗表示 + 取下げ）。
 * - 承認待ち: オーナーは全件、経路で委任された承認者は自分宛（assigned）の actionable を承認/却下。
 * - 承認済みの日は「打刻を修正」導線を出す（AKO-ATT-005: 親ページの打刻修正モーダルを開く）。
 */
import type { DirectRequestDto, DirectType } from '~/composables/useAttendance'
import {
  APPROVER_TYPE_LABELS, DIRECT_STATUS_CLASSES, DIRECT_STATUS_LABELS, DIRECT_TYPE_LABELS,
  approverTargetLabel,
} from '~/utils/attendance'

const props = defineProps<{ isOwner: boolean; canWrite: boolean; myUserId: string }>()
const emit = defineEmits<{ 'request-fix': [{ date: string; directRequestId: string }] }>()

const { loadDirectRequests, requestDirect, decideDirect } = useAttendance()
const { show } = useSnackbar()

const myRequests = ref<DirectRequestDto[]>([])
const queue = ref<DirectRequestDto[]>([])
const selfError = ref('')
const queueError = ref('')
const busy = ref(false)

const modalOpen = ref(false)
const formError = ref('')
const form = reactive<{ date: string; type: DirectType; reason: string }>({
  date: todayJst(),
  type: 'chokkou',
  reason: '',
})

const TYPE_OPTIONS = (Object.keys(DIRECT_TYPE_LABELS) as DirectType[]).map((value) => ({
  value,
  label: DIRECT_TYPE_LABELS[value],
}))

/** actionable（承認待ち/承認中）か。 */
const isActionable = (r: DirectRequestDto) => r.status === 'pending' || r.status === 'inReview'

const loadSelf = async () => {
  selfError.value = ''
  try {
    myRequests.value = (await loadDirectRequests('self')).items
  } catch (e) {
    selfError.value = getApiErrorMessage(e, '直行/直帰申請の取得に失敗しました')
  }
}

const loadQueue = async () => {
  queueError.value = ''
  try {
    if (props.isOwner) {
      // オーナーは全件の actionable。status は単一指定のため pending と inReview を別々に引いて結合する
      // （status 無指定の scope=all は全履歴を返すため、承認待ちが新しい 200 件に押し出され得る。
      //  打刻修正キューと同じサーバ側フィルタ方式に揃える = 原則3）。
      const [pending, inReview] = await Promise.all([
        loadDirectRequests('all', 'pending'),
        loadDirectRequests('all', 'inReview'),
      ])
      queue.value = [...pending.items, ...inReview.items]
        .sort((a, b) => (a.createdAt < b.createdAt ? 1 : a.createdAt > b.createdAt ? -1 : 0))
    } else {
      // 非オーナーの承認者は assigned（サーバ側で actionable 限定）。
      queue.value = (await loadDirectRequests('assigned')).items
    }
  } catch (e) {
    queueError.value = getApiErrorMessage(e, '承認待ちの取得に失敗しました')
  }
}

const reload = async () => { await Promise.all([loadSelf(), loadQueue()]) }
onMounted(reload)

/** 多段承認の進捗テキスト。 */
const progressText = (r: DirectRequestDto): string => {
  const snap = r.routeSnapshot ?? []
  if (snap.length === 0) return 'オーナー承認'
  const total = snap.length
  if (r.status === 'approved' || r.status === 'rejected' || r.status === 'withdrawn')
    return `${total} ステップ`
  const next = snap[r.currentStep - 1]
  const who = next ? `${APPROVER_TYPE_LABELS[next.approverType]}: ${approverTargetLabel(next)}` : ''
  return `ステップ ${r.currentStep}/${total}${who ? `（承認者 ${who}）` : ''}`
}

const openModal = () => {
  form.date = todayJst()
  form.type = 'chokkou'
  form.reason = ''
  formError.value = ''
  modalOpen.value = true
}
const closeModal = () => { modalOpen.value = false }

const submit = async () => {
  if (busy.value) return
  if (!form.reason.trim()) { formError.value = '申請理由を入力してください'; return }
  busy.value = true
  formError.value = ''
  try {
    await requestDirect({ date: form.date, type: form.type, reason: form.reason.trim() })
    show('直行/直帰を申請しました', 'success')
    modalOpen.value = false
    await reload()
  } catch (e) {
    formError.value = getApiErrorMessage(e, '申請に失敗しました')
  } finally {
    busy.value = false
  }
}

const act = async (r: DirectRequestDto, action: 'approved' | 'rejected' | 'withdrawn', confirmMsg?: string) => {
  if (busy.value) return
  if (confirmMsg && !window.confirm(confirmMsg)) return
  busy.value = true
  try {
    await decideDirect(r.id, action)
    show(action === 'approved' ? '承認しました' : action === 'rejected' ? '却下しました' : '取り下げました', 'success')
    await reload()
  } catch (e) {
    show(getApiErrorMessage(e, '処理に失敗しました'), 'error')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
    <div class="flex flex-wrap items-center gap-2">
      <h2 class="text-sm font-semibold text-gray-800">直行/直帰申請</h2>
      <p class="text-xs text-gray-500">承認された日は、その種別（出勤/退勤）の打刻修正を申請できます。</p>
      <button
        v-if="canWrite"
        type="button"
        class="ml-auto rounded-md bg-blue-600 px-3 py-1 text-sm font-medium text-white hover:bg-blue-700"
        @click="openModal"
      >＋ 直行/直帰を申請</button>
    </div>

    <!-- 承認待ち -->
    <div class="mt-3">
      <h3 class="text-xs font-semibold text-gray-600">承認待ち（{{ queue.length }} 件）</h3>
      <p v-if="queueError" class="mt-1 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{{ queueError }}</p>
      <p v-else-if="queue.length === 0" class="mt-1 text-sm text-gray-400">承認待ちの申請はありません。</p>
      <ul v-else class="mt-1 flex flex-col gap-2">
        <li v-for="r in queue" :key="r.id" class="rounded-md border border-amber-200 bg-amber-50/40 px-2.5 py-2">
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span class="font-medium text-gray-800">{{ r.userName }}</span>
            <span class="font-mono text-sm text-gray-600">{{ bizDateKey(r.date) }}</span>
            <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ DIRECT_TYPE_LABELS[r.type] }}</span>
            <span class="rounded-full px-2 py-0.5 text-xs" :class="DIRECT_STATUS_CLASSES[r.status]">{{ DIRECT_STATUS_LABELS[r.status] }}</span>
            <span class="text-xs text-gray-500">{{ progressText(r) }}</span>
          </div>
          <p class="mt-1 text-sm text-gray-600">理由: {{ r.reason }}</p>
          <div class="mt-1.5 flex flex-wrap items-center gap-2">
            <button type="button" :disabled="busy" class="ml-auto rounded-md bg-green-600 px-3 py-1 text-xs font-medium text-white hover:bg-green-700 disabled:opacity-40" @click="act(r, 'approved')">承認</button>
            <button type="button" :disabled="busy" class="rounded-md border border-red-300 px-3 py-1 text-xs text-red-600 hover:bg-red-50 disabled:opacity-40" @click="act(r, 'rejected', 'この申請を却下しますか？')">却下</button>
          </div>
        </li>
      </ul>
    </div>

    <!-- 自分の申請 -->
    <div class="mt-4">
      <h3 class="text-xs font-semibold text-gray-600">自分の直行/直帰申請</h3>
      <p v-if="selfError" class="mt-1 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{{ selfError }}</p>
      <p v-else-if="myRequests.length === 0" class="mt-1 text-sm text-gray-400">申請はありません。</p>
      <ul v-else class="mt-1 flex flex-col gap-2">
        <li v-for="r in myRequests" :key="r.id" class="rounded-md border border-gray-200 px-2.5 py-2">
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span class="font-mono text-sm text-gray-700">{{ bizDateKey(r.date) }}</span>
            <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ DIRECT_TYPE_LABELS[r.type] }}</span>
            <span class="rounded-full px-2 py-0.5 text-xs" :class="DIRECT_STATUS_CLASSES[r.status]">{{ DIRECT_STATUS_LABELS[r.status] }}</span>
            <span class="text-xs text-gray-500">{{ progressText(r) }}</span>
          </div>
          <p class="mt-1 text-sm text-gray-600">理由: {{ r.reason }}</p>
          <div class="mt-1.5 flex flex-wrap items-center gap-3">
            <button
              v-if="r.status === 'approved' && r.userId === myUserId && canWrite"
              type="button"
              class="text-xs text-blue-600 hover:underline"
              @click="emit('request-fix', { date: r.date, directRequestId: r.id })"
            >この日の打刻を修正</button>
            <button
              v-if="isActionable(r) && r.userId === myUserId"
              type="button"
              :disabled="busy"
              class="text-xs text-gray-500 hover:underline disabled:opacity-40"
              @click="act(r, 'withdrawn', 'この申請を取り下げますか？')"
            >取り下げ</button>
          </div>
        </li>
      </ul>
    </div>

    <!-- 申請モーダル -->
    <Teleport to="body">
      <div
        v-if="modalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="direct-modal-title"
        @click.self="closeModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-md flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="direct-modal-title" class="text-base font-semibold">直行/直帰を申請</h2>
          </header>
          <div class="flex-1 space-y-3 overflow-y-auto px-5 py-3">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">対象日 <span class="text-red-500">*</span></span>
              <input v-model="form.date" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">種別 <span class="text-red-500">*</span></span>
              <AutoComplete
                :model-value="form.type"
                :options="TYPE_OPTIONS"
                :allow-empty="false"
                @update:model-value="(v: string) => form.type = v as DirectType"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">理由 <span class="text-red-500">*</span></span>
              <textarea v-model="form.reason" rows="3" maxlength="512" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" placeholder="直行/直帰の理由"></textarea>
            </label>
            <div v-if="formError" class="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{{ formError }}</div>
          </div>
          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 px-3 py-1.5 text-sm" @click="closeModal">キャンセル</button>
            <button type="button" :disabled="busy" class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-40" @click="submit">
              {{ busy ? '送信中…' : '申請する' }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>
  </section>
</template>
