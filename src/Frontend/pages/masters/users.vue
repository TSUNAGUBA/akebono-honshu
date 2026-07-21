<script setup lang="ts">
// 利用者マスタ (Part5)。利用者と各ページ・機能の閲覧/操作権限を管理する。
// 書込はオーナー権限 (process_record_permission >= 1) が必要 (バックエンドで最終ガード)。
const { user } = useAuth()
const { apiData, apiFetch } = useApi()

const canManageUsers = computed(() => (user.value?.processRecordPermission ?? 0) >= 1)

interface UserItem {
  id: string
  employeeNo: string
  loginId: string
  displayName: string
  isActive: boolean
  email: string | null
  isPlanningStaff: boolean
  isSalesStaff: boolean
  productLedgerPermission: number
  purchaseOrderCreatePermission: number
  purchaseOrderInfoPermission: number
  processRecordPermission: number
  hasFirebaseLink: boolean
}

const users = ref<UserItem[]>([])
const loading = ref(true)
const submitting = ref(false)
const errorMessage = ref('')
const successMessage = ref('')

// 権限選択肢 (Phase 5 §3.18)。
const PRODUCT_LEDGER_OPTS = [
  { value: 0, label: 'なし' }, { value: 1, label: '更新可能' }, { value: 2, label: '参照のみ' }, { value: 3, label: '参照のみ(制限)' },
]
const ORDER_CREATE_OPTS = [
  { value: 0, label: 'なし' }, { value: 1, label: '更新可能' }, { value: 2, label: '参照のみ' },
]
const YESNO_OPTS = [{ value: 0, label: 'なし' }, { value: 1, label: 'あり' }]

const labelOf = (opts: { value: number; label: string }[], v: number): string =>
  opts.find((o) => o.value === v)?.label ?? String(v)

const emptyForm = () => ({
  id: null as string | null,
  employeeNo: '',
  loginId: '',
  displayName: '',
  email: '',
  isPlanningStaff: false,
  isSalesStaff: false,
  productLedgerPermission: 0,
  purchaseOrderCreatePermission: 0,
  purchaseOrderInfoPermission: 0,
  processRecordPermission: 0,
  isActive: true,
  firebaseUid: '',
})
const form = ref(emptyForm())
const showForm = ref(false)
const isEditing = computed(() => form.value.id != null)

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    users.value = await apiData<UserItem[]>('/users')
  } catch {
    errorMessage.value = '利用者一覧の取得に失敗しました'
  } finally {
    loading.value = false
  }
}
onMounted(reload)

const startCreate = () => {
  form.value = emptyForm()
  showForm.value = true
  successMessage.value = ''
}
const startEdit = (u: UserItem) => {
  form.value = {
    id: u.id,
    employeeNo: u.employeeNo,
    loginId: u.loginId,
    displayName: u.displayName,
    email: u.email ?? '',
    isPlanningStaff: u.isPlanningStaff,
    isSalesStaff: u.isSalesStaff,
    productLedgerPermission: u.productLedgerPermission,
    purchaseOrderCreatePermission: u.purchaseOrderCreatePermission,
    purchaseOrderInfoPermission: u.purchaseOrderInfoPermission,
    processRecordPermission: u.processRecordPermission,
    isActive: u.isActive,
    // 連携済 UID は変更させない (空で送れば既存値を保持)。
    firebaseUid: '',
  }
  showForm.value = true
  successMessage.value = ''
}
const cancelForm = () => { showForm.value = false }

const canSubmit = computed(() =>
  form.value.employeeNo.trim() !== '' &&
  form.value.loginId.trim() !== '' &&
  form.value.displayName.trim() !== '' &&
  !submitting.value)

