export default defineNuxtRouteMiddleware((to) => {
  // SSR では Firebase Web SDK が動作しないため middleware を skip。
  // app.vue 全体を <ClientOnly> ラップして実コンテンツは CSR でのみ描画するため、
  // 認証チェックも CSR でのみ実行することで /masters/* リロード時の誤遷移を防ぐ。
  if (import.meta.server) return

  const { isAuthenticated, watchAuthState } = useAuth()
  // 初回マウントで Firebase の onAuthStateChanged を購読開始
  // (initialized フラグで多重防止、リロード時に Firebase から currentUser 復元 → /auth/sync 実行)
  watchAuthState()

  const publicPaths = ['/login']

  if (to.path === '/') {
    return navigateTo(isAuthenticated.value ? '/users' : '/login')
  }

  if (!isAuthenticated.value && !publicPaths.includes(to.path)) {
    return navigateTo('/login')
  }

  if (isAuthenticated.value && to.path === '/login') {
    return navigateTo('/users')
  }
})
