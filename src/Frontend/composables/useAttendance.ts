/**
 * 勤怠 API ラッパー（勤怠移植仕様 §6.3）。
 * Backend `/api/maker/v1/attendance/**` を `useApi()` の apiData / apiFetch 経由で叩く薄いラッパ。
 *
 * ## キャッシュ規約（office 踏襲・§6.3）
 * - **月サマリ（キー `"{userId}:{month}"`）が集計の SoT**。日次・週次はそこから射影し、
 *   日別リクエストを出さない。単日だけサーバ計算すると月内累計が失われ `over60Ot` が常に 0 になるため。
 * - **自分の当日打刻だけは `/attendance/state` を SoT** にする（打刻直後の即時反映）。
 * - **書込（打刻・申請・承認・付与）後は必ず関連キャッシュを破棄して再取得**する（原則6: SoT → キャッシュ）。
 * - `dayRaw`（修正前打刻を含むタイムライン）はキャッシュしない。承認で内容が変わるうえ、
 *   日次タブの表示時に毎回最新が要るため。
 * - **申請一覧（`loadFixRequests` / `leaveRequests`）もキャッシュしない**。承認・却下で内容が変動し、
 *   画面は「自分の申請」と「承認待ち（全員）」を続けて別条件で読むため、単一スロットでは常にミスする。
 * - キャッシュ付きの取得は**進行中リクエストへ合流（coalescing）**させる。キャッシュ判定と書込の間に
 *   `await` が挟まるため、合流が無いと同一キーの並列呼び出し（週次タブの 7 日分など）が
 *   そのまま重複リクエストになる（`coalesce` 参照）。
 *
 * ## 引数の渡し方
 * 取得系は `(対象を絞る引数..., force?)` の**位置引数**に揃えている。userId は「省略 = 自分」を
 * 表すため先頭に置き、`undefined` / 空文字を渡すと自分の勤怠を取る。
 * 末尾の `force` は真を渡したときだけキャッシュを無視して再取得する
 * （非キャッシュの取得系は `force` を持たない）。
 *
 * ## 状態の持ち方
 * 仕様の「モジュールスコープのキャッシュ」は、Nuxt では `useState` で実現する。
 * module スコープの `ref` は SSR 時にリクエスト間で共有され、他ユーザの勤怠が混ざり得る
 * （本アプリは実質 CSR だが、fail-safe な方を採る）。`useState` はアプリ単位で共有され
 * ページ間でキャッシュを引き継ぐため、意図する挙動は同じ。
 * 格納するのは API 由来の plain object のみ（CLAUDE.md Vue 確認ポイント）。
 */

// ============================================
// 型定義（他班のページが import する契約）
// ============================================

export type PunchKind = 'in' | 'out' | 'breakStart' | 'breakEnd'
export type PunchSource = 'web' | 'mobile' | 'fix'
export type PunchState = 'before' | 'working' | 'breaking' | 'done'

/** 申請（打刻修正・休暇）のステータス。 */
export type RequestStatus = 'pending' | 'approved' | 'rejected'
/** 承認/却下アクション（§5.2 #9 / §5.4 #23）。 */
export type DecisionAction = 'approved' | 'rejected'
/** 申請一覧のスコープ。'all'（全員）はオーナーのみ。 */
export type RequestScope = 'self' | 'all'

export interface PunchDto {
  id: string
  kind: PunchKind
  /** UTC タイムスタンプ（表示は必ず JST 変換してから。`fmtJstHm` を使う）。 */
  at: string
  source: PunchSource
  /** 修正打刻が置換した旧打刻の `at`（UTC）。null = 通常打刻。 */
  fixedFrom: string | null
  fixReason: string | null
  approvedByUserId: string | null
}

/** 労働時間の 6 バケット分解（単位: 分）。night は他バケットと独立（重複カウント）。 */
export interface Buckets {
  scheduled: number
  statutoryOt: number
  nonStatutoryOt: number
  over60Ot: number
  night: number
  legalHoliday: number
}

