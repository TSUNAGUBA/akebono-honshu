<script setup lang="ts">
/**
 * マスタ参照の選択コントロール（数値 ID 用）。
 * 既存の `<select v-model.number="form.xId"><option :value="m.id">{{code}} - {{name}}</option>`
 * を置き換えるためのドロップイン。内部で AutoComplete（部分一致オートコンプリート）を使う。
 *
 * - items: マスタ配列。label が無ければ `code - name`（code 空なら name）でラベル生成。
 *   code は検索対象にも含める（コードでも引ける）。
 * - modelValue は number | null（未選択は null。既定 0 等の「未選択相当」は空表示）。
 */
interface Item {
  id: number
  code?: string | null
  name?: string | null
  label?: string | null
}

const props = withDefaults(defineProps<{
  modelValue: number | null
  items: Item[]
  placeholder?: string
  allowEmpty?: boolean
  emptyLabel?: string
  disabled?: boolean
}>(), {
  placeholder: '選択 / 入力して検索…',
  allowEmpty: false,
  emptyLabel: '（なし）',
  disabled: false,
})

const emit = defineEmits<{ (e: 'update:modelValue', value: number | null): void }>()

const labelOf = (it: Item): string => {
  if (it.label != null && it.label !== '') return it.label
  if (it.code != null && it.code !== '') return `${it.code} - ${it.name ?? ''}`
  return it.name ?? String(it.id)
}

const options = computed(() =>
  props.items.map((it) => ({
    value: String(it.id),
    label: labelOf(it),
    searchText: it.code ?? '',
  })),
)

// number(>=1) のみ有効値とみなす。0 / null は未選択 → 空表示。
const stringValue = computed(() =>
  props.modelValue != null && props.modelValue > 0 ? String(props.modelValue) : '',
)

const onUpdate = (v: string) => emit('update:modelValue', v === '' ? null : Number(v))
</script>

<template>
  <AutoComplete
    :model-value="stringValue"
    :options="options"
    :placeholder="placeholder"
    :allow-empty="allowEmpty"
    :empty-label="emptyLabel"
    :disabled="disabled"
    @update:model-value="onUpdate"
  />
</template>
