/** meta.page (AKB-DOC-12 §7.1 キーセットページング)。nextCursor は不透明トークン (中身を解釈しない)。 */
export interface PageMeta {
  nextCursor: string | null
  limit: number
  hasMore: boolean
}

/** ページング一覧の返却形 (items + meta.page)。 */
export interface PagedItems<T> {
  items: T[]
  page: PageMeta
}

/** 一覧 API の常用 limit。上限 200 (AKB-DOC-12 §7.1) を使い「さらに読み込む」でカーソルを辿る。 */
export const PAGE_LIMIT = 200

/** ページングクエリ断片を組み立てる (`limit=200` / `limit=200&cursor=...`)。 */
export const pageQuery = (cursor?: string | null): string =>
  `limit=${PAGE_LIMIT}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`

/**
 * 作成系 POST の Idempotency-Key セッション (AKB-DOC-12 §8.1)。
 * 送信試行ごとに新キーを振ると、タイムアウト後のユーザ再試行が「別操作」扱いになり
 * 初回作成が成功していた場合に二重作成される。本セッションは同一ペイロードの再試行に
 * 同じキーを使い回してサーバ側リプレイ (初回結果の再返却) を効かせ、
 * 成功後 (complete) はキーを破棄して意図的な同内容の再作成を妨げない。
 * ペイロードが変われば自動的に新キーになる (キーはペイロード JSON 単位で採番)。
 */
export const createIdempotencySession = () => {
  const keys = new Map<string, string>()
  const keyFor = (payload: unknown): string => {
    const json = JSON.stringify(payload)
    let key = keys.get(json)
    if (!key) {
      key = crypto.randomUUID()
      keys.set(json, key)
    }
    return key
  }
  const complete = (payload: unknown): void => {
    keys.delete(JSON.stringify(payload))
  }
  return { keyFor, complete }
}

export const useApi = () => {
  const config = useRuntimeConfig()
  // tenantId は getIdToken と同じく useAuth() (useState('auth') ストア) から読む。
  // useAuth は useApi に依存しないため循環 import は発生しない。
  const { getIdToken, user } = useAuth()

  const apiFetch = async <T>(path: string, opts: Parameters<typeof $fetch<T>>[1] = {}) => {
    const token = await getIdToken()
    const tenantId = user.value?.tenantId
    return await $fetch<T>(`${config.public.apiBase}${path}`, {
      ...opts,
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        // マルチテナント契約 (AKB-DOC-12): tenantId が判明していれば X-Tenant-Id を送る
        // (サーバ側で不一致は 403 AKB-TENANT-002)。auth/sync 前は未判明のため送らない。
        ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
        // 呼び出し側の追加ヘッダー (Idempotency-Key 等) を最後にマージして保持する。
        ...(opts.headers ?? {}),
      },
    })
  }

  /**
   * 成功エンベロープ { data, meta } (AKB-DOC-12) を unwrap して data を返す標準ヘルパー。
   * JSON を返す API 呼び出し (一覧・単一取得・作成) はこれを使う。
   * 204 No Content を返す void エンドポイントは body が無いため apiFetch<void> を使うこと。
   */
  const apiData = async <T>(path: string, opts: Parameters<typeof $fetch<{ data: T }>>[1] = {}): Promise<T> => {
    const res = await apiFetch<{ data: T }>(path, opts)
    return res.data
  }

  /**
   * キーセットページング一覧 (AKB-DOC-12 §7.1) の unwrap ヘルパー。
   * {data: T[], meta: {page}} を {items, page} に変換して返す。
   * 呼び出し側は page.hasMore の間 page.nextCursor を次リクエストの cursor に渡す。
   */
  const apiPaged = async <T>(path: string, opts: Parameters<typeof $fetch<{ data: T[] }>>[1] = {}): Promise<PagedItems<T>> => {
    const res = await apiFetch<{ data: T[]; meta: { page: PageMeta } }>(path, opts)
    return { items: res.data, page: res.meta.page }
  }

  return { apiFetch, apiData, apiPaged }
}
