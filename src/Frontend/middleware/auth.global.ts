export default defineNuxtRouteMiddleware((to) => {
  const { isAuthenticated } = useAuth()
  const publicPaths = ['/login']

  if (!isAuthenticated.value && !publicPaths.includes(to.path)) {
    return navigateTo('/login')
  }

  if (isAuthenticated.value && to.path === '/login') {
    return navigateTo('/users')
  }
})
