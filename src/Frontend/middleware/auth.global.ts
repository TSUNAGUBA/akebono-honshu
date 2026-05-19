export default defineNuxtRouteMiddleware((to) => {
  // SSR では localStorage が読めず常に未認証扱いになるため middleware を skip。
  // app.vue 側で <ClientOnly> ラップして実コンテンツは CSR でのみ描画するため、
  // 認証チェックも CSR でのみ実行することで /masters/* リロード時の誤遷移を防ぐ。
  if (import.meta.server) return

  const { isAuthenticated } = useAuth()
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