export interface DaySummary {
  /** 業務日付（JST の YYYY-MM-DD）。 */
  date: string
  workMinutes: number
  breakMinutes: number
  nightMinutes: number
  buckets: Buckets
  /** 労基法 34 条の休憩不足分（分）。0 なら適法。 */
  breakShortage: number
  /** 有効打刻（修正打刻による置換を解決済み）。 */
  punches: PunchDto[]
  /** `dayRaw()` でのみ同梱される生打刻（修正前を含む）。 */
  rawPunches?: PunchDto[]
}

export interface MonthSummary {
  /** 月内の全日（1 日〜月末）。打刻の無い日も 0 埋めで含まれる。 */
  days: DaySummary[]
  total: Buckets
  workDays: number
}

/** 36 協定アラート。`code` は HTTP エラーコードではなくアラート識別子（AKB-ATT-*）。 */
export interface Article36Alert {
  level: 'warn' | 'crit'
  code: string
  message: string
}

export interface TimecardRow {
  userId: string
  userName: string
  date: string
  /** 有効打刻の最初の In（UTC）。 */
  inAt: string | null
  /** 有効打刻の最後の Out（UTC）。 */
  outAt: string | null
  workMinutes: number
  breakMinutes: number
}

/** `GET /attendance/state` の応答（punches は生列）。 */
export interface PunchStateSnapshot {
  state: PunchState
  punches: PunchDto[]
}

/** `POST /attendance/punches` の応答（state は打刻後の状態）。 */
export interface PunchResult {
  id: string
  state: PunchState
}

// ---- 打刻修正申請（§5.2）----

export interface FixRequestDto {
  id: string
  userId: string
  userName: string
  date: string
  kind: PunchKind
  /** 修正後の打刻時刻（UTC）。 */
  requestedAt: string
  reason: string
  status: RequestStatus
  decidedByUserId: string | null
  decidedByUserName: string | null
  createdAt: string
}

export interface FixRequestInput {
  /** 業務日付（YYYY-MM-DD）。 */
  date: string
  kind: PunchKind
  /**
   * 修正後の打刻時刻。**JST オフセット付き ISO 文字列** `YYYY-MM-DDTHH:mm:00+09:00` で送る
   * （§5.2: サーバは DateTimeOffset.Parse → UtcDateTime で UTC 化して保存する）。
   * `toJstOffsetString()` で組み立てること。
   */
  requestedAt: string
  reason: string
}

// ---- 勤怠ルール（§5.3）----

export interface AttendanceRuleDto {
  id: string
  name: string
  /** 'HH:mm' */
  workStart: string
  /** 'HH:mm' */
  workEnd: string
  breakMinutes: number
  flexEnabled: boolean
  flexCoreStart: string | null
  flexCoreEnd: string | null
  flexSettlementMonths: number
  closingDay: number
  /** 0=日曜。 */
  legalHolidayWeekday: number
  isDefault: boolean
  isActive: boolean
  deletedAt: string | null
}

export interface AttendanceRuleWriteInput {
  name: string
  workStart: string
  workEnd: string
  breakMinutes: number
  flexEnabled: boolean
  flexCoreStart: string | null
  flexCoreEnd: string | null
  flexSettlementMonths: number
  closingDay: number
  legalHolidayWeekday: number
  isDefault: boolean
  isActive: boolean
}

/**
 * PATCH は部分更新。**送らないフィールドは更新されない**（サーバ側は null を「未指定」として扱う）。
 * 送っていない項目を既定値で潰さないため、変更した項目だけを詰めて送ること。
 */
export type AttendanceRulePatchInput = Partial<AttendanceRuleWriteInput>

// ---- 休暇（§5.4）----

export type LeaveGrantMethod = 'periodic' | 'manual'
export type LeaveUnit = 'full' | 'half'

export interface LeaveTypeDto {
  id: string
  name: string
  grantMethod: LeaveGrantMethod
  /** 失効までの月数。null = 無期限。 */
  expiryMonths: number | null
  /** 法定有給。true の種別は作成・編集・削除できない。 */
  isStatutory: boolean
  description: string
  displayOrder: number
  isActive: boolean
  /** 論理削除日時（UTC）。null = 未削除。`includeInactive=true` の一覧で復元対象を判別する。 */
  deletedAt: string | null
}

