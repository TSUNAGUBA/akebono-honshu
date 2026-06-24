export default defineNuxtRouteMiddleware(async (to) => {
  // SSR では Firebase Web SDK が動作しないため middleware を skip。
  // app.vue 全体を <ClientOnly> ラップして実コンテンツは CSR でのみ描画するため、
  // 認証チェックも CSR でのみ実行することで /masters/* リロード時の誤遷移を防ぐ。
  if (import.meta.server) return

  const { isAuthenticated, watchAuthState } = useAuth()
  // Firebase はリロード時、永続化済みセッションから currentUser を *非同期* に復元する
  // (onAuthStateChanged 経由)。watchAuthState() は初回発火 + Backend 同期の完了で resolve
  // する Promise を返すため、これを await してから認可判定する。await しないと復元前の
  // null 状態で判定し、ログイン状態のリロードでも /login へ誤遷移してしまう (本不具合の原因)。
  await watchAuthState()

  const publicPaths = ['/login']

  // 未認証は公開パス以外すべて /login へ（ホーム '/' も含む）。
  if (!isAuthenticated.value && !publicPaths.includes(to.path)) {
    return navigateTo('/login')
  }

  // 認証済みで /login に来たらホーム（ポータル）へ。
  // ホーム '/' はポータル（カテゴリカード）を表示するためリダイレクトしない。
  if (isAuthenticated.value && to.path === '/login') {
    return navigateTo('/')
  }
})
