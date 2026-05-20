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
      // Firebase Web SDK 用 config (公開情報、Backend は Project ID のみ参照)。
      // 本番は環境変数 NUXT_PUBLIC_FIREBASE_* で上書き、dev は Firebase Console から取得した値を直書き。
      firebase: {
        apiKey: process.env.NUXT_PUBLIC_FIREBASE_API_KEY || 'AIzaSyAdhBwA3IXlarKVNiVk-4JOymalID3067M',
        authDomain: process.env.NUXT_PUBLIC_FIREBASE_AUTH_DOMAIN || 'akebono-honshu.firebaseapp.com',
        projectId: process.env.NUXT_PUBLIC_FIREBASE_PROJECT_ID || 'akebono-honshu',
        storageBucket: process.env.NUXT_PUBLIC_FIREBASE_STORAGE_BUCKET || 'akebono-honshu.firebasestorage.app',
        messagingSenderId: process.env.NUXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID || '455760580161',
        appId: process.env.NUXT_PUBLIC_FIREBASE_APP_ID || '1:455760580161:web:90b175589dd87b82e0c0c0',
      },
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
