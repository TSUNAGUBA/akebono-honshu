// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: true },
  // SSR を無効化 (全 SPA / CSR 化)。
  // 理由: 認証情報を localStorage で管理しているため SSR 時に user が null
  //   になり、hydration mismatch でレイアウト破壊が発生する。Iteration 4 で
  //   Firebase Auth に移行する際にどのみち全 CSR 化する想定のため、前倒し対応。
  ssr: false,
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
