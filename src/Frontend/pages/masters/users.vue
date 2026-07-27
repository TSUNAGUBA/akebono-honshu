<script setup lang="ts">
// 利用者マスタ (Part5)。利用者と各ページ・機能の閲覧/操作権限を管理する。
// 書込はオーナー権限 (process_record_permission >= 1) が必要 (バックエンドで最終ガード)。
const { user } = useAuth()
const { apiData, apiFetch } = useApi()
// 勤務体系 (勤怠ルール) の選択肢。勤怠 API のラッパを再利用する (原則3)。
const { attendanceRules } = useAttendance()

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
  // 勤怠 (末尾追加・勤怠移植仕様 §2)。Backend 未更新の環境では欠落し得るため optional。
  // さらに**労務個人情報のためオーナー以外には null で返る** (サーバが応答から落とす)。
  // 本画面は非オーナーも「閲覧のみ」で開けるため、非オーナーでは null が届く。
  // null を「なし / 未設定」と描くと偽の断定になるので、該当列は一覧では描画しない
  // (canManageUsers で出し分け)。オーナーが編集フォームを開いたときの解釈のみ
  // fail-close (権限は 0、打刻対象は false) とする。
  attendancePermission?: number | null
  punchRequired?: boolean | null
  // 勤務体系 (null = 既定ルール **または 取得権限なし**)。入社日は有給の周期自動付与の起算日。
  // 週所定日数/時間は比例付与の判定に使う。DB 既定は 5 日 / 40 時間。
  attendanceRuleId?: string | null
  hireDate?: string | null
  weeklyDays?: number | null
  weeklyHours?: number | null
  hasFirebaseLink: boolean
}

const users = ref<UserItem[]>([])
// 勤務体系の選択肢 (MasterSelect 用)。取得失敗時は空のまま (原則4: 主フローを止めない)。
const attendanceRuleOptions = ref<{ id: string; name: string }[]>([])
// 選択肢の取得に失敗したか。空の選択肢を「勤務体系が未登録」と誤読させないための注記に使う
// (原則4: できたところまで進めて**結果を報告する**)。
const attendanceRulesUnavailable = ref(false)
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
// 勤怠権限 (勤怠移植仕様 §2)。既存 4 権限と同じ非単調スケール (1=更新可能 が 2=参照のみ より小さい)。
const ATTENDANCE_OPTS = [
  { value: 0, label: 'なし' }, { value: 1, label: '更新可能' }, { value: 2, label: '参照のみ' },
]

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
  // 勤怠は全従業員が使う機能のため既定は「更新可能・打刻対象」(DB 既定と一致)。
  // 実際に打刻対象かは punchRequired で個別に外す。
  attendancePermission: 1,
  punchRequired: true,
  // 勤怠の付与条件 (DB 既定と一致)。空欄の入社日 = 周期自動付与の対象外。
  attendanceRuleId: null as string | null,
  hireDate: '',
  weeklyDays: 5,
  weeklyHours: 40,
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

// 勤務体系の選択肢。勤怠権限が無いオーナーでは 403 になり得るが、
// 利用者マスタ全体を止めないよう握りつぶす (原則4: 補助処理の失敗で主フローを止めない)。
// ただし**黙って空にはしない**: 選択肢が空だと「勤務体系が 1 件も無い」と区別が付かないため、
// 取得できなかったことをフォームに注記する。既存の割当値は送信時もそのまま保持される。
const loadAttendanceRules = async () => {
  try {
    const rules = await attendanceRules()
    attendanceRuleOptions.value = rules.map((r) => ({ id: r.id, name: r.name }))
    attendanceRulesUnavailable.value = false
  } catch {
    attendanceRuleOptions.value = []
    attendanceRulesUnavailable.value = true
  }
}

