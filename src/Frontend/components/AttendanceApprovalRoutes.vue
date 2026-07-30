<script setup lang="ts">
/**
 * 勤怠承認経路の管理（設定タブ・オーナー専用）。Iteration 33。
 * 区分（直行/直帰・打刻修正）ごとに承認経路を登録し、各ステップの承認者を 役職 / ロール / 個人 から選ぶ。
 * 論理削除 + 復元に対応（誤操作の取消導線・原則: 操作の取消可能性）。
 */
import type {
  ApproverStepInput, AttendanceRouteCategory, AttendanceRouteDto,
} from '~/composables/useAttendance'
import {
  APPROVER_TYPE_LABELS, ROUTE_CATEGORY_LABELS, approverTargetLabel,
} from '~/utils/attendance'

interface MemberOption { id: string; displayName: string; loginId: string; title?: string | null }

const props = defineProps<{ members: MemberOption[]; isOwner: boolean }>()

const {
  attendanceRoutes, createAttendanceRoute, updateAttendanceRoute,
  deleteAttendanceRoute, restoreAttendanceRoute,
} = useAttendance()
const { show } = useSnackbar()

const routes = ref<AttendanceRouteDto[]>([])
const loading = ref(false)
const loadError = ref('')
const includeInactive = ref(false)

const modalOpen = ref(false)
const editingId = ref<string | null>(null)
const busy = ref(false)
const formError = ref('')
const form = reactive<{ category: AttendanceRouteCategory; isActive: boolean; steps: ApproverStepInput[] }>({
  category: 'direct',
  isActive: true,
  steps: [],
})

const CATEGORY_OPTIONS = (Object.keys(ROUTE_CATEGORY_LABELS) as AttendanceRouteCategory[]).map((value) => ({
  value,
  label: ROUTE_CATEGORY_LABELS[value],
}))

const memberOptions = computed(() =>
  props.members.map((m) => ({ id: m.id, name: m.displayName, code: m.loginId })))
const titleOptions = computed(() =>
  [...new Set(props.members.map((m) => m.title).filter((t): t is string => !!t))].sort())

