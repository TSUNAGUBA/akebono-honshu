/**
 * API エラーエンベロープ (AKB-DOC-12) の読み出しヘルパー。
 * 全エラー応答は { error: { code, message, userAction, traceId, details } } 形式
 * (旧 RFC7807 の detail フィールドは廃止)。
 */

interface ApiErrorEnvelope {
  error?: {
    code?: string
    message?: string
    userAction?: string | null
  }
}

/**
 * エラーからユーザー向けメッセージを取り出す。
 * error.message があればそれを返し、userAction があれば改行で連結する。
 * エンベロープが無い場合は statusMessage、それも無ければ fallback を返す。
 */
export const getApiErrorMessage = (err: unknown, fallback: string): string => {
  const e = err as { data?: ApiErrorEnvelope; statusMessage?: string } | null | undefined
  const apiError = e?.data?.error
  if (apiError?.message) {
    return apiError.userAction ? `${apiError.message}\n${apiError.userAction}` : apiError.message
  }
  // statusMessage は空文字のことがあるため truthy チェックで fallback に落とす。
  return e?.statusMessage || fallback
}

/** エラーコード (例: 'AKB-MAKER-011') を取り出す。エンベロープが無ければ undefined。 */
export const getApiErrorCode = (err: unknown): string | undefined => {
  const e = err as { data?: ApiErrorEnvelope } | null | undefined
  return e?.data?.error?.code
}
