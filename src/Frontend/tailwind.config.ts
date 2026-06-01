import type { Config } from 'tailwindcss'

export default {
  // @nuxtjs/tailwindcss モジュールがデフォルトで設定するパスを明示
  // (新規ファイル追加時の JIT 検知漏れを防ぐ二重保護)
  content: [
    './components/**/*.{vue,js,ts}',
    './layouts/**/*.{vue,js,ts}',
    './pages/**/*.{vue,js,ts}',
    './composables/**/*.{js,ts}',
    './plugins/**/*.{js,ts}',
    './utils/**/*.{js,ts}',
    './middleware/**/*.{js,ts}',
    './app.vue',
    './error.vue',
  ],
  theme: {
    extend: {},
  },
  plugins: [],
} satisfies Config
