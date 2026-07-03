<script setup lang="ts">
/**
 * 開閉可能なフィルター/検索パネル（汎用ラッパー）。
 * 一覧ページの絞込/検索コントロールを包み、ヘッダーの「表示/非表示」トグルで
 * パネル本体（default slot）を開閉する。閉じると画面の幅/高さがデータ表示に使える。
 *
 * - 純粋な UI ラッパー（中身のバインディング・filtered ロジックは一切変更しない）。
 * - 既定で開いた状態。開閉状態は storageKey を渡すと sessionStorage に永続化し、
 *   セッション内のページ遷移をまたいで維持する（storageKey 未指定なら ref のみ）。
 * - SSR 安全（sessionStorage は import.meta.client / onMounted でガード）。
 * - 本体は v-show で出し入れするため、閉じても入力値は保持される。
 * - ネイティブのプルダウン要素は使わない (MasterSelect 経由)。ヘッダーは小画面で折り返す。blue 系アクセント。
 */
interface Props {
  /** ヘッダーのラベル（既定「フィルター」）。 */
  title?: string
  /** 現在有効なフィルタ件数。> 0 で「絞込中 N」チップを表示。 */
  activeCount?: number
  /** sessionStorage の永続化キー（例 "filters:products"）。未指定なら永続化しない。 */
  storageKey?: string
  /** 初期表示で開くか（既定 true）。永続値があればそちらを優先。 */
  defaultOpen?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: 'フィルター',
  activeCount: undefined,
  storageKey: undefined,
  defaultOpen: true,
})

const open = ref(props.defaultOpen)

// sessionStorage から開閉状態を復元（SSR ではスキップし、マウント後にのみ参照）。
onMounted(() => {
  if (!props.storageKey || !import.meta.client) return
  try {
    const saved = window.sessionStorage.getItem(props.storageKey)
    if (saved === 'open') open.value = true
    else if (saved === 'closed') open.value = false
  } catch {
    // sessionStorage 無効環境（プライベートモード等）は ref 既定値のまま動作（原則4 非ブロッキング）。
  }
})

const toggle = () => {
  open.value = !open.value
  if (!props.storageKey || !import.meta.client) return
  try {
    window.sessionStorage.setItem(props.storageKey, open.value ? 'open' : 'closed')
  } catch {
    // 永続化失敗は致命的でない。開閉自体は ref で機能する。
  }
}
</script>

<template>
  <section class="mb-4 rounded-lg border border-gray-200 bg-white shadow-sm">
    <!-- ヘッダー行（小画面で折り返す）。左: ラベル + 絞込中チップ、右: actions スロット + トグル。 -->
    <div class="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2.5" :class="open ? 'border-b border-gray-100' : ''">
      <h2 class="text-sm font-semibold text-gray-700">{{ title }}</h2>
      <span
        v-if="activeCount != null && activeCount > 0"
        class="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-700"
      >
        絞込中 {{ activeCount }}
      </span>
      <div class="ml-auto flex items-center gap-2">
        <slot name="actions" />
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded-md border border-gray-300 bg-white px-3 py-1 text-xs text-gray-600 hover:bg-gray-50"
          :aria-expanded="open"
          @click="toggle"
        >
          <span>{{ open ? '非表示' : '表示' }}</span>
          <svg
            class="h-3.5 w-3.5 transition-transform"
            :class="open ? 'rotate-180' : ''"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m19 9-7 7-7-7" />
          </svg>
        </button>
      </div>
    </div>

    <!-- 本体（v-show で保持しつつ出し入れ）。 -->
    <div v-show="open" class="px-4 py-3">
      <slot />
    </div>
  </section>
</template>
