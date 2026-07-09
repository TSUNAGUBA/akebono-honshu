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

  return { apiFetch, apiData }
}
