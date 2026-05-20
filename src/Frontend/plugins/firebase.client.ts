import { initializeApp, getApps } from 'firebase/app'
import { getAuth, type Auth } from 'firebase/auth'

// Iter 4 段階 B: Firebase Auth 初期化。
// .client.ts なので SSR 時はサーバで実行されない (CLAUDE.md Nuxt 確認ポイント)。
// app.vue 全体が <ClientOnly> ラップされており実コンテンツは CSR でのみ描画される。
export default defineNuxtPlugin(() => {
  const config = useRuntimeConfig()
  const firebaseConfig = config.public.firebase as {
    apiKey: string
    authDomain: string
    projectId: string
    storageBucket: string
    messagingSenderId: string
    appId: string
  }

  // HMR で多重 init される事故を防ぐ
  const app = getApps().length === 0 ? initializeApp(firebaseConfig) : getApps()[0]
  const auth: Auth = getAuth(app)

  return {
    provide: {
      firebaseAuth: auth,
    },
  }
})
