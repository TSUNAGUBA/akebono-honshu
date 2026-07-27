<script setup lang="ts">
/**
 * 勤怠管理 (移植仕様 §6.5)。
 *
 * 8 タブ (日次 / 週次 / 月次 / 休暇 / 申請 / 全員のタイムカード / 休暇管理 / 設定) を
 * `?tab=` クエリと双方向同期する 1 画面。打刻そのものは /attendance/timecard (本人用) が担当し、
 * 本画面は「集計の参照」「申請」「オーナーの管理操作」を受け持つ。
 *
 * 設計上の約束:
 * - 集計データの SoT はサーバ (月サマリ)。日次・週次は月サマリからの射影で取得する
 *   (`daySummaryFromMonth`)。日別に問い合わせると 60h 超バケットの月内累計が失われるため。
 * - 書込 (申請・承認・付与・ルール変更) の後は必ず `invalidate()` でキャッシュを破棄してから
 *   再取得する (原則6: SoT → キャッシュの一方向)。
 * - 補助的な取得 (休暇一覧・ルール一覧など) の失敗は主要な表示を止めない (原則4)。
 * - すべての操作に取消・戻り導線を用意する: モーダルはキャンセル可、却下・削除は確認ダイアログ、
 *   打刻の誤りは「打刻修正を申請」から復帰できる旨を UI に明示する。
 */
import type {
  AttendanceRuleDto,
  Buckets,
  DaySummary,
  MonthSummary,
  Article36Alert,
  RequestStatus,
  TimecardRow,
  PunchDto,
  PunchKind,
  LeaveUnit,
} from '~/composables/useAttendance'
import { emptyBuckets, toJstOffsetString, TIMECARD_RANGE_MAX_DAYS } from '~/composables/useAttendance'
import {
  addBizDays,
  addBizMonths,
  bizDateKey,
  bizDaysInMonth,
  bizWeekday,
  bucketTextClass,
  diffBizDays,
  fmtBizDate,
  fmtHours,
  fmtJstHm,
  fmtMinutes,
  startOfBizWeek,
  BUCKET_LABELS,
  LEAVE_UNIT_LABELS,
  PUNCH_KIND_LABELS,
  REQUEST_STATUS_CLASSES,
  REQUEST_STATUS_LABELS,
  WEEKDAY_JP,
} from '~/utils/attendance'

// ------------------------------------------------------------------
// 1. 依存 (composable) と、本画面ローカルで持つ DTO 型
// ------------------------------------------------------------------
// 勤怠 API の呼び出しは useAttendance に集約する (キャッシュ規約は composable 側が SoT)。
// 勤怠ルール (§5.3 #10〜14) も composable 経由で叩く。ルール変更は所定労働時間・法定休日を通じて
// 日次集計に効くため、更新後は必ず invalidate() でサマリキャッシュを捨てる。
const {
  monthSummary,
  daySummaryFromMonth,
  alerts,
  dayRaw,
  timecard,
  loadFixRequests,
  requestFix,
  decideFix,
  attendanceRules,
  createAttendanceRule,
  updateAttendanceRule,
  deleteAttendanceRule,
  restoreAttendanceRule,
  leaveSummary,
  leaveTypes,
  leaveRequests,
  requestLeave,
  decideLeave,
  grantLeave,
  bulkGrantLeave,
  runPeriodicGrants,
  leaveAdminSummary,
  invalidate,
} = useAttendance()

// メンバー切替・個別付与で使う利用者一覧は既存の /users を再利用する (原則3)。
const { apiData } = useApi()

interface MemberOption {
  id: string
  displayName: string
  loginId: string
  isActive: boolean
}

// 以下のレスポンス型は API 契約 (§5.2 / §5.4) に対応する本画面ローカル定義。
// enum 相当の項目は string で受ける (composable 側の型定義と構造的に代入可能な、より広い型にして
// 契約の細部のブレで画面が壊れないようにする)。表示ラベルは *_LABELS の引き当てで解決する。
interface FixRequest {
  id: string
  userId: string
  userName: string
  date: string
  kind: string
  requestedAt: string
  reason: string
  status: string
  decidedByUserId: string | null
  decidedByUserName: string | null
  createdAt: string
}

interface LeaveTypeItem {
  id: string
  name: string
  grantMethod: string
  expiryMonths: number | null
  isStatutory: boolean
  description: string
  displayOrder: number
  isActive: boolean
}

interface LeaveRequestItem {
  id: string
  userId: string
  userName: string
  leaveTypeId: string
  leaveTypeName: string
  date: string
  unit: string
  status: string
  reason: string
  decidedByUserId: string | null
  decidedByUserName: string | null
  createdAt: string
}

interface LeaveSummaryData {
  paidRemaining: number
  paidUsedThisFiscalYear: number
  nextExpire: { date: string; days: number } | null
  byType: {
    leaveTypeId: string
    leaveTypeName: string
    isStatutory: boolean
    granted: number
    taken: number
    remaining: number
    nextExpireDate: string | null
  }[]
  obligation: {
    applicable: boolean
    grantDate: string | null
    deadline: string | null
    requiredDays: number
    takenDays: number
    daysLeft: number
    achieved: boolean
    warn: boolean
  }
  history: {
    date: string
    kind: string
    leaveTypeId: string
    leaveTypeName: string
    detail: string
    status: string
    days: string
  }[]
}

interface LeaveAdminRow {
  userId: string
  userName: string
  leaveTypeId: string
  leaveTypeName: string
  granted: number
  taken: number
  remaining: number
  lastGrantDate: string | null
}

// ------------------------------------------------------------------
// 2. 権限 (仕様書 §2)
// ------------------------------------------------------------------
// 勤怠権限は既存 4 権限と同じ非単調スケール (0=なし / 1=更新可能 / 2=参照のみ)。
// 書込判定は必ず `=== 1`。管理系はオーナー権限 (process_record_permission) に集約する。
const { user, canUseAttendance, isAttendanceAdmin } = useAuth()

/**
 * 管理操作 (全員のタイムカード / 承認・却下 / 休暇付与 / 勤怠ルール設定) の可否。
 * 判定式は useAuth の isAttendanceAdmin が SoT (本画面でオーナー判定を再実装しない・原則3)。
 */
const isOwner = isAttendanceAdmin
/** 勤怠の書込操作 (打刻修正申請・休暇申請) の可否。参照のみ (2) は不可なので `=== 1`。 */
const canWriteAttendance = computed(() => (user.value?.attendancePermission ?? 0) === 1)

const myUserId = computed(() => user.value?.userId ?? '')
const myDisplayName = computed(() => user.value?.displayName ?? '')

// ------------------------------------------------------------------
// 3. 勤怠の定数・表示ラベル (バックエンドの AttendanceCalc / AttendanceService と同値)
// ------------------------------------------------------------------
// 日付演算 (addBizDays / diffBizDays / bizWeekday / startOfBizWeek / addBizMonths / bizDaysInMonth)
// と表示ラベル・配色は utils/attendance.ts が SoT。本画面でローカル再実装しない (原則3)。
/** 労基法 32 条 週法定労働時間 (分)。AttendanceCalc.LegalWeeklyMinutes と同値。 */
const LEGAL_WEEKLY_MINUTES = 40 * 60
/** 36 協定 原則月上限 (分)。AttendanceCalc.OtMonthlyLimitMinutes と同値。 */
const OT_MONTHLY_LIMIT_MINUTES = 45 * 60

// ラベル引き当ては未知値が来ても画面が壊れないよう素の値へフォールバックする
// (定義そのものは utils/attendance.ts の *_LABELS / *_CLASSES が SoT)。
const statusLabel = (s: string): string => REQUEST_STATUS_LABELS[s as RequestStatus] ?? s
const statusClass = (s: string): string => REQUEST_STATUS_CLASSES[s as RequestStatus] ?? 'bg-gray-100 text-gray-600'
const unitLabel = (u: string): string => LEAVE_UNIT_LABELS[u as LeaveUnit] ?? u
const kindLabel = (k: string): string => PUNCH_KIND_LABELS[k as PunchKind] ?? k

const PUNCH_KIND_OPTIONS = (['in', 'out', 'breakStart', 'breakEnd'] as PunchKind[])
  .map((k) => ({ value: k, label: PUNCH_KIND_LABELS[k] }))
const UNIT_OPTIONS = (['full', 'half'] as LeaveUnit[])
  .map((u) => ({ value: u, label: LEAVE_UNIT_LABELS[u] }))
const WEEKDAY_OPTIONS = WEEKDAY_JP.map((label, i) => ({ value: String(i), label: `${label}曜日` }))

// ------------------------------------------------------------------
// 4. タブ管理 (?tab= と双方向同期)
// ------------------------------------------------------------------
const VALID_TABS = ['daily', 'weekly', 'monthly', 'leave', 'requests', 'timecard', 'leave-admin', 'settings'] as const
type TabKey = (typeof VALID_TABS)[number]

// Record で持つことでタブ追加時のラベル・権限定義漏れをコンパイル時に検出する。
const TAB_META: Record<TabKey, { label: string; ownerOnly: boolean }> = {
  'daily': { label: '日次', ownerOnly: false },
  'weekly': { label: '週次', ownerOnly: false },
  'monthly': { label: '月次', ownerOnly: false },
  'leave': { label: '休暇', ownerOnly: false },
  'requests': { label: '申請', ownerOnly: false },
  'timecard': { label: '全員のタイムカード', ownerOnly: true },
  'leave-admin': { label: '休暇管理', ownerOnly: true },
  'settings': { label: '設定', ownerOnly: true },
}

const route = useRoute()
const router = useRouter()
const PAGE_PATH = '/attendance'

const activeTab = ref<TabKey>('daily')
/** 対象メンバー切替を出すタブ (自分以外の勤怠を見られるのはオーナーのみ)。 */
const MEMBER_SWITCH_TABS: TabKey[] = ['daily', 'weekly', 'monthly', 'leave']

const isTabAllowed = (tab: TabKey): boolean => !TAB_META[tab].ownerOnly || isOwner.value
const visibleTabs = computed<TabKey[]>(() => VALID_TABS.filter((t) => isTabAllowed(t)))

/** 不正値・権限を満たさないタブは 'daily' へフォールバックする (§6.5)。 */
const normalizeTab = (raw: unknown): TabKey => {
  const value = Array.isArray(raw) ? String(raw[0] ?? '') : String(raw ?? '')
  const found = VALID_TABS.find((t) => t === value)
  return found && isTabAllowed(found) ? found : 'daily'
}

// 画面離脱中はクエリを書き換えない。ルート遷移が確定する前に立てるフラグ (onBeforeRouteLeave) と、
// 「現在のパスが本画面か」の二重ガードで、遷移先の URL に ?tab= を書き込む事故を防ぐ。
let isLeaving = false
onBeforeRouteLeave(() => { isLeaving = true })

const syncQueryFromTab = (tab: TabKey) => {
  if (isLeaving || route.path !== PAGE_PATH) return
  if (String(route.query.tab ?? '') === tab) return
  // replace: タブ切替で履歴を汚さない (戻るボタンで前の画面へ戻れることを保つ)。
  void router.replace({ query: { ...route.query, tab } })
}

// クエリ → タブ (初期表示・ブラウザの戻る/進む)。
watch(() => route.query.tab, (raw) => {
  if (isLeaving || route.path !== PAGE_PATH) return
  const next = normalizeTab(raw)
  if (next !== activeTab.value) activeTab.value = next
  // 不正値・権限不足のクエリが残っていたら URL 側も正す (URL と表示の乖離を作らない)。
  const current = Array.isArray(raw) ? String(raw[0] ?? '') : String(raw ?? '')
  if (current !== '' && current !== next) syncQueryFromTab(next)
}, { immediate: true })

// タブ → クエリ。
watch(activeTab, (tab) => syncQueryFromTab(tab))

// 滞在中に権限を失った場合も 'daily' へ戻す (オーナー限定タブに留まらせない)。
watch(isOwner, () => {
  if (!isTabAllowed(activeTab.value)) activeTab.value = 'daily'
})

const selectTab = (tab: TabKey) => {
  if (!isTabAllowed(tab)) return
  activeTab.value = tab
}

// ------------------------------------------------------------------
// 5. 対象メンバー・共通の状態
// ------------------------------------------------------------------
const members = ref<MemberOption[]>([])
/**
 * メンバー一覧の取得に失敗したか。空の候補を「在籍者がいない」と誤読させないための注記に使う
 * (users.vue の attendanceRulesUnavailable と同じ扱い・原則4「結果を報告する」)。
 */
const membersUnavailable = ref(false)
const targetUserId = ref('')

/**
 * 参照対象の userId。空になり得ないよう自分の userId をフォールバックにする
 * (自分自身を明示指定するのは API 契約上も正当。他人指定はオーナーのみサーバが許可する)。
 */
const effectiveUserId = computed(() => targetUserId.value || myUserId.value)
const isViewingSelf = computed(() => effectiveUserId.value === myUserId.value)
const targetUserName = computed(() => {
  if (isViewingSelf.value) return myDisplayName.value
  return members.value.find((m) => m.id === effectiveUserId.value)?.displayName ?? '（選択中のメンバー）'
})
const memberOptions = computed(() => members.value.map((m) => ({ id: m.id, name: m.displayName, code: m.loginId })))

const loadMembers = async () => {
  if (!isOwner.value) return
  try {
    const rows = await apiData<MemberOption[]>('/users')
    members.value = rows.filter((m) => m.isActive)
    membersUnavailable.value = false
  } catch (e) {
    // メンバー切替は補助機能。取得失敗でも自分の勤怠表示は続行する (原則4)。
    // ただし黙って空にはしない: 候補が空のとき「在籍者がいない」と取り違えさせないため注記する。
    // 取得済みの一覧は破棄しない (直前まで選べていた候補を失わせない)。
    console.error('[attendance] 利用者一覧の取得に失敗しました', e)
    membersUnavailable.value = true
  }
}

/** 日次タブへ移動する (週次・月次・全員のタイムカードからの導線)。 */
const goToDaily = (date: string | null, userId?: string) => {
  if (!date) return
  if (userId && userId !== myUserId.value) {
    // 他人の日次を開けるのはオーナーのみ (サーバ側も 403 で守るが UI でも辿らせない)。
    if (!isOwner.value) return
    targetUserId.value = userId
  }
  selectedDate.value = bizDateKey(date)
  activeTab.value = 'daily'
}

// ------------------------------------------------------------------
// 6. 日次タブ
// ------------------------------------------------------------------
// 日付の初期値は必ず todayJst() (UTC 由来の前日ずれ防止)。
const selectedDate = ref(todayJst())
const dailySummary = ref<DaySummary | null>(null)
const dailyRaw = ref<DaySummary | null>(null)
const dailyLoading = ref(false)
const dailyError = ref('')

const loadDaily = async () => {
  if (!effectiveUserId.value) return
  dailyLoading.value = true
  dailyError.value = ''
  try {
    // 集計は月サマリからの射影で取る (単日計算だと 60h 超バケットが常に 0 になるため)。
    const [summary, raw] = await Promise.all([
      daySummaryFromMonth(effectiveUserId.value, selectedDate.value),
      dayRaw(effectiveUserId.value, selectedDate.value),
    ])
    dailySummary.value = summary ?? null
    dailyRaw.value = raw ?? null
  } catch (e) {
    dailyError.value = getApiErrorMessage(e, '日次勤怠の取得に失敗しました')
    dailySummary.value = null
    dailyRaw.value = null
  } finally {
    dailyLoading.value = false
  }
}

/**
 * 打刻タイムライン。raw (全打刻) を時刻順に並べ、有効打刻に含まれないものを「修正前」として
 * 取消線で残す (記録系は削除されないため、修正の履歴が追跡できる)。
 */
interface TimelineEntry extends PunchDto { superseded: boolean }
const dailyTimeline = computed<TimelineEntry[]>(() => {
  const raw = dailyRaw.value?.rawPunches ?? dailyRaw.value?.punches ?? []
  const effectiveIds = new Set((dailyRaw.value?.punches ?? []).map((p) => p.id))
  return [...raw]
    .sort((a, b) => (a.at < b.at ? -1 : a.at > b.at ? 1 : 0))
    .map((p) => ({ ...p, superseded: !effectiveIds.has(p.id) }))
})

