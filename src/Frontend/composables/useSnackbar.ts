/**
 * 汎用スナックバー（トースト通知）。アプリ全体で共有する軽量な通知基盤。
 *
 * 既存フォームはインラインの赤/緑バナー（errorMessage/successMessage）で通知していたが、
 * 「登録ボタン押下時にスナックバーで警告を出す」要件（新規登録フォーム共通のバリデーション）に
 * 対応するため、単一の通知キューを本 composable で一元管理する。
 *
 * - 状態は useState で共有（複数コンポーネントから push しても 1 つのキューに集約）。
 * - 自動消滅はクライアントのみ（SSR では setTimeout を張らない。app.vue は ClientOnly 配下で描画）。
 * - AppSnackbar.vue が本キューを購読して画面右下（モバイルは下部中央）に積み上げ表示する。
 */

export type SnackbarType = 'error' | 'success' | 'info'

export interface SnackbarItem {
  id: number
  message: string
  type: SnackbarType
}

// インクリメンタルな id 採番。Math.random / Date.now を使わず単調増加のカウンタで一意化する。
let seq = 0

export const useSnackbar = () => {
  const items = useState<SnackbarItem[]>('snackbar-items', () => [])

  const dismiss = (id: number) => {
    items.value = items.value.filter((it) => it.id !== id)
  }

  const show = (message: string, type: SnackbarType = 'info', timeoutMs = 5000): number => {
    const id = ++seq
    items.value = [...items.value, { id, message, type }]
    // 自動消滅はクライアントのみ（SSR で setTimeout を張るとハイドレーション不整合の恐れ）。
    if (import.meta.client && timeoutMs > 0) {
      window.setTimeout(() => dismiss(id), timeoutMs)
    }
    return id
  }

  // 用途別の薄いショートカット（呼び出し側の可読性向上）。
  const showError = (message: string, timeoutMs = 6000) => show(message, 'error', timeoutMs)
  const showSuccess = (message: string, timeoutMs = 4000) => show(message, 'success', timeoutMs)
  const showInfo = (message: string, timeoutMs = 5000) => show(message, 'info', timeoutMs)

  return { items, show, showError, showSuccess, showInfo, dismiss }
}
