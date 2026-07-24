<script setup lang="ts">
/**
 * マスタ参照の選択コントロール（uuid 文字列 ID 用）。
 * 既存の `<select v-model="form.xId"><option :value="m.id">{{code}} - {{name}}</option>`
 * を置き換えるためのドロップイン。内部で AutoComplete（部分一致オートコンプリート）を使う。
 *
 * - items: マスタ配列。label が無ければ `code - name`（code 空なら name）でラベル生成。
 *   code は検索対象にも含める（コードでも引ける）。
 * - modelValue は string | null（未選択は null。空文字 '' も「未選択相当」として空表示）。
 */
interface Item {
  id: string
  code?: string | null
  name?: string | null
  label?: string | null
}

const props = withDefaults(defineProps<{
  modelValue: string | null
  items: Item[]
  placeholder?: string
  allowEmpty?: boolean
  emptyLabel?: string
  disabled?: boolean
  /** スプレッドシート風のセル入力 (枠なし)。表内の高密度入力向け。AutoComplete へ委譲。 */
  borderless?: boolean
}>(), {
  placeholder: '選択 / 入力して検索…',
  allowEmpty: false,
  emptyLabel: '（なし）',
  disabled: false,
  borderless: false,
})

const emit = defineEmits<{ (e: 'update:modelValue', value: string | null): void }>()

const labelOf = (it: Item): string => {
  if (it.label != null && it.label !== '') return it.label
  if (it.code != null && it.code !== '') return `${it.code} - ${it.name ?? ''}`
  return it.name ?? it.id
}

const options = computed(() =>
  props.items.map((it) => ({
    value: it.id,
    label: labelOf(it),
    searchText: it.code ?? '',
  })),
)

// 非空文字列のみ有効値とみなす。'' / null は未選択 → 空表示。
const stringValue = computed(() => props.modelValue ?? '')

const onUpdate = (v: string) => emit('update:modelValue', v === '' ? null : v)
</script>

<template>
  <AutoComplete
    :model-value="stringValue"
    :options="options"
    :placeholder="placeholder"
    :allow-empty="allowEmpty"
    :empty-label="emptyLabel"
    :disabled="disabled"
    :borderless="borderless"
    @update:model-value="onUpdate"
  />
</template>
