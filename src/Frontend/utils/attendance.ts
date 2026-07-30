/**
 * 勤怠の表示ユーティリティ（勤怠移植仕様 §6.6）。
 *
 * 日付・時刻そのものの整形は `utils/datetime.ts` が SoT（formatJstDateTime / formatJstDate /
 * toJstDateString / todayJst / todayJstPlusDays / currentMonthJst）。本ファイルは
 * 「分数 → 労働時間表示」「enum → 日本語ラベル」など**勤怠固有の表示**だけを持ち、
 * datetime.ts と同じ機能を二重実装しない（原則3）。
 *
 * datetime.ts に無いものだけをここに置く:
 * - `fmtJstHm` / `nowJstHms` ... 打刻時刻の HH:mm / 現在時刻の HH:mm:ss（datetime.ts は日付・日時のみ）
 * - `bizDateKey` / `addBizDays` / `diffBizDays` / `bizWeekday` / `fmtBizDate` / `startOfBizWeek` /
 *   `addBizMonths` / `bizDaysInMonth` ... 業務日付（YYYY-MM-DD、TZ を持たない）の演算
 *   ローカル TZ 依存を避けるため、業務日付は必ず UTC 正午 0 時アンカーで解釈する。
 *
 * **本ファイルが勤怠画面の表示ユーティリティの SoT**。/attendance と /attendance/timecard は
 * ここから auto-import して使う（ページ側でローカル再実装しない。配色の乖離を生むため・原則3）。
 */
import type {
  ApprovalMode, ApproverRole, ApproverStepDto, ApproverType, AttendanceRouteCategory,
  Buckets, DirectRequestStatus, DirectType, LeaveUnit, PunchKind, PunchState, RequestStatus,
} from '~/composables/useAttendance'

// ============================================
// 時間（分）の表示
// ============================================

/**
 * 分 → "H:MM"（符号付き）。例: 510 → "8:30" / -90 → "-1:30" / 0 → "0:00"。
 * 休憩不足・過不足など負値を扱う箇所があるため符号を保持する。
 */
export const fmtMinutes = (min: number): string => {
  const safe = Number.isFinite(min) ? Math.round(min) : 0
  const sign = safe < 0 ? '-' : ''
  const abs = Math.abs(safe)
  return `${sign}${Math.floor(abs / 60)}:${String(abs % 60).padStart(2, '0')}`
}

/**
 * 分 → "H.HHh"（小数 2 桁固定）。例: 495 → "8.25h" / 510 → "8.50h"。
 * 桁を固定して一覧で桁揃え比較できるようにする（8.5h と 8.25h が混在すると読みにくい）。
 */
export const fmtHours = (min: number): string => {
  const safe = Number.isFinite(min) ? min : 0
  return `${(safe / 60).toFixed(2)}h`
}

// ============================================
// 時刻の表示（JST）
// ============================================

/**
 * UTC タイムスタンプ（打刻 `at` 等）→ JST の "HH:mm"。null / 不正値は "—"。
 * en-GB ロケールは 24 時間表記（h23）が既定のため 0 時が "00:00" になる（ja-JP + hour12:false は
 * 環境により "24:00" になり得るため使わない）。
 */