const submit = async () => {
  errorMessage.value = ''
  successMessage.value = ''
  if (!canSubmit.value) { errorMessage.value = '社員番号 / ログインID / 表示名は必須です'; return }
  submitting.value = true
  try {
    const body = {
      employeeNo: form.value.employeeNo.trim(),
      loginId: form.value.loginId.trim(),
      displayName: form.value.displayName.trim(),
      email: form.value.email.trim() || null,
      isPlanningStaff: form.value.isPlanningStaff,
      isSalesStaff: form.value.isSalesStaff,
      productLedgerPermission: Number(form.value.productLedgerPermission),
      purchaseOrderCreatePermission: Number(form.value.purchaseOrderCreatePermission),
      purchaseOrderInfoPermission: Number(form.value.purchaseOrderInfoPermission),
      processRecordPermission: Number(form.value.processRecordPermission),
      isActive: form.value.isActive,
      firebaseUid: form.value.firebaseUid.trim() || null,
    }
    if (isEditing.value) {
      await apiData<UserItem>(`/users/${form.value.id}`, { method: 'PATCH', body })
      successMessage.value = `「${body.displayName}」を更新しました`
    } else {
      await apiData<UserItem>('/users', { method: 'POST', body })
      successMessage.value = `「${body.displayName}」を登録しました`
    }
    showForm.value = false
    await reload()
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '利用者の保存に失敗しました')
  } finally {
    submitting.value = false
  }
}

