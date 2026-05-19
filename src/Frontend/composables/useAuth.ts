// Iteration 0/1 専用ダミー認証 composable
// Iteration 4 Hardening で Firebase Web SDK 接続に置換

interface AuthUser {
  userId: number
  displayName: string
  token: string
  // C-02 4 権限カテゴリ (Phase 5 §3.18)
  productLedgerPermission: number       // 0=なし, 1=更新可能, 2=参照のみ, 3=参照のみ制限
  purchaseOrderCreatePermission: number // 0=なし, 1=更新可能, 2=参照のみ
  purchaseOrderInfoPermission: number   // 0=なし, 1=あり
  processRecordPermission: number       // 0=なし, 1=あり
}

interface LoginApiResponse {
  token: string
  userId: number
  displayName: string
  productLedgerPermission: number
  purchaseOrderCreatePermission: number
  purchaseOrderInfoPermission: number
  processRecordPermission: number
}

const STORAGE_KEY = 'akebono-auth'

export const useAuth = () => {
  const auth = useState<AuthUser | null>('auth', () => null)

  if (import.meta.client && !auth.value) {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      try { auth.value = JSON.parse(raw) } catch { /* ignore */ }
    }
  }

  const login = async (loginId: string, password: string) => {
    const config = useRuntimeConfig()
    const res = await $fetch<LoginApiResponse>(
      `${config.public.apiBase}/auth/login`,
      { method: 'POST', body: { loginId, password } },
    )
    auth.value = {
      userId: res.userId,
      displayName: res.displayName,
      token: res.token,
      productLedgerPermission: res.productLedgerPermission,
      purchaseOrderCreatePermission: res.purchaseOrderCreatePermission,
      purchaseOrderInfoPermission: res.purchaseOrderInfoPermission,
      processRecordPermission: res.processRecordPermission,
    }
    if (import.meta.client) localStorage.setItem(STORAGE_KEY, JSON.stringify(auth.value))
  }

  const logout = () => {
    auth.value = null
    if (import.meta.client) localStorage.removeItem(STORAGE_KEY)
  }

  // 権限ヘルパー (Iteration 1 ではマスタ編集権限のみ使用)
  const canEditMaster = computed(() => (auth.value?.productLedgerPermission ?? 0) >= 1)

  return {
    user: auth,
    isAuthenticated: computed(() => auth.value !== null),
    canEditMaster,
    login,
    logout,
  }
}
