<script setup lang="ts">
import type { MasterDef, MasterItem } from '#imports'
import { allFields } from '~/utils/master-definitions'

interface Props {
  open: boolean
  def: MasterDef
  /** 編集モード時の既存データ (新規時は null) */
  initial: MasterItem | null
  /** select-master 用 FK 選択肢取得済データ (slug → items) */
  referenceData: Record<string, MasterItem[]>
}
const props = defineProps<Props>()
const emit = defineEmits<{
  close: []
  saved: [payload: Record<string, unknown>]
}>()

const fields = computed(() => allFields(props.def))

const form = ref<Record<string, unknown>>({})
const errorMessage = ref('')
const submitting = ref(false)

watchEffect(() => {
  if (!props.open) return
  if (props.initial) {
    const init: Record<string, unknown> = {}
    // nullable な number/decimal は NULL (未設定) を保持する (?? で 0 に潰さない、review #5)。
    for (const f of fields.value) {
      init[f.key] = f.nullable ? (props.initial[f.key] ?? null) : (props.initial[f.key] ?? defaultFor(f.type))
    }
    form.value = init
  } else {
    const init: Record<string, unknown> = {}
    for (const f of fields.value) init[f.key] = f.nullable ? null : defaultFor(f.type)
    form.value = init
  }
  errorMessage.value = ''
})

function defaultFor(type: string): unknown {
  switch (type) {
    case 'number':
    case 'decimal': return 0
    case 'checkbox': return false
    default: return ''
  }
}

const onSubmit = async () => {
  errorMessage.value = ''
  // 簡易バリデーション
  for (const f of fields.value) {
    if (!f.required) continue
    const v = form.value[f.key]
    if (v === '' || v === null || v === undefined) {
      errorMessage.value = `${f.label} は必須です`
      return
    }
  }
  submitting.value = true
  try {
    // number / decimal は文字列入力を数値に変換
    const payload: Record<string, unknown> = {}
    for (const f of fields.value) {
      const v = form.value[f.key]
      // nullable な number/decimal は空入力を 0 ではなく null として送る (未設定を保持、review #5)。
      const isEmptyNum = v === '' || v === null || v === undefined || (typeof v === 'number' && Number.isNaN(v))
      if (f.type === 'number' || f.type === 'decimal') payload[f.key] = (f.nullable && isEmptyNum) ? null : Number(v)
      else if (f.type === 'checkbox') payload[f.key] = Boolean(v)
      else payload[f.key] = v === '' ? null : v
    }
    emit('saved', payload)
  } finally {
    submitting.value = false
  }
}

const referenceItemsFor = (slug: string): MasterItem[] => props.referenceData[slug] ?? []
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
      role="dialog"
      aria-modal="true"
      @click.self="emit('close')"
    >
      <!-- 画面高を超えないよう max-h + 縦フレックス。ヘッダ/フッタは固定、本文のみスクロールさせる
           (フィールド数の多いマスタ (仕入先等) でモーダルが画面外に溢れ操作不能になる問題の修正)。 -->
      <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-2xl flex-col rounded-lg bg-white shadow-xl">
        <header class="shrink-0 border-b border-gray-200 px-5 py-3">
          <h2 class="text-base font-semibold">
            {{ initial ? `${def.label} を編集` : `${def.label} を新規追加` }}
          </h2>
          <p v-if="initial" class="mt-0.5 text-xs text-gray-500">
            ID: {{ initial.id }} / 更新: {{ formatJstDateTime(initial.updatedAt) }}
          </p>
        </header>

        <form class="flex-1 space-y-2.5 overflow-y-auto px-5 py-3" @submit.prevent="onSubmit">
          <div v-for="f in fields" :key="f.key" class="flex flex-col gap-1">
            <label class="text-sm font-medium">
              {{ f.label }}
              <span v-if="f.required" class="text-red-500">*</span>
            </label>

            <input
              v-if="f.type === 'text'"
              v-model="form[f.key] as string"
              type="text"
              :maxlength="f.maxLength"
              :placeholder="f.placeholder"
              class="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />

            <input
              v-else-if="f.type === 'number'"
              v-model.number="form[f.key] as number"
              type="number"
              class="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />

            <input
              v-else-if="f.type === 'decimal'"
              v-model.number="form[f.key] as number"
              type="number"
              step="0.01"
              class="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />

            <textarea
              v-else-if="f.type === 'textarea'"
              v-model="form[f.key] as string"
              rows="4"
              class="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />

            <label v-else-if="f.type === 'checkbox'" class="inline-flex items-center gap-2">
              <input
                v-model="form[f.key] as boolean"
                type="checkbox"
                class="h-4 w-4 rounded border-gray-300"
              />
              <span class="text-sm">有効</span>
            </label>

            <MasterSelect
              v-else-if="f.type === 'select-master'"
              :model-value="(form[f.key] as string | null)"
              :items="referenceItemsFor(f.master ?? '')"
              @update:model-value="(v) => form[f.key] = v ?? null"
            />

            <!-- 参照先マスタの code (文字列) を選ぶドロップダウン (通貨等)。id ではなく code を保存する。 -->
            <select
              v-else-if="f.type === 'select-master-code'"
              v-model="form[f.key] as string"
              class="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option value="">（選択）</option>
              <option v-for="it in referenceItemsFor(f.master ?? '')" :key="it.id" :value="it.code">
                {{ it.code }} — {{ it.name }}
              </option>
            </select>

            <p v-if="f.help" class="text-xs text-gray-500">{{ f.help }}</p>
          </div>

          <div v-if="errorMessage" class="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
            {{ errorMessage }}
          </div>
        </form>

        <footer class="flex shrink-0 justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-2.5">
          <button
            type="button"
            class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-100"
            @click="emit('close')"
          >
            キャンセル
          </button>
          <button
            type="button"
            :disabled="submitting"
            class="rounded-md bg-blue-600 px-4 py-1.5 text-sm text-white hover:bg-blue-700 disabled:opacity-50"
            @click="onSubmit"
          >
            {{ submitting ? '保存中…' : '保存' }}
          </button>
        </footer>
      </div>
    </div>
  </Teleport>
</template>