onMounted(async () => {
  await reload()
  await loadAttendanceRules()
})

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
    // 応答に無い (Backend 未更新) 場合は **fail-close 側へ倒す** (useAuth と同じ方針)。
    // 送信は全項目を明示送信する全量 PATCH のため、ここで DB 既定 (1 / true) を補うと、
    // 権限 0 のユーザを編集画面で開いて保存しただけで 1 (更新可能) へ昇格させてしまう。
    // DB 既定値の初期表示は新規作成フォーム (emptyForm) の責務なので、そちらは 1 / true のまま。
    attendancePermission: u.attendancePermission ?? 0,
    punchRequired: u.punchRequired ?? false,
    attendanceRuleId: u.attendanceRuleId ?? null,
    // hireDate は "YYYY-MM-DD"。<input type="date"> 用に先頭 10 文字だけ使う。
    hireDate: u.hireDate ? u.hireDate.slice(0, 10) : '',
    weeklyDays: u.weeklyDays ?? 5,
    weeklyHours: u.weeklyHours ?? 40,
    isActive: u.isActive,
    // 連携済 UID は変更させない (空で送れば既存値を保持)。
    firebaseUid: '',
  }
  showForm.value = true
  successMessage.value = ''
}
const cancelForm = () => { showForm.value = false }

// 週所定の範囲 (サーバ側 UserQueryService.ValidatePermissions と同値)。超過は 422 になるため事前に弾く。
const isWeeklyValid = computed(() => {
  const days = Number(form.value.weeklyDays)
  const hours = Number(form.value.weeklyHours)
  return Number.isFinite(days) && days >= 0 && days <= 7
    && Number.isFinite(hours) && hours >= 0 && hours <= 168
})

const canSubmit = computed(() =>
  form.value.employeeNo.trim() !== '' &&
  form.value.loginId.trim() !== '' &&
  form.value.displayName.trim() !== '' &&
  isWeeklyValid.value &&
  !submitting.value)