export interface LeaveNextExpire {
  date: string
  days: number
}

export interface LeaveByTypeRow {
  leaveTypeId: string
  leaveTypeName: string
  isStatutory: boolean
  granted: number
  taken: number
  remaining: number
  nextExpireDate: string | null
}

/** 年 5 日取得義務（労基法 39 条）のトラッカー。 */
export interface LeaveObligation {
  applicable: boolean
  grantDate: string | null
  deadline: string | null
  requiredDays: number
  takenDays: number
  daysLeft: number
  achieved: boolean
  warn: boolean
}

export interface LeaveHistoryRow {
  date: string
  kind: 'grant' | 'take'
  leaveTypeId: string
  leaveTypeName: string
  detail: string
  status: string
  /** 表示専用の文字列（"+10" / "-0.5" / "—"）。数値計算に使わないこと。 */
  days: string
}

export interface LeaveSummary {
  paidRemaining: number
  paidUsedThisFiscalYear: number
  nextExpire: LeaveNextExpire | null
  byType: LeaveByTypeRow[]
  obligation: LeaveObligation
  history: LeaveHistoryRow[]
}

export interface LeaveRequestDto {
  id: string
  userId: string
  userName: string
  leaveTypeId: string
  leaveTypeName: string
  date: string
  unit: LeaveUnit
  status: RequestStatus
  reason: string
  decidedByUserId: string | null
  decidedByUserName: string | null
  createdAt: string
}

export interface LeaveRequestInput {
  leaveTypeId: string
  /** 取得日（YYYY-MM-DD）。 */
  date: string
  unit: LeaveUnit
  reason: string
}

export interface LeaveGrantInput {
  userId: string
  leaveTypeId: string
  /** 付与日（YYYY-MM-DD）。冪等キーの一部（同一 user × type × date は二重付与されない）。 */
  grantDate: string
  days: number
}

/** 一括付与。対象は honshu に雇用区分・部署の概念が無いため 'all'（在籍中の全ユーザ）のみ。 */
export interface LeaveBulkGrantInput {
  leaveTypeId: string
  days: number
  target: 'all'
  /** 付与日（YYYY-MM-DD）。省略時はサーバが当日を採番する。冪等キーの一部。 */
  grantDate?: string
}

/**
 * 個別付与の結果（バックエンド `LeaveGrantResultDto(Guid Id, int Skipped)`）。
 * 同一 (user, 種別, 付与日) の付与が既にある場合は**既存付与の id** と `skipped=1` を返す
 * （冪等・原則2。既存付与は更新も削除もしない）。`id` は常に非 null のため、
 * **スキップ判定は必ず `skipped` で行う**（`id === null` は構造上成立しない）。
 */
export interface LeaveGrantResult {
  id: string
  skipped: number
}

export interface LeaveGrantBulkResult {
  granted: number
  skipped: number
}

export interface LeaveAdminRow {
  userId: string
  userName: string
  leaveTypeId: string
  leaveTypeName: string
  granted: number
  taken: number
  remaining: number
  lastGrantDate: string | null
}

// ============================================
// 定数
// ============================================

/**
 * タイムカード期間の上限日数（**両端含み**）。
 * **SoT はバックエンド定数 `AttendanceService.TimecardRangeMaxDays`**（§5.1）。
 * フロントは同値を持ち、超過時はサーバ 422 を待たずに期間を丸めて警告表示する。
 *
 * 数え方はサーバの判定式 `to - from + 1 > TimecardRangeMaxDays` に揃える。
 * `diffBizDays` は両端含みより 1 少ない「差分」を返すため、判定は `diffBizDays(from, to) + 1`、
 * クランプ幅は `-(TIMECARD_RANGE_MAX_DAYS - 1)` になる。ここがずれると
 * 「画面が自動で縮めた期間がそのままサーバに 422 で弾かれる」状態になる。
 */
export const TIMECARD_RANGE_MAX_DAYS = 62

// ============================================
// 内部ヘルパー
// ============================================

