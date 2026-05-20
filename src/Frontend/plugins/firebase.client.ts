import { initializeApp, getApps, getApp } from 'firebase/app'
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

  // dev/prod project 取り違え事故防止のため、未定義は throw する (レビュー指摘 SA P0-1)。
  // nuxt.config.ts は default 値を持たない設計のため、.env (dev) / 環境変数 (prod) 必須。
  const missing = (Object.keys(firebaseConfig) as Array<keyof typeof firebaseConfig>)
    .filter(k => !firebaseConfig[k])
  if (missing.length > 0) {
    throw new Error(
      `Firebase config 未設定: ${missing.join(', ')}。dev は .env.example を .env にコピー、` +
      `prod は NUXT_PUBLIC_FIREBASE_* 環境変数を設定してください。`)
  }

  // HMR で多重 init される事故を防ぐ (getApp() は公式推奨パターン)。
  const app = getApps().length === 0 ? initializeApp(firebaseConfig) : getApp()
  const auth: Auth = getAuth(app)

  // 旧 Iter 0 ダミー認証で localStorage に書き込まれていたキーを除去 (下位互換性、原則 7)。
  // 段階 B 以降は Firebase SDK が token を内部保持するため不要。
  if (typeof window !== 'undefined') {
    try { window.localStorage.removeItem('akebono-auth') }
    catch { /* localStorage 無効環境 (Safari private mode 等) は無視 */ }
  }

  return {
    provide: {
      firebaseAuth: auth,
    },
  }
})