const dailyBuckets = computed<Buckets>(() => dailySummary.value?.buckets ?? emptyBuckets())
/** 労基法 34 条で必要な休憩時間 (不足分から逆算して表示する)。 */
const dailyRequiredBreak = computed(() =>
  (dailySummary.value?.breakMinutes ?? 0) + (dailySummary.value?.breakShortage ?? 0))

// ------------------------------------------------------------------
// 7. 週次タブ (週の起点は日曜)
// ------------------------------------------------------------------
const weekStart = computed(() => startOfBizWeek(selectedDate.value))
const weekDates = computed(() => Array.from({ length: 7 }, (_, i) => addBizDays(weekStart.value, i)))
const weekDays = ref<(DaySummary | null)[]>([])
const weeklyLoading = ref(false)
const weeklyError = ref('')

const loadWeekly = async () => {
  if (!effectiveUserId.value) return
  weeklyLoading.value = true
  weeklyError.value = ''
  try {
    // 週が月をまたぐ場合も daySummaryFromMonth が必要な月サマリを解決する。
    weekDays.value = await Promise.all(
      weekDates.value.map(async (d) => (await daySummaryFromMonth(effectiveUserId.value, d)) ?? null),
    )
  } catch (e) {
    weeklyError.value = getApiErrorMessage(e, '週次勤怠の取得に失敗しました')
    weekDays.value = []
  } finally {
    weeklyLoading.value = false
  }
}

const weekTotalMinutes = computed(() => weekDays.value.reduce((s, d) => s + (d?.workMinutes ?? 0), 0))
const weekPercent = computed(() => Math.round((weekTotalMinutes.value / LEGAL_WEEKLY_MINUTES) * 1000) / 10)
/** 週法定 40h に対する水準。>100% は超過 (crit)、>=80% は注意 (warn)。 */
const weekLevel = computed<'crit' | 'warn' | 'ok'>(() =>
  weekPercent.value > 100 ? 'crit' : weekPercent.value >= 80 ? 'warn' : 'ok')
const weekBarClass = computed(() =>
  weekLevel.value === 'crit' ? 'bg-red-500' : weekLevel.value === 'warn' ? 'bg-amber-500' : 'bg-blue-500')
const shiftWeek = (delta: number) => { selectedDate.value = addBizDays(selectedDate.value, delta * 7) }

// ------------------------------------------------------------------
// 8. 月次タブ
// ------------------------------------------------------------------
const selectedMonth = ref(currentMonthJst())
const monthData = ref<MonthSummary | null>(null)
const monthAlerts = ref<Article36Alert[]>([])
/**
 * 36 協定アラートの取得に失敗したか。
 * 失敗を無言にすると「現時点で 36 協定に関するアラートはありません。」と緑帯で断定してしまい、
 * 実際には超過しているのに問題なしと読ませる (誤読コストが最も高い箇所)。
 * 同じ関数内の休暇マーカー (monthLeaveNotice) と同様、必ず画面に告知する (原則4)。
 */
const alertsUnavailable = ref(false)
const monthLeaves = ref<LeaveRequestItem[]>([])
/**
 * カレンダーの「休暇」マーカーが実データと食い違い得るときの注記
 * （取得失敗・ページ上限での打ち切り）。空文字なら注記を出さない。
 * 欠落を無言にしないための告知なので、成功時は必ず空へ戻す（原則4）。
 */
const monthLeaveNotice = ref('')
const monthlyLoading = ref(false)
const monthlyError = ref('')

/**
 * 休暇マーカー取得の絞り込み期間（表示中の月の 月初〜月末・両端含み）。
 * **全期間を引かない**こと: 承認済み休暇が一覧の上限件数を超えると、過去月のマーカーが
 * 黙って欠ける（サーバ側の期間上限は `LeaveService.RequestRangeMaxDays` = 366 日）。
 * 年月入力が不正（`<input type="month">` を空にした等）なら絞り込まない = 従来どおり全期間。
 */
const monthLeaveRange = computed<{ from?: string; to?: string }>(() => {
  const month = selectedMonth.value
  const lastDay = bizDaysInMonth(month)
  if (lastDay === 0) return {}
  return { from: `${month}-01`, to: `${month}-${String(lastDay).padStart(2, '0')}` }
})

/**
 * 法定休日の曜日。既定ルール (isDefault かつ isActive) から取り、取得できなければ日曜 (0)。
 * 個別割当ルールはフロントから解決できないため、日別の集計値 (legalHoliday > 0) も併用して判定する。
 */
const legalHolidayWeekday = computed(() => {
  const def = rules.value.find((r) => r.isDefault && r.isActive && !r.deletedAt)
  return def?.legalHolidayWeekday ?? 0
})

/**
 * 勤怠ルールを**取得できなかった**か。`rulesError` は設定タブのエラー帯にしか出ないため、
 * 同じ状態を月次・週次タブの注記としても使えるよう真偽で公開する
 * (alertsUnavailable / membersUnavailable / leaveTypesUnavailable と同じ扱い・原則3/原則4)。
 *
 * 取得に失敗すると legalHolidayWeekday が日曜 (0) へ倒れるため、無告知だと既定ルールが土曜でも
 * カレンダー・凡例・週次テーブルが日曜を法定休日として塗り、通信失敗を「日曜が法定休日」と断定表示する。
 * **ルールが 1 件も無いときの日曜フォールバックはサーバ集計 (AttendanceCalc) と一致する正しい挙動**
 * なので、告知するのは通信失敗のときだけ (rulesError) に限る。
 * 状態は rulesError が SoT。二重管理を避けるため computed で導出する (原則3)。
 */
const rulesUnavailable = computed(() => rulesError.value !== '')

const loadMonthly = async () => {
  if (!effectiveUserId.value) return
  monthlyLoading.value = true
  monthlyError.value = ''
  try {
    monthData.value = await monthSummary(effectiveUserId.value, selectedMonth.value)
  } catch (e) {
    monthlyError.value = getApiErrorMessage(e, '月次勤怠の取得に失敗しました')
    monthData.value = null
  } finally {
    monthlyLoading.value = false
  }
  // 以降は補助情報。失敗しても月次サマリの表示は止めない (原則4)。
  try {
    monthAlerts.value = await alerts(effectiveUserId.value, selectedMonth.value)
    alertsUnavailable.value = false
  } catch (e) {
    console.error('[attendance] 36協定アラートの取得に失敗しました', e)
    monthAlerts.value = []
    alertsUnavailable.value = true
  }
  try {
    // 表示中の月だけを要求する (全期間を引くと上限件数で古い月が落ち、マーカーが静かに消える)。
    const { from, to } = monthLeaveRange.value
    const leavePage = isViewingSelf.value
      ? await leaveRequests('self', 'approved', from, to)
      : await leaveRequests('all', 'approved', from, to)
    monthLeaves.value = isViewingSelf.value
      ? leavePage.items
      : leavePage.items.filter((r) => r.userId === effectiveUserId.value)
    // 続きがある = マーカーが欠けている可能性がある。無言で切り詰めない (原則4)。
    monthLeaveNotice.value = leavePage.page.hasMore
      ? '承認済みの休暇が多いため、一部のみ表示しています。カレンダーの「休暇」表示が欠けている場合があります。'
      : ''
  } catch (e) {
    console.error('[attendance] 承認済み休暇の取得に失敗しました', e)
    monthLeaves.value = []
    monthLeaveNotice.value = '承認済みの休暇を取得できませんでした。カレンダーの「休暇」表示は反映されていません。'
  }
  // ルールは法定休日の曜日判定にのみ使う補助情報。
  if (rules.value.length === 0) await loadRules()
}

const monthDayMap = computed(() => {
  const map = new Map<string, DaySummary>()
  for (const d of monthData.value?.days ?? []) map.set(bizDateKey(d.date), d)
  return map
})
const monthLeaveDates = computed(() => new Set(monthLeaves.value.map((r) => bizDateKey(r.date))))

interface CalendarCell {
  date: string | null
  day: number
  summary: DaySummary | null
  isLegalHoliday: boolean
  isToday: boolean
  hasLeave: boolean
  warnBreak: boolean
  warnOver60: boolean
}
const calendarCells = computed<CalendarCell[]>(() => {
  const month = selectedMonth.value
  const cells: CalendarCell[] = []
  const blank = (): CalendarCell => ({
    date: null, day: 0, summary: null, isLegalHoliday: false, isToday: false,
    hasLeave: false, warnBreak: false, warnOver60: false,
  })
  const lead = bizWeekday(`${month}-01`)
  for (let i = 0; i < lead; i++) cells.push(blank())
  const total = bizDaysInMonth(month)
  const today = todayJst()
  for (let d = 1; d <= total; d++) {
    const date = `${month}-${String(d).padStart(2, '0')}`
    const summary = monthDayMap.value.get(date) ?? null
    cells.push({
      date,
      day: d,
      summary,
      isLegalHoliday: bizWeekday(date) === legalHolidayWeekday.value || (summary?.buckets.legalHoliday ?? 0) > 0,
      isToday: date === today,
      hasLeave: monthLeaveDates.value.has(date),
      warnBreak: (summary?.breakShortage ?? 0) > 0,
      warnOver60: (summary?.buckets.over60Ot ?? 0) > 0,
    })
  }
  while (cells.length % 7 !== 0) cells.push(blank())
  return cells
})

const monthTotals = computed<Buckets>(() => monthData.value?.total ?? emptyBuckets())
/** 当月の時間外労働 (法定外 + 60h 超)。36 協定の月 45h ゲージの分子。 */
const monthOtMinutes = computed(() => monthTotals.value.nonStatutoryOt + monthTotals.value.over60Ot)
const monthOtPercent = computed(() => Math.round((monthOtMinutes.value / OT_MONTHLY_LIMIT_MINUTES) * 1000) / 10)
const monthOtBarClass = computed(() =>
  monthOtPercent.value > 100 ? 'bg-red-500' : monthOtPercent.value >= 80 ? 'bg-amber-500' : 'bg-blue-500')
const shiftMonth = (delta: number) => { selectedMonth.value = addBizMonths(selectedMonth.value, delta) }

// ------------------------------------------------------------------
// 9. 休暇タブ
// ------------------------------------------------------------------
const leaveData = ref<LeaveSummaryData | null>(null)
const leaveTypeItems = ref<LeaveTypeItem[]>([])
/**
 * 休暇種別の取得に失敗したか。空の選択肢を「種別が 1 件も登録されていない」と誤読させないための注記に使う
 * (users.vue の attendanceRulesUnavailable と同じ扱い・原則3/原則4)。
 * 無言だと通信失敗でも「管理者に確認してください」と案内してしまい、利用者を誤誘導する。
 */
const leaveTypesUnavailable = ref(false)
const leaveLoading = ref(false)
const leaveError = ref('')

const loadLeave = async () => {
  if (!effectiveUserId.value) return
  leaveLoading.value = true
  leaveError.value = ''
  try {
    leaveData.value = await leaveSummary(effectiveUserId.value)
  } catch (e) {
    leaveError.value = getApiErrorMessage(e, '休暇情報の取得に失敗しました')
    leaveData.value = null
  } finally {
    leaveLoading.value = false
  }
  await loadLeaveTypes()
}

const loadLeaveTypes = async () => {
  if (leaveTypeItems.value.length > 0) return
  try {
    leaveTypeItems.value = await leaveTypes(false)
    leaveTypesUnavailable.value = false
  } catch (e) {
    // 種別が引けなくても残数表示は成立する。申請モーダルだけが使えなくなる (原則4)。
    // 握りつぶさず、選択肢が空の理由を申請・付与モーダルに出す (「未登録」と取り違えさせない)。
    console.error('[attendance] 休暇種別の取得に失敗しました', e)
    leaveTypesUnavailable.value = true
  }
}

const specialLeaveTypes = computed(() => (leaveData.value?.byType ?? []).filter((t) => !t.isStatutory))
/**
 * 年5日取得義務の表示用ビュー。未取得時も同じ形にそろえ、テンプレート側の null 分岐を無くす
 * (applicable=false のときは「対象なし」を表示するので値は参照されない)。
 */
const obligation = computed(() => leaveData.value?.obligation ?? {
  applicable: false,
  grantDate: null as string | null,
  deadline: null as string | null,
  requiredDays: 5,
  takenDays: 0,
  daysLeft: 0,
  achieved: false,
  warn: false,
})
/** 年5日取得義務の達成率 (5 日で 100%)。 */
const obligationPercent = computed(() => {
  const o = obligation.value
  if (o.requiredDays <= 0) return 0
  return Math.min(100, Math.round((o.takenDays / o.requiredDays) * 100))
})

// ------------------------------------------------------------------
// 10. 申請タブ
// ------------------------------------------------------------------
const pendingFixes = ref<FixRequest[]>([])
const pendingLeaves = ref<LeaveRequestItem[]>([])
const myFixes = ref<FixRequest[]>([])
const myLeaves = ref<LeaveRequestItem[]>([])
const requestsLoading = ref(false)
// エラーは取得単位で分ける。本タブは「自分の申請 (全員)」と「承認待ち (オーナーのみ)」を
// **独立した 2 本の取得**で読むため、片方の失敗でもう片方の表示を落としてはならない (原則4)。
// 1 本の ref を両セクションの描画条件に使うと、
//   - 承認待ちだけ失敗 → 取得できている「自分の申請」が理由の説明なく消える
//   - 自分の申請だけ失敗 → 取得できている承認待ちが消え、承認導線そのものが失われる
// という巻き添えが起きる。
/** 「自分の申請」の取得エラー。自分の申請セクションの描画条件はこちらを見る。 */
const requestsError = ref('')
/** 「承認待ち」(オーナーのみ) の取得エラー。承認導線・件数バッジの描画条件はこちらを見る。 */
const pendingError = ref('')
/**
 * 一覧がページ上限で打ち切られたときの注記 (空文字なら出さない)。
 * 本タブは期間を絞らずに読む (承認待ち・自分の申請とも全期間が対象) ため、
 * 運用年数に比例していつか必ず上限を超える。API 自体は `from` / `to` で期間を絞れるので、
 * 期間が決まっている画面 (月次カレンダーの休暇マーカー等) はそちらで要求を絞ること。
 * 黙って切り詰めず「一部のみ表示している」ことを告知する (原則4)。
 */
const myRequestsNotice = ref('')
const pendingNotice = ref('')
const TRUNCATED_NOTICE = '申請が多いため、新しいものから一部のみ表示しています。'

const loadRequests = async () => {
  requestsLoading.value = true
  requestsError.value = ''
  pendingError.value = ''
  myRequestsNotice.value = ''
  pendingNotice.value = ''
  try {
    // 自分の申請は全員に見せる。承認待ち (全員分) はオーナーのみ取得する (scope=all はオーナー限定)。
    const [mineFix, mineLeave] = await Promise.all([
      loadFixRequests('self'),
      leaveRequests('self'),
    ])
    myFixes.value = mineFix.items
    myLeaves.value = mineLeave.items
    if (mineFix.page.hasMore || mineLeave.page.hasMore) myRequestsNotice.value = TRUNCATED_NOTICE
  } catch (e) {
    requestsError.value = getApiErrorMessage(e, '申請一覧の取得に失敗しました')
    // 他のローダ (loadDaily / loadWeekly / loadTimecard 等) と同じく、失敗した取得の状態は捨てる。
    // 本体は描画されないのに古い一覧が残り続けると、再取得成功時に前回分と混ざって見える。
    myFixes.value = []
    myLeaves.value = []
  }
  if (isOwner.value) {
    try {
      const [pf, pl] = await Promise.all([
        loadFixRequests('all', 'pending'),
        leaveRequests('all', 'pending'),
      ])
      pendingFixes.value = pf.items
      pendingLeaves.value = pl.items
      if (pf.page.hasMore || pl.page.hasMore) pendingNotice.value = TRUNCATED_NOTICE
    } catch (e) {
      // 承認待ちの取得失敗は「自分の申請」の表示を止めない (原則4)。
      // そのため専用の ref に入れる (requestsError を使うと自分の申請まで巻き添えで消える)。
      console.error('[attendance] 承認待ち一覧の取得に失敗しました', e)
      pendingError.value = getApiErrorMessage(e, '承認待ち一覧の取得に失敗しました')
      // 件数バッジ (タブバー・見出し) が古い件数を出し続けないよう状態を捨てる。
      pendingFixes.value = []
      pendingLeaves.value = []
    }
  }
  requestsLoading.value = false
}