/** `YYYY-MM` の形式検査（不正値はサーバで 422 になるため、フロントで当月へフォールバックする）。 */
const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/

const normalizeMonth = (month?: string): string =>
  month && MONTH_PATTERN.test(month) ? month : currentMonthJst()

/** `YYYY-MM-DD` の形式検査。date 入力を空にされた場合の不正値を弾く。 */
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/

const normalizeDate = (date?: string): string =>
  date && DATE_PATTERN.test(date) ? date : todayJst()

const monthOf = (date: string): string => date.slice(0, 7)

const nextMonth = (month: string): string => {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  return m === 12 ? `${y + 1}-01` : `${y}-${String(m + 1).padStart(2, '0')}`
}

/** 空値を落としてクエリ文字列を組み立てる（値は必ず encodeURIComponent する）。 */
const qs = (params: Record<string, string | number | boolean | undefined | null>): string => {
  const parts: string[] = []
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    parts.push(`${key}=${encodeURIComponent(String(value))}`)
  }
  return parts.length > 0 ? `?${parts.join('&')}` : ''
}

/** 6 バケットの 0 埋め。集計が未取得のときの表示既定値としてページ側も使う。 */
export const emptyBuckets = (): Buckets => ({
  scheduled: 0, statutoryOt: 0, nonStatutoryOt: 0, over60Ot: 0, night: 0, legalHoliday: 0,
})

/** 月サマリに該当日が無い場合の 0 埋め（防御。サーバは月内全日を返す契約）。 */
const emptyDay = (date: string): DaySummary => ({
  date,
  workMinutes: 0,
  breakMinutes: 0,
  nightMinutes: 0,
  buckets: emptyBuckets(),
  breakShortage: 0,
  punches: [],
})

/**
 * `date`(YYYY-MM-DD) + `time`(HH:mm) → JST オフセット付き ISO 文字列
 * `YYYY-MM-DDTHH:mm:00+09:00`（打刻修正申請の `requestedAt` 用、§5.2）。
 * ローカル TZ に依存させないため、オフセットは固定文字列で付ける。
 */
export const toJstOffsetString = (date: string, time: string): string =>
  `${date}T${time.length === 5 ? time : time.slice(0, 5)}:00+09:00`

// ============================================
// 進行中リクエストの合流（coalescing）
// ============================================

/**
 * 進行中の GET を「キー」で共有するためのマップ。
 *
 * キャッシュ判定（`cache.value.x[key]`）と書込の間には必ず `await` が挟まるため、
 * 同一キーを同時に要求されるとキャッシュミスが並列数だけ重なる。実害が出ていたのは週次タブで、
 * 7 日分を `Promise.all` で射影する際に**同一月の重い `/attendance/month` が 7 本**飛び、
 * 「日別リクエストを出さない」というキャッシュ規約の意図が無効化されていた。
 * 進行中の Promise 自体をキーで共有して 1 本に合流させる。
 *
 * **client 限定**で使う。module スコープの Map は SSR ではリクエスト間で共有され、
 * 他ユーザ向けの応答 Promise を掴み得るため（キャッシュ本体を `useState` にしているのと同じ理由）。
 */
const inflight = new Map<string, Promise<unknown>>()

/**
 * `key` の取得が進行中なら、その Promise に合流させる（新規リクエストを出さない）。
 * `force` のときは合流せず必ず新規に取りに行く（以降の非 force 呼び出しはこの新しい方に合流する）。
 * 解決・失敗どちらでも登録を解除するので、失敗は次回呼び出しで再試行される。
 */
const coalesce = <T>(key: string, force: boolean, run: () => Promise<T>): Promise<T> => {
  if (!import.meta.client) return run()
  const running = inflight.get(key) as Promise<T> | undefined
  if (!force && running) return running
  const p = run().finally(() => {
    // force で上書きされた後に古い Promise が解決した場合、新しい登録を消さない。
    if (inflight.get(key) === p) inflight.delete(key)
  })
  inflight.set(key, p)
  return p
}

// ============================================
// キャッシュ
// ============================================