const load = async () => {
  if (!props.isOwner) return
  loading.value = true
  loadError.value = ''
  try {
    routes.value = await attendanceRoutes(includeInactive.value)
  } catch (e) {
    loadError.value = getApiErrorMessage(e, '承認経路の取得に失敗しました')
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(includeInactive, load)

const stepsSummary = (route: AttendanceRouteDto): string =>
  route.steps.length === 0
    ? '（未設定）'
    : route.steps
        .map((s, i) => `${i + 1}. ${APPROVER_TYPE_LABELS[s.approverType]}: ${approverTargetLabel(s)}`)
        .join(' → ')

const openCreate = () => {
  editingId.value = null
  form.category = 'direct'
  form.isActive = true
  form.steps = [{ order: 1, approverType: 'member', approverUserId: null }]
  formError.value = ''
  modalOpen.value = true
}

const openEdit = (route: AttendanceRouteDto) => {
  editingId.value = route.id
  form.category = route.category
  form.isActive = route.isActive
  form.steps = route.steps.map((s) => ({
    order: s.order,
    approverType: s.approverType,
    approverRole: s.approverRole,
    approverTitle: s.approverTitle,
    approverUserId: s.approverUserId,
    mode: s.mode,
  }))
  formError.value = ''
  modalOpen.value = true
}

const closeModal = () => { modalOpen.value = false }

/** クライアント側の事前検証（サーバの検証と同旨。送信前に分かりやすく弾く）。 */
const validate = (): string | null => {
  if (form.steps.length === 0) return '承認ステップを 1 つ以上設定してください'
  for (const [i, s] of form.steps.entries()) {
    if (s.approverType === 'title' && !(s.approverTitle ?? '').trim())
      return `ステップ ${i + 1}: 役職を入力してください`
    if (s.approverType === 'member' && !s.approverUserId)
      return `ステップ ${i + 1}: 承認者（個人）を選択してください`
  }
  return null
}

const submit = async () => {
  if (busy.value) return
  const err = validate()
  if (err) { formError.value = err; return }
  busy.value = true
  formError.value = ''
  try {
    if (editingId.value) {
      await updateAttendanceRoute(editingId.value, { steps: form.steps, isActive: form.isActive })
      show('承認経路を更新しました', 'success')
    } else {
      await createAttendanceRoute({ category: form.category, steps: form.steps, isActive: form.isActive })
      show('承認経路を登録しました', 'success')
    }
    modalOpen.value = false
    await load()
  } catch (e) {
    formError.value = getApiErrorMessage(e, '保存に失敗しました')
  } finally {
    busy.value = false
  }
}

const remove = async (route: AttendanceRouteDto) => {
  if (busy.value) return
  if (!window.confirm(`${ROUTE_CATEGORY_LABELS[route.category]} の承認経路を削除しますか？（復元できます）`)) return
  busy.value = true
  try {
    await deleteAttendanceRoute(route.id)
    show('承認経路を削除しました', 'success')
    await load()
  } catch (e) {
    show(getApiErrorMessage(e, '削除に失敗しました'), 'error')
  } finally {
    busy.value = false
  }
}

const restore = async (route: AttendanceRouteDto) => {
  if (busy.value) return
  busy.value = true
  try {
    await restoreAttendanceRoute(route.id)
    show('承認経路を復元しました', 'success')
    await load()
  } catch (e) {
    show(getApiErrorMessage(e, '復元に失敗しました'), 'error')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
    <div class="flex flex-wrap items-center gap-2">
      <h2 class="text-sm font-semibold text-gray-800">承認経路</h2>
      <p class="text-xs text-gray-500">
        区分ごとに承認者を 役職 / ロール / 個人 で設定します。未設定の区分はオーナー 1 名の単段承認になります。
      </p>
      <label class="ml-auto flex items-center gap-1 text-xs text-gray-600">
        <input v-model="includeInactive" type="checkbox" class="rounded border-gray-300" />
        無効・削除済みも表示
      </label>
      <button
        type="button"
        class="rounded-md bg-blue-600 px-3 py-1 text-sm font-medium text-white hover:bg-blue-700"
        @click="openCreate"
      >＋ 経路を追加</button>
    </div>

    <p v-if="loadError" class="mt-2 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{{ loadError }}</p>
    <p v-else-if="loading" class="mt-2 text-sm text-gray-500">読み込み中…</p>
    <p v-else-if="routes.length === 0" class="mt-2 rounded-md bg-gray-50 px-3 py-3 text-sm text-gray-500">
      承認経路は未登録です。各区分の申請はオーナー 1 名の単段承認になります。
    </p>

    <!-- PC: テーブル -->
    <table v-else class="mt-2 hidden w-full text-sm sm:table">
      <thead>
        <tr class="border-b border-gray-200 text-left text-xs text-gray-500">
          <th class="py-1.5 pr-2">区分</th>
          <th class="py-1.5 pr-2">承認ステップ</th>
          <th class="py-1.5 pr-2">状態</th>
          <th class="py-1.5">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in routes" :key="r.id" class="border-b border-gray-100">
          <td class="py-1.5 pr-2 font-medium text-gray-800">{{ ROUTE_CATEGORY_LABELS[r.category] }}</td>
          <td class="py-1.5 pr-2 text-gray-700">{{ stepsSummary(r) }}</td>
          <td class="py-1.5 pr-2">
            <span v-if="r.deletedAt" class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">削除済み</span>
            <span v-else-if="!r.isActive" class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">無効</span>
            <span v-else class="rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700">有効</span>
          </td>
          <td class="py-1.5">
            <div class="flex gap-3">
              <template v-if="!r.deletedAt">
                <button type="button" class="text-xs text-blue-600 hover:underline" @click="openEdit(r)">編集</button>
                <button type="button" :disabled="busy" class="text-xs text-red-600 hover:underline disabled:opacity-40" @click="remove(r)">削除</button>
              </template>
              <button v-else type="button" :disabled="busy" class="text-xs text-green-700 hover:underline disabled:opacity-40" @click="restore(r)">復元</button>
            </div>
          </td>
        </tr>
      </tbody>
    </table>

    <!-- モバイル: カード -->
    <ul v-if="!loading && routes.length > 0" class="mt-2 flex flex-col gap-2 sm:hidden">
      <li v-for="r in routes" :key="r.id" class="rounded-md border border-gray-200 p-2.5">
        <div class="flex items-center gap-2">
          <span class="font-medium text-gray-800">{{ ROUTE_CATEGORY_LABELS[r.category] }}</span>
          <span v-if="r.deletedAt" class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">削除済み</span>
          <span v-else-if="!r.isActive" class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">無効</span>
          <span v-else class="rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700">有効</span>
        </div>
        <p class="mt-1 text-sm text-gray-700">{{ stepsSummary(r) }}</p>
        <div class="mt-1.5 flex gap-4">
          <template v-if="!r.deletedAt">
            <button type="button" class="inline-flex min-h-[44px] items-center text-xs text-blue-600 hover:underline" @click="openEdit(r)">編集</button>
            <button type="button" :disabled="busy" class="inline-flex min-h-[44px] items-center text-xs text-red-600 hover:underline disabled:opacity-40" @click="remove(r)">削除</button>
          </template>
          <button v-else type="button" :disabled="busy" class="inline-flex min-h-[44px] items-center text-xs text-green-700 hover:underline disabled:opacity-40" @click="restore(r)">復元</button>
        </div>
      </li>
    </ul>

    <!-- モーダル -->
    <Teleport to="body">
      <div
        v-if="modalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="route-modal-title"
        @click.self="closeModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="route-modal-title" class="text-base font-semibold">
              {{ editingId ? '承認経路を編集' : '承認経路を追加' }}
            </h2>
          </header>
          <div class="flex-1 space-y-3 overflow-y-auto px-5 py-3">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">区分 <span class="text-red-500">*</span></span>
              <AutoComplete
                v-if="!editingId"
                :model-value="form.category"
                :options="CATEGORY_OPTIONS"
                :allow-empty="false"
                @update:model-value="(v: string) => form.category = v as AttendanceRouteCategory"
              />
              <span v-else class="text-sm text-gray-700">{{ ROUTE_CATEGORY_LABELS[form.category] }}</span>
            </label>

            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">承認ステップ <span class="text-red-500">*</span></span>
              <ApproverSteps
                v-model="form.steps"
                :member-options="memberOptions"
                :title-options="titleOptions"
              />
            </div>

            <label class="flex items-center gap-2 text-sm">
              <input v-model="form.isActive" type="checkbox" class="rounded border-gray-300" />
              有効にする
            </label>

            <div v-if="formError" class="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{{ formError }}</div>
          </div>
          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 px-3 py-1.5 text-sm" @click="closeModal">キャンセル</button>
            <button
              type="button"
              :disabled="busy"
              class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-40"
              @click="submit"
            >{{ busy ? '保存中…' : '保存' }}</button>
          </footer>
        </div>
      </div>
    </Teleport>
  </section>
</template>
