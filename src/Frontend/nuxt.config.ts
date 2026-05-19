// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: true },
  // ssr: false は Nuxt 3.21+ で Vite Node IPC エラーを引き起こすため使用しない。
  // 代わりに app.vue 全体を <ClientOnly> でラップすることで実質的に CSR 化する。
  modules: ['@nuxtjs/tailwindcss'],
  css: ['~/assets/css/main.css'],
  typescript: {
    strict: true,
    typeCheck: false,
  },
  runtimeConfig: {
    public: {
      apiBase: process.env.NUXT_PUBLIC_API_BASE || 'http://localhost:5000/api/v1',
    },
  },
  app: {
    head: {
      title: 'akebono アパレル生産管理 (Iteration 0)',
      meta: [
        { charset: 'utf-8' },
        { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      ],
    },
  },
})