interface AttendanceCaches {
  /** 自分の当日打刻状態（SoT: GET /attendance/state）。 */
  state: PunchStateSnapshot | null
  /** 月サマリ（集計の SoT）。キー `"{userId}:{month}"`（userId 省略時は空文字 = 自分）。 */
  months: Record<string, MonthSummary>
  /** 36 協定アラート。キー `"{userId}:{endMonth}"`。 */
  alerts: Record<string, Article36Alert[]>
  /** 休暇サマリ。キー userId（空文字 = 自分）。 */
  leaveSummaries: Record<string, LeaveSummary>
  /** 休暇種別。キー includeInactive の文字列。 */
  leaveTypes: Record<string, LeaveTypeDto[]>
}

/**
 * `invalidate()` の破棄対象。
 * 申請一覧（打刻修正・休暇）はキャッシュしない設計のためスコープを持たない（`loadFixRequests` 参照）。
 */
export type AttendanceCacheScope = 'all' | 'state' | 'months' | 'alerts' | 'leave'

const emptyCaches = (): AttendanceCaches => ({
  state: null,
  months: {},
  alerts: {},
  leaveSummaries: {},
  leaveTypes: {},
})

// ============================================
// composable
// ============================================

export const useAttendance = () => {
  const { apiData, apiFetch } = useApi()
  const cache = useState<AttendanceCaches>('attendance:cache', emptyCaches)

  /**
   * キャッシュを破棄する。書込後は必ず呼ぶ（原則6: SoT への書込 → キャッシュ破棄 → 再取得）。
   * 配列で複数スコープをまとめて破棄できる。
   */
  const invalidate = (scope: AttendanceCacheScope | AttendanceCacheScope[] = 'all'): void => {
    const scopes = Array.isArray(scope) ? scope : [scope]
    const hit = (s: AttendanceCacheScope) => scopes.includes('all') || scopes.includes(s)
    if (hit('state')) cache.value.state = null
    if (hit('months')) cache.value.months = {}
    if (hit('alerts')) cache.value.alerts = {}
    if (hit('leave')) {
      cache.value.leaveSummaries = {}
      cache.value.leaveTypes = {}
    }
  }

  // ------------------------------------------
  // 打刻
  // ------------------------------------------

  /** 自分の当日打刻状態。`loadState()` を呼ぶまでは 'before'。 */
  const punchState = computed<PunchState>(() => cache.value.state?.state ?? 'before')

  /** 自分の当日の打刻列（state と同じ SoT）。本日の打刻リスト表示に使う。 */
  const todayPunches = computed<PunchDto[]>(() => cache.value.state?.punches ?? [])

  /** `GET /attendance/state`。キャッシュ済みなら再取得しない（`force` で強制再取得）。 */
  const loadState = async (force = false): Promise<PunchStateSnapshot> => {
    const cached = cache.value.state
    if (!force && cached) return cached
    return await coalesce('state', force, async () => {
      const snapshot = await apiData<PunchStateSnapshot>('/attendance/state')
      cache.value.state = snapshot
      return snapshot
    })
  }

  /**
   * `POST /attendance/punches`。成功後に state / 集計キャッシュを破棄し、`/state` から再取得する。
   * 打刻順序違反はサーバが 409（AKB-SYS-007）を返すため、呼び出し側は
   * `getApiErrorMessage(e, '打刻に失敗しました')` で案内すること。
   */
  const punch = async (kind: PunchKind): Promise<PunchResult> => {
    const result = await apiData<PunchResult>('/attendance/punches', {
      method: 'POST',
      body: { kind },
    })
    invalidate(['state', 'months', 'alerts'])
    await loadState(true)
    return result
  }

  // ------------------------------------------
  // 集計（月サマリが SoT、日次・週次はそこから射影）
  // ------------------------------------------

  /**
   * `GET /attendance/month`。
   * `userId` 省略（undefined / 空文字）で自分、`month` 省略で当月（JST）。
   * 他人を指定できるのはオーナーのみ（サーバが 403 を返す）。
   *
   * 週次タブのように同一月を並列に要求されても、進行中のリクエストへ合流して 1 本にまとめる。
   */
  const monthSummary = async (
    userId?: string,
    month?: string,
    force = false,
  ): Promise<MonthSummary> => {
    const targetMonth = normalizeMonth(month)
    const key = `${userId ?? ''}:${targetMonth}`
    const cached = cache.value.months[key]
    if (!force && cached) return cached
    return await coalesce(`month:${key}`, force, async () => {
      const summary = await apiData<MonthSummary>(
        `/attendance/month${qs({ userId, month: targetMonth })}`,
      )
      cache.value.months[key] = summary
      return summary
    })
  }

  /**
   * 単日サマリを**月キャッシュから射影**する（日別 API を叩かない）。
   * 単日だけサーバ計算すると月内累計が失われ `over60Ot` が常に 0 になるため、この経路を使う。
   */
  const daySummaryFromMonth = async (
    userId: string | undefined,
    date: string,
    force = false,
  ): Promise<DaySummary> => {
    const summary = await monthSummary(userId, monthOf(date), force)
    return summary.days.find((d) => d.date.slice(0, 10) === date) ?? emptyDay(date)
  }

  /**
   * 期間 [from, to]（両端含む）の日次サマリを月キャッシュから射影する。
   * 週次（7 日）・タイムカード（最大 62 日）が月をまたぐケースを 1 経路で扱う。
   * - `from > to` は入れ替えて解釈する（§6.4）
   * - 日付が不正（date 入力を空にした等）なら当日へフォールバックする
   * - 返す日数は `TIMECARD_RANGE_MAX_DAYS` で頭打ちにする（API の期間上限と同値）
   */
  const rangeSummaries = async (
    from: string,
    to: string,
    userId?: string,
    force = false,
  ): Promise<DaySummary[]> => {
    const a = normalizeDate(from)
    const b = normalizeDate(to)
    const [start, end] = a <= b ? [a, b] : [b, a]
    const byDate = new Map<string, DaySummary>()
    let month = monthOf(start)
    const lastMonth = monthOf(end)
    // 期間上限 62 日 = 最大 3 ヶ月。異常な入力で無限ループしないよう上限を設ける（防御）。
    for (let guard = 0; guard < 24; guard++) {
      const summary = await monthSummary(userId, month, force)
      for (const day of summary.days) byDate.set(day.date.slice(0, 10), day)
      if (month === lastMonth) break
      month = nextMonth(month)
    }
    // 添字ループにして反復回数を静的に有界にする（日付文字列の比較でループすると
    // 不正入力時に日付が進まず無限ループになり得るため）。
    const total = Math.min(diffBizDays(start, end) + 1, TIMECARD_RANGE_MAX_DAYS)
    const days: DaySummary[] = []
    for (let i = 0; i < total; i++) {
      const date = addBizDays(start, i)
      days.push(byDate.get(date) ?? emptyDay(date))
    }
    return days
  }

  /** `GET /attendance/alerts`（36 協定）。endMonth 省略時は当月。 */
  const alerts = async (
    userId?: string,
    endMonth?: string,
    force = false,
  ): Promise<Article36Alert[]> => {
    const targetMonth = normalizeMonth(endMonth)
    const key = `${userId ?? ''}:${targetMonth}`
    const cached = cache.value.alerts[key]
    if (!force && cached) return cached
    return await coalesce(`alerts:${key}`, force, async () => {
      const items = await apiData<Article36Alert[]>(
        `/attendance/alerts${qs({ userId, endMonth: targetMonth })}`,
      )
      cache.value.alerts[key] = items
      return items
    })
  }

  /**
   * `GET /attendance/day?raw=true`。**修正前打刻を含む**タイムライン表示専用。
   * 承認で内容が変わるためキャッシュしない（毎回サーバに問い合わせる）。
   */
  const dayRaw = async (userId: string | undefined, date: string): Promise<DaySummary> =>
    await apiData<DaySummary>(`/attendance/day${qs({ userId, date, raw: true })}`)

  /**
   * `GET /attendance/timecard`（全員のタイムカード・**オーナーのみ**）。
   * 既定は当月 1 日 〜 今日。期間上限は `TIMECARD_RANGE_MAX_DAYS`（超過はサーバ 422）。
   */
  const timecard = async (from?: string, to?: string, q?: string): Promise<TimecardRow[]> =>
    await apiData<TimecardRow[]>(
      `/attendance/timecard${qs({
        from: from || `${currentMonthJst()}-01`,
        to: to || todayJst(),
        q,
      })}`,
    )

  // ------------------------------------------
  // 打刻修正申請
  // ------------------------------------------

  /**
   * `GET /attendance/fix-requests`（createdAt 降順）。`scope='all'` はオーナーのみ。
   * 承認・却下で内容が変動するうえ、画面は「自分の申請」と「承認待ち（全員）」を
   * 続けて別条件で読むため、**キャッシュしない**（同じ理由で `leaveRequests` も非キャッシュ）。
   * 再取得が要るときは呼び出し側が明示的に呼び直す。
   */
  const loadFixRequests = async (
    scope: RequestScope = 'self',
    status?: RequestStatus,
  ): Promise<FixRequestDto[]> =>
    await apiData<FixRequestDto[]>(`/attendance/fix-requests${qs({ status, scope })}`)

  /** `POST /attendance/fix-requests`（対象は常に本人）。 */
  const requestFix = async (input: FixRequestInput): Promise<{ id: string }> =>
    await apiData<{ id: string }>('/attendance/fix-requests', {
      method: 'POST',
      body: input,
    })

  /**
   * `POST /attendance/fix-requests/{id}/decision`（**オーナーのみ**）。
   * 承認は打刻列に fix レコードを追記するため、集計・当日状態のキャッシュを破棄する
   * （申請一覧自体は非キャッシュのため破棄対象に無い）。
   */
  const decideFix = async (id: string, action: DecisionAction): Promise<{ id: string }> => {
    const result = await apiData<{ id: string }>(`/attendance/fix-requests/${id}/decision`, {
      method: 'POST',
      body: { action },
    })
    invalidate(['months', 'alerts', 'state'])
    return result
  }

  // ------------------------------------------
  // 勤怠ルール（設定タブ）
  // ------------------------------------------

  /** `GET /attendance/rules`。設定タブは編集のたびに読み直すためキャッシュしない。 */
  const attendanceRules = async (includeInactive = false): Promise<AttendanceRuleDto[]> =>
    await apiData<AttendanceRuleDto[]>(`/attendance/rules${qs({ includeInactive })}`)

  /** `POST /attendance/rules`（オーナーのみ）。`isDefault=true` は他ルールの既定を外す。 */
  const createAttendanceRule = async (input: AttendanceRuleWriteInput): Promise<AttendanceRuleDto> =>
    await apiData<AttendanceRuleDto>('/attendance/rules', { method: 'POST', body: input })

  /** `PATCH /attendance/rules/{id}`（オーナーのみ・部分更新）。 */
  const updateAttendanceRule = async (
    id: string,
    patch: AttendanceRulePatchInput,
  ): Promise<AttendanceRuleDto> =>
    await apiData<AttendanceRuleDto>(`/attendance/rules/${id}`, { method: 'PATCH', body: patch })

  /** `DELETE /attendance/rules/{id}`（論理削除・204）。 */
  const deleteAttendanceRule = async (id: string): Promise<void> => {
    await apiFetch<void>(`/attendance/rules/${id}`, { method: 'DELETE' })
  }

  /** `POST /attendance/rules/{id}/restore`（204）。誤削除からの復帰導線。 */
  const restoreAttendanceRule = async (id: string): Promise<void> => {
    await apiFetch<void>(`/attendance/rules/${id}/restore`, { method: 'POST' })
  }

  // ------------------------------------------
  // 休暇
  // ------------------------------------------

  /** `GET /attendance/leave/summary`。userId 省略時は自分（他人指定はオーナーのみ）。 */
  const leaveSummary = async (userId?: string, force = false): Promise<LeaveSummary> => {
    const key = userId ?? ''
    const cached = cache.value.leaveSummaries[key]
    if (!force && cached) return cached
    return await coalesce(`leaveSummary:${key}`, force, async () => {
      const summary = await apiData<LeaveSummary>(`/attendance/leave/summary${qs({ userId })}`)
      cache.value.leaveSummaries[key] = summary
      return summary
    })
  }

  /** `GET /attendance/leave/types`。 */
  const leaveTypes = async (includeInactive = false, force = false): Promise<LeaveTypeDto[]> => {
    const key = String(includeInactive)
    const cached = cache.value.leaveTypes[key]
    if (!force && cached) return cached
    return await coalesce(`leaveTypes:${key}`, force, async () => {
      const items = await apiData<LeaveTypeDto[]>(`/attendance/leave/types${qs({ includeInactive })}`)
      cache.value.leaveTypes[key] = items
      return items
    })
  }

  /** `GET /attendance/leave/requests`。`scope='all'` はオーナーのみ。承認で変動するためキャッシュしない。 */
  const leaveRequests = async (
    scope: RequestScope = 'self',
    status?: RequestStatus,
  ): Promise<LeaveRequestDto[]> =>
    await apiData<LeaveRequestDto[]>(`/attendance/leave/requests${qs({ scope, status })}`)

  /** `POST /attendance/leave/requests`（本人）。 */
  const requestLeave = async (input: LeaveRequestInput): Promise<{ id: string }> => {
    const result = await apiData<{ id: string }>('/attendance/leave/requests', {
      method: 'POST',
      body: input,
    })
    invalidate('leave')
    return result
  }

  /** `POST /attendance/leave/requests/{id}/decision`（オーナーのみ）。 */
  const decideLeave = async (id: string, action: DecisionAction): Promise<{ id: string }> => {
    const result = await apiData<{ id: string }>(`/attendance/leave/requests/${id}/decision`, {
      method: 'POST',
      body: { action },
    })
    invalidate('leave')
    return result
  }

  /** `POST /attendance/leave/grants`（個別付与・オーナーのみ）。重複付与はサーバがスキップする（冪等）。 */
  const grantLeave = async (input: LeaveGrantInput): Promise<LeaveGrantResult> => {
    const result = await apiData<LeaveGrantResult>('/attendance/leave/grants', {
      method: 'POST',
      body: input,
    })
    invalidate('leave')
    return result
  }

  /** `POST /attendance/leave/grants/bulk`（一括付与・オーナーのみ）。 */
  const bulkGrantLeave = async (input: LeaveBulkGrantInput): Promise<LeaveGrantBulkResult> => {
    const result = await apiData<LeaveGrantBulkResult>('/attendance/leave/grants/bulk', {
      method: 'POST',
      body: input,
    })
    invalidate('leave')
    return result
  }

  /** `POST /attendance/leave/periodic-grants/run`（周期自動付与の手動実行・オーナーのみ）。 */
  const runPeriodicGrants = async (): Promise<LeaveGrantBulkResult> => {
    const result = await apiData<LeaveGrantBulkResult>('/attendance/leave/periodic-grants/run', {
      method: 'POST',
    })
    invalidate('leave')
    return result
  }

  /** `GET /attendance/leave/admin/summary`（メンバー × 種別の付与/取得/残・オーナーのみ）。 */
  const leaveAdminSummary = async (): Promise<LeaveAdminRow[]> =>
    await apiData<LeaveAdminRow[]>('/attendance/leave/admin/summary')

  return {
    // 打刻
    punchState,
    todayPunches,
    loadState,
    punch,
    // 集計
    monthSummary,
    daySummaryFromMonth,
    rangeSummaries,
    alerts,
    dayRaw,
    timecard,
    // 打刻修正申請
    loadFixRequests,
    requestFix,
    decideFix,
    // 勤怠ルール
    attendanceRules,
    createAttendanceRule,
    updateAttendanceRule,
    deleteAttendanceRule,
    restoreAttendanceRule,
    // 休暇
    leaveSummary,
    leaveTypes,
    leaveRequests,
    requestLeave,
    decideLeave,
    grantLeave,
    bulkGrantLeave,
    runPeriodicGrants,
    leaveAdminSummary,
    // キャッシュ
    invalidate,
  }
}
