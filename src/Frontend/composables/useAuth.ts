// Iter 4 段階 B: Firebase Auth ベースの認証 composable。
// - Firebase JS SDK が ID Token (1h 有効) の保持と自動更新を担う
// - useState には plain object (UID + 業務情報) のみ格納する
//   (CLAUDE.md Vue 確認ポイント: Firebase User オブジェクトは Proxy traverse 不安全のため格納禁止)
// - ID Token は Firebase の currentUser.getIdToken() で都度取得し、useApi で Authorization に乗せる

import {
  signInWithEmailAndPassword,
  signOut,
  onAuthStateChanged,
  type Auth,
  type User as FirebaseUser,
} from 'firebase/auth'

interface AkebonoUser {
  firebaseUid: string
  email: string | null
  userId: number
  displayName: string
  // C-02 4 権限カテゴリ (Phase 5 §3.18)
  productLedgerPermission: number       // 0=なし, 1=更新可能, 2=参照のみ, 3=参照のみ制限
  purchaseOrderCreatePermission: number // 0=なし, 1=更新可能, 2=参照のみ
  purchaseOrderInfoPermission: number   // 0=なし, 1=あり
  processRecordPermission: number       // 0=なし, 1=あり
}

interface SyncApiResponse {
  userId: number
  employeeNo: string
  displayName: string
  isActive: boolean
  productLedgerPermission: number
  purchaseOrderCreatePermission: number
  purchaseOrderInfoPermission: number
  processRecordPermission: number
}

// onAuthStateChanged の初回発火 (+ Backend 同期) 完了を表すゲート Promise。
// client でのみ生成されるアプリ内シングルトン (module スコープ)。middleware はこれを
// await して「認証状態が確定してから」認可判定し、リロード時に復元前の null 状態で
// /login へ誤遷移する race を防ぐ (nuxt/nuxt#8174 と同種)。SSR では生成されない。
let authReadyPromise: Promise<void> | null = null

export const useAuth = () => {
  const auth = useState<AkebonoUser | null>('auth', () => null)

  // Firebase ID Token を都度取得 (refresh は SDK が自動)。
  // currentUser は plugin の provide 経由 (Proxy 経由でなく直参照)。
  const getIdToken = async (): Promise<string | null> => {
    if (!import.meta.client) return null
    const { $firebaseAuth } = useNuxtApp()
    const fbUser = ($firebaseAuth as Auth).currentUser
    return fbUser ? await fbUser.getIdToken() : null
  }

  /**
   * Firebase Auth でログイン後、Backend /auth/sync を叩いて業務情報 (権限) を取得し、
   * useState にロード。失敗時はログアウトして例外を伝搬する。
   */
  const syncWithBackend = async (firebaseUser: FirebaseUser): Promise<void> => {
    const config = useRuntimeConfig()
    const idToken = await firebaseUser.getIdToken()
    const res = await $fetch<SyncApiResponse>(
      `${config.public.apiBase}/auth/sync`,
      {
        method: 'POST',
        headers: { Authorization: `Bearer ${idToken}` },
      },
    )
    auth.value = {
      firebaseUid: firebaseUser.uid,
      email: firebaseUser.email,
      userId: res.userId,
      displayName: res.displayName,
      productLedgerPermission: res.productLedgerPermission,
      purchaseOrderCreatePermission: res.purchaseOrderCreatePermission,
      purchaseOrderInfoPermission: res.purchaseOrderInfoPermission,
      processRecordPermission: res.processRecordPermission,
    }
  }

  /**
   * onAuthStateChanged を購読し、リロード時に Firebase が復元する currentUser から
   * Backend と再同期する。client で 1 回だけ購読する (authReadyPromise で多重防止)。
   *
   * 戻り値は「認証状態が確定した」ことを表す Promise。初回の onAuthStateChanged 発火
   * (ログイン済なら Backend 同期まで) 完了で resolve する。middleware はこれを await して
   * から認可判定することで、復元前の null 状態で /login へ誤遷移する race を防ぐ。
   */
  const watchAuthState = (): Promise<void> => {
    // SSR では Firebase Web SDK が動かないため何もしない (app.vue 全体が <ClientOnly>)。
    if (!import.meta.client) return Promise.resolve()
    // 既に購読済みなら、初回確定を表す同一 Promise を返す (多重購読防止 + ゲート共有)。
    if (authReadyPromise) return authReadyPromise

    const { $firebaseAuth } = useNuxtApp()
    authReadyPromise = new Promise<void>((resolve) => {
      let settled = false
      onAuthStateChanged($firebaseAuth as Auth, async (fbUser) => {
        try {
          if (fbUser) {
            // ログイン済 (リロード時は Firebase が永続化セッションから *非同期* に復元)。Backend と再同期。
            try {
              await syncWithBackend(fbUser)
            }
            catch {
              // sync 失敗 (例: users.firebase_uid 未紐付け / inactive) は Firebase もログアウト。
              // signOut が失敗しても UI の認証済状態を残さないよう、auth.value=null は finally で保証 (レビュー CR-6)。
              try {
                await signOut($firebaseAuth as Auth)
              }
              finally {
                auth.value = null
              }
            }
          }
          else {
            auth.value = null
          }
        }
        finally {
          // 初回の emission (= 認証状態の確定) で middleware のゲートを 1 度だけ解放する。
          // 以降の emission (token refresh / login / logout) でも auth.value は更新し続ける。
          // 失敗時も finally で必ず解放し、リロードがローディングのまま固まらないようにする (原則4)。
          if (!settled) {
            settled = true
            resolve()
          }
        }
      })
    })
    return authReadyPromise
  }

  const login = async (email: string, password: string): Promise<void> => {
    const { $firebaseAuth } = useNuxtApp()
    const cred = await signInWithEmailAndPassword($firebaseAuth as Auth, email, password)
    await syncWithBackend(cred.user)
  }

  const logout = async (): Promise<void> => {
    const { $firebaseAuth } = useNuxtApp()
    // signOut が失敗しても UI の認証済状態を残さないため auth.value=null は finally で保証
    // (watchAuthState の対応と整合、レビュー 2 周目 P2 CR)
    try {
      await signOut($firebaseAuth as Auth)
    }
    finally {
      auth.value = null
    }
  }

  const canEditMaster = computed(() => (auth.value?.productLedgerPermission ?? 0) >= 1)

  return {
    user: auth,
    isAuthenticated: computed(() => auth.value !== null),
    canEditMaster,
    getIdToken,
    login,
    logout,
    watchAuthState,
  }
}
