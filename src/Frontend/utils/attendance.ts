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
 * - `addBizDays` / `diffBizDays` / `bizWeekday` / `fmtBizDate` ... 業務日付（YYYY-MM-DD、TZ を持たない）の演算
 *   ローカル TZ 依存を避けるため、業務日付は必ず UTC 正午 0 時アンカーで解釈する。
 */
import type { Buckets, LeaveUnit, PunchKind, PunchState, RequestStatus } from '~/composables/useAttendance'

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
 * 業務日付を UTC 0 時アンカーの Date に変換する（内部ヘルパー）。
 * `new Date('2026-07-27')` は UTC 解釈だが、`new Date('2026-07-27T00:00:00')` は
 * ローカル解釈になり実行環境の TZ で日付がずれる。必ず末尾 Z を付けて固定する。
 */
const bizDateUtc = (date: string): Date => new Date(`${date.slice(0, 10)}T00:00:00Z`)

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

const WEEKDAY_JP = ['日', '月', '火', '水', '木', '金', '土']

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
  approved: '承認済み',
  rejected: '却下',
}

/** 申請ステータスバッジの配色。 */
export const REQUEST_STATUS_CLASSES: Record<RequestStatus, string> = {
  pending: 'bg-amber-100 text-amber-700',
  approved: 'bg-green-100 text-green-700',
  rejected: 'bg-gray-100 text-gray-500',
}

/** 休暇の取得単位ラベル。 */
export const LEAVE_UNIT_LABELS: Record<LeaveUnit, string> = {
  full: '全日',
  half: '半日',
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
