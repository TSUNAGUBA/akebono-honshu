<script setup lang="ts">
/**
 * 承認経路のステップ編集（役職 / ロール / 個人 から承認者を選ぶ）。Iteration 33。
 * v-model は ApproverStepInput[]。order は「配列の並び順 = 1..n」に正規化して常に一意・連続にする
 * （バックエンドの order 重複チェックを確実に満たす）。承認方式(mode)は現状 serial 固定のため UI では出さない。
 */
import type { ApproverStepInput, ApproverType } from '~/composables/useAttendance'
import { APPROVER_TYPE_LABELS } from '~/utils/attendance'

const props = defineProps<{
  modelValue: ApproverStepInput[]
  /** 個人指定の候補（在籍メンバー）。MasterSelect 用 { id, name, code }。 */
  memberOptions: { id: string; name: string; code?: string }[]
  /** 役職の入力候補（在籍メンバーの役職を distinct したもの。datalist で補完に使う）。 */
  titleOptions?: string[]
}>()

const emit = defineEmits<{ 'update:modelValue': [ApproverStepInput[]] }>()

const TYPE_OPTIONS = (Object.keys(APPROVER_TYPE_LABELS) as ApproverType[]).map((value) => ({
  value,
  label: APPROVER_TYPE_LABELS[value],
}))

const datalistId = `approver-titles-${Math.random().toString(36).slice(2)}`

/** order を並び順で採番し直して emit する（1..n・一意・連続を保証）。 */
const commit = (steps: ApproverStepInput[]) => {
  emit('update:modelValue', steps.map((s, i) => ({ ...s, order: i + 1 })))
}

const addStep = () => {
  commit([
    ...props.modelValue,
    { order: props.modelValue.length + 1, approverType: 'member', approverUserId: null },
  ])
}

const removeStep = (index: number) => {
  commit(props.modelValue.filter((_, i) => i !== index))
}

/** ステップの一部を差し替える。承認者種別を変えたら他種別のフィールドをクリアする（誤送信防止）。 */
const patchStep = (index: number, patch: Partial<ApproverStepInput>) => {
  const next = props.modelValue.map((s, i) => {
    if (i !== index) return s
    const merged: ApproverStepInput = { ...s, ...patch }
    if (patch.approverType && patch.approverType !== s.approverType) {
      merged.approverRole = patch.approverType === 'role' ? 'owner' : null
      merged.approverTitle = null
      merged.approverUserId = null
    }
    return merged
  })
  commit(next)
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <datalist :id="datalistId">
      <option v-for="t in (titleOptions ?? [])" :key="t" :value="t" />
    </datalist>

    <p v-if="modelValue.length === 0" class="rounded-md bg-gray-50 px-3 py-2 text-sm text-gray-500">
      承認者が未設定です。「＋ 承認ステップを追加」で 1 名以上追加してください。
    </p>

    <ul class="flex flex-col gap-2">
      <li
        v-for="(step, index) in modelValue"
        :key="index"
        class="rounded-md border border-gray-200 bg-gray-50/60 p-2.5"
      >
        <div class="flex items-center gap-2">
          <span
            class="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-blue-100 text-xs font-semibold text-blue-700"
          >{{ index + 1 }}</span>
          <span class="text-xs text-gray-500">承認者の指定方法</span>
          <div class="ml-auto">
            <button
              type="button"
              class="rounded-md border border-red-200 px-2 py-0.5 text-xs text-red-600 hover:bg-red-50"
              @click="removeStep(index)"
            >削除</button>
          </div>
        </div>

        <div class="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-2">
          <AutoComplete
            :model-value="step.approverType"
            :options="TYPE_OPTIONS"
            :allow-empty="false"
            @update:model-value="(v: string) => patchStep(index, { approverType: v as ApproverType })"
          />

          <!-- 個人（member）: メンバー選択 -->
          <MasterSelect
            v-if="step.approverType === 'member'"
            :model-value="step.approverUserId ?? null"
            :items="memberOptions"
            allow-empty
            empty-label="（未選択）"
            placeholder="承認者を検索…"
            @update:model-value="(v: string | null) => patchStep(index, { approverUserId: v })"
          />

          <!-- 役職（title）: 役職名の入力（在籍者の役職を datalist で補完） -->
          <input
            v-else-if="step.approverType === 'title'"
            :value="step.approverTitle ?? ''"
            type="text"
            maxlength="64"
            :list="datalistId"
            placeholder="役職名（例: マネージャー）"
            class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
            @input="(e) => patchStep(index, { approverTitle: (e.target as HTMLInputElement).value })"
          />

          <!-- ロール（role）: 現状はオーナーのみ -->
          <div
            v-else
            class="flex items-center rounded-md border border-gray-200 bg-white px-2.5 py-1.5 text-sm text-gray-600"
          >オーナー（勤怠管理者）</div>
        </div>
      </li>
    </ul>

    <button
      type="button"
      class="self-start rounded-md border border-blue-300 px-3 py-1 text-sm text-blue-700 hover:bg-blue-50"
      @click="addStep"
    >＋ 承認ステップを追加</button>
  </div>
</template>