const submit = async () => {
  errorMessage.value = ''
  successMessage.value = ''
  // 範囲エラーの方が具体的な案内になるため、必須チェックより先に判定する。
  if (!isWeeklyValid.value) { errorMessage.value = '週所定日数は 0〜7、週所定時間は 0〜168 で入力してください'; return }
  if (!canSubmit.value) { errorMessage.value = '社員番号 / ログインID / 表示名は必須です'; return }
  submitting.value = true
  try {
    const body = {
      employeeNo: form.value.employeeNo.trim(),
      loginId: form.value.loginId.trim(),
      displayName: form.value.displayName.trim(),
      // PATCH では null = 未指定 (保持) のため、クリアは空文字で送る (POST では空文字 = null 扱い)。
      email: form.value.email.trim(),
      isPlanningStaff: form.value.isPlanningStaff,
      isSalesStaff: form.value.isSalesStaff,
      productLedgerPermission: Number(form.value.productLedgerPermission),
      purchaseOrderCreatePermission: Number(form.value.purchaseOrderCreatePermission),
      purchaseOrderInfoPermission: Number(form.value.purchaseOrderInfoPermission),
      processRecordPermission: Number(form.value.processRecordPermission),
      attendancePermission: Number(form.value.attendancePermission),
      punchRequired: form.value.punchRequired,
      // 勤怠の付与条件。**必ず送る**（送らないと PATCH で現在値のまま据え置かれ、
      // 画面で消したつもりの値が反映されない）。null 化は下の clear* フラグで明示する。
      attendanceRuleId: form.value.attendanceRuleId,
      hireDate: form.value.hireDate || null,
      weeklyDays: Number(form.value.weeklyDays),
      weeklyHours: Number(form.value.weeklyHours),
      isActive: form.value.isActive,
      firebaseUid: form.value.firebaseUid.trim() || null,
    }
    if (isEditing.value) {
      // NULL 許容列は「未指定」と「クリア」を区別できないため、明示クリアはフラグで送る。
      await apiData<UserItem>(`/users/${form.value.id}`, {
        method: 'PATCH',
        body: {
          ...body,
          clearHireDate: form.value.hireDate === '',
          clearAttendanceRule: form.value.attendanceRuleId == null,
        },
      })
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
  <main class="mx-auto max-w-screen-2xl px-3 py-3">
    <header class="mb-3">
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
    <section v-if="showForm && canManageUsers" class="mb-4 rounded-lg border border-blue-200 bg-white p-2.5 shadow-sm">
      <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">{{ isEditing ? '利用者を編集' : '利用者を追加' }}</h2>
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
          <!-- 打刻要否: 役員・外注等は打刻対象から外す (勤怠移植仕様 §2)。 -->
          <label class="flex items-center gap-2 text-sm"><input v-model="form.punchRequired" type="checkbox" class="h-4 w-4" /> 打刻対象</label>
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
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">勤怠</span>
              <select v-model.number="form.attendancePermission" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm">
                <option v-for="o in ATTENDANCE_OPTS" :key="o.value" :value="o.value">{{ o.label }}</option>
              </select>
            </label>
          </div>
          <p class="mt-2 text-xs text-gray-500">
            勤怠の管理操作 (全員のタイムカード・打刻修正/休暇の承認・休暇付与・勤怠ルール設定) は「利用者管理 (オーナー)」に集約されます。
          </p>
        </fieldset>

        <!-- 勤怠の付与条件。入社日が空欄だと有給の周期自動付与の対象外になるため必ず入力させる。 -->
        <fieldset class="rounded-md border border-gray-200 p-3">
          <legend class="px-1 text-xs font-semibold text-gray-600">勤怠 (有給付与の条件)</legend>
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">入社日</span>
              <input v-model="form.hireDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">週所定日数</span>
              <input v-model.number="form.weeklyDays" type="number" min="0" max="7" step="0.5" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">週所定時間</span>
              <input v-model.number="form.weeklyHours" type="number" min="0" max="168" step="0.5" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">勤務体系</span>
              <MasterSelect
                v-model="form.attendanceRuleId"
                :items="attendanceRuleOptions"
                allow-empty
                empty-label="（既定の勤務体系）"
                placeholder="勤務体系を選択…"
              />
              <!-- 選択肢が空のとき「未登録」と「取得できなかった」を取り違えさせない (原則4)。 -->
              <p v-if="attendanceRulesUnavailable" class="text-xs text-amber-700">
                勤務体系の選択肢を取得できませんでした（勤怠の参照権限が必要です）。現在の割当は保持されます。
              </p>
            </div>
          </div>
          <p class="mt-2 text-xs" :class="isWeeklyValid ? 'text-gray-500' : 'text-red-600'">
            入社日は有給の周期自動付与の起算日です (空欄 = 対象外)。週所定日数 0〜7 / 週所定時間 0〜168 は比例付与の判定に使います。
          </p>
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
            <!-- 勤怠権限・入社日は労務個人情報のため、サーバがオーナー以外には null を返す。
                 非オーナーに null を描くと「なし」「未設定」という偽の断定になるため列ごと隠す。 -->
            <th v-if="canManageUsers" class="px-3 py-2 text-left">勤怠</th>
            <th v-if="canManageUsers" class="px-3 py-2 text-left">入社日</th>
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
            <td v-if="canManageUsers" class="px-3 py-2">
              <!-- 応答にフィールドが無い場合の解釈は編集フォーム (startEdit) と揃える。
                   一覧が「更新可能」、フォームが「なし」と食い違うと、開いて保存しただけで
                   権限が変わったように見えるため。権限は常に fail-close 側で表示する。 -->
              {{ labelOf(ATTENDANCE_OPTS, u.attendancePermission ?? 0) }}
              <span v-if="!(u.punchRequired ?? false)" class="ml-1 text-xs text-gray-400">(打刻対象外)</span>
            </td>
            <td v-if="canManageUsers" class="whitespace-nowrap px-3 py-2">
              <template v-if="u.hireDate">{{ u.hireDate.slice(0, 10) }}</template>
              <span v-else class="text-xs text-gray-400">未設定</span>
            </td>
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
            <!-- 勤怠・入社日の 2 列は非オーナーでは描画しないため colspan を連動させる。 -->
            <td :colspan="canManageUsers ? 10 : 8" class="px-3 py-6 text-center text-gray-500">利用者が登録されていません</td>
          </tr>
        </tbody>
      </table>
    </div>
  </main>
</template>