const pendingCount = computed(() => pendingFixes.value.length + pendingLeaves.value.length)

/** 承認は即時実行。却下のみ確認ダイアログを挟む (誤操作で申請を潰さない)。 */
const approveFix = async (r: FixRequest) => {
  await runWrite(async () => {
    await decideFix(r.id, 'approved')
    successMessage.value = `${r.userName} さんの打刻修正を承認しました（${bizDateKey(r.date)} ${kindLabel(r.kind)}）`
    // 承認は打刻列を書き換える = 集計キャッシュが陳腐化するため必ず破棄する。
    invalidate()
    await Promise.all([loadRequests(), reloadActiveSummary()])
  }, '打刻修正の承認に失敗しました')
}
const rejectFix = (r: FixRequest) => {
  askConfirm({
    title: '打刻修正を却下しますか？',
    message: `${r.userName} さん / ${bizDateKey(r.date)} ${kindLabel(r.kind)} → ${fmtJstHm(r.requestedAt)}`,
    note: '却下した申請は元に戻せません。申請者は必要に応じて再申請できます。',
    confirmLabel: '却下する',
    run: async () => {
      await decideFix(r.id, 'rejected')
      successMessage.value = `${r.userName} さんの打刻修正を却下しました`
      invalidate()
      await loadRequests()
    },
    fallbackError: '打刻修正の却下に失敗しました',
  })
}
const approveLeave = async (r: LeaveRequestItem) => {
  await runWrite(async () => {
    await decideLeave(r.id, 'approved')
    successMessage.value = `${r.userName} さんの休暇申請を承認しました（${bizDateKey(r.date)}）`
    invalidate()
    await Promise.all([loadRequests(), reloadActiveSummary()])
  }, '休暇申請の承認に失敗しました')
}
const rejectLeave = (r: LeaveRequestItem) => {
  askConfirm({
    title: '休暇申請を却下しますか？',
    message: `${r.userName} さん / ${bizDateKey(r.date)} ${r.leaveTypeName}（${unitLabel(r.unit)}）`,
    note: '却下した申請は元に戻せません。申請者は必要に応じて再申請できます。',
    confirmLabel: '却下する',
    run: async () => {
      await decideLeave(r.id, 'rejected')
      successMessage.value = `${r.userName} さんの休暇申請を却下しました`
      invalidate()
      await loadRequests()
    },
    fallbackError: '休暇申請の却下に失敗しました',
  })
}

// ------------------------------------------------------------------
// 11. 全員のタイムカードタブ (オーナー)
// ------------------------------------------------------------------
const tcFrom = ref(todayJstPlusDays(-6))
const tcTo = ref(todayJst())
const tcQuery = ref('')
const tcRows = ref<TimecardRow[]>([])
const tcLoading = ref(false)
const tcError = ref('')

/**
 * from > to は入れ替えて解釈し、期間は上限 `TIMECARD_RANGE_MAX_DAYS` 日にクランプする
 * (サーバの 422 を未然に防ぐ)。
 *
 * **日数は「両端含み」で数える**: サーバ (`AttendanceService.GetTimecardAsync`) が
 * `to - from + 1 > TimecardRangeMaxDays` で弾くため、フロントも同じ数え方に揃える。
 * `diffBizDays` は差分 (両端含みより 1 少ない) を返すので、判定には +1、
 * クランプ幅は `-(上限 - 1)` になる。ここを揃えないと「画面が自動で縮めた期間が
 * そのままサーバに 422 で弾かれる」状態になる (/attendance/timecard と同一の式)。
 */
const tcRange = computed(() => {
  let from = tcFrom.value || todayJstPlusDays(-6)
  let to = tcTo.value || todayJst()
  if (from > to) [from, to] = [to, from]
  const capped = diffBizDays(from, to) + 1 > TIMECARD_RANGE_MAX_DAYS
  if (capped) from = addBizDays(to, -(TIMECARD_RANGE_MAX_DAYS - 1))
  return { from, to, capped }
})

const loadTimecard = async () => {
  if (!isOwner.value) return
  tcLoading.value = true
  tcError.value = ''
  try {
    tcRows.value = await timecard(tcRange.value.from, tcRange.value.to, tcQuery.value.trim())
  } catch (e) {
    tcError.value = getApiErrorMessage(e, 'タイムカードの取得に失敗しました')
    tcRows.value = []
  } finally {
    tcLoading.value = false
  }
}
const tcTotalMinutes = computed(() => tcRows.value.reduce((s, r) => s + r.workMinutes, 0))
const tcActiveFilters = computed(() => (tcQuery.value.trim() ? 1 : 0))
const resetTimecardFilter = () => {
  tcFrom.value = todayJstPlusDays(-6)
  tcTo.value = todayJst()
  tcQuery.value = ''
  void loadTimecard()
}

// ------------------------------------------------------------------
// 12. 休暇管理タブ (オーナー)
// ------------------------------------------------------------------
const leaveAdminRows = ref<LeaveAdminRow[]>([])
const leaveAdminLoading = ref(false)
const leaveAdminError = ref('')
const leaveAdminQuery = ref('')

const loadLeaveAdmin = async () => {
  if (!isOwner.value) return
  leaveAdminLoading.value = true
  leaveAdminError.value = ''
  try {
    leaveAdminRows.value = await leaveAdminSummary()
  } catch (e) {
    leaveAdminError.value = getApiErrorMessage(e, '休暇管理情報の取得に失敗しました')
    leaveAdminRows.value = []
  } finally {
    leaveAdminLoading.value = false
  }
  await Promise.all([loadMembers(), loadLeaveTypes()])
}

const filteredLeaveAdminRows = computed(() => {
  const q = leaveAdminQuery.value.trim().toLowerCase()
  if (!q) return leaveAdminRows.value
  return leaveAdminRows.value.filter((r) => r.userName.toLowerCase().includes(q))
})

/** 手動付与できる休暇種別 (周期自動付与の種別は手動付与の対象外 = §5.4)。 */
const manualGrantTypes = computed(() => leaveTypeItems.value.filter((t) => t.isActive && t.grantMethod !== 'periodic'))
const manualGrantTypeOptions = computed(() => manualGrantTypes.value.map((t) => ({ id: t.id, name: t.name })))
const activeLeaveTypeOptions = computed(() =>
  leaveTypeItems.value.filter((t) => t.isActive).map((t) => ({ id: t.id, name: t.name })))

// ------------------------------------------------------------------
// 13. 設定タブ (勤怠ルール・オーナー)
// ------------------------------------------------------------------
const rules = ref<AttendanceRuleDto[]>([])
const rulesLoading = ref(false)
const rulesError = ref('')
const rulesIncludeInactive = ref(false)

const loadRules = async () => {
  rulesLoading.value = true
  rulesError.value = ''
  try {
    rules.value = await attendanceRules(rulesIncludeInactive.value)
  } catch (e) {
    rulesError.value = getApiErrorMessage(e, '勤怠ルールの取得に失敗しました')
    rules.value = []
  } finally {
    rulesLoading.value = false
  }
}
watch(rulesIncludeInactive, () => { if (activeTab.value === 'settings') void loadRules() })

// ------------------------------------------------------------------
// 14. データロードのディスパッチ (タブ・対象メンバー・日付の変化で必要な分だけ取りにいく)
// ------------------------------------------------------------------
const successMessage = ref('')
const writeBusy = ref(false)
/** 書込操作・入力検証のエラー (モーダル内と画面上部のエラー帯の両方に出す)。 */
const formError = ref('')

/** 表示中のタブのサマリを再取得する (承認等でデータが変わったとき用)。 */
const reloadActiveSummary = async () => {
  if (activeTab.value === 'daily') await loadDaily()
  else if (activeTab.value === 'weekly') await loadWeekly()
  else if (activeTab.value === 'monthly') await loadMonthly()
  else if (activeTab.value === 'leave') await loadLeave()
}

watch([activeTab, effectiveUserId, selectedDate], () => {
  if (!canUseAttendance.value) return
  if (activeTab.value === 'daily') void loadDaily()
  else if (activeTab.value === 'weekly') void loadWeekly()
}, { immediate: true })

watch([activeTab, effectiveUserId, selectedMonth], () => {
  if (!canUseAttendance.value) return
  if (activeTab.value === 'monthly') void loadMonthly()
}, { immediate: true })

watch([activeTab, effectiveUserId], () => {
  if (!canUseAttendance.value) return
  if (activeTab.value === 'leave') void loadLeave()
}, { immediate: true })

watch(activeTab, (tab) => {
  if (!canUseAttendance.value) return
  if (tab === 'requests') void loadRequests()
  else if (tab === 'timecard') void loadTimecard()
  else if (tab === 'leave-admin') void loadLeaveAdmin()
  else if (tab === 'settings') void loadRules()
}, { immediate: true })

onMounted(() => {
  if (!canUseAttendance.value) return
  void loadMembers()
})

/**
 * 書込操作の共通ラッパ。二重送信を防ぎ、失敗はエラー帯に出して主要フローを止めない (原則4)。
 * 成功可否を返すので、モーダルは true のときだけ閉じる (入力を失わせない)。
 */
const runWrite = async (fn: () => Promise<void>, fallback: string): Promise<boolean> => {
  if (writeBusy.value) return false
  writeBusy.value = true
  formError.value = ''
  try {
    await fn()
    return true
  } catch (e) {
    formError.value = getApiErrorMessage(e, fallback)
    return false
  } finally {
    writeBusy.value = false
  }
}

// ------------------------------------------------------------------
// 15. 汎用の確認ダイアログ (却下・論理削除・周期付与の実行など、取り返しの付かない操作の直前確認)
// ------------------------------------------------------------------
interface ConfirmOptions {
  title: string
  message: string
  note?: string
  confirmLabel: string
  tone?: 'danger' | 'primary'
  run: () => Promise<void>
  fallbackError: string
}
const confirmState = ref<ConfirmOptions | null>(null)
const askConfirm = (opts: ConfirmOptions) => {
  formError.value = ''
  successMessage.value = ''
  confirmState.value = opts
}
/**
 * 閉じる側でも `formError` をクリアする (open 側と対にする)。
 * 入力検証のエラーはモーダル内と画面上部の帯の両方に出るため、クリアしないと
 * 「休暇の種別を選択してください」等が、キャンセル後も存在しないフォームの警告として残り続ける。
 * 以降の各モーダルの close も同じ理由で必ずクリアする。
 */
const closeConfirm = () => {
  confirmState.value = null
  formError.value = ''
}
const runConfirm = async () => {
  const state = confirmState.value
  if (!state) return
  const ok = await runWrite(state.run, state.fallbackError)
  if (ok) closeConfirm()
}

// ------------------------------------------------------------------
// 16. 打刻修正の申請モーダル
// ------------------------------------------------------------------
const fixModalOpen = ref(false)
const fixForm = ref({ date: todayJst(), kind: 'in' as PunchKind, time: '09:00', reason: '' })

const openFixModal = (punch?: PunchDto) => {
  formError.value = ''
  successMessage.value = ''
  fixForm.value = {
    date: selectedDate.value,
    kind: punch?.kind ?? 'in',
    // 既存打刻を指定して開いた場合は、その打刻の現在値を初期値にする (どこを直すのかが一目で分かる)。
    // fmtJstHm は 24 時間制・ゼロ埋めの "HH:mm" を返す ('9:30' のような非ゼロ埋めだと
    // <input type="time"> が値を受け付けず入力欄が空になるため、この保証が要る)。
    time: punch ? fmtJstHm(punch.at) : '09:00',
    reason: '',
  }
  fixModalOpen.value = true
}
const closeFixModal = () => {
  fixModalOpen.value = false
  formError.value = ''
}

const submitFix = async () => {
  const f = fixForm.value
  if (!f.date) { formError.value = '対象の日付を入力してください'; return }
  // 秒付き ("09:00:00") も受ける: <input type="time"> は step 指定やブラウザによって秒を返す。
  // 送信値は toJstOffsetString が HH:mm へ丸める。
  if (!/^\d{2}:\d{2}(:\d{2})?$/.test(f.time)) { formError.value = '修正後の時刻を HH:mm で入力してください'; return }
  if (!f.reason.trim()) { formError.value = '修正理由を入力してください（客観的記録の担保）'; return }
  // requestedAt は JST のオフセット付きで送る (サーバは DateTimeOffset で受けて UTC 化する)。
  // 組み立ては toJstOffsetString に任せる: <input type="time"> は環境により "09:00:00" を返すため、
  // 素朴なテンプレート結合だと "...T09:00:00:00+09:00" になり 422 になる。
  const requestedAt = toJstOffsetString(f.date, f.time)
  const ok = await runWrite(async () => {
    await requestFix({ date: f.date, kind: f.kind, requestedAt, reason: f.reason.trim() })
    successMessage.value = '打刻修正を申請しました。承認されると打刻に反映されます。'
    invalidate()
    await loadDaily()
    if (activeTab.value === 'requests') await loadRequests()
  }, '打刻修正の申請に失敗しました')
  if (ok) closeFixModal()
}

// ------------------------------------------------------------------
// 17. 休暇の申請モーダル
// ------------------------------------------------------------------
const leaveModalOpen = ref(false)
// unit は 'full' | 'half' に固定する (string へ広げると不正値が API まで素通りするため)。
const leaveForm = ref({ leaveTypeId: '', date: todayJst(), unit: 'full' as LeaveUnit, reason: '' })

// AutoComplete は string を emit するため、休暇単位として妥当な値のみ受け付ける
// (不正値は無視して現在値を保持する = 不正な単位で申請させない)。
const setLeaveUnit = (v: string) => {
  if (v === 'full' || v === 'half') leaveForm.value.unit = v
}

const openLeaveModal = async () => {
  formError.value = ''
  successMessage.value = ''
  await loadLeaveTypes()
  leaveForm.value = {
    leaveTypeId: activeLeaveTypeOptions.value[0]?.id ?? '',
    date: todayJst(),
    unit: 'full',
    reason: '',
  }
  leaveModalOpen.value = true
}
const closeLeaveModal = () => {
  leaveModalOpen.value = false
  formError.value = ''
}

const submitLeave = async () => {
  const f = leaveForm.value
  if (!f.leaveTypeId) { formError.value = '休暇の種別を選択してください'; return }
  if (!f.date) { formError.value = '取得する日付を入力してください'; return }
  const ok = await runWrite(async () => {
    await requestLeave({ leaveTypeId: f.leaveTypeId, date: f.date, unit: f.unit, reason: f.reason.trim() })
    successMessage.value = '休暇を申請しました。承認されると残数に反映されます。'
    invalidate()
    await loadLeave()
    if (activeTab.value === 'requests') await loadRequests()
  }, '休暇の申請に失敗しました')
  if (ok) closeLeaveModal()
}

// ------------------------------------------------------------------
// 18. 休暇付与モーダル (個別 / 一括 / 周期実行)
// ------------------------------------------------------------------
const grantModalOpen = ref(false)
const grantForm = ref({ userId: '', leaveTypeId: '', grantDate: todayJst(), days: 1 })

const openGrantModal = async () => {
  formError.value = ''
  successMessage.value = ''
  await Promise.all([loadMembers(), loadLeaveTypes()])
  grantForm.value = {
    userId: '',
    leaveTypeId: manualGrantTypeOptions.value[0]?.id ?? '',
    grantDate: todayJst(),
    days: 1,
  }
  grantModalOpen.value = true
}
const closeGrantModal = () => {
  grantModalOpen.value = false
  formError.value = ''
}