const remove = async (u: UserItem) => {
  errorMessage.value = ''
  successMessage.value = ''
  if (u.id === user.value?.userId) { errorMessage.value = '自分自身は削除できません'; return }
  if (!confirm(`利用者「${u.displayName}」を削除 (無効化) しますか？`)) return
  try {
    await apiFetch<void>(`/users/${u.id}`, { method: 'DELETE' })
    successMessage.value = `「${u.displayName}」を削除しました`
    await reload()
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '削除に失敗しました')
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <header class="mb-4">
      <div class="text-xs text-gray-500">
        <NuxtLink to="/masters" class="hover:underline">マスタ一覧</NuxtLink>
        <span class="mx-1">/</span>
        <span>利用者</span>
      </div>
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-bold">利用者マスタ</h1>
          <p class="mt-1 text-sm text-gray-500">利用者と、各ページ・機能の閲覧/操作権限を管理します。</p>
        </div>
        <button
          v-if="canManageUsers"
          type="button"
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          @click="startCreate"
        >+ 利用者を追加</button>
      </div>
    </header>

    <div v-if="!canManageUsers" class="mb-3 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800">
      閲覧のみ (利用者管理はオーナー権限が必要です)。
    </div>
    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>

    <!-- 作成/編集フォーム -->
    <section v-if="showForm && canManageUsers" class="mb-4 rounded-lg border border-blue-200 bg-white p-4 shadow-sm">
      <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">{{ isEditing ? '利用者を編集' : '利用者を追加' }}</h2>
      <form class="space-y-3" @submit.prevent="submit">
        <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-3">
          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">社員番号 <span class="text-red-500">*</span></span>
            <input v-model="form.employeeNo" type="text" maxlength="16" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">ログイン ID <span class="text-red-500">*</span></span>
            <input v-model="form.loginId" type="text" maxlength="64" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">表示名 <span class="text-red-500">*</span></span>
            <input v-model="form.displayName" type="text" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">メール</span>
            <input v-model="form.email" type="email" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">ログイン連携 UID <span class="text-xs font-normal text-gray-400">(任意)</span></span>
            <input v-model="form.firebaseUid" type="text" maxlength="128" :placeholder="isEditing ? '(変更しない場合は空欄)' : 'Firebase UID (任意)'" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
          </label>
        </div>

        <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <label class="flex items-center gap-2 text-sm"><input v-model="form.isActive" type="checkbox" class="h-4 w-4" /> 有効</label>
          <label class="flex items-center gap-2 text-sm"><input v-model="form.isPlanningStaff" type="checkbox" class="h-4 w-4" /> 企画担当</label>
          <label class="flex items-center gap-2 text-sm"><input v-model="form.isSalesStaff" type="checkbox" class="h-4 w-4" /> 営業担当</label>
        </div>

        <fieldset class="rounded-md border border-gray-200 p-3">
          <legend class="px-1 text-xs font-semibold text-gray-600">権限 (閲覧/操作)</legend>
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">品番台帳</span>
              <select v-model.number="form.productLedgerPermission" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm">
                <option v-for="o in PRODUCT_LEDGER_OPTS" :key="o.value" :value="o.value">{{ o.label }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">発注書作成</span>
              <select v-model.number="form.purchaseOrderCreatePermission" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm">
                <option v-for="o in ORDER_CREATE_OPTS" :key="o.value" :value="o.value">{{ o.label }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">発注情報</span>
              <select v-model.number="form.purchaseOrderInfoPermission" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm">
                <option v-for="o in YESNO_OPTS" :key="o.value" :value="o.value">{{ o.label }}</option>
              </select>
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">利用者管理 (オーナー)</span>
              <select v-model.number="form.processRecordPermission" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm">
                <option v-for="o in YESNO_OPTS" :key="o.value" :value="o.value">{{ o.label }}</option>
              </select>
            </label>
          </div>
        </fieldset>

        <div class="flex justify-end gap-2">
          <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm hover:bg-gray-50" @click="cancelForm">キャンセル</button>
          <button type="submit" :disabled="!canSubmit" class="rounded-md bg-blue-600 px-6 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50">
            {{ submitting ? '保存中…' : (isEditing ? '更新' : '登録') }}
          </button>
        </div>
      </form>
    </section>

    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

    <div v-else class="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-sm">
        <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
          <tr>
            <th class="px-3 py-2 text-left">社員番号</th>
            <th class="px-3 py-2 text-left">表示名 (ログインID)</th>
            <th class="px-3 py-2 text-left">品番台帳</th>
            <th class="px-3 py-2 text-left">発注書作成</th>
            <th class="px-3 py-2 text-left">発注情報</th>
            <th class="px-3 py-2 text-left">利用者管理</th>
            <th class="px-3 py-2 text-center">状態</th>
            <th class="px-3 py-2 text-right"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in users" :key="u.id" class="border-b border-gray-100 last:border-0">
            <td class="whitespace-nowrap px-3 py-2 font-mono text-gray-600">{{ u.employeeNo }}</td>
            <td class="px-3 py-2">{{ u.displayName }} <span class="text-xs text-gray-400">({{ u.loginId }})</span></td>
            <td class="px-3 py-2">{{ labelOf(PRODUCT_LEDGER_OPTS, u.productLedgerPermission) }}</td>
            <td class="px-3 py-2">{{ labelOf(ORDER_CREATE_OPTS, u.purchaseOrderCreatePermission) }}</td>
            <td class="px-3 py-2">{{ labelOf(YESNO_OPTS, u.purchaseOrderInfoPermission) }}</td>
            <td class="px-3 py-2">{{ labelOf(YESNO_OPTS, u.processRecordPermission) }}</td>
            <td class="px-3 py-2 text-center">
              <span :class="u.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'" class="inline-block rounded-full px-2 py-0.5 text-xs font-medium">
                {{ u.isActive ? '有効' : '無効' }}
              </span>
            </td>
            <td class="whitespace-nowrap px-3 py-2 text-right">
              <template v-if="canManageUsers">
                <button type="button" class="text-xs text-blue-600 hover:underline" @click="startEdit(u)">編集</button>
                <button type="button" class="ml-3 text-xs text-red-600 hover:underline disabled:opacity-30" :disabled="u.id === user?.userId" @click="remove(u)">削除</button>
              </template>
            </td>
          </tr>
          <tr v-if="users.length === 0">
            <td colspan="8" class="px-3 py-6 text-center text-gray-500">利用者が登録されていません</td>
          </tr>
        </tbody>
      </table>
    </div>
  </main>
</template>
