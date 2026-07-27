<script setup lang="ts">
/**
 * ナビゲーション用アイコン（インライン SVG・文字列キー解決）。
 * warehouse は lucide-vue-next を使うが、本州は既存スタイル（インライン SVG・
 * Tailwind prefix 無し）に合わせ、新規依存を増やさず本コンポーネントで解決する。
 * サイズ・色は呼び出し側の class（例: class="h-5 w-5 text-blue-600"）で制御する。
 * 未知のキーは汎用ドットにフォールバックする（表示が壊れない）。
 */
defineProps<{ name: string }>()
</script>

<template>
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.8"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <template v-if="name === 'home'">
      <path d="M3 10.5 12 4l9 6.5" />
      <path d="M5 9.5V20h14V9.5" />
    </template>
    <template v-else-if="name === 'box'">
      <path d="M12 3 4 7v10l8 4 8-4V7z" />
      <path d="m4 7 8 4 8-4" />
      <path d="M12 11v10" />
    </template>
    <template v-else-if="name === 'clipboard'">
      <rect x="6" y="4" width="12" height="17" rx="2" />
      <rect x="9" y="2.5" width="6" height="3" rx="1" />
      <path d="M9 11h6" />
      <path d="M9 15h4" />
    </template>
    <template v-else-if="name === 'clipboard-check'">
      <rect x="6" y="4" width="12" height="17" rx="2" />
      <rect x="9" y="2.5" width="6" height="3" rx="1" />
      <path d="m9 13 2 2 4-4" />
    </template>
    <template v-else-if="name === 'clock'">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7.5V12l3.2 2" />
    </template>
    <template v-else-if="name === 'factory'">
      <path d="M3 21V11l5 3V11l5 3V7l5 3v8" />
      <path d="M2 21h20" />
      <path d="M9 21v-3M15 21v-3" />
    </template>
    <template v-else-if="name === 'sliders'">
      <path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3" />
      <path d="M2 14h4M10 8h4M18 16h4" />
    </template>
    <template v-else-if="name === 'shield'">
      <path d="M12 3 5 6v5c0 4 3 7 7 9 4-2 7-5 7-9V6z" />
    </template>
    <template v-else-if="name === 'file-text'">
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
      <path d="M14 3v5h5" />
      <path d="M9 13h6M9 17h6" />
    </template>
    <template v-else-if="name === 'layers'">
      <path d="m12 3 9 5-9 5-9-5z" />
      <path d="m3 13 9 5 9-5" />
    </template>
    <template v-else-if="name === 'users'">
      <path d="M16 19c0-2.8-2.2-5-5-5s-5 2.2-5 5" />
      <circle cx="11" cy="8" r="3.2" />
      <path d="M16 5.2a3.2 3.2 0 0 1 0 6" />
      <path d="M16.5 14.2A4.6 4.6 0 0 1 20 18.6" />
    </template>
    <template v-else-if="name === 'database'">
      <ellipse cx="12" cy="5" rx="7" ry="3" />
      <path d="M5 5v6c0 1.7 3.1 3 7 3s7-1.3 7-3V5" />
      <path d="M5 11v6c0 1.7 3.1 3 7 3s7-1.3 7-3v-6" />
    </template>
    <template v-else-if="name === 'arrow-left'">
      <path d="M19 12H5" />
      <path d="m12 19-7-7 7-7" />
    </template>
    <template v-else-if="name === 'chevron-right'">
      <path d="m9 6 6 6-6 6" />
    </template>
    <template v-else-if="name === 'log-out'">
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <path d="m16 17 5-5-5-5" />
      <path d="M21 12H9" />
    </template>
    <template v-else-if="name === 'cart'">
      <path d="M3 4h2l2.2 11.1a1 1 0 0 0 1 .8h8.5a1 1 0 0 0 1-.8L20 8H6.2" />
      <circle cx="9.5" cy="20" r="1.3" />
      <circle cx="17" cy="20" r="1.3" />
    </template>
    <template v-else-if="name === 'truck'">
      <path d="M2 7a1 1 0 0 1 1-1h10v9H2z" />
      <path d="M13 9h4l4 4v2h-8z" />
      <circle cx="6.5" cy="18" r="1.6" />
      <circle cx="17.5" cy="18" r="1.6" />
    </template>
    <template v-else-if="name === 'boxes'">
      <rect x="3" y="9" width="8" height="7" rx="1" />
      <rect x="13" y="9" width="8" height="7" rx="1" />
      <rect x="8" y="3" width="8" height="6" rx="1" />
    </template>
    <template v-else-if="name === 'chart'">
      <path d="M3 3v18h18" />
      <path d="M7 16v-4M12 16V8M17 16v-7" />
    </template>
    <template v-else-if="name === 'send'">
      <path d="M22 2 11 13" />
      <path d="M22 2 15 22l-4-9-9-4z" />
    </template>
    <template v-else-if="name === 'inbox'">
      <path d="M22 12h-6l-2 3h-4l-2-3H2" />
      <path d="M5 5h14l3 7v6a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-6z" />
    </template>
    <template v-else-if="name === 'coin'">
      <circle cx="12" cy="12" r="8" />
      <path d="M14.5 9.2A3 3 0 0 0 9.5 11c0 2.5 5 1.5 5 4a3 3 0 0 1-5 1.8" />
      <path d="M12 7.2v1.4M12 15.4v1.4" />
    </template>
    <template v-else-if="name === 'ledger'">
      <path d="M5 4h11a2 2 0 0 1 2 2v14H7a2 2 0 0 1-2-2z" />
      <path d="M5 18a2 2 0 0 1 2-2h11" />
      <path d="M10 8h5M10 11h5" />
    </template>
    <template v-else>
      <circle cx="12" cy="12" r="2.5" />
    </template>
  </svg>
</template>