const submitGrant = async () => {
  const f = grantForm.value
  if (!f.userId) { formError.value = '付与するメンバーを選択してください'; return }
  if (!f.leaveTypeId) { formError.value = '休暇の種別を選択してください'; return }
  if (!f.grantDate) { formError.value = '付与日を入力してください'; return }
  if (!(f.days > 0)) { formError.value = '付与日数は 0 より大きい値を入力してください'; return }
  const ok = await runWrite(async () => {
    const res = await grantLeave({
      userId: f.userId,
      leaveTypeId: f.leaveTypeId,
      grantDate: f.grantDate,
      days: Number(f.days),
    })
    const name = members.value.find((m) => m.id === f.userId)?.displayName ?? 'メンバー'
    successMessage.value = res.skipped
      ? `${name} には同じ付与日の付与が既にあるため、スキップしました（重複付与は行いません）`
      : `${name} に ${f.days} 日を付与しました`
    invalidate()
    await loadLeaveAdmin()
  }, '休暇の付与に失敗しました')
  if (ok) closeGrantModal()
}

const bulkModalOpen = ref(false)
const bulkForm = ref({ leaveTypeId: '', grantDate: todayJst(), days: 1 })

/**
 * 一括付与の対象を表す確認文言。人数は在籍中メンバー一覧 (/users) の件数を使う。
 * 一覧を取得できていないときは人数を伏せる (確認文言に推測値を出さない)。
 */
const bulkTargetLabel = computed(() =>
  members.value.length > 0 ? `在籍中の全メンバー ${members.value.length} 名` : '在籍中の全メンバー')

const openBulkModal = async () => {
  formError.value = ''
  successMessage.value = ''
  // 確認ダイアログに対象人数を出すため、種別と併せてメンバー一覧も読む (個別付与と同じ経路・原則3)。
  await Promise.all([loadMembers(), loadLeaveTypes()])
  bulkForm.value = { leaveTypeId: manualGrantTypeOptions.value[0]?.id ?? '', grantDate: todayJst(), days: 1 }
  bulkModalOpen.value = true
}
const closeBulkModal = () => {
  bulkModalOpen.value = false
  formError.value = ''
}

/**
 * 一括付与は**在籍者全員**が対象で、**付与を取り消す API は無い**。
 * 影響範囲が同等の「周期付与を実行」と同じく確認ダイアログを挟む (取り返しの付かない操作は手前で止める)。
 * 確認文言には対象人数と付与日数を必ず出す。
 */
const submitBulk = () => {
  const f = bulkForm.value
  if (!f.leaveTypeId) { formError.value = '休暇の種別を選択してください'; return }
  if (!f.grantDate) { formError.value = '付与日を入力してください'; return }
  if (!(f.days > 0)) { formError.value = '付与日数は 0 より大きい値を入力してください'; return }
  const typeName = manualGrantTypeOptions.value.find((t) => t.id === f.leaveTypeId)?.name ?? '選択中の種別'
  const days = Number(f.days)
  askConfirm({
    title: '休暇を一括付与しますか？',
    message: `${bulkTargetLabel.value}に「${typeName}」を ${days} 日ずつ付与します（付与日 ${f.grantDate}）。`,
    note: '一括付与を取り消す機能はありません（付与の記録は保護され、まとめて取り消せません）。既に同じ付与日の付与があるメンバーはスキップします。',
    confirmLabel: '一括付与する',
    tone: 'primary',
    run: async () => {
      const res = await bulkGrantLeave({
        leaveTypeId: f.leaveTypeId,
        grantDate: f.grantDate,
        days,
        target: 'all',
      })
      successMessage.value = `一括付与しました（付与 ${res.granted} 件 / 既存のためスキップ ${res.skipped} 件）`
      invalidate()
      await loadLeaveAdmin()
      // 確認ダイアログは runConfirm が閉じる。入力元のモーダルは成功時のみここで閉じる
      // (失敗時は開いたままにして入力を失わせない)。
      closeBulkModal()
    },
    fallbackError: '一括付与に失敗しました',
  })
}

/** 周期自動付与の実行。既存の付与は絶対に更新・削除せず、重複分は skipped として数える (冪等)。 */
const confirmPeriodicRun = () => {
  askConfirm({
    title: '周期付与を実行しますか？',
    message: '入社日を基準に、法定有給の周期自動付与を対象メンバー全員へ生成します。',
    note: '既に生成済みの付与は「スキップ」として数え、既存の付与日数・消化履歴は一切変更しません（何度実行しても安全です）。',
    confirmLabel: '実行する',
    tone: 'primary',
    run: async () => {
      const res = await runPeriodicGrants()
      successMessage.value = `周期付与を実行しました（付与 ${res.granted} 件 / 既存のためスキップ ${res.skipped} 件）`
      invalidate()
      await loadLeaveAdmin()
    },
    fallbackError: '周期付与の実行に失敗しました',
  })
}

// ------------------------------------------------------------------
// 19. 勤怠ルールの追加・編集モーダル
// ------------------------------------------------------------------
const ruleModalOpen = ref(false)
const ruleEditingId = ref<string | null>(null)
const emptyRuleForm = () => ({
  name: '',
  workStart: '09:00',
  workEnd: '18:00',
  breakMinutes: 60,
  flexEnabled: false,
  flexCoreStart: '10:00',
  flexCoreEnd: '15:00',
  flexSettlementMonths: 1,
  closingDay: 31,
  legalHolidayWeekday: 0,
  isDefault: false,
  isActive: true,
})
const ruleForm = ref(emptyRuleForm())

const openRuleModal = (rule?: AttendanceRuleDto) => {
  formError.value = ''
  successMessage.value = ''
  if (rule) {
    ruleEditingId.value = rule.id
    ruleForm.value = {
      name: rule.name,
      workStart: rule.workStart,
      workEnd: rule.workEnd,
      breakMinutes: rule.breakMinutes,
      flexEnabled: rule.flexEnabled,
      flexCoreStart: rule.flexCoreStart ?? '10:00',
      flexCoreEnd: rule.flexCoreEnd ?? '15:00',
      flexSettlementMonths: rule.flexSettlementMonths,
      closingDay: rule.closingDay,
      legalHolidayWeekday: rule.legalHolidayWeekday,
      isDefault: rule.isDefault,
      isActive: rule.isActive,
    }
  } else {
    ruleEditingId.value = null
    ruleForm.value = emptyRuleForm()
  }
  ruleModalOpen.value = true
}
const closeRuleModal = () => {
  ruleModalOpen.value = false
  formError.value = ''
}

const HHMM = /^([01]\d|2[0-3]):[0-5]\d$/
/** 入力検証はサーバ (§5.3) と同じ条件・同じ文言にする (どちらで弾かれても案内が変わらない)。 */
const validateRule = (): string => {
  const f = ruleForm.value
  if (!f.name.trim()) return '名称を入力してください'
  if (!HHMM.test(f.workStart) || !HHMM.test(f.workEnd)) return '始業・終業は HH:mm 形式で入力してください'
  if (f.workStart >= f.workEnd) return '終業は始業より後の時刻にしてください'
  if (!(f.breakMinutes >= 0 && f.breakMinutes <= 240)) return '休憩時間は 0〜240 分で入力してください'
  if (!(f.closingDay >= 1 && f.closingDay <= 31)) return '締め日は 1〜31 で入力してください'
  if (!(f.legalHolidayWeekday >= 0 && f.legalHolidayWeekday <= 6)) return '法定休日の曜日を選択してください'
  if (!(f.flexSettlementMonths >= 1 && f.flexSettlementMonths <= 3)) return '清算期間は 1〜3 ヶ月で入力してください'
  if (f.flexEnabled) {
    if (!HHMM.test(f.flexCoreStart) || !HHMM.test(f.flexCoreEnd)) return 'コアタイムは HH:mm 形式で入力してください'
    if (f.flexCoreStart >= f.flexCoreEnd) return 'コアタイムの終了は開始より後の時刻にしてください'
  }
  return ''
}

const submitRule = async () => {
  const message = validateRule()
  if (message) { formError.value = message; return }
  const f = ruleForm.value
  const body = {
    name: f.name.trim(),
    workStart: f.workStart,
    workEnd: f.workEnd,
    breakMinutes: Number(f.breakMinutes),
    flexEnabled: f.flexEnabled,
    // フレックス無効時はコアタイムを保持しない (無効なのに値が残ると設定意図が読めなくなる)。
    flexCoreStart: f.flexEnabled ? f.flexCoreStart : null,
    flexCoreEnd: f.flexEnabled ? f.flexCoreEnd : null,
    flexSettlementMonths: Number(f.flexSettlementMonths),
    closingDay: Number(f.closingDay),
    legalHolidayWeekday: Number(f.legalHolidayWeekday),
    isDefault: f.isDefault,
    isActive: f.isActive,
  }
  const editingId = ruleEditingId.value
  const ok = await runWrite(async () => {
    if (editingId) {
      // 全項目を送る「全量更新」。PATCH は部分更新だが、モーダルは全項目を編集させるため
      // 送っていない項目が既定値で潰れる心配は無い。
      await updateAttendanceRule(editingId, body)
      successMessage.value = `勤怠ルール「${body.name}」を更新しました`
    } else {
      await createAttendanceRule(body)
      successMessage.value = `勤怠ルール「${body.name}」を追加しました`
    }
    // ルールは所定労働時間・法定休日の判定に効くため、集計キャッシュを破棄して次回再計算させる。
    invalidate()
    await loadRules()
  }, '勤怠ルールの保存に失敗しました')
  if (ok) closeRuleModal()
}

const confirmDeleteRule = (rule: AttendanceRuleDto) => {
  askConfirm({
    title: '勤怠ルールを削除しますか？',
    message: `「${rule.name}」を論理削除します。`,
    note: '論理削除のため、一覧の「無効・削除済みを含む」を有効にすればいつでも復元できます。',
    confirmLabel: '削除する',
    run: async () => {
      await deleteAttendanceRule(rule.id)
      successMessage.value = `勤怠ルール「${rule.name}」を削除しました（復元できます）`
      invalidate()
      await loadRules()
    },
    fallbackError: '勤怠ルールの削除に失敗しました',
  })
}

const restoreRule = async (rule: AttendanceRuleDto) => {
  await runWrite(async () => {
    await restoreAttendanceRule(rule.id)
    successMessage.value = `勤怠ルール「${rule.name}」を復元しました`
    invalidate()
    await loadRules()
  }, '勤怠ルールの復元に失敗しました')
}

