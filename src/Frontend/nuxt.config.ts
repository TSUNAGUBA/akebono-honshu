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
      apiBase: process.env.NUXT_PUBLIC_API_BASE || 'http://localhost:5000/api/maker/v1',
      // Firebase Web SDK 用 config (公開情報、Backend は Project ID のみ参照)。
      // dev/prod で必ず異なる project を使い分けるため default 値は持たない:
      // - dev: .env (リポジトリ外、`.env.example` をコピー) から注入
      // - prod: ビルド時に NUXT_PUBLIC_FIREBASE_* 環境変数で注入
      // 未定義の場合 plugins/firebase.client.ts が起動時に throw する。
      firebase: {
        apiKey: process.env.NUXT_PUBLIC_FIREBASE_API_KEY || '',
        authDomain: process.env.NUXT_PUBLIC_FIREBASE_AUTH_DOMAIN || '',
        projectId: process.env.NUXT_PUBLIC_FIREBASE_PROJECT_ID || '',
        storageBucket: process.env.NUXT_PUBLIC_FIREBASE_STORAGE_BUCKET || '',
        messagingSenderId: process.env.NUXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID || '',
        appId: process.env.NUXT_PUBLIC_FIREBASE_APP_ID || '',
      },
    },
  },
  app: {
    head: {
      title: 'Akebono Honshu | アパレル生産管理',
      meta: [
        { charset: 'utf-8' },
        { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      ],
    },
  },
})
