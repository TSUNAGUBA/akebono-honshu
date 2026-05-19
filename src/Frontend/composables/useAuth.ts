// Iteration 0 専用ダミー認証 composable
// Iteration 4 Hardening で Firebase Web SDK 接続に置換

interface AuthUser {
  userId: number
  displayName: string
  token: string
}

const STORAGE_KEY = 'akebono-auth'

export const useAuth = () => {
  const auth = useState<AuthUser | null>('auth', () => null)

  // クライアント側でのみ localStorage を扱う
  if (import.meta.client && !auth.value) {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      try { auth.value = JSON.parse(raw) } catch { /* ignore */ }
    }
  }

  const login = async (loginId: string, password: string) => {
    const config = useRuntimeConfig()
    const res = await $fetch<{ token: string; userId: number; displayName: string }>(
      `${config.public.apiBase}/auth/login`,
      { method: 'POST', body: { loginId, password } },
    )
    auth.value = { userId: res.userId, displayName: res.displayName, token: res.token }
    if (import.meta.client) localStorage.setItem(STORAGE_KEY, JSON.stringify(auth.value))
  }

  const logout = () => {
    auth.value = null
    if (import.meta.client) localStorage.removeItem(STORAGE_KEY)
  }

  return {
    user: auth,
    isAuthenticated: computed(() => auth.value !== null),
    login,
    logout,
  }
}