export const fmtJstHm = (value: string | null | undefined): string => {
  if (!value) return '—'
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleTimeString('en-GB', {
    timeZone: 'Asia/Tokyo',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

/** 現在時刻（JST）の "HH:mm:ss"。打刻カードの時計表示用（1 秒ごとに呼ぶ）。 */
export const nowJstHms = (): string =>
  new Date().toLocaleTimeString('en-GB', {
    timeZone: 'Asia/Tokyo',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })

// ============================================
// 業務日付（YYYY-MM-DD）の演算
// ============================================

/**
 * `'YYYY-MM-DD...'` の先頭 10 文字を取り出して業務日付キーに正規化する。
 * DateOnly（`2026-07-27`）でも ISO タイムスタンプ（`2026-07-27T00:00:00Z`）でも同じキーになる。
 * null / undefined は空文字（表示・Map キーとして安全な値）。
 */
export const bizDateKey = (value: string | null | undefined): string => (value ?? '').slice(0, 10)

/**
 * 業務日付を UTC 0 時アンカーの Date に変換する（内部ヘルパー）。
 * `new Date('2026-07-27')` は UTC 解釈だが、`new Date('2026-07-27T00:00:00')` は
 * ローカル解釈になり実行環境の TZ で日付がずれる。必ず末尾 Z を付けて固定する。
 */
const bizDateUtc = (date: string): Date => new Date(`${bizDateKey(date)}T00:00:00Z`)

/** 業務日付 + N 日 → YYYY-MM-DD（日付ナビの前日/翌日・期間クランプ用）。不正値はそのまま返す。 */
export const addBizDays = (date: string, days: number): string => {
  const d = bizDateUtc(date)
  if (Number.isNaN(d.getTime())) return date
  return new Date(d.getTime() + days * 86400000).toISOString().slice(0, 10)
}

/** 業務日付の日数差（to - from）。同日は 0。不正値は 0。 */
export const diffBizDays = (from: string, to: string): number => {
  const a = bizDateUtc(from)
  const b = bizDateUtc(to)
  if (Number.isNaN(a.getTime()) || Number.isNaN(b.getTime())) return 0
  return Math.round((b.getTime() - a.getTime()) / 86400000)
}

/** 曜日ラベル（index 0=日曜）。カレンダーのヘッダ・法定休日の曜日選択で共有する。 */
export const WEEKDAY_JP = ['日', '月', '火', '水', '木', '金', '土']

/** 業務日付の曜日 index（0=日）。法定休日（既定 0=日曜）判定・週次表示に使う。 */
export const bizWeekday = (date: string): number => {
  const d = bizDateUtc(date)
  return Number.isNaN(d.getTime()) ? 0 : d.getUTCDay()
}

/** 業務日付 → "7/27(月)"。一覧・カレンダーの日付ラベル用。 */
export const fmtBizDate = (date: string): string => {
  const d = bizDateUtc(date)
  if (Number.isNaN(d.getTime())) return date
  return `${d.getUTCMonth() + 1}/${d.getUTCDate()}(${WEEKDAY_JP[d.getUTCDay()]})`
}

/** 業務日付を含む週の起点（**日曜**）へ丸める。週次タブの週境界。 */
export const startOfBizWeek = (date: string): string => addBizDays(date, -bizWeekday(date))

// ============================================
// 業務年月（YYYY-MM）の演算
// ============================================

/** 業務年月を UTC の月初 Date に変換する（内部ヘルパー）。delta で前後の月へずらす。 */
const bizMonthUtc = (month: string, delta = 0): Date => {
  const [year, mon] = month.split('-').map(Number)
  return new Date(Date.UTC(year, (mon - 1) + delta, 1))
}

/** 業務年月 + N ヶ月 → "YYYY-MM"（月次ナビの前月/翌月）。不正値はそのまま返す。 */
export const addBizMonths = (month: string, delta: number): string => {
  const d = bizMonthUtc(month, delta)
  if (Number.isNaN(d.getTime())) return month
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`
}

/** 業務年月の日数（月末日）。月間カレンダーのセル生成用。不正値は 0。 */
export const bizDaysInMonth = (month: string): number => {
  // 翌月 1 日の前日 = 当月の月末日。
  const d = bizMonthUtc(month, 1)
  if (Number.isNaN(d.getTime())) return 0
  return new Date(d.getTime() - 86400000).getUTCDate()
}

// ============================================
// ラベル
// ============================================

/** 打刻種別のラベル（§6.6）。 */
export const PUNCH_KIND_LABELS: Record<PunchKind, string> = {
  in: '出勤',
  out: '退勤',
  breakStart: '休憩開始',
  breakEnd: '休憩終了',
}

/** 打刻状態のラベル（§6.4 状態バッジ）。 */
export const PUNCH_STATE_LABELS: Record<PunchState, string> = {
  before: '未出勤',
  working: '勤務中',
  breaking: '休憩中',
  done: '退勤済み',
}

/** 打刻状態バッジの配色（Tailwind ユーティリティ直書き）。 */
export const PUNCH_STATE_CLASSES: Record<PunchState, string> = {
  before: 'bg-gray-100 text-gray-600',
  working: 'bg-green-100 text-green-700',
  breaking: 'bg-amber-100 text-amber-700',
  done: 'bg-blue-100 text-blue-700',
}

/** 申請ステータスのラベル（打刻修正申請・休暇申請で共通）。 */
export const REQUEST_STATUS_LABELS: Record<RequestStatus, string> = {
  pending: '承認待ち',
  inReview: '承認中',
  approved: '承認済み',
  rejected: '却下',
}

/**
 * 申請ステータスバッジの配色。**同じ意味のバッジが画面ごとに違う色にならないよう本定義を唯一の SoT とする**
 * （ページ側でローカル定義を持たない）。
 */
export const REQUEST_STATUS_CLASSES: Record<RequestStatus, string> = {
  pending: 'bg-amber-100 text-amber-700',
  inReview: 'bg-blue-100 text-blue-700',
  approved: 'bg-green-100 text-green-700',
  rejected: 'bg-gray-100 text-gray-500',
}

/** 休暇の取得単位ラベル。 */
export const LEAVE_UNIT_LABELS: Record<LeaveUnit, string> = {
  full: '全日',
  half: '半日',
}

// ============================================
// 勤怠承認経路（Iteration 33）
// ============================================

/** 直行/直帰の種別ラベル。 */
export const DIRECT_TYPE_LABELS: Record<DirectType, string> = {
  chokkou: '直行',
  chokki: '直帰',
  both: '直行直帰',
}

/** 直行/直帰申請のステータスラベル（申請共通 + 取下げ）。 */
export const DIRECT_STATUS_LABELS: Record<DirectRequestStatus, string> = {
  ...REQUEST_STATUS_LABELS,
  withdrawn: '取下げ',
}

/** 直行/直帰申請のステータスバッジ配色。 */
export const DIRECT_STATUS_CLASSES: Record<DirectRequestStatus, string> = {
  ...REQUEST_STATUS_CLASSES,
  withdrawn: 'bg-gray-100 text-gray-500',
}

/** 承認経路の区分ラベル。 */
export const ROUTE_CATEGORY_LABELS: Record<AttendanceRouteCategory, string> = {
  direct: '直行/直帰申請',
  fix: '打刻修正申請',
}

/** 承認者の指定方法ラベル（役職 / ロール / 個人）。 */
export const APPROVER_TYPE_LABELS: Record<ApproverType, string> = {
  title: '役職',
  role: 'ロール',
  member: '個人',
}

/** 承認経路のロールラベル（honshu の権限ロール）。 */
export const APPROVER_ROLE_LABELS: Record<ApproverRole, string> = {
  owner: 'オーナー（勤怠管理者）',
}

/** 承認方式ラベル（office 踏襲。現状は serial 単承認者として扱う）。 */
export const APPROVAL_MODE_LABELS: Record<ApprovalMode, string> = {
  serial: '直列',
  all: '全員承認',
  majority: '過半数',
}

/**
 * 承認ステップの承認者を短く表す（役職名 / ロール名 / 個人名）。
 * 役職・ロールは値未設定のとき種別名へフォールバック。個人はサーバ解決名、無ければ「個人指定」。
 */
export const approverTargetLabel = (step: ApproverStepDto): string => {
  if (step.approverType === 'title') return step.approverTitle ?? '役職'
  if (step.approverType === 'role') return step.approverRole ? APPROVER_ROLE_LABELS[step.approverRole] : 'ロール'
  return step.approverUserName ?? '個人指定'
}

// ============================================
// 6 バケット（§6.5）
// ============================================

/** 6 バケットの表示順とラベル。日次・月次の集計表示で共有する。 */
export const BUCKET_LABELS: { key: keyof Buckets; label: string }[] = [
  { key: 'scheduled', label: '所定内' },
  { key: 'statutoryOt', label: '法定内残業' },
  { key: 'nonStatutoryOt', label: '法定外残業' },
  { key: 'over60Ot', label: '60h超残業' },
  { key: 'night', label: '深夜' },
  { key: 'legalHoliday', label: '法定休日' },
]

/**
 * 6 バケット値の文字色（§6.5 の配色定義）。
 * 0 以下は淡色、60h 超残業は赤、法定外残業・法定休日は琥珀（割増賃金が発生する区分）。
 */
export const bucketTextClass = (key: keyof Buckets, minutes: number): string => {
  if (minutes <= 0) return 'text-gray-400'
  if (key === 'over60Ot') return 'text-red-600'
  if (key === 'nonStatutoryOt' || key === 'legalHoliday') return 'text-amber-600'
  return 'text-gray-800'
}
