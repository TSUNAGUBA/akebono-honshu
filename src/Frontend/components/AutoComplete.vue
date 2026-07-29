<script setup lang="ts">
/**
 * 部分一致オートコンプリート付きドロップダウン（汎用）。
 * akebono-warehouse の AutoComplete を本州向けに移植（Tailwind prefix 無し・blue 系）。
 * - 入力で部分一致フィルタ（全角半角・大小文字・ハイフンを正規化）
 * - キーボード操作（↑↓/Enter/Esc）、クリア、Teleport で z 重なり回避
 * 値は string。uuid 文字列 ID のマスタ選択は MasterSelect.vue 経由で使う。
 */
interface Option {
  value: string
  label: string
  /** 表示しないが検索対象に含めたい追加テキスト（例: コード等）。 */
  searchText?: string
}

interface Props {
  modelValue: string
  options: Option[]
  placeholder?: string
  disabled?: boolean
  allowEmpty?: boolean
  emptyLabel?: string
  inputClass?: string
  /** スプレッドシート風のセル入力 (枠なし・セルを埋める・フォーカス時のみ強調)。表内の高密度入力向け。 */
  borderless?: boolean
  /** バリデーションエラー状態。true で赤枠表示（新規登録フォームの必須未入力表示に使用）。 */
  error?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  placeholder: '入力して検索...',
  disabled: false,
  allowEmpty: true,
  emptyLabel: '（未選択）',
  inputClass: '',
  borderless: false,
  error: false,
})

// 通常は角丸+枠線、borderless は枠なし・角丸なしでセルを埋め、フォーカス時のみ内側リングと淡い背景で示す。
// 高さは main.css の base で 2rem (h-full 指定時はセルに追従)。表セルではグリッド線を td 側に任せる。
// error=true の場合は枠線・フォーカスリングを赤系にして必須未入力を明示する（要件: 該当 input を赤枠）。
const inputClasses = computed(() => {
  if (props.borderless) {
    return props.error
      ? 'w-full border-0 bg-red-50 px-2 pr-6 text-sm outline-none ring-1 ring-inset ring-red-400 focus:bg-red-50 focus:ring-1 focus:ring-inset focus:ring-red-500 disabled:bg-gray-50 disabled:text-gray-400'
      : 'w-full border-0 bg-transparent px-2 pr-6 text-sm outline-none focus:bg-blue-50 focus:ring-1 focus:ring-inset focus:ring-blue-400 disabled:bg-gray-50 disabled:text-gray-400'
  }
  return props.error
    ? 'w-full rounded-md border border-red-400 px-2.5 pr-8 text-sm focus:border-red-500 focus:outline-none focus:ring-1 focus:ring-red-500 disabled:bg-gray-50 disabled:text-gray-400'
    : 'w-full rounded-md border border-gray-300 px-2.5 pr-8 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:bg-gray-50 disabled:text-gray-400'
})

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const wrapperRef = ref<HTMLElement>()
const inputRef = ref<HTMLInputElement>()
const dropdownRef = ref<HTMLElement>()
const showDropdown = ref(false)
const query = ref('')
const highlightIndex = ref(-1)
const isSelecting = ref(false)
const dropdownStyle = ref<Record<string, string>>({})

// 全角半角・大小文字・ハイフンを正規化して部分一致の取りこぼしを防ぐ。
const normalize = (str: string): string =>
  str
    .replace(/[Ａ-Ｚａ-ｚ０-９]/g, (c) => String.fromCharCode(c.charCodeAt(0) - 0xFEE0))
    .replace(/[ァ-ヶ]/g, (c) => String.fromCharCode(c.charCodeAt(0) - 0x60))
    .toLowerCase()
    .replace(/　/g, ' ')
    .replace(/[-－‐]/g, '')
    .trim()

const displayText = computed(() => {
  if (showDropdown.value && !isSelecting.value) return query.value
  const opt = props.options.find((o) => o.value === props.modelValue)
  return opt?.label ?? ''
})

const filteredOptions = computed(() => {
  const q = normalize(query.value)
  if (!q) return props.options
  return props.options.filter((opt) =>
    normalize(opt.searchText ? `${opt.label} ${opt.searchText}` : opt.label).includes(q),
  )
})

const highlightMatch = (label: string): string => {
  const q = normalize(query.value)
  if (!q) return label
  const nLabel = normalize(label)
  const idx = nLabel.indexOf(q)
  if (idx === -1) return label
  // 正規化で長さが変わる場合は位置がずれるためハイライト省略（検索自体は機能する）。
  if (nLabel.length !== label.length || normalize(query.value).length !== query.value.length) return label
  const before = label.slice(0, idx)
  const match = label.slice(idx, idx + query.value.length)
  const after = label.slice(idx + query.value.length)
  return `${before}<span class="font-bold text-blue-600">${match}</span>${after}`
}

