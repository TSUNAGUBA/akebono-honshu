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
  // 第二段階契約: users.id は uuid 文字列 (JSON では文字列で受け渡し)
  userId: string
  displayName: string
  // マルチテナント (AKB-DOC-12)。/auth/sync 応答から取得し、useApi が X-Tenant-Id ヘッダーに使う。
  tenantId: string
  tenantCode: string
  // C-02 4 権限カテゴリ (Phase 5 §3.18)
  productLedgerPermission: number       // 0=なし, 1=更新可能, 2=参照のみ, 3=参照のみ制限
  purchaseOrderCreatePermission: number // 0=なし, 1=更新可能, 2=参照のみ
  purchaseOrderInfoPermission: number   // 0=なし, 1=あり
  processRecordPermission: number       // 0=なし, 1=あり
  // 勤怠 (5 つ目の権限カテゴリ。勤怠移植仕様 §2 / §6.1)。末尾追加 = 下位互換。
  attendancePermission: number          // 0=なし, 1=更新可能, 2=参照のみ
  punchRequired: boolean                // 打刻対象か (役員・外注等は false)
}

interface SyncApiResponse {
  userId: string
  employeeNo: string
  displayName: string
  isActive: boolean
  productLedgerPermission: number
  purchaseOrderCreatePermission: number
  purchaseOrderInfoPermission: number
  processRecordPermission: number
  // マルチテナント (AKB-DOC-12)
  tenantId: string
  tenantCode: string
  // 勤怠 (末尾追加)。Backend 未更新の環境では欠落し得るため optional で受け、
  // 権限は 0 (なし) へフォールバックする (fail-close)。
  attendancePermission?: number
  punchRequired?: boolean
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
    // 成功応答はエンベロープ { data, meta } (AKB-DOC-12)。$fetch 直叩きのためここで unwrap する。
    const envelope = await $fetch<{ data: SyncApiResponse; meta: unknown }>(
      `${config.public.apiBase}/auth/sync`,
      {
        method: 'POST',
        headers: { Authorization: `Bearer ${idToken}` },
      },
    )
    const res = envelope.data
    auth.value = {
      firebaseUid: firebaseUser.uid,
      email: firebaseUser.email,
      userId: res.userId,
      displayName: res.displayName,
      tenantId: res.tenantId,
      tenantCode: res.tenantCode,
      productLedgerPermission: res.productLedgerPermission,
      purchaseOrderCreatePermission: res.purchaseOrderCreatePermission,
      purchaseOrderInfoPermission: res.purchaseOrderInfoPermission,
      processRecordPermission: res.processRecordPermission,
      // 勤怠 (末尾追加)。応答に無い = Backend 未更新のため、権限なし・打刻対象外として扱う
      // (勤怠ナビ・打刻ボタンが出ない。権限は必ず fail-close 側へ倒す)。
      attendancePermission: res.attendancePermission ?? 0,
      punchRequired: res.punchRequired ?? false,
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

    authReadyPromise = new Promise<void>((resolve) => {
      let settled = false
      // 初回の emission (= 認証状態の確定) で middleware のゲートを 1 度だけ解放する。
      // 以降の emission (token refresh / login / logout) でも auth.value は更新し続ける。
      const release = () => {
        if (!settled) {
          settled = true
          resolve()
        }
      }
      try {
        const { $firebaseAuth } = useNuxtApp()
        onAuthStateChanged($firebaseAuth as Auth, async (fbUser) => {
          try {
            if (fbUser) {
              // ログイン済 (リロード時は Firebase が永続化セッションから *非同期* に復元)。Backend と再同期。
              try {
                await syncWithBackend(fbUser)
              }
              catch (e) {
                // sync 失敗 (例: users.firebase_uid 未紐付け / inactive) は Firebase もログアウト。
                // 強制ログアウトの原因究明のためログを残す。signOut が失敗しても UI の認証済状態を
                // 残さないよう、auth.value=null は finally で保証 (レビュー CR-6)。
                console.error('[auth] backend 同期に失敗したためサインアウトします', e)
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
            // 失敗時も finally で必ず解放し、リロードがローディングのまま固まらないようにする (原則4)。
            release()
          }
        })
      }
      catch (e) {
        // 購読セットアップ自体の失敗 ($firebaseAuth 未提供 = Firebase config 未設定で plugin が
        // throw した場合等)。ここで reject すると middleware の await が throw してナビゲーションが
        // 中断し、ローディングのまま復帰不能になる (rejected な singleton もキャッシュされ以降の
        // 遷移も全て失敗)。fail open で resolve し、未認証として middleware に判定させる
        // (→ /login へ可視的に誘導)。原則4 非ブロッキング。
        console.error('[auth] watchAuthState の購読セットアップに失敗しました', e)
        auth.value = null
        release()
      }
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

  // 書込 (マスタ編集) は「更新可能 (==1)」のみ。参照のみ (2/3) は編集不可 (バックエンドと一致)。
  const canEditMaster = computed(() => (auth.value?.productLedgerPermission ?? 0) === 1)

  // ------------------------------------------
  // 勤怠 (勤怠移植仕様 §6.1)
  // ------------------------------------------
  // 権限スケールは既存 4 カテゴリと同じく非単調 (0=なし / 1=更新可能 / 2=参照のみ)。
  // 「参照のみ」が「更新可能」より大きい値のため、書込判定に >= 1 を使うとバグる。必ず === 1 で判定する。

  /** 打刻できるか。勤怠が更新可能 (===1) かつ打刻対象 (punchRequired) であること。 */
  const canPunch = computed(() =>
    (auth.value?.attendancePermission ?? 0) === 1 && (auth.value?.punchRequired ?? false))

  /** 勤怠機能 (自分の勤怠参照) を使えるか。0=なし のみ不可。 */
  const canUseAttendance = computed(() => {
    const p = auth.value?.attendancePermission ?? 0
    return p === 1 || p === 2
  })

  /**
   * 勤怠の管理操作 (全員のタイムカード参照 / 各種承認 / 休暇付与 / 勤怠ルール設定) が可能か。
   * office の admin 相当を既存オーナー権限に集約する (§2)。
   *
   * **オーナー権限 (process_record_permission) は 0=なし / 1=あり の 2 値**であり、他の 4 カテゴリの
   * ような非単調スケール (2=参照のみ) を持たない。そのため `>= 1` と `=== 1` は同値になるが、
   * 判定式は既存 pages/masters/users.vue と揃えて **`>= 1` に統一**する
   * (同一概念の判定式が画面ごとに違うと、権限値が拡張されたときに片方だけ挙動が変わるため)。
   * 勤怠の書込権限 (attendancePermission) は非単調スケールなので、こちらは必ず `=== 1` で判定すること。
   */
  const isAttendanceAdmin = computed(() => (auth.value?.processRecordPermission ?? 0) >= 1)

  return {
    user: auth,
    isAuthenticated: computed(() => auth.value !== null),
    canEditMaster,
    canPunch,
    canUseAttendance,
    isAttendanceAdmin,
    getIdToken,
    login,
    logout,
    watchAuthState,
  }
}
