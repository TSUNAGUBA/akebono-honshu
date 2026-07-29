<script setup lang="ts">
/**
 * グローバルスナックバー表示（useSnackbar のキューを購読）。
 * 画面右下（モバイルは下部中央・全幅寄り）に積み上げて表示し、種別で配色する。
 * アクセシビリティ: role="status" / aria-live="polite"（エラーは assertive）で読み上げ対応（U-5）。
 */
const { items, dismiss } = useSnackbar()

const styleOf = (type: string): string => {
  switch (type) {
    case 'error': return 'border-red-300 bg-red-50 text-red-800'
    case 'success': return 'border-green-300 bg-green-50 text-green-800'
    default: return 'border-blue-300 bg-blue-50 text-blue-800'
  }
}
</script>

<template>
  <Teleport to="body">
    <!-- 原則8 レスポンシブ: モバイルは左右余白付きの下部中央、PC は右下に固定。 -->
    <div class="pointer-events-none fixed inset-x-0 bottom-0 z-[10000] flex flex-col items-center gap-2 px-3 pb-4 sm:inset-x-auto sm:right-4 sm:items-end">
      <TransitionGroup name="snackbar">
        <div
          v-for="it in items"
          :key="it.id"
          class="pointer-events-auto flex w-full max-w-sm items-start gap-2 rounded-md border px-3 py-2 text-sm shadow-lg"
          :class="styleOf(it.type)"
          :role="it.type === 'error' ? 'alert' : 'status'"
          :aria-live="it.type === 'error' ? 'assertive' : 'polite'"
        >
          <span class="flex-1 whitespace-pre-line">{{ it.message }}</span>
          <button
            type="button"
            class="shrink-0 rounded p-0.5 text-current opacity-60 transition hover:opacity-100"
            aria-label="閉じる"
            @click="dismiss(it.id)"
          >
            <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18 18 6M6 6l12 12" /></svg>
          </button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<style scoped>
.snackbar-enter-active,
.snackbar-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.snackbar-enter-from,
.snackbar-leave-to {
  opacity: 0;
  transform: translateY(0.5rem);
}
</style>
