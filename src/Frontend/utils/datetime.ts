/**
 * API タイムスタンプ (createdAt/updatedAt/exportedAt 等) の JST 表示ヘルパー (AKB-DOC-12)。
 * タイムスタンプは UTC ISO-8601 (末尾 Z) で届くため、文字列 slice で日付を取り出すと
 * UTC 日付が表示されてしまう (JST とは最大 9 時間ずれる)。必ず本ヘルパーで変換する。
 *
 * 業務日付 (DateOnly、YYYY-MM-DD の dueDate/orderDate 等) はタイムゾーンを持たないため
 * 対象外 (そのまま表示する)。
 */

/** UTC タイムスタンプ → JST の日時表示 (例: 2026/7/9 14:30:15) */
export const formatJstDateTime = (value: string): string =>
  new Date(value).toLocaleString('ja-JP', { timeZone: 'Asia/Tokyo' })

/** UTC タイムスタンプ → JST の日付表示 (例: 2026/7/9) */
export const formatJstDate = (value: string): string =>
  new Date(value).toLocaleDateString('ja-JP', { timeZone: 'Asia/Tokyo' })

/** UTC タイムスタンプ → JST の YYYY-MM-DD (date input 値との辞書順比較・フィルタ用) */
export const toJstDateString = (value: string): string =>
  new Date(value).toLocaleDateString('en-CA', { timeZone: 'Asia/Tokyo' })

/** 業務上の「今日」(JST) の YYYY-MM-DD (フォーム既定値用。UTC 由来の前日ずれを防ぐ) */
export const todayJst = (): string =>
  new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tokyo' })

/** 業務上の「今日 + N 日」(JST) の YYYY-MM-DD (納期既定値等) */
export const todayJstPlusDays = (days: number): string =>
  new Date(Date.now() + days * 86400000).toLocaleDateString('en-CA', { timeZone: 'Asia/Tokyo' })

/** 業務上の「今月」(JST) の YYYY-MM (為替マスタ等の年月既定値用) */
export const currentMonthJst = (): string => todayJst().slice(0, 7)