/** 既定ルールの付け替えは他ルールの既定を外す。UI にも明示して不意の切替を防ぐ。 */
const currentDefaultRuleName = computed(() =>
  rules.value.find((r) => r.isDefault && !r.deletedAt && r.id !== ruleEditingId.value)?.name ?? '')
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-3 py-3">
    <!-- ===================== ヘッダ ===================== -->
    <header class="mb-3 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 class="text-2xl font-bold">勤怠管理</h1>
        <p class="mt-1 text-sm text-gray-500">
          打刻の集計・休暇・各種申請を確認します。打刻そのものは
          <NuxtLink to="/attendance/timecard" class="text-blue-600 hover:underline">タイムカード</NuxtLink>
          から行います。
        </p>
      </div>
      <div v-if="isOwner" class="rounded-md border border-blue-200 bg-blue-50 px-3 py-1.5 text-xs text-blue-800">
        オーナー権限：全員のタイムカード・承認・休暇付与・設定を利用できます
      </div>
    </header>

    <!-- 勤怠権限なし (0) は本画面を使えない -->
    <div
      v-if="!canUseAttendance"
      class="rounded-lg border border-amber-300 bg-amber-50 px-4 py-6 text-sm text-amber-800"
    >
      <p class="font-semibold">勤怠機能の利用権限がありません。</p>
      <p class="mt-1">管理者に「勤怠権限」の付与を依頼してください。</p>
      <NuxtLink to="/" class="mt-3 inline-block rounded-md border border-amber-300 bg-white px-3 py-1.5 text-amber-800 hover:bg-amber-100">
        ホームへ戻る
      </NuxtLink>
    </div>

    <template v-else>
      <!-- ===================== タブバー ===================== -->
      <nav class="mb-3 overflow-x-auto border-b border-gray-200">
        <div class="flex items-center gap-1">
          <button
            v-for="t in visibleTabs"
            :key="t"
            type="button"
            class="whitespace-nowrap border-b-2 px-3 py-2 text-sm font-medium transition"
            :class="activeTab === t
              ? 'border-blue-600 text-blue-700'
              : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-800'"
            :aria-current="activeTab === t ? 'page' : undefined"
            @click="selectTab(t)"
          >
            {{ TAB_META[t].label }}
            <!-- 承認待ちの取得に失敗しているときは件数を出さない (古い件数を残さない・原則4) -->
            <span
              v-if="t === 'requests' && isOwner && !pendingError && pendingCount > 0"
              class="ml-1 inline-flex items-center rounded-full bg-amber-100 px-1.5 py-0.5 text-[11px] font-semibold text-amber-800"
            >{{ pendingCount }}</span>
          </button>
        </div>
      </nav>

      <!-- ===================== 共通の通知帯 ===================== -->
      <div v-if="successMessage" class="mb-3 flex items-start gap-2 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
        <span class="flex-1 whitespace-pre-line">{{ successMessage }}</span>
        <button type="button" class="text-xs text-green-700 hover:underline" @click="successMessage = ''">閉じる</button>
      </div>
      <div v-if="formError" class="mb-3 flex items-start gap-2 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
        <span class="flex-1 whitespace-pre-line">{{ formError }}</span>
        <button type="button" class="text-xs text-red-700 hover:underline" @click="formError = ''">閉じる</button>
      </div>

      <!-- ===================== 対象メンバー切替 (オーナーのみ) ===================== -->
      <section
        v-if="isOwner && MEMBER_SWITCH_TABS.includes(activeTab)"
        class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm"
      >
        <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
          <span class="text-sm font-medium text-gray-700">対象メンバー</span>
          <div class="w-full sm:w-72">
            <MasterSelect
              :model-value="targetUserId || null"
              :items="memberOptions"
              allow-empty
              :empty-label="`自分（${myDisplayName}）`"
              placeholder="メンバーを検索…"
              @update:model-value="(v) => targetUserId = v ?? ''"
            />
          </div>
          <span class="text-xs text-gray-500">
            表示中: {{ targetUserName }}{{ isViewingSelf ? '（自分）' : '' }}
          </span>
          <!-- 候補が空のとき「在籍者がいない」と取り違えさせない (原則4) -->
          <span v-if="membersUnavailable" class="text-xs text-amber-700">
            メンバー一覧を取得できませんでした。切替候補が表示されない場合があります。
          </span>
          <button
            v-if="!isViewingSelf"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1 text-xs text-gray-600 hover:bg-gray-50 sm:ml-auto"
            @click="targetUserId = ''"
          >自分の勤怠に戻す</button>
        </div>
      </section>

      <!-- ======================================================== -->
      <!-- タブ: 日次                                                -->
      <!-- ======================================================== -->
      <template v-if="activeTab === 'daily'">
        <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="flex flex-wrap items-center gap-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="selectedDate = addBizDays(selectedDate, -1)">← 前日</button>
            <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100" @click="selectedDate = todayJst()">今日</button>
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="selectedDate = addBizDays(selectedDate, 1)">翌日 →</button>
            <input v-model="selectedDate" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" aria-label="表示する日付" />
            <span class="text-sm font-semibold text-gray-700">{{ fmtBizDate(selectedDate) }}</span>
            <button
              v-if="canWriteAttendance && isViewingSelf"
              type="button"
              class="ml-auto rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
              @click="openFixModal()"
            >打刻修正を申請</button>
          </div>
          <p v-if="canWriteAttendance && isViewingSelf" class="mt-1.5 text-xs text-gray-500">
            打刻を間違えても記録は消えません。「打刻修正を申請」から修正でき、承認されると集計に反映されます（修正前の打刻は履歴として残ります）。
          </p>
        </section>

        <div v-if="dailyError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ dailyError }}</div>
        <div v-if="dailyLoading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

        <!-- 取得に失敗した日は集計カードを描画しない: 0 埋めの「実労働 0:00」を本物の記録と誤読させないため -->
        <div v-else-if="!dailyError" class="grid gap-3 lg:grid-cols-2">
          <!-- 打刻タイムライン -->
          <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">打刻タイムライン</h2>
            <p v-if="dailyTimeline.length === 0" class="px-2 py-6 text-center text-sm text-gray-500">
              この日の打刻がありません。
            </p>
            <ul v-else class="space-y-1.5">
              <li
                v-for="p in dailyTimeline"
                :key="p.id"
                class="rounded-md border px-2 py-1.5"
                :class="p.superseded ? 'border-gray-200 bg-gray-50' : 'border-gray-200 bg-white'"
              >
                <div class="flex flex-wrap items-center gap-2">
                  <span class="inline-block min-w-[68px] rounded-full bg-gray-100 px-2 py-0.5 text-center text-xs font-medium text-gray-700">
                    {{ kindLabel(p.kind) }}
                  </span>
                  <span
                    class="font-mono text-base"
                    :class="p.superseded ? 'text-gray-400 line-through' : 'text-gray-900'"
                  >{{ fmtJstHm(p.at) }}</span>
                  <span v-if="p.superseded" class="rounded-full bg-gray-200 px-2 py-0.5 text-[11px] font-medium text-gray-600">修正前</span>
                  <span v-else-if="p.source === 'fix'" class="rounded-full bg-blue-100 px-2 py-0.5 text-[11px] font-medium text-blue-700">修正反映</span>
                  <!-- 指で押す導線のため 44px 以上のタップ領域を確保する (原則8) -->
                  <button
                    v-if="canWriteAttendance && isViewingSelf && !p.superseded"
                    type="button"
                    class="ml-auto inline-flex min-h-[44px] items-center text-xs text-blue-600 hover:underline"
                    @click="openFixModal(p)"
                  >この打刻を修正</button>
                </div>
                <p v-if="p.fixReason" class="mt-1 text-xs text-gray-600">修正理由: {{ p.fixReason }}</p>
                <p v-if="p.fixedFrom" class="text-xs text-gray-400">
                  修正前の打刻: {{ fmtJstHm(p.fixedFrom) }}
                </p>
              </li>
            </ul>
            <p v-if="dailyTimeline.some((p) => p.superseded)" class="mt-2 text-xs text-gray-500">
              取消線の打刻は修正によって置き換えられた記録です。監査のため削除せず保持しています。
            </p>
          </section>

          <!-- 日次集計 -->
          <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">日次集計</h2>
            <div class="grid grid-cols-3 gap-2">
              <div class="rounded-md border border-gray-200 bg-gray-50 p-2">
                <div class="text-xs text-gray-500">実労働</div>
                <div class="mt-0.5 text-xl font-bold text-gray-800">{{ fmtMinutes(dailySummary?.workMinutes ?? 0) }}</div>
              </div>
              <div class="rounded-md border border-gray-200 bg-gray-50 p-2">
                <div class="text-xs text-gray-500">休憩</div>
                <div class="mt-0.5 text-xl font-bold text-gray-800">{{ fmtMinutes(dailySummary?.breakMinutes ?? 0) }}</div>
              </div>
              <div class="rounded-md border border-gray-200 bg-gray-50 p-2">
                <div class="text-xs text-gray-500">深夜</div>
                <div class="mt-0.5 text-xl font-bold text-gray-800">{{ fmtMinutes(dailySummary?.nightMinutes ?? 0) }}</div>
              </div>
            </div>

            <!-- 休憩不足の警告 (労基法 34 条) -->
            <div
              v-if="(dailySummary?.breakShortage ?? 0) > 0"
              class="mt-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
            >
              <span class="font-semibold">休憩が {{ dailySummary?.breakShortage }} 分不足しています。</span>
              実労働 {{ fmtMinutes(dailySummary?.workMinutes ?? 0) }} には休憩 {{ dailyRequiredBreak }} 分が必要です（労基法 34 条）。
              休憩打刻の漏れであれば「打刻修正を申請」から修正できます。
            </div>

            <h3 class="mt-3 text-sm font-semibold text-gray-700">労働時間の内訳（6 区分）</h3>
            <dl class="mt-1.5 grid grid-cols-2 gap-1.5 sm:grid-cols-3">
              <div v-for="b in BUCKET_LABELS" :key="b.key" class="rounded-md border border-gray-200 px-2 py-1.5">
                <dt class="text-xs text-gray-500">{{ b.label }}</dt>
                <dd class="mt-0.5 font-mono text-sm font-semibold" :class="bucketTextClass(b.key, dailyBuckets[b.key])">
                  {{ fmtMinutes(dailyBuckets[b.key]) }}
                </dd>
              </div>
            </dl>
            <p class="mt-2 text-xs text-gray-400">
              深夜は他の区分と重複して計上されます（割増の対象時間帯を表すため）。
            </p>
          </section>
        </div>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 週次                                                -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'weekly'">
        <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="flex flex-wrap items-center gap-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="shiftWeek(-1)">← 前週</button>
            <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100" @click="selectedDate = todayJst()">今週</button>
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="shiftWeek(1)">翌週 →</button>
            <span class="text-sm font-semibold text-gray-700">
              {{ fmtBizDate(weekStart) }} 〜 {{ fmtBizDate(addBizDays(weekStart, 6)) }}
            </span>
            <span class="text-xs text-gray-400">週の起点は日曜</span>
          </div>
        </section>

        <div v-if="weeklyError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ weeklyError }}</div>
        <div v-if="weeklyLoading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

        <!-- 取得に失敗した週は集計を描画しない (0 埋めを実績と誤読させない。日次と同じ扱い) -->
        <template v-else-if="!weeklyError">
          <!-- 週合計 vs 法定 40h -->
          <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <div class="flex flex-wrap items-baseline justify-between gap-2">
              <h2 class="font-semibold">週合計</h2>
              <span class="text-sm text-gray-600">
                <span class="font-mono text-lg font-bold text-gray-800">{{ fmtMinutes(weekTotalMinutes) }}</span>
                / 法定 {{ fmtMinutes(LEGAL_WEEKLY_MINUTES) }}（{{ weekPercent }}%）
              </span>
            </div>
            <div class="mt-2 h-3 w-full overflow-hidden rounded-full bg-gray-200">
              <div class="h-full rounded-full transition-all" :class="weekBarClass" :style="{ width: `${Math.min(100, weekPercent)}%` }" />
            </div>
            <p v-if="weekLevel === 'crit'" class="mt-2 rounded border border-red-300 bg-red-50 px-3 py-1.5 text-sm text-red-700">
              週の法定労働時間（40 時間）を超えています。36 協定の範囲内か確認してください。
            </p>
            <p v-else-if="weekLevel === 'warn'" class="mt-2 rounded border border-amber-300 bg-amber-50 px-3 py-1.5 text-sm text-amber-800">
              週の法定労働時間の 80% に達しています。
            </p>
          </section>

          <!-- 7 日分 (PC=テーブル / モバイル=カード) -->
          <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">日別の内訳（行をクリックで日次へ）</h2>
            <!-- 曜日のグレー表示は法定休日の曜日に依存する。取得できていないと日曜へ倒れるため注記する
                 （月次カレンダーの凡例と同じ扱い・原則4） -->
            <p v-if="rulesUnavailable" class="mb-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              勤務体系を取得できなかったため、法定休日を日曜として表示しています。実際の法定休日とは異なる場合があります。
            </p>

            <div class="hidden overflow-x-auto md:block">
              <table class="w-full text-sm">
                <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
                  <tr>
                    <th class="px-3 py-2 text-left">日付</th>
                    <th class="px-3 py-2 text-right">実労働</th>
                    <th class="px-3 py-2 text-right">休憩</th>
                    <th class="px-3 py-2 text-right">深夜</th>
                    <th class="px-3 py-2 text-right">法定外残業</th>
                    <th class="px-3 py-2 text-left">注意</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(d, i) in weekDates"
                    :key="d"
                    class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50"
                    @click="goToDaily(d)"
                  >
                    <td class="px-3 py-2" :class="bizWeekday(d) === legalHolidayWeekday ? 'text-gray-400' : ''">
                      {{ fmtBizDate(d) }}
                    </td>
                    <td class="px-3 py-2 text-right font-mono">{{ fmtMinutes(weekDays[i]?.workMinutes ?? 0) }}</td>
                    <td class="px-3 py-2 text-right font-mono text-gray-600">{{ fmtMinutes(weekDays[i]?.breakMinutes ?? 0) }}</td>
                    <td class="px-3 py-2 text-right font-mono text-gray-600">{{ fmtMinutes(weekDays[i]?.nightMinutes ?? 0) }}</td>
                    <td class="px-3 py-2 text-right font-mono" :class="bucketTextClass('nonStatutoryOt', weekDays[i]?.buckets.nonStatutoryOt ?? 0)">
                      {{ fmtMinutes(weekDays[i]?.buckets.nonStatutoryOt ?? 0) }}
                    </td>
                    <td class="px-3 py-2">
                      <span v-if="(weekDays[i]?.breakShortage ?? 0) > 0" class="rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-800">休憩不足</span>
                      <span v-if="(weekDays[i]?.buckets.over60Ot ?? 0) > 0" class="ml-1 rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-700">60h超</span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- モバイル: カード型 (原則8) -->
            <ul class="space-y-1.5 md:hidden">
              <li v-for="(d, i) in weekDates" :key="d">
                <button type="button" class="w-full rounded-md border border-gray-200 px-2.5 py-2 text-left hover:bg-blue-50" @click="goToDaily(d)">
                  <div class="flex items-center justify-between">
                    <span class="text-sm font-semibold" :class="bizWeekday(d) === legalHolidayWeekday ? 'text-gray-400' : 'text-gray-800'">
                      {{ fmtBizDate(d) }}
                    </span>
                    <span class="font-mono text-base font-bold text-gray-800">{{ fmtMinutes(weekDays[i]?.workMinutes ?? 0) }}</span>
                  </div>
                  <div class="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-gray-500">
                    <span>休憩 {{ fmtMinutes(weekDays[i]?.breakMinutes ?? 0) }}</span>
                    <span>深夜 {{ fmtMinutes(weekDays[i]?.nightMinutes ?? 0) }}</span>
                    <span :class="bucketTextClass('nonStatutoryOt', weekDays[i]?.buckets.nonStatutoryOt ?? 0)">
                      法定外残業 {{ fmtMinutes(weekDays[i]?.buckets.nonStatutoryOt ?? 0) }}
                    </span>
                    <span v-if="(weekDays[i]?.breakShortage ?? 0) > 0" class="rounded-full bg-amber-100 px-2 text-amber-800">休憩不足</span>
                    <span v-if="(weekDays[i]?.buckets.over60Ot ?? 0) > 0" class="rounded-full bg-red-100 px-2 text-red-700">60h超</span>
                  </div>
                </button>
              </li>
            </ul>
          </section>
        </template>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 月次                                                -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'monthly'">
        <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="flex flex-wrap items-center gap-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="shiftMonth(-1)">← 前月</button>
            <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100" @click="selectedMonth = currentMonthJst()">今月</button>
            <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="shiftMonth(1)">翌月 →</button>
            <input v-model="selectedMonth" type="month" class="rounded-md border border-gray-300 px-2.5 text-sm" aria-label="表示する年月" />
            <span v-if="monthData" class="text-sm text-gray-600">出勤 {{ monthData.workDays }} 日</span>
          </div>
        </section>

        <div v-if="monthlyError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ monthlyError }}</div>
        <div v-if="monthlyLoading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

        <!-- 取得に失敗した月はカレンダー・集計を描画しない (0 埋めを実績と誤読させない。日次と同じ扱い) -->
        <template v-else-if="!monthlyError">
          <!-- カレンダー -->
          <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">月間カレンダー（日をクリックで日次へ）</h2>
            <div class="grid grid-cols-7 gap-1 text-center text-xs font-semibold text-gray-500">
              <div v-for="(w, i) in WEEKDAY_JP" :key="w" :class="i === legalHolidayWeekday ? 'text-gray-400' : ''">{{ w }}</div>
            </div>
            <div class="mt-1 grid grid-cols-7 gap-1">
              <div v-for="(c, i) in calendarCells" :key="i">
                <div v-if="!c.date" class="h-16 rounded-md bg-gray-50 sm:h-20" />
                <button
                  v-else
                  type="button"
                  class="flex h-16 w-full flex-col items-start rounded-md border p-1 text-left transition hover:border-blue-400 hover:bg-blue-50 sm:h-20"
                  :class="[
                    c.isLegalHoliday ? 'border-gray-200 bg-gray-100' : 'border-gray-200 bg-white',
                    c.isToday ? 'ring-2 ring-blue-400' : '',
                  ]"
                  @click="goToDaily(c.date)"
                >
                  <span class="flex w-full items-center gap-1">
                    <span class="text-xs font-semibold" :class="c.isLegalHoliday ? 'text-gray-400' : 'text-gray-700'">{{ c.day }}</span>
                    <span v-if="c.warnBreak" class="h-1.5 w-1.5 rounded-full bg-amber-500" title="休憩不足" />
                    <span v-if="c.warnOver60" class="h-1.5 w-1.5 rounded-full bg-red-500" title="60h 超残業" />
                  </span>
                  <span v-if="c.hasLeave" class="mt-0.5 rounded bg-green-100 px-1 text-[10px] font-medium text-green-700">休暇</span>
                  <span v-if="(c.summary?.workMinutes ?? 0) > 0" class="mt-auto w-full truncate font-mono text-[11px] text-gray-700 sm:text-xs">
                    {{ fmtMinutes(c.summary?.workMinutes ?? 0) }}
                  </span>
                </button>
              </div>
            </div>
            <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500">
              <span><span class="mr-1 inline-block h-2.5 w-2.5 rounded-sm bg-gray-100 align-middle ring-1 ring-gray-300" />法定休日</span>
              <span><span class="mr-1 inline-block h-1.5 w-1.5 rounded-full bg-amber-500 align-middle" />休憩不足</span>
              <span><span class="mr-1 inline-block h-1.5 w-1.5 rounded-full bg-red-500 align-middle" />60h 超残業</span>
              <span><span class="mr-1 rounded bg-green-100 px-1 text-[10px] text-green-700">休暇</span>承認済みの休暇</span>
            </div>
            <!-- 勤務体系を取得できていないと法定休日が日曜へ倒れる。無告知だとグレー塗り・凡例が
                 「日曜が法定休日」と断定してしまうため注記する (原則4) -->
            <p v-if="rulesUnavailable" class="mt-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              勤務体系を取得できなかったため、法定休日を日曜として表示しています。実際の法定休日とは異なる場合があります。
            </p>
            <p v-if="monthLeaveNotice" class="mt-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              {{ monthLeaveNotice }}
            </p>
          </section>

          <div class="grid gap-3 lg:grid-cols-2">
            <!-- 月次 6 バケット合計 -->
            <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">月次合計（6 区分）</h2>
              <dl class="grid grid-cols-2 gap-1.5 sm:grid-cols-3">
                <div v-for="b in BUCKET_LABELS" :key="b.key" class="rounded-md border border-gray-200 px-2 py-1.5">
                  <dt class="text-xs text-gray-500">{{ b.label }}</dt>
                  <dd class="mt-0.5 font-mono text-sm font-semibold" :class="bucketTextClass(b.key, monthTotals[b.key])">
                    {{ fmtMinutes(monthTotals[b.key]) }}
                  </dd>
                </div>
              </dl>
              <p class="mt-2 text-xs text-gray-500">
                出勤日数 {{ monthData?.workDays ?? 0 }} 日 / 実労働合計
                {{ fmtHours((monthData?.days ?? []).reduce((s, d) => s + d.workMinutes, 0)) }}
              </p>
            </section>

            <!-- 36 協定アラート -->
            <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">36 協定の状況</h2>
              <div class="flex flex-wrap items-baseline justify-between gap-2">
                <span class="text-sm text-gray-600">当月の時間外労働（法定外 + 60h 超）</span>
                <span class="text-sm text-gray-600">
                  <span class="font-mono text-lg font-bold text-gray-800">{{ fmtMinutes(monthOtMinutes) }}</span>
                  / 月 {{ fmtMinutes(OT_MONTHLY_LIMIT_MINUTES) }}（{{ monthOtPercent }}%）
                </span>
              </div>
              <div class="mt-2 h-3 w-full overflow-hidden rounded-full bg-gray-200">
                <div class="h-full rounded-full transition-all" :class="monthOtBarClass" :style="{ width: `${Math.min(100, monthOtPercent)}%` }" />
              </div>

              <ul v-if="monthAlerts.length > 0" class="mt-3 space-y-1.5">
                <li
                  v-for="a in monthAlerts"
                  :key="a.code"
                  class="rounded border px-3 py-2 text-sm"
                  :class="a.level === 'crit' ? 'border-red-300 bg-red-50 text-red-700' : 'border-amber-300 bg-amber-50 text-amber-800'"
                >
                  <span class="mr-2 rounded-full px-2 py-0.5 text-[11px] font-semibold" :class="a.level === 'crit' ? 'bg-red-200 text-red-800' : 'bg-amber-200 text-amber-900'">
                    {{ a.level === 'crit' ? '超過' : '注意' }}
                  </span>
                  {{ a.message }}
                  <span class="ml-1 font-mono text-[11px] text-gray-500">{{ a.code }}</span>
                </li>
              </ul>
              <!-- 取得に失敗しているときは「アラートなし」と断定しない。緑帯で問題なしと読ませると
                   実際の 36 協定超過を見落とす (同じ関数内の休暇マーカーと同じく告知する・原則4) -->
              <p v-else-if="alertsUnavailable" class="mt-3 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                36 協定のアラートを取得できませんでした。超過の有無は判断できません（月を切り替えるか、時間をおいて再表示してください）。
              </p>
              <p v-else class="mt-3 rounded border border-green-200 bg-green-50 px-3 py-2 text-sm text-green-700">
                現時点で 36 協定に関するアラートはありません。
              </p>
            </section>
          </div>
        </template>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 休暇                                                -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'leave'">
        <section class="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 class="text-lg font-semibold">{{ targetUserName }} さんの休暇</h2>
          <button
            v-if="canWriteAttendance && isViewingSelf"
            type="button"
            class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
            @click="openLeaveModal"
          >休暇を申請</button>
        </section>

        <div v-if="leaveError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ leaveError }}</div>
        <div v-if="leaveLoading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

        <template v-else-if="leaveData">
          <!-- KPI 3 枚 -->
          <div class="mb-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
            <div class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <div class="text-sm text-gray-500">有給残数</div>
              <div class="mt-1 text-3xl font-bold text-gray-800">{{ leaveData.paidRemaining }}<span class="ml-1 text-base font-normal text-gray-500">日</span></div>
            </div>
            <div class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <div class="text-sm text-gray-500">直近の失効予定</div>
              <div v-if="leaveData.nextExpire" class="mt-1">
                <div class="text-3xl font-bold text-amber-600">{{ leaveData.nextExpire.days }}<span class="ml-1 text-base font-normal text-gray-500">日</span></div>
                <div class="text-xs text-gray-500">{{ leaveData.nextExpire.date }} に失効</div>
              </div>
              <div v-else class="mt-1 text-3xl font-bold text-gray-400">—</div>
            </div>
            <div class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <div class="text-sm text-gray-500">今年度の取得</div>
              <div class="mt-1 text-3xl font-bold text-gray-800">{{ leaveData.paidUsedThisFiscalYear }}<span class="ml-1 text-base font-normal text-gray-500">日</span></div>
              <div class="text-xs text-gray-400">会計年度（4 月起算）</div>
            </div>
          </div>

          <div class="mb-3 grid gap-3 lg:grid-cols-2">
            <!-- 年5日取得義務トラッカー -->
            <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">年 5 日取得義務（労基法 39 条）</h2>
              <template v-if="obligation.applicable">
                <div class="flex flex-wrap items-baseline justify-between gap-2 text-sm text-gray-600">
                  <span>取得済み <span class="font-mono text-lg font-bold text-gray-800">{{ obligation.takenDays }}</span> / {{ obligation.requiredDays }} 日</span>
                  <span>期限 {{ obligation.deadline ?? '—' }}（残り {{ obligation.daysLeft }} 日）</span>
                </div>
                <div class="mt-2 h-3 w-full overflow-hidden rounded-full bg-gray-200">
                  <div
                    class="h-full rounded-full transition-all"
                    :class="obligation.achieved ? 'bg-green-500' : obligation.warn ? 'bg-red-500' : 'bg-blue-500'"
                    :style="{ width: `${obligationPercent}%` }"
                  />
                </div>
                <p v-if="obligation.achieved" class="mt-2 rounded border border-green-200 bg-green-50 px-3 py-1.5 text-sm text-green-700">
                  年 5 日の取得義務を達成しています。
                </p>
                <p v-else-if="obligation.warn" class="mt-2 rounded border border-red-300 bg-red-50 px-3 py-1.5 text-sm text-red-700">
                  期限まで残り {{ obligation.daysLeft }} 日です。あと {{ obligation.requiredDays - obligation.takenDays }} 日の取得が必要です。
                </p>
                <p v-else class="mt-2 text-sm text-gray-500">
                  基準日 {{ obligation.grantDate ?? '—' }} から 1 年以内に 5 日の取得が必要です。
                </p>
              </template>
              <p v-else class="px-2 py-6 text-center text-sm text-gray-500">
                年 5 日取得義務の対象となる付与（10 日以上）がありません。
              </p>
            </section>

            <!-- 特別休暇の残数 -->
            <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
              <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">特別休暇の残数</h2>
              <p v-if="specialLeaveTypes.length === 0" class="px-2 py-6 text-center text-sm text-gray-500">
                特別休暇の付与はありません。
              </p>
              <ul v-else class="space-y-1.5">
                <li v-for="t in specialLeaveTypes" :key="t.leaveTypeId" class="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-md border border-gray-200 px-2.5 py-1.5">
                  <span class="text-sm font-medium text-gray-800">{{ t.leaveTypeName }}</span>
                  <span class="ml-auto font-mono text-sm">
                    残 <span class="text-lg font-bold text-gray-800">{{ t.remaining }}</span> 日
                  </span>
                  <span class="w-full text-xs text-gray-500">
                    付与 {{ t.granted }} 日 / 取得 {{ t.taken }} 日
                    <template v-if="t.nextExpireDate"> / 次回失効 {{ t.nextExpireDate }}</template>
                  </span>
                </li>
              </ul>
            </section>
          </div>

          <!-- 付与・取得履歴 -->
          <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <h2 class="mb-2 border-b border-gray-100 pb-1.5 font-semibold">付与・取得履歴</h2>
            <p v-if="leaveData.history.length === 0" class="px-2 py-6 text-center text-sm text-gray-500">履歴がありません。</p>

            <div v-else class="hidden overflow-x-auto md:block">
              <table class="w-full text-sm">
                <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
                  <tr>
                    <th class="px-3 py-2 text-left">日付</th>
                    <th class="px-3 py-2 text-left">区分</th>
                    <th class="px-3 py-2 text-left">内容</th>
                    <th class="px-3 py-2 text-center">状態</th>
                    <th class="px-3 py-2 text-right">日数</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(h, i) in leaveData.history" :key="`${h.date}-${i}`" class="border-b border-gray-100 last:border-0">
                    <td class="whitespace-nowrap px-3 py-2 font-mono text-gray-600">{{ h.date }}</td>
                    <td class="px-3 py-2">
                      <span class="rounded-full px-2 py-0.5 text-xs" :class="h.kind === 'grant' ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-700'">
                        {{ h.kind === 'grant' ? '付与' : '取得' }}
                      </span>
                    </td>
                    <td class="px-3 py-2 text-gray-700">{{ h.detail }}</td>
                    <td class="px-3 py-2 text-center">
                      <span v-if="h.kind !== 'grant'" class="rounded-full px-2 py-0.5 text-xs" :class="statusClass(h.status)">{{ statusLabel(h.status) }}</span>
                      <span v-else class="text-xs text-gray-400">—</span>
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right font-mono">{{ h.days }}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- モバイル: カード型 (原則8) -->
            <ul v-if="leaveData.history.length > 0" class="space-y-1.5 md:hidden">
              <li v-for="(h, i) in leaveData.history" :key="`m-${h.date}-${i}`" class="rounded-md border border-gray-200 px-2.5 py-2">
                <div class="flex items-center gap-2">
                  <span class="rounded-full px-2 py-0.5 text-xs" :class="h.kind === 'grant' ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-700'">
                    {{ h.kind === 'grant' ? '付与' : '取得' }}
                  </span>
                  <span class="font-mono text-xs text-gray-600">{{ h.date }}</span>
                  <span class="ml-auto font-mono text-sm font-semibold">{{ h.days }}</span>
                </div>
                <p class="mt-1 text-sm text-gray-700">{{ h.detail }}</p>
                <span v-if="h.kind !== 'grant'" class="mt-1 inline-block rounded-full px-2 py-0.5 text-xs" :class="statusClass(h.status)">{{ statusLabel(h.status) }}</span>
              </li>
            </ul>
          </section>
        </template>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 申請                                                -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'requests'">
        <!-- エラー帯は取得単位で出す。片方だけ失敗したとき、成功した側の一覧は表示を続ける (原則4) -->
        <div v-if="pendingError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ pendingError }}</div>
        <div v-if="requestsError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ requestsError }}</div>
        <div v-if="requestsLoading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

        <template v-else>
          <!-- 承認待ち (オーナーのみ)。描画条件は承認待ち専用の pendingError を見る -->
          <section v-if="isOwner" class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <h2 class="mb-2 flex items-center gap-2 border-b border-gray-100 pb-1.5 font-semibold">
              承認待ち
              <span v-if="!pendingError && pendingCount > 0" class="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-800">{{ pendingCount }} 件</span>
            </h2>
            <p v-if="pendingNotice" class="mb-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              {{ pendingNotice }}
            </p>
            <!-- 取得に失敗したときは空状態も一覧も描画しない: エラー帯を出しながら「ありません」と
                 断定すると誤読させる (集計 3 タブ・タイムカードと同じ扱い・原則4) -->
            <p v-if="!pendingError && pendingCount === 0" class="px-2 py-6 text-center text-sm text-gray-500">承認待ちの申請はありません。</p>

            <div v-else-if="!pendingError" class="space-y-3">
              <!-- 承認は即時反映で取り消せない。却下のみ確認ダイアログを挟む仕様のため、注記で補う -->
              <p class="rounded border border-gray-200 bg-gray-50 px-3 py-1.5 text-xs text-gray-600">
                <span class="font-semibold">承認した申請は取り消せません。</span>
                承認すると打刻列・休暇残数へ即時反映されます。誤って承認した打刻は、あらためて「打刻修正を申請」から直してください。
              </p>

              <!-- 打刻修正 -->
              <div v-if="pendingFixes.length > 0">
                <h3 class="mb-1.5 text-sm font-semibold text-gray-700">打刻修正（{{ pendingFixes.length }} 件）</h3>
                <ul class="space-y-1.5">
                  <li v-for="r in pendingFixes" :key="r.id" class="rounded-md border border-amber-200 bg-amber-50/40 px-2.5 py-2">
                    <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                      <span class="font-medium text-gray-800">{{ r.userName }}</span>
                      <span class="font-mono text-sm text-gray-600">{{ bizDateKey(r.date) }}</span>
                      <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ kindLabel(r.kind) }}</span>
                      <span class="font-mono text-sm font-semibold text-gray-800">→ {{ fmtJstHm(r.requestedAt) }}</span>
                    </div>
                    <p class="mt-1 text-sm text-gray-600">理由: {{ r.reason }}</p>
                    <div class="mt-1.5 flex flex-wrap items-center gap-2">
                      <span class="text-xs text-gray-400">申請 {{ formatJstDateTime(r.createdAt) }}</span>
                      <!-- 承認は取り消せない操作。同じカード内の遷移リンクと同様、指で押す前提の
                           44px 以上のタップ領域を確保する (本タブは PC/モバイル共通のカード一覧・原則8) -->
                      <button type="button" :disabled="writeBusy" class="ml-auto inline-flex min-h-[44px] items-center justify-center rounded-md bg-green-600 px-3 py-1 text-sm font-semibold text-white hover:bg-green-700 disabled:opacity-50" @click="approveFix(r)">承認</button>
                      <button type="button" :disabled="writeBusy" class="inline-flex min-h-[44px] items-center justify-center rounded-md border border-red-300 bg-white px-3 py-1 text-sm text-red-600 hover:bg-red-50 disabled:opacity-50" @click="rejectFix(r)">却下</button>
                    </div>
                  </li>
                </ul>
              </div>

              <!-- 休暇 -->
              <div v-if="pendingLeaves.length > 0">
                <h3 class="mb-1.5 text-sm font-semibold text-gray-700">休暇（{{ pendingLeaves.length }} 件）</h3>
                <ul class="space-y-1.5">
                  <li v-for="r in pendingLeaves" :key="r.id" class="rounded-md border border-amber-200 bg-amber-50/40 px-2.5 py-2">
                    <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                      <span class="font-medium text-gray-800">{{ r.userName }}</span>
                      <span class="font-mono text-sm text-gray-600">{{ bizDateKey(r.date) }}</span>
                      <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ r.leaveTypeName }}</span>
                      <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ unitLabel(r.unit) }}</span>
                    </div>
                    <p v-if="r.reason" class="mt-1 text-sm text-gray-600">理由: {{ r.reason }}</p>
                    <div class="mt-1.5 flex flex-wrap items-center gap-2">
                      <span class="text-xs text-gray-400">申請 {{ formatJstDateTime(r.createdAt) }}</span>
                      <!-- 打刻修正と同じく、承認・却下に 44px 以上のタップ領域を確保する (原則8) -->
                      <button type="button" :disabled="writeBusy" class="ml-auto inline-flex min-h-[44px] items-center justify-center rounded-md bg-green-600 px-3 py-1 text-sm font-semibold text-white hover:bg-green-700 disabled:opacity-50" @click="approveLeave(r)">承認</button>
                      <button type="button" :disabled="writeBusy" class="inline-flex min-h-[44px] items-center justify-center rounded-md border border-red-300 bg-white px-3 py-1 text-sm text-red-600 hover:bg-red-50 disabled:opacity-50" @click="rejectLeave(r)">却下</button>
                    </div>
                  </li>
                </ul>
              </div>
            </div>
          </section>

          <!-- 自分の申請 (全員) -->
          <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
            <div class="mb-2 flex flex-wrap items-center justify-between gap-2 border-b border-gray-100 pb-1.5">
              <h2 class="font-semibold">自分の申請</h2>
              <div v-if="canWriteAttendance" class="flex gap-2">
                <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100" @click="openFixModal()">打刻修正を申請</button>
                <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100" @click="openLeaveModal">休暇を申請</button>
              </div>
            </div>

            <p v-if="myRequestsNotice" class="mb-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              {{ myRequestsNotice }}
            </p>

            <!-- 承認待ちと同様、取得に失敗したときは空状態・一覧とも描画しない (原則4) -->
            <p v-if="!requestsError && myFixes.length === 0 && myLeaves.length === 0" class="px-2 py-6 text-center text-sm text-gray-500">
              申請はまだありません。
            </p>

            <div v-else-if="!requestsError" class="space-y-3">
              <div v-if="myFixes.length > 0">
                <h3 class="mb-1.5 text-sm font-semibold text-gray-700">打刻修正</h3>
                <ul class="space-y-1.5">
                  <li v-for="r in myFixes" :key="r.id" class="rounded-md border border-gray-200 px-2.5 py-2">
                    <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                      <span class="rounded-full px-2 py-0.5 text-xs font-medium" :class="statusClass(r.status)">{{ statusLabel(r.status) }}</span>
                      <span class="font-mono text-sm text-gray-600">{{ bizDateKey(r.date) }}</span>
                      <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ kindLabel(r.kind) }}</span>
                      <span class="font-mono text-sm text-gray-800">→ {{ fmtJstHm(r.requestedAt) }}</span>
                      <!-- 指で押す導線のため 44px 以上のタップ領域を確保する (原則8) -->
                      <button type="button" class="ml-auto inline-flex min-h-[44px] items-center text-xs text-blue-600 hover:underline" @click="goToDaily(r.date)">その日の勤怠を見る</button>
                    </div>
                    <p class="mt-1 text-sm text-gray-600">理由: {{ r.reason }}</p>
                    <p class="mt-0.5 text-xs text-gray-400">
                      申請 {{ formatJstDateTime(r.createdAt) }}
                      <template v-if="r.decidedByUserName"> / 処理者 {{ r.decidedByUserName }}</template>
                    </p>
                  </li>
                </ul>
              </div>

              <div v-if="myLeaves.length > 0">
                <h3 class="mb-1.5 text-sm font-semibold text-gray-700">休暇</h3>
                <ul class="space-y-1.5">
                  <li v-for="r in myLeaves" :key="r.id" class="rounded-md border border-gray-200 px-2.5 py-2">
                    <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                      <span class="rounded-full px-2 py-0.5 text-xs font-medium" :class="statusClass(r.status)">{{ statusLabel(r.status) }}</span>
                      <span class="font-mono text-sm text-gray-600">{{ bizDateKey(r.date) }}</span>
                      <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ r.leaveTypeName }}</span>
                      <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">{{ unitLabel(r.unit) }}</span>
                    </div>
                    <p v-if="r.reason" class="mt-1 text-sm text-gray-600">理由: {{ r.reason }}</p>
                    <p class="mt-0.5 text-xs text-gray-400">
                      申請 {{ formatJstDateTime(r.createdAt) }}
                      <template v-if="r.decidedByUserName"> / 処理者 {{ r.decidedByUserName }}</template>
                    </p>
                  </li>
                </ul>
              </div>
            </div>
            <p class="mt-2 text-xs text-gray-500">
              却下された申請は再度申請し直せます。承認待ちの内容に誤りがある場合は、正しい内容で申請し直してください。
            </p>
          </section>
        </template>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 全員のタイムカード (オーナー)                        -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'timecard'">
        <FilterPanel title="期間・氏名で絞り込み" storage-key="filters:attendance:timecard" :active-count="tcActiveFilters">
          <div class="flex flex-wrap items-end gap-3">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">日付（から）</span>
              <input v-model="tcFrom" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">日付（まで）</span>
              <input v-model="tcTo" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">氏名</span>
              <input v-model="tcQuery" type="search" placeholder="氏名の一部で検索" class="w-56 max-w-full rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <button type="button" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-blue-700" @click="loadTimecard">検索</button>
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-50" @click="resetTimecardFilter">条件をリセット（直近 7 日）</button>
          </div>
        </FilterPanel>

        <div v-if="tcRange.capped" class="mb-3 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          期間が長いため直近 {{ TIMECARD_RANGE_MAX_DAYS }} 日分（{{ tcRange.from }} 〜 {{ tcRange.to }}）のみ表示しています。期間を狭めてください。
        </div>
        <div v-if="tcError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ tcError }}</div>

        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="mb-2 flex flex-wrap items-center justify-between gap-2 border-b border-gray-100 pb-1.5">
            <h2 class="font-semibold">全員のタイムカード</h2>
            <!-- 取得に失敗したときは件数・合計を出さない (0 件 / 0:00 を実績と誤読させない・原則4) -->
            <span v-if="!tcError" class="text-sm text-gray-600">
              {{ tcRows.length }} 件 / 実労働合計 <span class="font-mono font-semibold">{{ fmtMinutes(tcTotalMinutes) }}</span>
            </span>
          </div>

          <div v-if="tcLoading" class="p-8 text-center text-gray-500">読み込み中…</div>
          <!-- 同上: 失敗時は空状態も一覧も描画しない -->
          <p v-else-if="!tcError && tcRows.length === 0" class="px-2 py-8 text-center text-sm text-gray-500">この期間の打刻がありません。</p>

          <template v-else-if="!tcError">
            <div class="hidden overflow-x-auto md:block">
              <table class="w-full text-sm">
                <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
                  <tr>
                    <th class="px-3 py-2 text-left">日付</th>
                    <th class="px-3 py-2 text-left">名前</th>
                    <th class="px-3 py-2 text-right">出勤</th>
                    <th class="px-3 py-2 text-right">退勤</th>
                    <th class="px-3 py-2 text-right">休憩</th>
                    <th class="px-3 py-2 text-right">労働時間</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(r, i) in tcRows"
                    :key="`${r.userId}-${r.date}-${i}`"
                    class="cursor-pointer border-b border-gray-100 last:border-0 hover:bg-blue-50"
                    @click="goToDaily(r.date, r.userId)"
                  >
                    <td class="whitespace-nowrap px-3 py-2 font-mono text-gray-600">{{ bizDateKey(r.date) }}</td>
                    <td class="px-3 py-2">{{ r.userName }}</td>
                    <td class="px-3 py-2 text-right font-mono">{{ r.inAt ? fmtJstHm(r.inAt) : '—' }}</td>
                    <td class="px-3 py-2 text-right font-mono">{{ r.outAt ? fmtJstHm(r.outAt) : '—' }}</td>
                    <td class="px-3 py-2 text-right font-mono text-gray-600">{{ fmtMinutes(r.breakMinutes) }}</td>
                    <td class="px-3 py-2 text-right font-mono font-semibold">{{ fmtMinutes(r.workMinutes) }}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- モバイル: カード型 (原則8) -->
            <ul class="space-y-1.5 md:hidden">
              <li v-for="(r, i) in tcRows" :key="`m-${r.userId}-${r.date}-${i}`">
                <button type="button" class="w-full rounded-md border border-gray-200 px-2.5 py-2 text-left hover:bg-blue-50" @click="goToDaily(r.date, r.userId)">
                  <div class="flex items-center justify-between">
                    <span class="text-sm font-semibold text-gray-800">{{ r.userName }}</span>
                    <span class="font-mono text-xs text-gray-500">{{ bizDateKey(r.date) }}</span>
                  </div>
                  <div class="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-gray-600">
                    <span>出勤 <span class="font-mono">{{ r.inAt ? fmtJstHm(r.inAt) : '—' }}</span></span>
                    <span>退勤 <span class="font-mono">{{ r.outAt ? fmtJstHm(r.outAt) : '—' }}</span></span>
                    <span>休憩 <span class="font-mono">{{ fmtMinutes(r.breakMinutes) }}</span></span>
                    <span class="font-semibold">労働 <span class="font-mono">{{ fmtMinutes(r.workMinutes) }}</span></span>
                  </div>
                </button>
              </li>
            </ul>
          </template>
        </section>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 休暇管理 (オーナー)                                  -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'leave-admin'">
        <section class="mb-3 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="flex flex-wrap items-center gap-2">
            <input v-model="leaveAdminQuery" type="search" placeholder="氏名で絞り込み" class="w-56 max-w-full rounded-md border border-gray-300 px-2.5 text-sm" />
            <div class="ml-auto flex flex-wrap gap-2">
              <button type="button" class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-blue-700" @click="openGrantModal">個別付与</button>
              <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1.5 text-sm text-blue-700 hover:bg-blue-100" @click="openBulkModal">一括付与</button>
              <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50" @click="confirmPeriodicRun">周期付与を実行</button>
            </div>
          </div>
          <p class="mt-1.5 text-xs text-gray-500">
            付与は同一メンバー・同一種別・同一付与日の重複を自動でスキップします（何度実行しても既存の付与・消化履歴は変わりません）。
          </p>
        </section>

        <div v-if="leaveAdminError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ leaveAdminError }}</div>

        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="mb-2 flex items-center justify-between border-b border-gray-100 pb-1.5">
            <h2 class="font-semibold">メンバー × 種別の付与・取得・残</h2>
            <!-- 取得に失敗したときは件数を出さない (0 件を「該当なし」と誤読させない・原則4) -->
            <span v-if="!leaveAdminError" class="text-xs text-gray-500">{{ filteredLeaveAdminRows.length }} 件</span>
          </div>

          <div v-if="leaveAdminLoading" class="p-8 text-center text-gray-500">読み込み中…</div>
          <!-- 同上: 失敗時は空状態も一覧も描画しない -->
          <p v-else-if="!leaveAdminError && filteredLeaveAdminRows.length === 0" class="px-2 py-8 text-center text-sm text-gray-500">表示できる休暇情報がありません。</p>

          <template v-else-if="!leaveAdminError">
            <div class="hidden overflow-x-auto md:block">
              <table class="w-full text-sm">
                <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
                  <tr>
                    <th class="px-3 py-2 text-left">名前</th>
                    <th class="px-3 py-2 text-left">種別</th>
                    <th class="px-3 py-2 text-right">付与</th>
                    <th class="px-3 py-2 text-right">取得</th>
                    <th class="px-3 py-2 text-right">残</th>
                    <th class="px-3 py-2 text-left">最終付与日</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in filteredLeaveAdminRows" :key="`${r.userId}-${r.leaveTypeId}`" class="border-b border-gray-100 last:border-0">
                    <td class="px-3 py-2">{{ r.userName }}</td>
                    <td class="px-3 py-2 text-gray-700">{{ r.leaveTypeName }}</td>
                    <td class="px-3 py-2 text-right font-mono">{{ r.granted }}</td>
                    <td class="px-3 py-2 text-right font-mono text-gray-600">{{ r.taken }}</td>
                    <td class="px-3 py-2 text-right font-mono font-semibold" :class="r.remaining <= 0 ? 'text-gray-400' : 'text-gray-800'">{{ r.remaining }}</td>
                    <td class="px-3 py-2 font-mono text-gray-500">{{ r.lastGrantDate ?? '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- モバイル: カード型 (原則8) -->
            <ul class="space-y-1.5 md:hidden">
              <li v-for="r in filteredLeaveAdminRows" :key="`m-${r.userId}-${r.leaveTypeId}`" class="rounded-md border border-gray-200 px-2.5 py-2">
                <div class="flex items-center justify-between">
                  <span class="text-sm font-semibold text-gray-800">{{ r.userName }}</span>
                  <span class="text-xs text-gray-600">{{ r.leaveTypeName }}</span>
                </div>
                <div class="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-gray-600">
                  <span>付与 <span class="font-mono">{{ r.granted }}</span></span>
                  <span>取得 <span class="font-mono">{{ r.taken }}</span></span>
                  <span class="font-semibold">残 <span class="font-mono">{{ r.remaining }}</span></span>
                  <span>最終付与 <span class="font-mono">{{ r.lastGrantDate ?? '—' }}</span></span>
                </div>
              </li>
            </ul>
          </template>
        </section>
      </template>

      <!-- ======================================================== -->
      <!-- タブ: 設定 (オーナー)                                      -->
      <!-- ======================================================== -->
      <template v-else-if="activeTab === 'settings'">
        <section class="mb-3 flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 class="text-lg font-semibold">勤怠ルール（勤務体系）</h2>
            <p class="mt-0.5 text-sm text-gray-500">
              所定労働時間・休憩・法定休日の曜日・締め日を定義します。日次集計はこのルールを基準に計算されます。
            </p>
          </div>
          <button type="button" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-700" @click="openRuleModal()">+ ルールを追加</button>
        </section>

        <div class="mb-3 flex items-center gap-4">
          <label class="inline-flex items-center gap-2 text-sm text-gray-600">
            <input v-model="rulesIncludeInactive" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
            無効・削除済みを含む
          </label>
          <!-- 取得に失敗したときは件数を出さない (0 件を「未登録」と誤読させない・原則4) -->
          <span v-if="!rulesError" class="ml-auto text-xs text-gray-500">{{ rules.length }} 件</span>
        </div>

        <div v-if="rulesError" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ rulesError }}</div>

        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div v-if="rulesLoading" class="p-8 text-center text-gray-500">読み込み中…</div>
          <!-- 取得に失敗したときは「未登録」と断定しない。この空状態は集計仕様 (所定 8 時間・法定休日は日曜)
               まで言い切るため、通信失敗時に出すと誤誘導になる (原則4) -->
          <p v-else-if="!rulesError && rules.length === 0" class="px-2 py-8 text-center text-sm text-gray-500">
            勤怠ルールが登録されていません。「+ ルールを追加」から作成してください（未登録の場合は所定 8 時間・法定休日は日曜として集計します）。
          </p>

          <template v-else-if="!rulesError">
            <div class="hidden overflow-x-auto md:block">
              <table class="w-full text-sm">
                <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
                  <tr>
                    <th class="px-3 py-2 text-left">名称</th>
                    <th class="px-3 py-2 text-left">勤務時間</th>
                    <th class="px-3 py-2 text-right">休憩</th>
                    <th class="px-3 py-2 text-left">フレックス</th>
                    <th class="px-3 py-2 text-left">法定休日</th>
                    <th class="px-3 py-2 text-right">締め日</th>
                    <th class="px-3 py-2 text-center">状態</th>
                    <th class="px-3 py-2 text-right">操作</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in rules" :key="r.id" class="border-b border-gray-100 last:border-0" :class="r.deletedAt ? 'bg-gray-50 text-gray-400' : ''">
                    <td class="px-3 py-2">
                      {{ r.name }}
                      <span v-if="r.isDefault" class="ml-1 rounded-full bg-blue-100 px-2 py-0.5 text-xs text-blue-700">既定</span>
                    </td>
                    <td class="px-3 py-2 font-mono">{{ r.workStart }} – {{ r.workEnd }}</td>
                    <td class="px-3 py-2 text-right font-mono">{{ r.breakMinutes }} 分</td>
                    <td class="px-3 py-2 text-xs">
                      <template v-if="r.flexEnabled">
                        あり（コア {{ r.flexCoreStart ?? '—' }}–{{ r.flexCoreEnd ?? '—' }} / 清算 {{ r.flexSettlementMonths }} ヶ月）
                      </template>
                      <template v-else>—</template>
                    </td>
                    <td class="px-3 py-2">{{ WEEKDAY_JP[r.legalHolidayWeekday] }}曜日</td>
                    <td class="px-3 py-2 text-right font-mono">{{ r.closingDay === 31 ? '月末' : r.closingDay }}</td>
                    <td class="px-3 py-2 text-center">
                      <span v-if="r.deletedAt" class="rounded-full bg-gray-200 px-2 py-0.5 text-xs text-gray-600">削除済</span>
                      <span v-else-if="!r.isActive" class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">無効</span>
                      <span v-else class="rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700">有効</span>
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right">
                      <template v-if="!r.deletedAt">
                        <button type="button" class="text-xs text-blue-600 hover:underline" @click="openRuleModal(r)">編集</button>
                        <button type="button" :disabled="writeBusy" class="ml-3 text-xs text-red-600 hover:underline disabled:opacity-40" @click="confirmDeleteRule(r)">削除</button>
                      </template>
                      <button v-else type="button" :disabled="writeBusy" class="text-xs text-green-700 hover:underline disabled:opacity-40" @click="restoreRule(r)">復元</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- モバイル: カード型 (原則8) -->
            <ul class="space-y-1.5 md:hidden">
              <li v-for="r in rules" :key="`m-${r.id}`" class="rounded-md border border-gray-200 px-2.5 py-2" :class="r.deletedAt ? 'bg-gray-50' : ''">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="text-sm font-semibold" :class="r.deletedAt ? 'text-gray-400' : 'text-gray-800'">{{ r.name }}</span>
                  <span v-if="r.isDefault" class="rounded-full bg-blue-100 px-2 py-0.5 text-xs text-blue-700">既定</span>
                  <span v-if="r.deletedAt" class="rounded-full bg-gray-200 px-2 py-0.5 text-xs text-gray-600">削除済</span>
                  <span v-else-if="!r.isActive" class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">無効</span>
                </div>
                <div class="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-gray-600">
                  <span class="font-mono">{{ r.workStart }} – {{ r.workEnd }}</span>
                  <span>休憩 {{ r.breakMinutes }} 分</span>
                  <span>法定休日 {{ WEEKDAY_JP[r.legalHolidayWeekday] }}曜</span>
                  <span>締め日 {{ r.closingDay === 31 ? '月末' : r.closingDay }}</span>
                  <span v-if="r.flexEnabled">フレックスあり</span>
                </div>
                <!-- 破壊的操作を含む導線。指で押す前提のため 44px 以上のタップ領域を確保する (原則8) -->
                <div class="mt-1.5 flex gap-4">
                  <template v-if="!r.deletedAt">
                    <button type="button" class="inline-flex min-h-[44px] items-center text-xs text-blue-600 hover:underline" @click="openRuleModal(r)">編集</button>
                    <button type="button" :disabled="writeBusy" class="inline-flex min-h-[44px] items-center text-xs text-red-600 hover:underline disabled:opacity-40" @click="confirmDeleteRule(r)">削除</button>
                  </template>
                  <button v-else type="button" :disabled="writeBusy" class="inline-flex min-h-[44px] items-center text-xs text-green-700 hover:underline disabled:opacity-40" @click="restoreRule(r)">復元</button>
                </div>
              </li>
            </ul>
          </template>
        </section>
      </template>
    </template>

    <!-- ======================================================== -->
    <!-- モーダル: 打刻修正の申請                                   -->
    <!-- ======================================================== -->
    <Teleport to="body">
      <div
        v-if="fixModalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="fix-modal-title"
        @click.self="closeFixModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="fix-modal-title" class="text-base font-semibold">打刻修正を申請</h2>
            <p class="mt-0.5 text-xs text-gray-500">
              承認されると修正後の打刻が集計に反映されます。修正前の打刻は履歴として残ります（記録は削除されません）。
            </p>
          </header>

          <form class="flex-1 space-y-2.5 overflow-y-auto px-5 py-3" @submit.prevent="submitFix">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">対象の日付 <span class="text-red-500">*</span></span>
              <input v-model="fixForm.date" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">打刻の種別 <span class="text-red-500">*</span></span>
              <AutoComplete
                :model-value="fixForm.kind"
                :options="PUNCH_KIND_OPTIONS"
                :allow-empty="false"
                @update:model-value="(v) => fixForm.kind = (v as PunchKind)"
              />
            </div>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">修正後の時刻 <span class="text-red-500">*</span></span>
              <input v-model="fixForm.time" type="time" class="rounded-md border border-gray-300 px-2.5 text-sm" />
              <span class="text-xs text-gray-500">日本時間（JST）で入力してください。</span>
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">修正理由 <span class="text-red-500">*</span></span>
              <textarea v-model="fixForm.reason" rows="3" maxlength="512" placeholder="例: 出勤打刻を忘れたため（始業時刻に出社していました）" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
              <span class="text-xs text-gray-500">客観的記録の担保のため、理由の入力は必須です。</span>
            </label>

            <div v-if="formError" class="whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {{ formError }}
            </div>
          </form>

          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100" @click="closeFixModal">キャンセル</button>
            <button type="button" :disabled="writeBusy" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50" @click="submitFix">
              {{ writeBusy ? '送信中…' : '申請する' }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>

    <!-- ======================================================== -->
    <!-- モーダル: 休暇の申請                                       -->
    <!-- ======================================================== -->
    <Teleport to="body">
      <div
        v-if="leaveModalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="leave-modal-title"
        @click.self="closeLeaveModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="leave-modal-title" class="text-base font-semibold">休暇を申請</h2>
            <p class="mt-0.5 text-xs text-gray-500">承認されると残数に反映されます。申請内容に誤りがあれば、却下後に申請し直せます。</p>
          </header>

          <form class="flex-1 space-y-2.5 overflow-y-auto px-5 py-3" @submit.prevent="submitLeave">
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">休暇の種別 <span class="text-red-500">*</span></span>
              <MasterSelect
                :model-value="leaveForm.leaveTypeId || null"
                :items="activeLeaveTypeOptions"
                placeholder="種別を選択…"
                @update:model-value="(v) => leaveForm.leaveTypeId = v ?? ''"
              />
              <!-- 選択肢が空のとき「未登録」と「取得できなかった」を取り違えさせない (原則4)。
                   通信失敗で「管理者に確認してください」と案内すると利用者を誤誘導する -->
              <span v-if="leaveTypesUnavailable" class="text-xs text-amber-700">
                休暇種別を取得できませんでした。時間をおいて開き直してください（種別が未登録とは限りません）。
              </span>
              <span v-else-if="activeLeaveTypeOptions.length === 0" class="text-xs text-amber-700">
                利用できる休暇種別がありません。管理者に確認してください。
              </span>
            </div>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">取得日 <span class="text-red-500">*</span></span>
              <input v-model="leaveForm.date" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">単位 <span class="text-red-500">*</span></span>
              <AutoComplete
                :model-value="leaveForm.unit"
                :options="UNIT_OPTIONS"
                :allow-empty="false"
                @update:model-value="setLeaveUnit"
              />
            </div>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">理由 <span class="text-xs font-normal text-gray-400">(任意)</span></span>
              <textarea v-model="leaveForm.reason" rows="3" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
            </label>

            <div v-if="formError" class="whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {{ formError }}
            </div>
          </form>

          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100" @click="closeLeaveModal">キャンセル</button>
            <button type="button" :disabled="writeBusy" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50" @click="submitLeave">
              {{ writeBusy ? '送信中…' : '申請する' }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>

    <!-- ======================================================== -->
    <!-- モーダル: 個別付与                                         -->
    <!-- ======================================================== -->
    <Teleport to="body">
      <div
        v-if="grantModalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="grant-modal-title"
        @click.self="closeGrantModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="grant-modal-title" class="text-base font-semibold">休暇を個別付与</h2>
            <p class="mt-0.5 text-xs text-gray-500">
              同一メンバー・同一種別・同一付与日の付与が既にある場合はスキップします（重複付与になりません）。
            </p>
          </header>

          <form class="flex-1 space-y-2.5 overflow-y-auto px-5 py-3" @submit.prevent="submitGrant">
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">メンバー <span class="text-red-500">*</span></span>
              <MasterSelect
                :model-value="grantForm.userId || null"
                :items="memberOptions"
                placeholder="メンバーを検索…"
                @update:model-value="(v) => grantForm.userId = v ?? ''"
              />
              <!-- 候補が空のとき「在籍者がいない」と取り違えさせない (原則4) -->
              <span v-if="membersUnavailable" class="text-xs text-amber-700">
                メンバー一覧を取得できませんでした。時間をおいて開き直してください。
              </span>
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">休暇の種別 <span class="text-red-500">*</span></span>
              <MasterSelect
                :model-value="grantForm.leaveTypeId || null"
                :items="manualGrantTypeOptions"
                placeholder="種別を選択…"
                @update:model-value="(v) => grantForm.leaveTypeId = v ?? ''"
              />
              <span class="text-xs text-gray-500">周期自動付与の種別（法定有給）は手動付与できません。</span>
              <!-- 選択肢が空のとき「未登録」と「取得できなかった」を取り違えさせない (原則4) -->
              <span v-if="leaveTypesUnavailable" class="text-xs text-amber-700">
                休暇種別を取得できませんでした。時間をおいて開き直してください。
              </span>
            </div>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">付与日 <span class="text-red-500">*</span></span>
              <input v-model="grantForm.grantDate" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">付与日数 <span class="text-red-500">*</span></span>
              <input v-model.number="grantForm.days" type="number" min="0.5" step="0.5" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>

            <div v-if="formError" class="whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {{ formError }}
            </div>
          </form>

          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100" @click="closeGrantModal">キャンセル</button>
            <button type="button" :disabled="writeBusy" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50" @click="submitGrant">
              {{ writeBusy ? '付与中…' : '付与する' }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>

    <!-- ======================================================== -->
    <!-- モーダル: 一括付与                                         -->
    <!-- ======================================================== -->
    <Teleport to="body">
      <div
        v-if="bulkModalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="bulk-modal-title"
        @click.self="closeBulkModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="bulk-modal-title" class="text-base font-semibold">休暇を一括付与</h2>
            <p class="mt-0.5 text-xs text-gray-500">
              在籍中の全メンバーへ同条件で付与します。既に同じ付与日の付与があるメンバーはスキップします。
            </p>
          </header>

          <form class="flex-1 space-y-2.5 overflow-y-auto px-5 py-3" @submit.prevent="submitBulk">
            <div class="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              対象は「在籍中の全メンバー」です（部署・雇用区分での絞り込みは行いません）。
              <span class="font-semibold">一括付与を取り消す機能はありません。</span>
              「一括付与する」を押すと、対象人数と付与日数の確認ダイアログが出ます。
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-sm font-medium">休暇の種別 <span class="text-red-500">*</span></span>
              <MasterSelect
                :model-value="bulkForm.leaveTypeId || null"
                :items="manualGrantTypeOptions"
                placeholder="種別を選択…"
                @update:model-value="(v) => bulkForm.leaveTypeId = v ?? ''"
              />
              <!-- 選択肢が空のとき「未登録」と「取得できなかった」を取り違えさせない (原則4) -->
              <span v-if="leaveTypesUnavailable" class="text-xs text-amber-700">
                休暇種別を取得できませんでした。時間をおいて開き直してください。
              </span>
            </div>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">付与日 <span class="text-red-500">*</span></span>
              <input v-model="bulkForm.grantDate" type="date" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">付与日数 <span class="text-red-500">*</span></span>
              <input v-model.number="bulkForm.days" type="number" min="0.5" step="0.5" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>

            <div v-if="formError" class="whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {{ formError }}
            </div>
          </form>

          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100" @click="closeBulkModal">キャンセル</button>
            <button type="button" :disabled="writeBusy" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50" @click="submitBulk">
              {{ writeBusy ? '付与中…' : '一括付与する' }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>

    <!-- ======================================================== -->
    <!-- モーダル: 勤怠ルールの追加・編集                            -->
    <!-- ======================================================== -->
    <Teleport to="body">
      <div
        v-if="ruleModalOpen"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="rule-modal-title"
        @click.self="closeRuleModal"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-2xl flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="rule-modal-title" class="text-base font-semibold">
              {{ ruleEditingId ? '勤怠ルールを編集' : '勤怠ルールを追加' }}
            </h2>
            <p class="mt-0.5 text-xs text-gray-500">
              所定労働時間は「終業 − 始業 − 休憩」で計算されます。日次・月次の集計はこのルールを基準に再計算されます。
            </p>
          </header>

          <form class="flex-1 space-y-2.5 overflow-y-auto px-5 py-3" @submit.prevent="submitRule">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">名称 <span class="text-red-500">*</span></span>
              <input v-model="ruleForm.name" type="text" maxlength="128" placeholder="例: 標準勤務" class="rounded-md border border-gray-300 px-2.5 text-sm" />
            </label>

            <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-3">
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">始業 <span class="text-red-500">*</span></span>
                <input v-model="ruleForm.workStart" type="time" class="rounded-md border border-gray-300 px-2.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">終業 <span class="text-red-500">*</span></span>
                <input v-model="ruleForm.workEnd" type="time" class="rounded-md border border-gray-300 px-2.5 text-sm" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">休憩（分） <span class="text-red-500">*</span></span>
                <input v-model.number="ruleForm.breakMinutes" type="number" min="0" max="240" class="rounded-md border border-gray-300 px-2.5 text-sm" />
              </label>
            </div>

            <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-2">
              <div class="flex flex-col gap-1">
                <span class="text-sm font-medium">法定休日の曜日 <span class="text-red-500">*</span></span>
                <AutoComplete
                  :model-value="String(ruleForm.legalHolidayWeekday)"
                  :options="WEEKDAY_OPTIONS"
                  :allow-empty="false"
                  @update:model-value="(v) => ruleForm.legalHolidayWeekday = Number(v)"
                />
              </div>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">締め日 <span class="text-red-500">*</span></span>
                <input v-model.number="ruleForm.closingDay" type="number" min="1" max="31" class="rounded-md border border-gray-300 px-2.5 text-sm" />
                <span class="text-xs text-gray-500">31 を指定すると「月末」として扱います。</span>
              </label>
            </div>

            <fieldset class="rounded-md border border-gray-200 p-3">
              <legend class="px-1 text-xs font-semibold text-gray-600">フレックスタイム</legend>
              <label class="inline-flex items-center gap-2 text-sm">
                <input v-model="ruleForm.flexEnabled" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
                フレックスタイム制を有効にする
              </label>
              <div v-if="ruleForm.flexEnabled" class="mt-2 grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-3">
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">コア開始</span>
                  <input v-model="ruleForm.flexCoreStart" type="time" class="rounded-md border border-gray-300 px-2.5 text-sm" />
                </label>
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">コア終了</span>
                  <input v-model="ruleForm.flexCoreEnd" type="time" class="rounded-md border border-gray-300 px-2.5 text-sm" />
                </label>
                <label class="flex flex-col gap-1">
                  <span class="text-sm font-medium">清算期間（月）</span>
                  <input v-model.number="ruleForm.flexSettlementMonths" type="number" min="1" max="3" class="rounded-md border border-gray-300 px-2.5 text-sm" />
                </label>
              </div>
            </fieldset>

            <div class="space-y-1.5">
              <label class="inline-flex items-center gap-2 text-sm">
                <input v-model="ruleForm.isDefault" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
                このルールを既定にする
              </label>
              <p v-if="ruleForm.isDefault" class="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
                既定は 1 件だけです。保存すると
                <template v-if="currentDefaultRuleName">現在の既定「{{ currentDefaultRuleName }}」の既定が外れます。</template>
                <template v-else>他のルールの既定が外れます。</template>
                個別にルールを割り当てていないメンバーは、既定ルールで集計されます。
              </p>
              <label class="inline-flex items-center gap-2 text-sm">
                <input v-model="ruleForm.isActive" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
                有効
              </label>
            </div>

            <div v-if="formError" class="whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {{ formError }}
            </div>
          </form>

          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100" @click="closeRuleModal">キャンセル</button>
            <button type="button" :disabled="writeBusy" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50" @click="submitRule">
              {{ writeBusy ? '保存中…' : '保存' }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>

    <!-- ======================================================== -->
    <!-- モーダル: 汎用の確認ダイアログ (却下・削除・周期付与の実行)  -->
    <!-- ======================================================== -->
    <Teleport to="body">
      <div
        v-if="confirmState"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-modal-title"
        @click.self="closeConfirm"
      >
        <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-md flex-col rounded-lg bg-white shadow-xl">
          <header class="shrink-0 border-b border-gray-200 px-5 py-3">
            <h2 id="confirm-modal-title" class="text-base font-semibold">{{ confirmState.title }}</h2>
          </header>
          <div class="flex-1 space-y-2 overflow-y-auto px-5 py-3">
            <p class="text-sm text-gray-700">{{ confirmState.message }}</p>
            <p v-if="confirmState.note" class="rounded border border-gray-200 bg-gray-50 px-3 py-2 text-xs text-gray-600">
              {{ confirmState.note }}
            </p>
            <div v-if="formError" class="whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {{ formError }}
            </div>
          </div>
          <footer class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100" @click="closeConfirm">キャンセル</button>
            <button
              type="button"
              :disabled="writeBusy"
              class="rounded-md px-4 py-1.5 text-sm font-semibold text-white disabled:opacity-50"
              :class="confirmState.tone === 'primary' ? 'bg-blue-600 hover:bg-blue-700' : 'bg-red-600 hover:bg-red-700'"
              @click="runConfirm"
            >
              {{ writeBusy ? '処理中…' : confirmState.confirmLabel }}
            </button>
          </footer>
        </div>
      </div>
    </Teleport>
  </main>
</template>
