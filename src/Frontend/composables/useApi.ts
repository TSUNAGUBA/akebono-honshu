export const useApi = () => {
  const config = useRuntimeConfig()
  const { getIdToken } = useAuth()

  const apiFetch = async <T>(path: string, opts: Parameters<typeof $fetch<T>>[1] = {}) => {
    const token = await getIdToken()
    return await $fetch<T>(`${config.public.apiBase}${path}`, {
      ...opts,
      headers: {
        ...(opts.headers ?? {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    })
  }

  return { apiFetch }
}
