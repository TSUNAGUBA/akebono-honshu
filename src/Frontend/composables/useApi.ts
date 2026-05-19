export const useApi = () => {
  const config = useRuntimeConfig()
  const { user } = useAuth()

  const apiFetch = <T>(path: string, opts: Parameters<typeof $fetch<T>>[1] = {}) =>
    $fetch<T>(`${config.public.apiBase}${path}`, {
      ...opts,
      headers: {
        ...(opts.headers ?? {}),
        ...(user.value ? { Authorization: `Bearer ${user.value.token}` } : {}),
      },
    })

  return { apiFetch }
}