const updateDropdownPosition = () => {
  if (!wrapperRef.value) return
  const rect = wrapperRef.value.getBoundingClientRect()
  dropdownStyle.value = {
    top: `${rect.bottom + 4}px`,
    left: `${rect.left}px`,
    width: `${rect.width}px`,
  }
}

const onFocus = () => {
  query.value = ''
  isSelecting.value = false
  showDropdown.value = true
  highlightIndex.value = -1
  nextTick(updateDropdownPosition)
}

const onInput = (e: Event) => {
  query.value = (e.target as HTMLInputElement).value
  isSelecting.value = false
  showDropdown.value = true
  highlightIndex.value = -1
  nextTick(updateDropdownPosition)
}

const select = (value: string) => {
  isSelecting.value = true
  emit('update:modelValue', value)
  showDropdown.value = false
  query.value = ''
  inputRef.value?.blur()
}

const clear = () => {
  emit('update:modelValue', '')
  query.value = ''
  showDropdown.value = false
}

const onKeydown = (e: KeyboardEvent) => {
  if (!showDropdown.value) return
  const maxIdx = filteredOptions.value.length - 1
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    highlightIndex.value = Math.min(highlightIndex.value + 1, maxIdx)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    highlightIndex.value = Math.max(highlightIndex.value - 1, props.allowEmpty ? -1 : 0)
  } else if (e.key === 'Enter') {
    e.preventDefault()
    if (highlightIndex.value === -1 && props.allowEmpty) select('')
    else if (highlightIndex.value >= 0 && highlightIndex.value <= maxIdx) select(filteredOptions.value[highlightIndex.value].value)
  } else if (e.key === 'Escape') {
    showDropdown.value = false
    inputRef.value?.blur()
  }
}

const onClickOutside = (e: MouseEvent) => {
  if (wrapperRef.value?.contains(e.target as Node) || dropdownRef.value?.contains(e.target as Node)) return
  showDropdown.value = false
  query.value = ''
}

// 選択肢窓の外でスクロールが起きたらドロップダウンを閉じる。
// Teleport ドロップダウンは fixed 位置を開いた瞬間に計算するため、ページスクロールすると
// 入力欄から乖離して浮いて見える（気持ち悪い）。外側スクロールで閉じることでこれを防ぐ。
// 選択肢リスト自身（dropdownRef 内）のスクロールは閉じない。scroll はバブルしないため capture で捕捉。
const onScrollOutside = (e: Event) => {
  if (!showDropdown.value) return
  if (dropdownRef.value?.contains(e.target as Node)) return
  showDropdown.value = false
  query.value = ''
}

onMounted(() => {
  document.addEventListener('mousedown', onClickOutside)
  document.addEventListener('scroll', onScrollOutside, true)
})
onUnmounted(() => {
  document.removeEventListener('mousedown', onClickOutside)
  document.removeEventListener('scroll', onScrollOutside, true)
})
</script>

<template>
  <div ref="wrapperRef" class="relative">
    <input
      ref="inputRef"
      :value="displayText"
      :placeholder="placeholder"
      :disabled="disabled"
      :class="[inputClasses, inputClass]"
      autocomplete="off"
      @input="onInput"
      @focus="onFocus"
      @keydown="onKeydown"
    >
    <button
      v-if="modelValue && !disabled && allowEmpty"
      type="button"
      class="absolute right-2 top-1/2 -translate-y-1/2 rounded p-0.5 text-gray-300 transition-colors hover:text-gray-500"
      tabindex="-1"
      @mousedown.prevent="clear"
    >
      <svg class="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18 18 6M6 6l12 12" /></svg>
    </button>
    <div
      v-if="!disabled && !(modelValue && allowEmpty)"
      class="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400"
    >
      <svg class="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m19 9-7 7-7-7" /></svg>
    </div>

    <Teleport to="body">
      <div
        v-if="showDropdown && filteredOptions.length > 0"
        ref="dropdownRef"
        class="fixed z-[9999] max-h-56 overflow-y-auto rounded-md border border-gray-200 bg-white py-1 shadow-lg"
        :style="dropdownStyle"
      >
        <button
          v-if="allowEmpty"
          type="button"
          class="w-full px-3 py-2 text-left text-sm transition-colors"
          :class="highlightIndex === -1 ? 'bg-blue-50 text-blue-700' : 'text-gray-500 hover:bg-gray-50'"
          @mousedown.prevent="select('')"
        >
          {{ emptyLabel }}
        </button>
        <button
          v-for="(opt, idx) in filteredOptions"
          :key="opt.value"
          type="button"
          class="w-full px-3 py-2 text-left text-sm transition-colors"
          :class="[
            idx === highlightIndex ? 'bg-blue-50 text-blue-700' : 'text-gray-700 hover:bg-gray-50',
            opt.value === modelValue ? 'font-medium' : '',
          ]"
          @mousedown.prevent="select(opt.value)"
        >
          <span v-html="highlightMatch(opt.label)" />
        </button>
      </div>
    </Teleport>
  </div>
</template>
