export default defineNuxtRouteMiddleware((to) => {
  const { isAuthenticated } = useAuth()
  const publicPaths = ['/login']

  // ルート / にアクセスされたら認証状態に応じてリダイレクト
  // (pages/index.vue の onMounted ではなく middleware で行うことで確実に発火)
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
